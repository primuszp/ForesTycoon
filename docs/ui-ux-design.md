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

- **Paletta – „erdészház”, a mostani menüszínvilág finomhangolva** (`HudTheme`):
  panel #141A16 (90% fedés, a dioráma átdereng; előtte #161C18 94%), világos panel #1D2620,
  pergamen szöveg #EAE4CF, halvány szöveg #A3AA96, moha #5C8045 (marad), világos moha #7DA45B,
  mézborostyán #E6B35A az aktív eszközre és a figyelemre (előtte a neonosabb #F0BA4F), halk címsor
  #212C1E (előtte #304529), jó #94CF7C, veszteség agyagszínű #E2836B (előtte #FF735C), info/víz #94C0E0.
  A Térképasztal sötét „íróasztal” (#0F1411) világos pergamen térképpel.
  Évszakos kiemelőszín (évszakkerék, évszakkártya): tavasz #8DB45A, nyár #3F7A3A, ősz #C7782F, tél #7F9DB0.
- **Betűk:** Fraunces (címek, helynevek, elbeszélő dőlt mondatok), Figtree (adat, gomb, szám).
  Magyar ékezetek: mindkettő tartalmazza a Latin Extended-A tartományt.
- **Panelek:** sötét lucfenyő, 90% fedés, 10 px sarok, 1 px moha hajszálkeret, aktív elem mézborostyán belső kerettel.
- **Mozgás:** 240 ms kiúszás, rugózás nélkül; 2 s évszakos fényátmenet; csillapított kamerarepülés.
- **Hang:** évszak- és időjárásfüggő háttérhang (szél, madár, eső); halk fakoppanás kattintásra.

## 6. Megvalósítás a mostani motorban (ImGui)

1. **Téma:** a `HudTheme` finomhangolása kész (2026-10-08); hátravan a Fraunces és
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

## 7. Állapot (2026-10-08)

Beépítve a játékba (`ForesTycoon/App/Viewport.Lodge.cs`), az erdészház-színekkel:

- Birtokkártya évszakkerékkel (az aktuális évszak íve erős, a jelölő az év pontos helyén), dátum és időjárás; mögötte a játékmenü.
- Mérlegsáv: élőfakészlet, leszállított faanyag, erdőállapot-gyűrű; alatta az ablakgombok (járművek, erdészet, környezet, grafika, kamera, súgó). A fejlesztői eszközök csak F12-re.
- Erdőnapló a bal oldalon (a toastok helyett): az új bejegyzés röviden borostyánban dereng, aztán a naplóban marad; összecsukható.
- Tempó bal lent: szünet, 1×, 2×, gyorsítómenü és az évszak haladása.
- Igedokk: Megfigyel (Q), Gondoz (W), Termel (E), Formál (R); fölötte az ige eszközei és az aktív eszköz beállításai (pl. fafajválasztó). A dokk a két alsó sarokpanel között középen ül.
- Birtoktérkép jobb lent (Teendők lencse kicsiben), kattintásra kamerarepülés.
- Évszakkártya évszakváltáskor: az elmúlt évszak (készletváltozás, leszállított faanyag, erdőállapot) és évszakos tanács; megállítja az időt, kikapcsolható; nagy gyorsításnál, felvételnél és füstpróbán nem jelenik meg.
- Talpas címbetű (Georgia, ha elérhető) a birtok, a napló és az évszakkártya címeihez.
- Gyorsbillentyű-változás: a Környezet ablak K-ra került (E most a Termel ige).
