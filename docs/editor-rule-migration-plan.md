> Archivált tervezési anyag. A korábbi szabályalakok kompatibilitási átvezetései 2026-10-16-án kivezetésre kerültek; a jelenlegi működést a [szabályszerkesztő útmutatója](rule-editor.md) írja le.

# Szerkeszthető világszabályok: értékelés és első közös minta

2026-10-15 · Átvizsgált kiindulás: `0f90403`.
Állapot: megvalósítási javaslat, nem elkészült funkció. Ebben a körben a játék viselkedése nem változik.

Utókövetés: az első, rögzített paraméterkártyát általános gráfos hatásszerkesztő váltotta fel.
A források, célok, görbék, szorzók és feltételek szabadon összeállíthatók; a régi kártyák
betöltéskor erre a modellre alakulnak át. [Aktuális használat](rule-editor.md#szabályok).
Az alábbi értékelés az eredeti kiindulást rögzíti; a teljes migráció továbbra is terv.

## Cél és alapdöntés

A világ alkotója programozás nélkül válasszon objektumot, feltételt és következményt.
A játék ugyanazt a mentett szabályt futtassa, amelyet az editor szerkeszt és kipróbál.
Az objektumok és változók azonosítói angolul, a játék kódjából származzanak;
a magyar magyarázat és felületi címke ne legyen része a programozási szerződésnek.

A meglévő `WorldRule` és `RuleSchema` továbbfejlesztését javaslom. Új, párhuzamos
szabálymotor helyett egy szerkesztési és végrehajtási modell kell. A képlet, a görbe
és a feltételkártya ugyanannak a szabálynak különböző nézete legyen.

## Amit a forráskód ténylegesen tud

| Terület | Ellenőrzött állapot | Következmény |
| --- | --- | --- |
| Kódalapú felderítés | `RuleSchema.Discover` attribútummal megjelölt interfészeket, örökölt mezőket, kapcsolatokat és műveleteket olvas. | Meglévő, használható alap. Nem minden C# változót tesz automatikusan szerkeszthetővé. |
| Angol nevek | `WaterSupply` → `waterSupply`; az interfész és az attribútum felülírhatja a nevet. | Az új objektummodell megfelel az iránynak. A név stabilitását átnevezéskor még biztosítani kell. |
| Világkapcsolat | A `RuleEntities.cs` nézetosztályai az élő állapotot olvassák/írják; a konkrét hatókörök regisztrálása kézi. | Új mezőhöz nem kell editorkód, új objektumtípushoz még kell játékoldali adapter és regisztráció. |
| Szabálykönyv | `WorldRule`: időzítés, hatókör, feltétel, rendezett hatások; `set`, `add`, `action`. | Valódi végrehajtható szabály, nem puszta katalógus. |
| Átköltöztetett szabályok | `trail.age`, `forest.stumps`; a `tree.drought` kikapcsolt demonstráció. | A szárazságminta nem bizonyítja a meglévő faegészség-számítás kiváltását. |
| Régi felületek | Külön útkopásmodell, behavior hookok, vezérlők, dinamika és algoritmusgráfok is léteznek. | A „szerkeszthető” ma nem mindenütt jelenti a teljes szabály szerkeszthetőségét. |
| Felület | A Szabálykönyvben van Mikor/Hol/Ha/Akkor, mezőválasztás és görbeszerkesztés. | A feltétel és sok hatás továbbra is képletszöveget kér; ez még nem elég a nem programozó alkotónak. |
| Időzítés | Csak `step`; a `GameWorld.WeatherRoads` legalább 0,5 játék-másodperc összegzése után futtatja a könyvet. | Nem minden motorlépés, és a futtatás jelenleg az útfrissítéshez kötött. |
| Kapcsolatok | Egy szint mélységű, olvasható kapcsolati mezők. | Többszintű kapcsolatok, halmazok és szomszédsági összesítések külön fejlesztést igényelnek. |
| Saját állapot | Indexelt `double[]`; méretváltozáskor a `Values` új tömböt inicializál. | Változó elemszámnál értékvesztés, átrendezésnél téves hozzárendelés lehetséges. Stabil objektumazonosító szükséges. |
| Mentés és alkalmazás | Projektklónozás, validálás, parancson keresztüli alkalmazás és checkpoint létezik. | Erre épüljön a próbálás és a játékba átvétel. |

Fő források: `ForesTycoon.Rules/RuleSchema.cs`, `WorldRules.cs`,
`ForesTycoon.Simulation/World/RuleEngine.cs`, `RuleEntities.cs`, `GameWorld.RuleProject.cs`,
`GameWorld.cs`, `ForesTycoon.Editor/RuleEditorView.RuleBook.cs`.
A korábbi tervek: [objektummodell](editor-object-model.md), [építőkövek](rule-building-blocks.md).

## A tömeges átállás előtti szerződések

1. **Felderítés és engedélyezett írás.** A kód jelölje a szabályok számára elérhető
   mezőket, típusukat, egységüket, tartományukat és kapcsolatukat. Az editor ezekből
   építkezzen. A fizikai tárolók, gyorsítótárak és belső indexek ne legyenek tetszőlegesen írhatók.
   A setter őrizze meg az állapot érvényességét és jelezze a szükséges frissítéseket.
2. **Stabil angol azonosítók.** Objektum: `road`; mező: `condition`; kapcsolat:
   `tile.moisture`. A kiadott azonosító ne változzon csendben C# átnevezéskor.
   Legyen sémaellenőrzés duplikációra, hibás mezőtípusra és nem támogatott kapcsolatra.
   A magyar címke külön metaadat. A régi `lépés`, `hangolás.*`, `saját.*` és kézi
   mezőnevek verziózott, parseren alapuló migrációt kapjanak; ne szöveges cserét.
3. **Típus és időegység.** Szám mellett logikai érték és felsorolás is kell
   (például burkolat). Javasolt időnév: `deltaYears`, később `deltaSeconds` is,
   mindig egyértelmű órával. A sebesség jellegű hatás idővel szorzását a felület kezelje.
4. **Egy tulajdonos szabályonként.** Az adott folyamatot vagy a natív ág, vagy a
   szabálykönyv végzi. A kikapcsolt, már átköltöztetett szabály nem kapcsolja vissza
   automatikusan a natív működést. A több külön szabályból származó, szándékos hatások
   viszont legyenek láthatók és rendezettek.
5. **Sorrend és hibák.** Rögzített fázisok és szabálysorrend; látható, hogy egy szabály
   mely korábbi változásokat látja. Jelenleg egy későbbi hatás hibája nem vonja vissza
   a korábbi írásokat, és egy végrehajtott művelet lezárja az elem további hatásait.
   Kezdetben ezt mutassuk világosan; összetett pénz-/anyagátadáshoz atomikus művelet kell.
6. **Példányazonosság.** Saját állapot kulcsa objektumtípus + stabil példányazonosító
   legyen; létrehozás, törlés, mentés, visszatöltés és projektcsere meghatározott életciklussal.
7. **Adatcsomag.** A jelenlegi alapok C#-ban állítanak elő szerkeszthető szabályadatot.
   Később verziózott alap-szabálycsomag legyen az egyetlen forrás. Az új alapverzió,
   a felhasználó módosítása és a hiányzó szabály pótlása külön kezelendő.

## Első kis minta: időjárási útromlás (`road.weather`)

Ezt javaslom közösen kidolgozni. Van már natív megvalósítása, hangolása és tesztje;
kevés bemenetből egyetlen kimenetet számol. Megmutatja az objektum–környezet kapcsolatot,
és nem igényel még faegyed-életciklust vagy anyagmérleget.

### A jelenlegi viselkedés pontos megőrzése

Forrás: `TerrainMap.Roads.cs: WeatherRoads`, `GameWorld.cs: WeatherRoads`.

- `wet = clamp(hydro.TileMoisture[id], 0, 1)`.
- `rain = clamp(Environment.RainRate / 20, 0, 1)`.
- Aszfalt: `rate = AsphaltBase * (1 + wet) + AsphaltRain * rain`.
- Makadám: `rate = MacadamBase * (1 + 2.5 * wet) + MacadamRain * rain * (0.4 + wet)`.
- `condition = clamp(condition - rate * deltaYears, 0, 1)`.
- Alapértékek: `0.015`, `0.08`, `0.10`, `1.6`; a meglévő tuning felülírhatja őket.
- A régi behavior hook a teljes romlási rátát felülírhatja, ennek kompatibilitása is feladat.

Fontos: a `hydro.TileMoisture` nem cserélhető ki automatikusan az élő talajvízrendszer
vízellátottságára. Az már modellváltoztatás lenne. Az eső is a jelenlegi `RainRate`,
nem az ettől eltérő `LiquidRainRate`. Előbb viselkedéshű migráció, utána külön hangolás.

### Amit az alkotó látna

> **Időjárási útromlás**  
> Mikor: minden szabálylépésben  
> Hol: minden út  
> Ha: burkolat = aszfalt  
> Akkor: az állapot csökkenjen az alábbi éves ütemmel  
> Száraz alapromlás: **0,015 / erdőév**  
> Nedvesség hatása: **1 + nedvesség**  
> Eső hatása: **0,08 × esőintenzitás / erdőév**

A makadám külön ága ugyanazon szabály alatt látszik. Mezők és műveletek kereshető
választókból, feltétel összehasonlító kártyából, számok egységgel. A képlet haladó
nézet; a szokásos módosításhoz ne kelljen gépelni. Minden kártyán elérhető az angol név.

Az első felületi próba kérdése: egy nem programozó tudja-e segítség nélkül növelni
az eső hatását, és megmagyarázni, miért romlott egy kijelölt út? Ezt közösen érdemes
értékelni, mielőtt az összes szabályra kiterjesztjük a felületet.

### Javasolt felderíthető adatok — még nem létező API

| Angol név | Jelentés | Hozzáférés |
| --- | --- | --- |
| `road.condition` | Útállapot, 0–1 | olvasás, ellenőrzött írás |
| `road.surface` | Asphalt / Macadam | olvasás, felsorolás |
| `road.tile.moisture` | A jelenlegi hidrológiai térkép nedvessége, 0–1 | kapcsolat, olvasás |
| `world.rainIntensity` | A jelenlegi `RainRate / 20`, 0–1-re korlátozva | olvasás |
| `deltaYears` | Az aktuális szabálylépés erdőévben | futtatói bemenet |

A road hatókörön belüli képletben `condition`, `surface`, `tile.moisture` szerepel.
A négy meglévő tuningparamétert megtartjuk; a nedvességi szorzók is szerkeszthető
szabályrészek lesznek. Ne maradjon a teljes romlási képlet rejtett C# getterben.

### Végrehajtható első munkacsomag

1. Minimális `IRoad` nézet és a szükséges környezeti kapcsolat; séma által generált választók.
2. Angol idő- és hangolási nevek, a régi projektek kompatibilis beolvasásával.
3. Feltételkártya és éves csökkenés szerkesztése; a haladó képlet ugyanarra a modellre fordul.
   A felület által nem ábrázolható régi képlet maradjon veszteségmentesen haladó nézetben.
4. `road.weather` szabályadat; a régi hook támogatott gráfjainak átalakítása, a nem
   átalakítható esetek tételes diagnosztikája. Csendes eldobás vagy dupla futás tilos.
5. Natív útromlás kiváltása ugyanabban az időzítési pontban. Az általános futtató
   későbbi leválasztása az útfrissítésről külön, viselkedést megőrző változtatás legyen.
6. Kijelölt útra magyarázat: bemenetek, feltétel eredménye, ráta, eltelt idő,
   állapot előtte/utána, korlátozás és a ható szabályok sorrendje.

Konkrét ellenőrző példa: aszfalt, `wet = 0.5`, `rain = 0.25`, `deltaYears = 0.1`.
A ráta `0.015 * 1.5 + 0.08 * 0.25 = 0.0425`; az állapot `1`-ről `0.99575`-re változik.

Elfogadási feltételek:

- Natív–szabály összehasonlítás mindkét burkolattal, száraz és nedves széleken,
  több időlépéssel, nem alapértelmezett tuninggal és alsó állapotkorláttal; dokumentált float tolerancia.
- Kikapcsolva nincs időjárási kopás; a forgalmi kopás továbbra is külön működik.
- Az editorban végzett módosítás mentés és játékba alkalmazás után ugyanazt adja.
- Régi hookos projekt, szabály nélküli projekt és módosított új projekt betöltése tesztelt.
- Mentésből folytatás megőrzi a frissítési időmaradékot és az eredményt.
- Ugyanabból a checkpointból az editor és a játék azonos állapotváltozást mér.
- Mérjük a teljes szabálylépés idejét és allokációját reprezentatív úthálózaton;
  reflexió/fordítás ne történjen objektumonként minden lépésben.

## A teljes játék átállási sorrendje

| Szakasz | Tartalom | Kilépési feltétel |
| --- | --- | --- |
| 1 | A fenti útromlási minta és a kártyás szerkesztés | Nem programozóval kipróbált felület, viselkedéshű futás |
| 2 | Trail/tile kézi regiszterek, angol nevek, meglévő tönkszabály rendbetétele | Egységes séma és verziózott névmigráció |
| 3 | Járműkopás, szerviz, javítás | Stabil példányazonosítók, típusos mezők, műveleti paraméterek |
| 4 | Gazdaság, szállítás, bevétel és költség | Események, pontosan egyszeri elszámolás, atomikus átadás |
| 5 | Faegészség, elhalás, növekedés, versengés, újulat | Havi ütemezés, stabil faazonosság, pillanatkép → számítás → alkalmazás |
| 6 | Talaj, hó, vízáramlás és időjárási folyamatok | Egységellenőrzés, készletátadás, megmaradási és időlépés-tesztek |
| 7 | Gépek, állatok és flották döntései | Eseményekkel integrált állapotgépek, követhető döntések |

Az elején a katalógus minden bejegyzését végrehajtási helyhez kell rendelni, majd
jelölni: csak leírt / paraméterezhető / részben szerkeszthető / teljesen adatból fut / motorfeladat.
A katalógusban szereplés önmagában nem bizonyít teljességet: a világfrissítés,
ökológia, logisztika és parancskezelés ágait is végig kell leltározni.
Minden migráció kapjon külön ekvivalenciatesztet és kivezetett natív belépési pontot.

A játékdöntések és hangolható összefüggések kerüljenek az editorba. A GPU-kezelés,
mentésformátum, útkereső algoritmus és fizikai megoldó mechanikája maradhat C#.
Ezek játékmeneti paraméterei és alkalmazási feltételei ettől még szerkeszthetők legyenek.

## Ellenőrzési állapot

Forráskód- és meglévőteszt-átvizsgálat történt. A célzott `RuleObjectModelTests` és
`RuleBookTests` futtatása `--no-restore` mellett nem jutott el a tesztvégrehajtásig:
`NETSDK1127` (hiányzó targeting pack), illetve `NETSDK1004` (hiányzó RuleCompiler
assets fájl). Ez nem teszteredmény. A megvalósítás előtt restore és sikeres alapteszt szükséges.
A grafikus editor használhatóságát ebben a körben nem ellenőriztük élő felületen.
