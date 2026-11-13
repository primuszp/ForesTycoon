# Hosszú szimuláció és tesztelakadás, 2026-11-13

## Elakadás: bizonyíték és javítás

A korábbi, 625 teszt után megállt folyamat natív mintájában egy szál a
`ThreadSuspend::SuspendAllThreads` belsejében várakozik, a többi szál nagy
része `RareDisablePreemptiveGC` / `GCEvent::Wait` állapotban áll, egy szál
pedig menedzselt kódot futtat. Ez GC-felfüggesztési elakadás, nem egyszerűen
lassú teszt. A függő GC azt is megmagyarázza, miért nem válaszolt a
menedzselt veremlekérdezés.

A [dotnet/runtime #134669 hibajegy](https://github.com/dotnet/runtime/issues/134669)
ugyanezt a macOS-hibaképet és elvesző aktiválási jelhez kapcsolódó okot írja le.
Az érintett folyamatok gyermekfolyamatokat is indítanak. A régi minta JIT-címei
nem szimbolizáltak: az eredeti futás konkrét C# metódusát és jelkezelő-flageit
utólag nem lehet bizonyítani. A GC-elakadást közvetlenül mutatja a napló;
az elveszett jel mechanizmusának hozzárendelése a hibajegy és a reprodukció
alapján erősen alátámasztott következtetés.

Önálló, a projekt várakozó iterátorának mintáját használó reprodukció:
memóriát foglaló háttérfeladat, az iterátort szinkron módon fogyasztó ciklus,
valamint `/usr/bin/true` gyermekfolyamatok indítása. A telepített **.NET 10.0.15,
macOS ARM64** alatt a régi minta **1,2 másodperc után** megállt. A rögzített
natív minta ismét GC-felfüggesztést mutatott. A `Thread.Sleep(0)` átadást
használó változat 30 másodpercig, több mint 6600 körön át befejeződött.

A javítás közös tulajdonosa a `SimulationWork.TaskPending`. A még futó
feladat lekérdezésekor átadja a szálat; az iterátor `yield` önmagában ezt nem
teszi meg. Nem vár meg egy teljes háttérfeladatot az interaktív képkockában.
A hiba/kivétel megfigyelése a tulajdonosnál marad, így a lekérdezési köteg
előbb minden olvasót bevár, és csak utána adja tovább az első hibát.

Átvezetett utak: lekérdezési kötegek, szabályfordítás, viselkedésfordítás,
világgenerálás, jelenet-/mentésbetöltés, procedurális felület és famodell.
A valódi `SlicedQueryBatch` 60 másodperces próbája **8700 körben 17400 feladatot**
fejezett be allokációs és gyermekfolyamat-terhelés mellett. Ez alkalmazásoldali
védelem; nem a telepített .NET futtató hibájának javítása.

## A hosszú futásban talált anyagmérleghiba

Az érett erdőt, harvestert, forwardert, teherautót és malmot futtató teszt a
hatodik hónap mentését nem tudta visszatölteni: `Invalid checkpoint: mill mass balance`.
A malom `Received`, `Stock`, `Processed` mezői egyszeres pontosságúak voltak.
Sok apró készletlevonás és összegzés eltérő kerekítési hibát halmozott fel.

A rövid reprodukáló teszt 10000 m³ induló készletet 12000 darab 1/30 s-os
feldolgozási lépésen vezet át: a javítás előtt **5,47110748 m³** mérleghiba
keletkezett. A mezők, a feldolgozási mennyiség és a checkpointmezők most
kettős pontosságúak. A mentés eredeti `1e-5` relatív anyagmérleg-korlátja
változatlan. A regresszió szigorúbb, `1e-6 m³` abszolút hibát enged meg,
és visszatöltést, valamint szándékosan elrontott mérleg elutasítását is vizsgálja.

Ugyanezt a pontossági hibát az összesített leszállított famennyiségnél is
reprodukáltuk: 1000000 m³ induló összeg után 10000 darab 0,01 m³-es átadás
egyetlen bitet sem változtatott a régi `float` számlálón. A javítás a teljes
adatutat érinti: `TimberCargoSystem.Delivered`, checkpoint, világ-lekérdezés
és a játék évszakos jelentése kettős pontosságú. A régi mentésmigráció sem
szűkíti vissza az összesített értéket. A szállítmány egyedi, `float` mennyiségét
a teszt annak tényleges bináris értékével összegzi; nem feltételez egzakt
tizedestörtet a bemeneten.

A szimuláció futtatóazonosítója `forestycoon-simulation/2026-11-13.1`.
A korábbi `2026-10-31.4` teljes checkpointjai továbbra is importálhatók;
a régi aritmetikára épülő történeti visszajátszást nem jelöljük ekvivalensnek.
A JSON alakja változatlan, az új számok nagyobb pontosságot őriznek meg.
A már hibás anyagmérlegű régi mentések ellenőrzését nem lazítottuk fel.

## Reprodukálható terhelési forgatókönyv

A `--simulation-soak <hónap> <oldalméret> <seed> <eredménykönyvtár>`
a tényleges, renderelés nélküli világot futtatja 30 Hz-en, alapértelmezett
900 s/erdőév ütemben. Két azonos világot használ: teljes tick és váltakozó
1/7/64/257 egységes szeletek. Minden hónapban teljes checkpoint-egyezést
követel, majd csak a szeletelt világot menti/visszatölti. A következő hónap
az így visszatöltött állapotot is ellenőrzi.

A pálya szerkesztett sík terepet, tölgyet és erdeifenyőt, utat, közelítőnyomot,
malmot, telephelyet és gépeket tartalmaz. Váltakozó időjárás, útjavítás,
ültetés és projektalkalmazás fut; a külső tesztbemenet havi faanyag-utánpótlást
ad a szállítás tartós terheléséhez. Ez utóbbi csak teszt-fixture, nem új
játékszabály. A természetes pusztulás megengedett kimenet: a stresszteszt
nem állítja, hogy a szélsőséges időjárás mellett minden fa életben marad.

A `months.jsonl` teljes tickenkénti számlálót, erdőidőt, élőfaszámot,
szállítást, egyenleget, GC utáni menedzselt heapméretet, kumulált allokációt
és a két világ együttes tickidejét közli. A havi kényszerített GC és a
párhuzamos tesztterhelés miatt ezek diagnosztikai adatok, nem játék-FPS-mérések.
A parancsnapló szándékosan nő; heapnövekedés önmagában nem bizonyít szivárgást.

```sh
dotnet build ForesTycoon.sln -c Release
dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --background-wait-stress 60
dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --simulation-soak 120 16 42 artifacts/soak-16
dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --simulation-soak 24 32 7 artifacts/soak-32
```

A meglévő `tools/verify-engine.ps1 -Stress -StressMonths 24` is futtatja a
próbákat. A teljes xUnit-futás most részletes naplót és 180 másodperces,
külső elakadásfigyelőt kap. A `SimulationSoakTests` rövid, egyhónapos része
az alap tesztcsomagba is bekerült.

Bizonyítékok: `artifacts/stability-2026-11-13/`. A natív GC-minta és a hibás
reprodukció eredménye is megmarad; ezek nem sikeres futásokként szerepelnek.

## Hosszú futások eredménye

| Forgatókönyv | Szimulált idő | Tick világonként | Mentés/visszatöltés | Eredmény |
| --- | --- | --- | --- | --- |
| 16×16, seed 42, havi utánpótlás | 120 hónap / 10 év | 270000 | 120 | Minden havi összehasonlítás pontosan egyezik |
| 32×32, seed 7, havi utánpótlás | 24 hónap / 2 év | 54000 | 24 | Minden havi összehasonlítás pontosan egyezik |

Két-két világ: összesen **648000 világfrissítés**, **144 mentés/visszatöltés**,
**288 teljes checkpoint-összehasonlítás**. A 16×16 futás 269,27 s, a 32×32
futás 152,45 s faliórát igényelt, egymással és a regressziós csomaggal részben
párhuzamosan. A szimuláció végig 1/30 s lépéssel haladt, nem nagy időugrásokkal.
A végleges naplók: `sustained-16.log`, `sustained-32.log`, a két könyvtár
`months.jsonl` állományaival.

A GC utáni menedzselt heap 94,5 → 156,0 MiB, illetve 143,0 → 179,8 MiB
között változott; a mentések mérete eközben 3,6 → 28,9 MiB, illetve
11,7 → 17,0 MiB lett. A két világ megőrzi a havi parancsokat és a hat hónaponként
alkalmazott teljes szabályprojekteket. A naplónövekedés ezért jelentős része
a terhelésnek; a mérés nem állít korlátos memóriát tetszőlegesen hosszú
parancstörténet mellett, és nem helyettesít heap-gyökérszintű szivárgásvizsgálatot.

A végleges Release build **0 hiba, 0 figyelmeztetés**. A teljes regresszió
**1966/1966 sikeres, 0 kihagyott**, 5,36 perc (`full-final.trx`), automatikus
elakadásfigyeléssel. Az előző javítási állapoton 1965 teszt ment át; az új
összesített szállítási regresszió a végleges futásban már szintén sikeres.
A szabálykatalógus-ellenőrzés és a `git diff --check` is sikeres.

A 16×16 forgatókönyvben az ismétlődő szélsőséges időjárás mellett a faállomány
elfogyott, a 32×32 pálya végén 3670 fa maradt. Ezt nem kezeljük numerikus
hibának, és nem tekintjük az ökológia tudományos validációjának. A kisebb pálya
első próbájának „maradjon élő fa a végén” fixture-feltételét ezért kezdőállapoti
követelménnyé pontosítottuk; állapotegyezési vagy anyagmérleg-tűrést nem
lazítottunk. Az első tízéves próba ennek a fixture-feltételnek a hibájával
zárult; a fenti táblázat az újrafuttatott végleges protokoll eredménye.

A natív többablakos életcikluspróba aktív kijelző mellett sikeres.
Alvó kijelzővel a már korábban dokumentált `_glfwGetVideoModeCocoa`
összeomlás újra jelentkezett; ez nem a GC-elakadás, és ez a változtatás nem
javítja a GLFW kijelzőkezelését. A natív eredmény csak az aktív kijelzős
macOS-környezetre vonatkozik. Windows/Linux, többnapos valós idejű futás és
valós pályák teljesítményprofilja nem része ennek az ellenőrzésnek.
