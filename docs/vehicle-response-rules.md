# Járművek és fenntartás: az 5. csomag első része

Az 5/a csomag 17 projektben mentett, közös válaszgráfra viszi a tömeget,
gördülést, menetellenállást, vonóerőt, alap motorfogyasztást, lejtő-/kanyar-
és útállapot-/megállási sebességválaszt, valamint a közös járműfenntartást.
A nyolc lezárt katalógustétel: `vehicle.mass`, `vehicle.resistance`,
`vehicle.motion`, `vehicle.fuel`, `upkeep.wear`, `upkeep.effects`,
`upkeep.repair`, `upkeep.service`.

Ez **nem a teljes 5. csomag lezárása**. A rakodás, a gépek munkafázisai,
a gépi munkaválasztás, az utasítások feltételei és a malom feldolgozása az 5/b
része. Ezek részleges folyamatkapcsolatai addig megmaradnak; a leltár nem
állítja róluk, hogy teljesen átvezetettek.

## Szerkesztés és tényleges futás

A Szabályok lap tömör, pipás listájából nyithatók meg. A meglévő közös
gráfvászon, objektum → mező választó, görbecsaládok, számbemenetek és csúszkák
működnek rajtuk. A kódazonosítók angolok, például `vehicle.cargo`,
`vehicle.wear`, `road.damage`, `spec.power`, `step.seconds`; a mezőknek saját
megjelenítési neve és súgója van a kódból felderített sémában.

| Gráf | Mit ad vissza? | Ki használja? |
| --- | --- | --- |
| Jármű és rakomány tömege | kg | Menetellenállás, gyorsulás, áthaladási kopás |
| Útfelület gördülési válasza | Gördülési tényező | Menetellenállás |
| Jármű menetellenállása | Előjeles erő, N | Fizikai integrátor |
| Rendelkezésre álló vonóerő | N | Gyorsítás és sebességtartás |
| Motor üzemanyag-fogyasztása | Az al-lépés literei | Fogyasztásmérő és költségelszámolás |
| Lejtő és kanyar sebességválasza | csempe/s | Útállapot- és megállási sebességválasz |
| Útállapot és megállás sebességhatása | csempe/s | Fizikai integrátor célsebessége |
| Rakomány és terep terhelése | Terhelési szorzó | Teherautók és gépek kopása/meghibásodása |
| Munkavégzés kopása | Kopásnövekmény | Tényleges járműállapot |
| Meghibásodási esély | 0–1 | A jármű saját mentett sorsolása |
| Kopás fogyasztási hatása | Fogyasztási szorzó | Gépi fogyasztás és teherautó-költség |
| Kopás munkatempó-hatása | Pozitív tempószorzó | Erdészeti gépek munkalépése |
| Helyszíni javítás ideje | Másodperc | Meghibásodáskor rögzített visszaszámláló |
| Helyszíni javítás kopásenyhítése | Elengedett kopás | Megjavított jármű/gép |
| Helyszíni javítás költsége | eFt | Befejezett javítás könyvelése |
| Telephelyi karbantartás kopásenyhítése | Elengedett kopás | Parkoló, nem hibás jármű/gép |
| Telephelyi karbantartás költsége | eFt | Tényleges karbantartási lépés könyvelése |

Példa képlet nélkül: a tömeggráfban válassz Jármű → Rakomány forrást, köss
rá közös görbét, majd a görbe kimenetét állítsd a tömeg eredményére.
A tesztpélda 0 m³-nél 10 000 kg-ot, 20 m³-nél 30 000 kg-ot ad; 10 m³-nél
a tényleges fizikai tömeg 20 000 kg. Ugyanígy kopásból görbével szerkeszthető
a meghibásodási esély vagy a fogyasztási szorzó.

A pipa kivétele a választott fix eredményt adja, és megőrzi a gráfot.
A kártya elmondja, hogy ez mit jelent: például 0 kopásnövekmény mellett nincs
új kopás, 0 hibavalószínűségnél nem indul új meghibásodás, a már megkezdett
javítás azonban folytatódik. A javítási idő módosítása az ezután kezdődő
javításokra hat; a már mentett visszaszámlálót nem írja át.

## A motor határa

Nincs külön natív tömeg-, gördülési, vonóerő-, fogyasztási vagy kopásképlet.
Az alapdefiníciókat a `VehicleResponseRules` állítja elő; futáskor a projektből
fordított `VehicleRuleProgram` adja az értékeket. A járműfizika méterben
integrálja a gráfok által meghatározott erőket és célsebességet. A lejtő
szinusza/koszinusza, a sarok felismerése, az út menti helyzet és a sebesség
numerikus integrálása geometriai/motorfeladat.

A kopás és a tömeg fizikai állapotkorlátai, a rendelkezésre álló kopás
korlátozása, a visszaszámláló csökkentése, a sorsolási generátor és a költség
összegzése motorfeladat marad. A sorsolás üzemelő lépésenként egy számot
fogyaszt akkor is, ha a gráf 0 esélyt ad. A javítás alatt nem fogyaszt új
meghibásodási számot. A sorrend: kopásnövekmény → új kopás → esély → sorsolás;
javítás végén költség → kopásenyhítés. A karbantartás ára a ténylegesen
enyhített kopást olvassa. A kopás munkatempó-válasza az erdészeti gépekre
vonatkozik; nem állítjuk, hogy külön teherautó-kopási sebességhatást vezettünk be.

A tömeg pozitív minimuma védi az integrátort. A tempó pozitív minimuma védi
a gépi munkára jutó fogyasztás osztását. A javítási idő felső határa a mentett
állapot meglévő 1200 s korlátjával egyezik. Hibás aritmetikánál a kiértékelő
hibát jelez és a nemnegatív biztonságos alsó értéket adja; nincs visszaváltás
régi natív képletre.

## Szeletelés és mentés

A gráfok legfeljebb 256 műveletesek; ugyanazt a közös görbefordítót használják.
A teherautók feldolgozása járművenként, a gépeké gépenként folytatható.
A fizikai al-lépés legfeljebb 1/30 jármű-s; az interaktív világóra és a
járműidő felső korlátja határolja az egy járműre jutó integrációt. Nem került
be teljes flottát szinkron végigfuttató befejezési ág.

A teherautók az őket birtokló `VehicleSystem` aktuális, közös specifikációját
olvassák. Emiatt a projektcsere nem másolja végig az új adatokat minden
teherautóra. A visszatöltés a meglévő járművenkénti munkában köti vissza a
közös adatforrást. Önálló tesztjárműnek saját specifikációja lehet.

A járműszabályok változása nem indít teljes erdei rátafrissítést; az erdei
függőségi aláírás csak az ökológiai gráfokat tartalmazza. A gráf, a kopás,
a javítási idő, a hibaszámláló és a sorsolási állapot a meglévő projekt- és
világmentés részei. A fordított program és a munkaiterátor nem kerül a mentésbe.
A mentés továbbra is teljes tick/parancshatárra vár.

Az általános alapadatok nem fognak meg egy globálisan megosztott, módosítható
kiértékelőt: az alapértelmezett program szálankénti, a világ saját programja
világonkénti. Ezt párhuzamos alapértelmezett futások is ellenőrzik.

A futtató verziója `forestycoon-simulation/2026-10-16.12`. A frissen rögzített
nyitóvilágnál csak a `Behaviors` lenyomat változott; a többi kezdeti állapot
egyezik. Nincs korábbi járműképletet fenntartó betöltési ág.

## Ellenőrzések

`VehicleResponseRuleTests`: felderített objektumok/mezők; közös rakománygörbe;
valódi erő-, fogyasztás-, javítás- és karbantartási eredmények; párhuzamos
alapértelmezett használat; mozgó jármű projektcseréje; teljes checkpoint,
szeletelt folytatás, függő projektmentés és csak naplóból visszajátszás.
`FleetWorkTests`: közös szerkesztett kopás dolgozó processzoron és
teherautón, azonos teljes és szeletelt logisztikai/járműállapottal.
A meglévő fizikai, fenntartási, rakodási és flottatesztek is futnak.

Az editor füsttesztje a `vehicle-mass-graph.png`, `vehicle-fuel-graph.png`,
`vehicle-breakdown-graph.png`, `vehicle-repair-graph.png` képeket is elkészíti.
A `--vehicle-response-benchmark` 1000 és 10 000 fizikai teherautót futtat;
30 tickből az első három bemelegítés. A teljes és 64 egységes folytatható
futás végén a teljes jármű-checkpointot hasonlítja össze. Ez CPU-szimulációs
mérés, nem rajzolási vagy teljes képkocka-mérés.

### Mért eredmény és ellenőrzés

2026-10-16, Intel Core i7-8700 @ 3.20 GHz, Release. Egy-egy helyi futás;
a fizikai terheléspróba azonos kétszakaszos geometriát használ, útkeresés és
rajzolás nélkül. A bemelegítés nem zár ki minden későbbi JIT-/GC-/ütemezési
kiugrást. A két járműszám egy folyamatban, egymás után futott.

| Fizikai teherautó | Teljes tick p95 / p99 / max (ms) | 64 egységes szelet p95 / p99 / max (ms) | Teljes járműállapot |
| --- | ---: | ---: | --- |
| 1000 | 2,428 / 6,139 / 6,139 | 0,141 / 0,203 / 3,083 | Bitre azonos |
| 10 000 | 7,400 / 7,434 / 7,434 | 0,046 / 0,057 / 0,189 | Bitre azonos |

A külön nagy pályás hostpróba 900 s/évvel, 120 szimulációs másodpercig,
4 ms-os CPU-kerettel és virtuális 60 Hz-es időbeérkezéssel, erdőhónap-határon át:

| Pálya / fák | Host CPU p95 / p99 / max (ms) | Csúcshátralék / végi hátralék (szimuláció-s) |
| --- | ---: | ---: |
| 64² / 15 795 | 4,031 / 4,086 / 17,891 | 2,6000 / 0,0333 |
| 128² / 65 086 | 4,059 / 4,080 / 12,913 | 7,0667 / 0,0333 |

Ez utóbbi erdő- és szabályterheléses világpróba; nem a 10 000 teherautós
esetet futtatja a pályán. A 128² csúcshátraléka az előző 7,05 s-hoz képest
egy logikai tick felével több; lényegében ugyanaz a fennmaradó hónaphatári
kapacitáskorlát. Nem volt eldobott idő vagy extra ledolgozó képkocka.
A maximális hostminták továbbra is meghaladhatják a 4 ms-os célkeretet.

A teljes Release csomag **1925/1925** teszttel sikeres
(`artifacts/vehicle-response-full-tests.txt`). A beágyazott és önálló editor
füsttesztje átment (`artifacts/vehicle-response-smoke.txt`). A fordítás 0 hibával,
0 figyelmeztetéssel és a leltár `--check` ellenőrzése sikeres.
Nyers mérési eredmények: `artifacts/vehicle-response-benchmark.txt`,
`artifacts/vehicle-response-host-benchmark.txt`. Reprodukció:
`--vehicle-response-benchmark`, illetve `--rule-host-benchmark`.
