# UI/UX terv – „Erdei csend”

Látványterv (6 tábla): https://claude.ai/artifact/REr848rNGNybEBFHWsgNqQ

## 1. Hangulat és alapelvek

A ForesTycoon erdőgazdálkodás: lassú folyamatok, évszakok, évtizedek. A felület ezt a tempót
közvetítse – **nyugalom, áttekinthetőség, gondoskodás** –, a Transport Tycoon-örökséget (eszköztár,
térkép, gazdasági visszajelzés) pedig csendesebb, természetközeli formában vigye tovább.

1. **A dioráma a főszereplő.** A felület a széleken él, áttetsző „nyírpapír” panelekben; nyugalmi
   állapotban a képernyő legfeljebb ~25%-át, nyitott paneleknél is legfeljebb 40%-át takarja.
2. **Igék, nem eszközök.** A játékos négy dolgot tesz: **Megfigyel – Gondoz – Termel – Formál**.
   Ez az eszköztár szerkezete (ma 22 egyenrangú ikon).
3. **Ok és következmény, emberi nyelven.** Minden diagnózis lánc („kevés eső → homoktalaj →
   sűrű állomány párologtat”), minden beavatkozás előrejelzéssel jár.
4. **Halk értesítések.** Négy halkságfok: suttogás (napló) → jelölő (térkép) → kártya → megállás
   (csak évszakváltás és nagy veszteség).
5. **Az idő ünnep.** Az évszakváltás „szusszanó” pillanat: összefoglaló, tanulság, előretekintés.
6. **Akadálymentes alapból:** ≥44 px célterületek, 4,5:1 kontraszt, színvak-barát skálák
   (bíbor–krém–zöld, barna–homok–kék), minden színkód mellett ikon vagy szöveg.

## 2. A játékhurok és a felület

| Hurok lépése | Kérdés | Felület |
|---|---|---|
| Megfigyelés | Hogy van az erdő? | Erdőállapot-mutató, erdőnapló, térképjelölők, **Térképasztal** lencsékkel (víz, talaj, egészség, állomány, teendők) |
| Megértés | Miért? | **Erdőrészlet-panel**: vitalitás, víz, fény, növőtér; ok-okozati lánc |
| Döntés | Mit tegyek? | Javasolt beavatkozás-kártyák költséggel, hatással, időtartammal; 10 éves előrevetítés |
| Cselekvés | Hogyan? | Igék szerinti dokk; ültetésnél fafajkártyák termőhelyi illeszkedéssel és elegyítéssel |
| Várakozás | Mi történik? | Tempóvezérlő, évszakkerék, lágy évszakos fényátmenet |
| Visszajelzés | Megérte? | Évszak-összefoglaló (növedék, faanyag, bevétel, erdőállapot), napló |

## 3. Képernyőszerkezet (fő játékképernyő)

- **Bal fent – Birtokkártya:** birtoknév, évszakkerék (4 évszak színe, jelölő az aktuális időponton),
  dátum, időjárás. Mögötte a játékmenü.
- **Jobb fent – Mérlegsáv:** egyenleg (gazdasági modul után), élőfakészlet, erdőállapot-gyűrű
  trendszöveggel („javul”).
- **Bal oldalt – Erdőnapló:** az utolsó 3 bejegyzés; kattintásra az érintett erdőrészlet.
- **Lent középen – Igedokk:** Megfigyel (Q), Gondoz (W), Termel (E), Formál (R). A kiválasztott ige
  fölé nyílik az alfunkció-sor (pl. Gondoz → Ültetés, Gyérítés, Egészségügyi, Alátelepítés).
- **Bal lent – Tempó:** szünet, 1×, 2×, „évszak ›” (ugrás a következő évszak elejére).
- **Jobb lent – Birtoktérkép:** a Teendők lencse kicsiben; kattintásra kameraugrás, M: Térképasztal.
- **A világban:** lüktető borostyán gyűrű és címke a figyelmet kérő erdőrészleteken.

Ami kikerül a fő képernyőről: fejlesztői eszközök (csak F12), grafikai beállítások (játékmenü →
Beállítások), Környezet-ablak (tartalma a Térképasztal Víz/Talaj lencséjébe és az erdőrészlet-panelbe olvad).

## 4. Fő folyamatok

- **Figyelmeztetéstől a beavatkozásig (≤3 kattintás):** napló vagy térképjelölő → Erdőrészlet-panel
  (Mi történik itt?) → javasolt kártya → a megfelelő eszköz előre kijelölt területtel.
- **Ültetés:** Gondoz → Ültetés → terület húzása → fafajkártyák illeszkedés szerint rendezve (a
  rossz illeszkedés agyagszínű, indoklással) → elegyarány → Telepítés.
- **Kitermelés:** Termel → Vágás → terület → kártya: kinyerhető m³, várható bevétel, hatás az
  erdőállapotra és a tájra; felújítási kötelezettség jelzése.
- **Térképasztal:** Q vagy M; 1–5 lencse, Tab alréteg, V vetítés a 3D erdőre, Esc vissza.
  Erdőrészlet-határok, helynevek dőlt Fraunces-szel, lépték és északjel – régi üzemi térkép hangulat.

## 5. Vizuális nyelv

- **Paletta:** nyírpapír #F3EFE4, erdőtinta #23302A, mélymoha #2F4F33 (elsődleges), moha #4F7A4A,
  borostyán #C98A2E (figyelem), patakvíz #4C86A8, kéreg #7A5A3E, agyag #B4553F (veszteség).
  Évszakos kiemelőszín: tavasz #8DB45A, nyár #3F7A3A, ősz #C7782F, tél #7F9DB0.
- **Betűk:** Fraunces (címek, helynevek, elbeszélő dőlt mondatok), Figtree (adat, gomb, szám).
  Magyar ékezetek: mindkettő tartalmazza a Latin Extended-A tartományt.
- **Panelek:** áttetsző (≈90%) papírszín, 18 px sarok, lágy árnyék, 10 px háttérelmosás.
- **Mozgás:** 240 ms kiúszás, rugózás nélkül; 2 s évszakos fényátmenet; csillapított kamerarepülés.
- **Hang:** évszak- és időjárásfüggő háttérhang (szél, madár, eső); halk fakoppanás kattintásra.

## 6. Megvalósítás a mostani motorban (ImGui)

1. **Téma:** `HudTheme` átszínezése a fenti palettára (világos panelek, sötét tinta), Fraunces és
   Figtree TTF betöltése az ImGui fontatlaszba (a meglévő magyar glifkészlettel).
2. **Igedokk:** a 22 ikonos eszköztár helyett 4 ige + alfunkció-sor; a fejlesztői és grafikai
   gombok a játékmenübe/F12 mögé.
3. **Erdőrészlet-panel:** a `ManagementView` panelje kiválasztásra a jobb szélre csúszik; a
   `ForestManagementSurvey` adja a mérőket és a diagnózist, az ok-okozati lánc a szabályokból jön.
4. **Erdőnapló:** a toastok helyett napló-szolgáltatás halkságfokkal; a jelölőket a
   `DrawManagementMarkers` mintájára rajzoljuk.
5. **Évszakváltás:** az évszakhatáron az idő megáll, az összefoglaló a szimuláció havi/éves
   mutatóiból készül (`LastAnnualForestGrowth`, leszállított faanyag, erdőállapot).
6. **Térképasztal:** a Teendők/lencse-nézet teljes képernyős változata erdőrészlet-határokkal.

Feltétel a teljes tervhez: **gazdasági modul** (egyenleg, költségek, árak) – a táblákon szögletes
zárójeles helyőrzők jelölik ([EGYENLEG], [KÖLTSÉG], [BEVÉTEL]); és **erdőrészletek** (csempék
csoportosítása elnevezett egységekbe), amelyre a napló, a térkép és a panel épít.
