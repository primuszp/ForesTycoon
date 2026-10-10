# Nagy térképes teljesítménykapu

A `large-world-v1` protokoll 64×64 és 128×128 csempés, 42-es seedű természetes világot mér napsütésben és viharban. Mind a négy eset külön folyamatban fut. A referencia 1280×720, MSAA4, High minőség, 30 Hz szimuláció és 900 másodperces erdőév. A mérés tartalmazza a valódi világfrissítést, a befejezett GPU-rajzolást és a diorama utófeldolgozást. UI, bemenet, swap és képkockaütemezés nem része; a járműpopuláció az alapvilágé, nem száz teherautós stresszteszt.

## Mérés és memória

### Az induló kép minőségének helyreállítása

A felhasználó az induló erdőkép és a dioráma hangulatának romlását jelezte. A korábbi, részletes procedurális faformák generátora változatlan; a regresszió a hideg, fokozatos geometriaépítésben és a memóriahatár miatti Far-LOD kényszerítésben volt. A renderer most az első megjelenítés előtt elkészíti az induló kamera összes látható chunkját a kamera által kért LOD-on. A térkép többi része továbbra is fokozatosan töltődik. A cache a nem használt geometriát üríti; a látható részletesség túllépése külön mérve, nem automatikus minőségromlással kezelve. Új, még betöltődő chunkok nem egyszerűsítik le a már megjelenített fákat. A térképrács alapból kikapcsolva, kézzel továbbra is bekapcsolható.

Az első rajzolás ezzel több munkát végez: ez tudatosan a teljes induló dioráma megjelenítéséhez tartozik. A lent szereplő korábbi hidegindítási és teljesítményadatok nem igazolják ezt az új útvonalat. A rögzített teljesítménykeretek nem emelve; az új vizuális minőség mellett később ismét ellenőrizni kell őket. A natív regresszió minden látható chunkban kész Near-geometriát követel a közeli kamera első rajzolása után, valamint kerettúllépés mellett is ellenőrzi a részletes modellekre való visszatérést.

Az első konstrukció és rajzolás után 180 rajzolási lépés melegíti a világot. Az élő, már GPU-erőforrásokat birtokló világ mentését ugyanabba a világba töltjük vissza, tehát a tranzakció alatt az új jelölt és a régi világ is él. A betöltési idő, a mentés mérete és a betöltött világ első rajzolása külön adat. A szimuláció 65 másodpercre előrelép, a megváltozott világ rajzolása újra bemelegszik, majd 600 szimulációs/rajzolási képkocka készül. Az első 300 álló kamerával, a következő 300 a térkép közepét körbejáró kamerával fut. A 75. szimulációs másodpercnél pontosan egy havi váltásnak kell lennie.

A P95 a rendezett 300 elemű fázis 285. eleme, nem átlag. Minden képkocka update/render/összes idejét, allokációját, látható chunkját, erdőrezidenciáját, CPU/GPU-payloadját és a renderfázisok CPU/GPU-időbélyegeit is elmentjük. A fázisprofil a világ renderpassait méri; az utófeldolgozás az összes renderidőben szerepel.

A betöltést 2 ms-os mintavétel figyeli: working set, privát memória és GC committed memória. Ez **mintavételezett betöltési csúcs**, rövidebb tüskét kihagyhat. Külön adat az operációs rendszer teljes folyamatra mért working-set high-water értéke; ez a konstrukciót, betöltést, melegítést és az összes képkockát is tartalmazza. A working set a folyamat rezidens memóriája, nem a teljes VRAM vagy a driver minden belső allokációja. A payloadszámlálók kizárólag a motor által tárolt renderadatokat mérik. A két memóriaadat nem cserélhető fel.

## Rögzített profil

```powershell
pwsh -File tools/verify-performance.ps1 -Profile docs/performance-profiles/rtx5060-i78700.json
```

A profil RTX 5060 / i7-8700 gépre, NVIDIA 616.92 driverre, Windows 10.0.26300 rendszerre és .NET 8.0.31-re vonatkozik. A hardver, driver, OS, runtime, architektúra, processzorszám és CPU-név eltérése leállítja az összehasonlítást. Más géphez vagy frissített driverhez külön, áttekintett mérési alap és profil kell. A kapu nem írja át automatikusan a küszöböket. Hiányzó eset, hibás munkaterhelés, hiányzó/nem véges mérés vagy küszöbtúllépés hibás futást eredményez.

A referencia első mérési sorozata 2026. október 10-én:

| Világ / időjárás | Álló P95 | Mozgó P95 | Havi váltás | Betöltés | Mintavételezett betöltési working set | Folyamat high water |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 64 / Sunny | 17,21 ms | 10,25 ms | 16,47 ms | 191,6 ms | 185,5 MiB | 396,0 MiB |
| 64 / Storm | 17,66 ms | 12,62 ms | 17,80 ms | 191,2 ms | 188,4 MiB | 363,4 MiB |
| 128 / Sunny | 19,86 ms | 21,06 ms | 27,81 ms | 482,5 ms | 245,6 MiB | 332,4 MiB |
| 128 / Storm | 22,46 ms | 16,53 ms | 28,26 ms | 516,6 ms | 245,0 MiB | 326,0 MiB |

A kapu 64-es térképnél 25/20 ms álló/mozgó P95-et, 35 ms havi váltást és 500 ms betöltést enged. 128-as térképnél 30/30 ms P95, 45 ms havi váltás és 1000 ms betöltés a keret. Mindkét méret working-set korlátja 384 MiB mintavételezett betöltési csúcs és 768 MiB teljes folyamatcsúcs. A teljes képkockatüske és az első rajzolás is külön korlátot kap a JSON-profilban. A küszöbök a mérési szórásnak tartalékot hagyó **regressziós keretek**, nem 60 FPS-es kiadási minősítés. Az első mérés szerint a High világ még nem teljesíti a stabil 16,67 ms-os képkockacél követelményét; a havi szimulációs tüske is további optimalizálási lehetőség.

Az eredmények az `artifacts/performance-validation` könyvtárba kerülnek. A `validation.json` a profilt, annak SHA-256 hashét, a játékassembly és a kimeneti mappa közvetlen DLL-jeinek SHA-256 hashét, a Git revíziót, a munkapéldány állapotát és a kapuhibákat rögzíti. A futás alatt ne menjen más benchmark vagy grafikai teszt ugyanazon a gépen.

## CI

A `performance` workflow-input alapértéke `true`. Kifejezetten `false` értékkel a Windows build/teszt és natív regresszió külön is futtatható; ez nem ad teljesítmény-minősítést. A felhasználó a kód és a CI beállításának lezárását kérte, a tiszta GPU-s teljesítménymérést későbbre hagyta. Ennek a körnek a Windows CI-je ezért natív ellenőrzést futtat, teljesítménykapu nélkül.

A rögzített NVIDIA-profil mérése előtt a kapu ellenőrzi a háttér-GPU-terhelést és a profil `CompetingGpuProcessNames` listáján szereplő aktív GPU-klienseket. A listán Python és más dotnet benchmark szerepel; a Windows asztali folyamatok nem automatikusan tiltottak. Ez kiegészítő védelem, nem teljes gépizoláció vagy a futás közbeni terhelés minden lehetséges forrásának bizonyítása. Hibás futás mindig `Failed` manifestet ír, így korábbi zöld eredmény nem maradhat az új futás státuszaként. A `tools/test-performance-gate.ps1` felvett mintákon ellenőrzi a helyes elutasítást; a benchmarkfolyamatot helyettesíti, a tényleges validációs kódot futtatja.

Az első teljes kapufutás sikeres volt. Egy későbbi ismétlés a 64/Sunny legrosszabb képkockáján, a 128/Sunny mozgó P95-én és a 128/Storm havi váltásán túllépést jelzett. Ekkor egy másik projekt PyTorch-folyamata is használta a GPU-t, egy mérésen kívüli lekérdezés 76% terhelést mutatott. A túllépést megőriztük hibás bizonyítékként, a kereteket nem emeltük. Ez valós háttérterhelést igazol, de önmagában nem bizonyítja minden lassú képkocka okát. A legutóbbi kapuváltozat tiszta gépes megismétlése még szükséges.

Az `Engine validation` automatikus workflow Windows build/tesztet és Ubuntu 24.04 build/tesztet, majd Mesa/Xvfb alatt Linux natív próbákat futtat. A Linux-verzió az ImGui.NET bináris glibc 2.38-as minimuma miatt szükséges. A platformpróba nem teljesítménymérés.

Az `Engine hardware validation` kézzel indítható workflow a `self-hosted`, `Windows`, `X64`, `forestycoon-rtx5060-i78700` címkéjű runneren futtatja a teljes Windows natív csomagot, majd a rögzített teljesítménykaput. A runnernek aktív asztali környezet és a profilnak megfelelő rendszer kell. A felhasználó jóváhagyásával a helyi VEGA gép `ForesTycoon-VEGA` néven regisztrálva, a GitHub API szerint online. A runner 2.338.0 hivatalos ZIP-jének SHA-256 ellenőrzése sikeres. Telepítése a gitből kizárt `artifacts/github-runner` mappában van; jelenleg háttérfolyamatként fut, nem Windows-szolgáltatásként. Újraindítás után ugyanebből a mappából a `run.cmd` indítja. A workflow szándékosan felülvizsgált revízió kézi indításához kötött. A távoli futás sikerét csak a GitHub-futás eredménye igazolja, a helyi sikeres csomag nem.

## Lezárt platformellenőrzés, 2026-10-10

Az `1f85b08d255769a3e7d358cedfe08694fa301474` revízión az [Engine validation](https://github.com/primuszp/ForesTycoon/actions/runs/38038727776) sikeres: Windows és Linux alatt 1574 teszt, 0 buildhiba/figyelmeztetés, valamint a teljes Linux Mesa/Xvfb natív csomag. Az [Engine hardware validation](https://github.com/primuszp/ForesTycoon/actions/runs/38038728426) ugyanazon a revízión a VEGA runneren szintén sikeres: 1574 teszt, 0 buildhiba/figyelmeztetés és a teljes Windows natív csomag. Mindkét natív csomagban sikeres a kétkontextusos jelenet és az alkalmazásablakok életciklusának ellenőrzése.

A self-hosted runner SDK-telepítése az írható `runner.tool_cache` könyvtárat használja. A checkout, setup-dotnet és upload-artifact actionök Node 24-es verzióra frissítve; a workflowk actionlint ellenőrzése sikeres. A teljesítménykapu tíz helyi szerződéstesztje sikeres, beleértve a hamis összesítések, hibás minták, hiányos profil és versengő GPU-terhelés elutasítását.

A Windows futás a felhasználó kérésére `performance=false` értékkel indult: a teljesítménykapu és annak CI-s szerződéstesztjei kihagyva. A friss, háttérterhelés nélküli teljesítménymérés későbbre halasztva. Ezek a sikeres platformfutások nem írják felül a korábbi hibás teljesítménymérés eredményét és nem igazolnak stabil 60 FPS-t.
