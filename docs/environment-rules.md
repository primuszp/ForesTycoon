# Környezeti szabályok a közös gráfszerkesztőben

A 4. csomag hat katalógustétele (`weather.events`, `weather.evaporation`,
`climate.regional`, `forest.canopy`, `water.snowdrift`, `trail.trafficability`)
31 projektben tárolt válaszgráfból fut. Az alapgráfok forrása
`ForesTycoon.Rules/EnvironmentRules.cs`. A játék a projektből fordított gráfokat
hívja; nincs mellettük natív időjárási, lombvíz-, hóhordási vagy járhatósági képlet.

## Szerkesztés

A Szabályok lap közös, pipás listájában vannak. A forrásnál előbb objektumot
(Naptár, Időjárási esemény, Levegő, Régió, Csempe, Talaj, Lombkorona, Fa,
Fafaj, Szomszéd, Hó, Lépés), majd mezőt választasz. A kódbeli azonosítók
angolok, például `weather.temperature`, `tile.snow`, `soil.bearingCapacity`.
Ezek a gráfok is a közös művelet-, görbe-, szám- és csúszkaszerkesztőt használják.
Nem szükséges képletet írni.

A „Mikor fut, mire hat, ki használja?” rész leírja a lekérdezés időpontját,
a ténylegesen bekötött bemeneteket, az eredményt és annak felhasználóit.
A pipa kivételekor meg kell adni a visszaadandó fix értéket; a régi gráf
megmarad és visszakapcsolható. Például a hóhordási hányad 0 értéke helyben
hagyja a havat; a helyi csapadékszorzó 0 értéke kizárja a csempét a csapadékból.
Az induló időjárás gráfjai csak új világ létrehozásakor hatnak.

## A számítás útja

| Csoport | Gráfok és eredményük | Motorfeladat |
| --- | --- | --- |
| Időjárási események | Induló típus/idő/erősség; következő típus/idő/erősség; kért időjárás alapideje/erőssége; csapadék rámpája | Seedelt sorsolási számok, óra, típus egészre kerekítése, évszakhatár, a lineáris csapadékszakaszok pontos integrálása |
| Légkör | Hőmérséklet, páratartalom, szél, felhőátmenet, sugárzás, párologtató képesség | A naptár évszakos szinusz-bemenete; az időjárási pillanatkép továbbítása |
| Helyi klíma | Hőmérséklet, páratartalom, csapadékszorzó | Seedelt, sima régióminta; a klímakatalógus kontrasztjai; csempénkénti gyorsítótár |
| Lombkorona | Fa koronaterülete, vízfogása, lombfelülete; összegzett borítás, vízkapacitás, lombfelületi index | Fák mért mérete és egészsége, csempeterület, rögzített sorrendű összegzés |
| Hóhordás | Szomszéd iránypontszáma, szélvédettség, visszatartott hó, elmozduló hányad | Legfeljebb négy szomszéd; legnagyobb pozitív pontszám választása; készletkorlát; késleltetett, konzervatív publikálás |
| Járhatóság | Talaj teherbírási válasza, átnedvesedés, járhatóság | Talajtípus és vízkészletek olvasása; az eredmény átadása a járműveknek és az áthaladási kopásnak |

A külön kért eseményidő és intenzitás továbbra is felülírja a megfelelő
alapértéket. A következő esemény ideje az évszakhatárig tart. A pozitív
minimális eseményidő és a legfeljebb fél esemény hosszúságú rámpa az idővonal
előrehaladását védi, nem biológiai vagy időjárási döntés.

A száraz események nem fogyasztanak csapadékintenzitási véletlenszámot.
A sorsolási stream ugyanúgy mentett, mint korábban. A gráf nem hívhat
véletlengenerátort, fájlt, órát vagy másik élő világot.

## Szeletelés, mentés és visszajátszás

A környezeti munka csempénként, a lombok összegzése fánként adja vissza a
vezérlést. A hófluxus nullázása, forrásvizsgálata, ellenőrzése és publikálása
is csempénként szeletelt. A beérkezett hó csak a következő környezeti
lépésben szállítható tovább; zárt pályaszélen megmarad.

A gráfok és a sorsolási állapot a meglévő projekt- és világmentés részei.
Félkész logikai tick közben továbbra is a teljes tick határára vár a mentés.
Nincs szerializált iterator vagy eltérő, gyorsított visszajátszó számítás.
A helyi csapadék tényleges szorzója a vízmérlegben is megjelenik, egyenletes
klímájú régi térképen is.

A környezeti gráfok cseréjekor a helyi klíma és a lombadatok gyorsítótára
érvénytelenedik. Mentés betöltésekor a már mentett lombrevízió megmarad;
a futtató bekötése önmagában nem módosítja a visszaállított állapotot.

A futtató verziója `forestycoon-simulation/2026-10-16.11`. A szállított
nyitójelenet frissített gráfokat tartalmaz; nincs a korábbi natív számításokat
fenntartó futtatási ág.

## Ellenőrzés

`EnvironmentRuleTests`: valódi mezők és világfutás, hőmérsékletből szerkesztett
párolgási görbe, induló és következő időjárás, explicit parancsértékek,
csapadékintegrálás, helyi klíma, lombcache-csere, hóirány megfordítása,
vízmegmaradás és kaszkádmentes átrendezés. A teljes checkpoint 1, 7 és 64
munkadarabos szeleteknél, mentés/betöltés és csak naplóból visszajátszás után
is egyezik. A meglevő szeletelési tesztek a tickhatárra halasztott mentést és
projektcserét is ellenőrzik.

Valódi editor füstteszt: új időjárás-, klíma-, lomb-, hó- és járhatósági
képernyőképek az `artifacts/rule-editor/environment-*-graph.png` fájlokban.
A nagy pályás CPU-mérés a `--rule-benchmark` parancs: 64²/128², eső/hó,
teljes és 64 egységes futás pontos állapotegyezése, majd 4 ms-os hostmérés.
A havi lemaradás korábban azonosított problémáját ez a szabályátvezetés
önmagában nem oldja meg.

### Mért eredmény

2026-10-16, Intel Core i7-8700 @ 3.20 GHz, Release, ablak/GPU/rajzolás nélkül.
Egy-egy helyi futás, nem hardverfüggetlen időgarancia. A gyorsított év 120 s;
a mérés erdőhónap-határt is tartalmaz.

| Pálya / időjárás | 64 egységes szelet p95 / p99 / max (ms) | Teljes és szeletelt checkpoint |
| --- | ---: | --- |
| 64² / eső | 0,058 / 0,090 / 1,867 | Bitre azonos |
| 64² / hó | 0,038 / 0,082 / 2,313 | Bitre azonos |
| 128² / eső | 0,045 / 0,090 / 6,031 | Bitre azonos |
| 128² / hó | 0,036 / 0,084 / 3,516 | Bitre azonos |

Az alaptempós külön hostmérés 900 s/évvel, 120 szimulációs másodpercig,
4 ms-os CPU-kerettel és virtuális 60 Hz-es időbeérkezéssel futott:

| Pálya | Host CPU p95 / p99 / max (ms) | Csúcshátralék / mérés végi hátralék (szimuláció-s) |
| --- | ---: | ---: |
| 64², 15 795 fa | 4,034 / 4,090 / 16,223 | 2,5167 / 0,0333 |
| 128², 65 086 fa | 4,061 / 4,081 / 13,048 | 7,0500 / 0,0333 |

A csúcshátralék mindkét méreten az előző motorcsomagéval egyezik. Nem volt
eldobott idő vagy extra ledolgozó képkocka. A legrosszabb CPU-minták a 4 ms-os
hostkeretet meghaladják; a mérés nem választja szét a JIT, GC és rendszerütemezés
hatását. A rövid szeletek mellett továbbra is van hónaphatári szimulációs késés.
A gyorsított, rövidebb hostpróbán a 128² pálya 8,0333 s hátralékkal zárt,
amit időbeérkezés nélkül külön ledolgozott; ez sem elvesztett szimulációs idő.

Nyers eredmények: `artifacts/environment-rule-benchmark.txt`,
`artifacts/environment-host-benchmark.txt`. A teljes Release tesztcsomag
**1918/1918** sikeres (`artifacts/environment-final-full-tests.txt`), a javítások
célzott csomagja **53/53**, a félkész hópublikálás visszatöltési próbája **1/1**.
A beágyazott és önálló editor füsttesztje is átment
(`artifacts/environment-final-smoke.txt`), a fordítás figyelmeztetés és hiba nélkül
sikeres. A lefedettségi leltár `--check` ellenőrzése is sikeres.
