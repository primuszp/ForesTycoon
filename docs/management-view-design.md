# Erdőgazdálkodási menedzsmentnézet – játéktervezés

Cél: a játékos **egy pillantással lássa, hol és milyen beavatkozás kell**, és onnan
egy kattintással el is indíthassa. Minta a Transport Tycoon / OpenTTD „térkép +
jelmagyarázat” ablaka és a SimCity 2000/4 adatnézetei (talajvíz, szennyezés,
földérték): a térkép nem díszlet, hanem **döntési eszköz**.

A jelenlegi `EcologyRasterView` (Talaj, víz és klíma térképe) a kiindulópont: már
csempeszintű, csak olvasó, nem lépteti az ökológiát, és a talaj-, víz- és
klímaadatot kiolvassa. Ezt bővítjük négy fő réteggé, diagnózissal és akciókkal.

---

## 1. A négy fő réteg (lencse)

Minden réteg ugyanarra a csempe-raszterre (`RasterGrid`) épül, és két módban
látható: **2D áttekintő ablakban** (kistérkép) és **3D-ben a terepre vetítve**
(színezett csempefedő, a fák áttetszővé/alacsonnyá halványítva). A billentyűk
a TTD-szerű gyors váltást adják.

| Billentyű | Lencse | Kérdés, amire felel | Forrásadat (már létezik) |
|---|---|---|---|
| `F1` | **Víz** | Hol szárad ki vagy áll a víz? | `EnvironmentCell.Soil/Drought/Waterlogging`, `SoilProperties.Availability()`, `SurfaceWaterFlux` |
| `F2` | **Talaj** | Mit bír el a termőhely? | `SoilLandscape.Profile(id)`: típus, `Fertility`, vízkapacitás, beszivárgás |
| `F3` | **Erdőegészség** | Hol beteg, stresszes, pusztul az erdő? | `ForestTree.Health`, `StressYears`, `ForestResources` (fény/víz/tér), `ForestDeadTree` |
| `F4` | **Állomány** | Mi áll ott, hány éves, mikor vágható? | `ForestStand` (faj, kor, biomassza, érettség), `ForestTree` egyedek, fatérfogat |
| `F5` | **Teendők** (összesítő) | Mit csináljak most? | a fenti négy diagnózisa (2. pont) |

Minden lencsének 2–4 **alréteg**e van (lenyíló vagy `Tab`):

- **Víz:** elérhető gyökérzónavíz (%) · aszálystressz · vízborítás/pangóvíz ·
  felszíni lefolyás iránya (nyilak, `SurfaceWaterFlux`) · éves vízmérleg-trend.
- **Talaj:** talajtípus (kategorikus) · termékenység · vízkapacitás · taposás/erózió
  (később, a gépek és utak nyomán).
- **Erdőegészség:** vitalitás (átlagos `Health`) · stressz oka (fény / víz / tér –
  domináns limitáló tényező színkóddal) · elhalt fák aránya · kártevő/kockázat
  (később: szú a stresszes lucosokban).
- **Állomány:** fafaj (kategorikus) · korosztály · érettség / vághatóság ·
  záródás (`GetCrowding`) · faanyagérték (m³ és pénz).

### Színnyelv (egységes minden lencsén)

- **Folytonos skálák** perceptuálisan egyenletes, színvak-barát palettákkal:
  víz = barna → homok → kék (divergáló, közép = optimális szabadföldi vízkapacitás);
  egészség = piros → sárga → zöld helyett **bíbor → krém → zöld** (deuteranóp-biztos, a füvön is kiválik);
  kor = világos → sötét zöld egyetlen árnyalatsorban.
- **Kategorikus** (talajtípus, fafaj): rögzített, fajonként azonos szín, mint a
  3D modellek koronaszíne, hogy a térkép és a világ összeolvasható legyen.
- **Ikonréteg** a színek fölött (TTD-s „állomásjel” logika): ha egy csempén
  beavatkozás kell, kis ikon jelenik meg (csepp, fejsze, csemete, kereszt), így a
  diagnózis szín nélkül is olvasható. A meglévő `GameIcons` bővül.
- Semleges szürke = nincs adat / nem erdő; így az erdő-specifikus lencséken a
  szántó, víz, út elhalványul.

---

## 2. Diagnózis: a térkép mondja meg, mi a baj

A nyers rétegek mellé egy **szabályalapú diagnosztikai réteg** kerül
(`ForestDiagnosis`, csak olvasó, havonta frissül a havi előkészítéssel együtt).
Minden csempe legfeljebb egy *elsődleges* és egy *másodlagos* jelzést kap,
súlyossággal (0–3).

| Jelzés | Feltétel (első közelítés) | Javasolt beavatkozás |
|---|---|---|
| 🔥 **Aszály** | elérhető víz < 25% legalább 2 hónapja, és a fák víz-limitáltak | ritkítás (kevesebb párologtató), szárazságtűrő faj (tölgy, cser, erdeifenyő) |
| 💧 **Pangóvíz** | `Waterlogging` > 0,5 tartósan | vízelvezető árok, nyír/éger/kőris telepítés |
| 🌑 **Túlsűrű** | záródás magas, tér-limitált fák aránya > 40% | **gyérítés** (tisztítás / törzskiválasztó gyérítés) |
| 🪓 **Vághatóság** | érettség ≥ 1 és értéknövekedés lassul | véghasználat (tarvágás / fokozatos felújító vágás) |
| ☠ **Pusztulás** | elhalt fák aránya > 15% vagy `StressYears` ≥ 4 sok egyednél | egészségügyi termelés, fafajcsere |
| 🌱 **Felújítandó** | üres vagy tönkös csempe erdő mellett, jó termőhely | csemeteültetés (a talaj+víz alapján ajánlott faj) |
| ⚠ **Fafaj–termőhely eltérés** | `Fitness(species, …)` alacsony | következő ciklusban fafajcsere |

A szabályok küszöbei adatfájlban (`management-rules.json`) legyenek, mint a
`profiles.json` talajkatalógus, hogy hangolhatók és tesztelhetők maradjanak.

**Termőhely-ajánló:** egy kiválasztott csempére/területre a játék kiszámolja,
melyik faj illik oda (`ForestSystem.Fitness` + talaj termékenység + vízmérleg),
és csillagokkal (★★★☆☆) rangsorolja. Ez a „mit ültessek?” kérdés válasza.

---

## 3. Képernyőterv

```
┌─────────────────────────────────────────────────────────────────────────┐
│ [Víz][Talaj][Egészség][Állomány][Teendők]   alréteg: ▼ Aszálystressz    │  ← lencse sáv (F1–F5)
├───────────────────────────────────────────────┬─────────────────────────┤
│                                               │ ERDŐRÉSZLET 12/B        │
│        3D világ, a terepre vetített            │ Lucos · 64 év · 2,3 ha  │
│        rasztertérképpel; a fák alacsony,       │ Egészség  ▓▓▓▓░░ 62%    │
│        áttetsző "makett" módban                │ Víz       ▓▓░░░░ 31% ⚠  │
│                                               │ Záródás   ▓▓▓▓▓░ 88%    │
│   ┌───────────┐  ikonok a gondos csempéken     │ Készlet   412 m³ · 9,8 M│
│   │ kistérkép │  (💧 🪓 🌱 ☠)                   │ Fő gond: aszály + sűrű  │
│   │ (2D)      │                                │ ─────────────────────── │
│   └───────────┘                                │ [Gyérítés]  [Fafajcsere]│
├───────────────────────────────────────────────┤ [Árok]      [Megfigyel] │
│ Jelmagyarázat: barna 0% ──── kék 100%  · hónap: május · trend ↘        │
└─────────────────────────────────────────────────────────────────────────┘
```

- **Lencse sáv** felül, a TTD eszköztárához hasonlóan ikonokkal; a kiválasztott
  lencse a terepre vetül. `Esc` vagy ismételt lenyomás kikapcsolja.
- **Erdőrészlet-panel** jobbra: kattintásra a csempe, húzással terület (a meglévő
  terület-kijelölés, `PlantForestAreaCommand`/`HarvestForestAreaCommand` mintájára)
  összesítve. Diagnózis + 2–4 akciógomb, mindegyiken költség és várható hatás.
- **Kistérkép** bal alul: a teljes birtok áttekintése, kattintásra kameraugrás.
- **Jelmagyarázat-sáv** alul: skála, aktuális hónap, trend nyíl (javul/romlik
  az előző hónaphoz képest).

### 3D vetítés (a „rasztertérkép a világban”)

1. A rétegértékeket egy `R8`/`RGBA8` textúrába írjuk (csempénként egy texel,
   frissítés csak `Revision` változáskor).
2. A terep-shader egy `uOverlay` mintavevővel és keverési tényezővel a csempe
   színére keveri, csempehatáron vékony sötét vonallal (TTD-rács hangulat).
3. A fák „makett módba” kerülnek: koronájuk 35%-ra áttetsző, vagy csak a törzs és
   egy lapos korongárnyék marad, hogy a térkép látszódjon. Ezt a meglévő
   `ForestVisualState` kapcsolója vezérelheti.
4. Az ikonok képernyőtérben, billboardként rajzolódnak; távoli zoomnál
   területenként összevonva (klaszterezve), hogy ne zsúfolódjanak.

### Interakció

- **Hover**: tooltip pontos értékkel és mértékegységgel (a mostani tooltip mintájára).
- **Kattintás**: erdőrészlet kiválasztása, jobb panel.
- **Húzás**: terület, összesített diagnózis („36 csempe, 22 túlsűrű, 5 aszályos”).
- **Jobb klikk akciógombra**: előnézet – a térkép megmutatja, hogyan változna a
  réteg 5/10 év múlva (determinisztikus előrevetítés a checkpoint-másolaton; a
  szimulációt nem lépteti, összhangban a mostani „csak olvasó” elvvel).

---

## 4. Játékmenet-hurok

1. **Felderítés** – a játékos a Teendők lencsén látja a piros/ikonos foltokat.
2. **Diagnózis** – rákattint, a panel megmondja a *fő okot* (pl. víz-limitált).
3. **Döntés** – akciót választ (költség, munkaerő, gép kell hozzá).
4. **Végrehajtás** – parancs a meglévő `WorldCommandQueue`-n át (mentés, visszajátszás működik).
5. **Visszajelzés** – a következő hónapokban a lencse színe javul; a trend nyíl és
   egy „beavatkozási napló” mutatja, megérte-e.

A pontozás és bevétel ehhez köthető: **erdőállapot-index** (vitalitás × fajdiverzitás
× vízmérleg) a birtok-szintű kitüntetésekhez / tanúsítványhoz (FSC-szerű), így nem
csak a tarvágás fizet.

### Új beavatkozások (a meglévő ültetés és vágás mellé)

| Akció | Hatás a szimulációban | Kapcsolódás |
|---|---|---|
| Tisztítás / gyérítés (x% egyed) | kevesebb verseny → több fény/víz/tér a maradéknak | `ForestCompetition`, egyedszintű kivágás már létezik |
| Egészségügyi termelés | elhalt és `StressYears` magas fák eltávolítása | `ForestDeadTree` |
| Vízelvezető árok | `DrainagePerHour` növelés a csempén | `SoilProperties` csempe-szintű felülírás |
| Víztartó (gát, tó) | lefolyás lassítása, talajvíz feltöltés | `SurfaceWaterFlux`, terepszerkesztés |
| Talajjavítás (mész, szerves anyag) | `Fertility` lassú emelése | talajmodell perzisztencia már van |
| Alátelepítés | árnyéktűrő faj ültetése idős állomány alá | `PlantInArea` + árnyéktűrés |

---

## 5. Technikai felépítés

- **`ManagementLayer` absztrakció** (`ForesTycoon/UI/Management/`):
  `Id`, `Name`, `Unit`, `Categorical`, `Sample(int tileId) → float/byte`,
  `Palette`, `Legend`. A meglévő `RasterFieldDescriptor`-t használja metaadatként,
  így a modell-oldali rétegek (talaj, víz) és a származtatott rétegek egy formában jönnek.
- **`ForestTileSummary`**: csempénkénti aggregátum (átlagos egészség, domináns
  limitáló tényező, elhalt arány, készlet m³) – havonta, a havi előkészítés után
  egyszer számolva, nem képkockánként. A `ForestStand` mezői részben adják.
- **`ForestDiagnosis`**: szabálymotor a 2. pont táblájára; tiszta függvény,
  egységtesztelhető (`ForesTycoon.Tests/ForestDiagnosisTests.cs`).
- **Renderelés**: `TerrainRenderer` kap egy opcionális overlay-textúrát; a 2D nézet
  a mostani ImGui-rajzolást tartja (64×64 mintavétel), de a csempeszínt ugyanabból
  a `ManagementLayer`-ből kéri, így a két nézet nem térhet el.
- **Architektúra-elv**: a nézet *csak olvas*, az akciók `IWorldCommand`-ként
  mennek; az `ArchitectureTests` már őrzi a rétegek közti függőségeket.

## 6. Ütemezés

1. **M1 – Lencsék 2D-ben**: `ManagementLayer` + 4 lencse az ImGui ablakban,
   jelmagyarázat, tooltip, a mostani `EcologyRasterView` erre átírva.
2. **M2 – Vetítés a terepre**: overlay textúra, makett-fa mód, F1–F5 billentyűk.
3. **M3 – Diagnózis és ikonok**: `ForestDiagnosis`, Teendők lencse, panelakciók.
4. **M4 – Új beavatkozások**: gyérítés, egészségügyi termelés, árok.
5. **M5 – Előrevetítés és napló**: „mi lenne, ha” előnézet, beavatkozási napló, index.

## 7. Állapot (2026-10-08)

**M1 kész** (2D lencsék, diagnózis, panel):

- `ForesTycoon.Ecology/Management/ForestManagementSurvey.cs`: csempénkénti, csak olvasó felmérés
  (fafaj, kor, érettség, egészség, sűrűség, készlet, elhalt/stresszes/zsúfolt arány, limitáló
  tényező, víz, talaj), `ManagementRules` küszöbök és `Diagnose` szabálymotor, fafaj-ajánló.
  Felújítandónak csak a letermelt/kipusztult folt számít, nem minden üres föld; a pusztulás
  küszöbe 30% elhalt fa vagy 30% alatti egészség (az önritkulás egy-egy elhalt fája nem riaszt).
- `ForesTycoon/UI/ManagementView.cs`: „Erdőgazdálkodás” ablak (eszköztár vagy **M**), öt lencse
  alrétegekkel, színvak-barát skálák, jelmagyarázat, tooltip, súlyosság szerinti ikonok,
  erdőrészlet-panel diagnózissal, ajánlott fafajokkal és akciókkal (odaugrás, ültetés a javasolt
  fajjal, kitermelés). Dupla kattintás a térképen: kameraugrás.
- Tesztek: `ForestManagementSurveyTests`; füstpróba: `--management-smoke-test`
  (`artifacts/management-view/`, 9 lencsekép, ellenőrzi, hogy a nézet nem módosítja a szimulációt).

**M2 kész** (vetítés a terepre):

- Az aktív lencse csempénkénti áttetsző színként a 3D terepre kerül (`Terrain.DrawManagementOverlay`,
  csak a látható csempéken, a matrica-menetben). Ablakbezárásra eltűnik.
- „Fák elrejtése” (alapértelmezett): a fák helyén csak az erdőtalaj-folt marad, így a színezés olvasható.
  A csak-törzs változatot elvetettük, mert kiszáradt erdőnek hatott, és a pusztulással volt összekeverhető.
- A Teendők lencse ikonjai a 3D nézetben is megjelennek a csempék fölött, zoomtól függően ritkítva.
- Képernyőképek: `--capture-frame <könyvtár>` 08–11. kép.

Következő: M3/M4 új beavatkozások (gyérítés, egészségügyi termelés, árok), a szabályok JSON-ba szervezése.
