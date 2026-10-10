# ForesTycoon játékmotor code review

A solution mind a 11 projektjének függőségeire, fő belépési pontjaira és motor szempontból fontos végrehajtási útjaira kiterjedő áttekintés, 2026. október 10. Az alábbi hibaleírások a review idején látott állapotot rögzítik; a javítások aktuális státuszát a következő táblázat tartalmazza. Az áttekintés nem jelent minden forrássor teljes auditját.

## Javítások aktuális állapota

Legújabb felhasználói finomítás: a rácsháló alapból bekapcsolt. A szarvasok külön, négyszeres naptárórát kapnak, ahogy a járművek már korábban: az alap 1× nézetben a mozgás valós idejű. A járási és legelési animáció órájának interpolációja és mentése is megoldott. A hó csempék között konzervatív szélfluxussal rendeződik át; a növényzet és a szél felőli magasabb terep visszatartja. A shader helyi hókészletből számít fedést, eltérő hóárnyalatot és szélirányú felületi normálhullámokat; geometriai hóbuckát nem épít. A lombkoronák Near hálója nagy fáknál 96 helyett 192 pont, a Medium 40 helyett 80 pont; kisebb koronák is finomodtak. A fenyők Near oldal- és szintkorlátja 8/5 helyett 16/8, a törzsek és főágak körirányú hálója részletesebb. A korábbi fafajok, koronaprofilok és textúrák megmaradnak. Aktuális runtime: `forestycoon-simulation/2026-10-10.3`, a v13/.2 és v12/.1 mentések támogatott migrációval tölthetők. A korábbi alábbi állapotleírások a saját revíziójukra vonatkoznak.

Ellenőrzés: 1589 sikeres teszt; Release build 0 hibával/figyelmeztetéssel. A természetes 1× óra, szünet, a korábbi runtime mentésének migrációja, a hótranszport vízmérlege és pontos folytatása tesztelt. A Windows natív csomag minden eleme sikeres: az első futás képi próbájában jelentkező háttérpublikálási verseny után a javított grafikai próba és az összes további elem is átment. A natív téli mintán a csempék hókészlete 3,28–29,96 mm vízegyenérték között változik (`artifacts/seasonal-weather/05-winter-drifts.png`). A részletesebb famodellek galériája: `artifacts/tree-gallery/winter-detail-near.png`. A grafikai összehasonlító próba előre felépíti a kis fixture LOD-jait, hogy képi összehasonlítás közben ne publikálódjon háttérben másik háló. A teljesítménykapuk küszöbei változatlanok; tiszta GPU-s új mérés még nem készült.

Évszakos időjárás: az ősz gyakoribb, hosszabb esőt, a nyár rövid záport és erős vihart, a tél havazást kap. A szállingózó hó egyedi lassú süllyedést, oldalirányú lebegést, forgást és billegést használ; a kutatási alap és a grafikai közelítés határai a [kutatási jegyzetben](rain-storm-cloud-research.md) olvashatók. A fafajspecifikus koronák és a helyreállított dioráma geometriája megmarad. A hó külön, konzervált vízkészlet; melegedéskor a felszíni vízbe olvad. A mentés aktuális verziója 13, a runtime `forestycoon-simulation/2026-10-10.2`; a korábbi ismert v12/.1 mentés üres hókészlettel migrálható. Helyi Release build: 0 hiba/figyelmeztetés; 1586 sikeres teszt és a teljes Windows natív csomag sikeres. A natív évszakos próba természetes vihart/esőt/havazást, hófedést, a GPU-pelyhek szüneteltetését és animációját ellenőrizte. Képi minták és mozgó előnézet: `artifacts/seasonal-weather`; az ellenőrzési napló: `artifacts/seasonal-weather-verification.log`. Friss izolált teljesítménymérés továbbra is a korábban elhalasztott feladat.

Fafajspecifikus koronafelületek: a 16 faj külön, determinisztikus levél-/tűlevélmintázatot kapott. A meglévő faformák, szezonális alapszínek és teljes koronafedettség megmaradnak. A triplanáris, mipmapes mintavétel lekerekített felületen is folyamatos és zoomoláskor szűrt. A lomb saját fajazonosítót tárol, a kéreg korábbi anyagcsaládjai változatlanok. A 256×256-os anyagtömb 22 rétege 5,5 MiB alapadat, teljes mipmapsorral körülbelül 7,33 MiB környezetenként. A natív fajpróba 16 különböző képet, textúra ki-/bekapcsolási visszaállítást és változatlan szín-/mélységi fedettséget igazol; ez bekerült a közös natív CI-csomagba.

Az induló dioráma vizuális regressziójának helyreállítása: az első kép előtt a látható chunkok a kamera által kért részletes faformákkal készülnek el; a cache kerete nem kényszerít Far-LOD-ra, és új chunkok betöltése sem egyszerűsíti le a már látható fákat. Az eredeti procedurális modellgenerátor változatlan. A térképrács alapból kikapcsolva. Az első képkockát a `--capture-frame` próba külön elmenti. A korábbi platform- és teljesítménybizonyítékok az alábbi megjelölt revízióra vonatkoznak; az új indulási útvonal teljesítményét újra kell mérni.

A helyreállítás helyi ellenőrzése: 1583 sikeres teszt, teljes Release build 0 hibával/figyelmeztetéssel, teljes Windows natív csomag sikeres. Az első képkocka és a 60. képkocka tényleges játékablakból mentett képe ellenőrizve (`artifacts/startup-final/00-first-frame.png`, `01-hud-default.png`); a fák már az első képen jelen vannak. A 64/128-as natív Near-próba első rajzolása 1,79/1,16 másodperc volt: ez tesztkörnyezeti minta, nem izolált teljesítmény-minősítés. A korábbi elsőrajzolási keretek teljesítése ezzel még nincs igazolva.

Legutóbbi platform- és mérési bizonyíték: az `1f85b08d255769a3e7d358cedfe08694fa301474` revízión az [automatikus Windows/Linux CI](https://github.com/primuszp/ForesTycoon/actions/runs/38038727776) és a [helyi runner Windows natív CI-je](https://github.com/primuszp/ForesTycoon/actions/runs/38038728426) sikeres. Mindkét platformon 1574 teszt sikeres, a Release buildek 0 hibával/figyelmeztetéssel zárultak; a teljes Linux Mesa/Xvfb és Windows natív csomag is sikeres. Helyben a licencelt assetekkel együtt 1583 Windows teszt sikeres. A nagy térképes kapu első futása sikeres; későbbi, párhuzamos PyTorch GPU-terhelés mellett futó ismétlés három túllépést jelzett, amelyeket nem fedtünk el. A kapu azóta háttér-GPU-ellenőrzést is kapott, tíz helyi szerződéstesztje sikeres. A felhasználó kérésére a kód és a CI lezárva, a legutóbbi kapu tiszta GPU-s ismétlése későbbre halasztva; a Windows CI `performance=false` értékkel futott. Részletek: [large-world-performance.md](large-world-performance.md).

| Feladat | Állapot és bizonyíték |
| --- | --- |
| Útöregedés részidejének mentése | Javítva. Mentésverzió 12, checkpointverzió 2, validált `RoadWeatherSeconds`. Három időpontban mentett világ további 180 tickje checkpointszinten azonos. |
| Új világ pénzügyi és szimulációs állapotának nullázása | Javítva. Elkülönített új világ cseréli a régit; a felhasználó hangolása és a grafikai beállítások megmaradnak. Teljes checkpoint és függő parancsok ellenőrzése. |
| Teherautó üzemanyag-mérlege | Javítva. Minden járműcsere előtt elszámolás, utána új számlálóalap. A három járműpéldányon át futó teljes út fogyasztása egyezik a terhelt költséggel. |
| Dokkok súlyozott útválasztása | Javítva. A kereső visszaadja az összköltséget, a dokkválasztás ezt használja; stabil döntés azonos költségeknél. Rövidebb, drágább nyom és hosszabb, olcsóbb közút tesztje. |
| Sikertelen regenerálás és betöltés | Javítva. Jelöltvilág készül a publikálás előtt; hibás paraméter vagy checkpoint után az eredeti világ és a függő input megmarad. Injektált bufferfeltöltési hiba esetén az új erőforrások felszabadulnak, a régi megjelenítés megmarad. |
| Grafika nélküli teljes játékállapot | Megvalósítva. A `GameWorld` közvetlen `TerrainMap` tulajdonos; `enableRendering: false` mellett mentés, betöltés, gazdaság és szimuláció natív ablak nélkül működik. Az Editor ezt használja. |
| Hibás világ frissítés utáni kezelése | Javítva. Részleges futási hiba után frissítés, új parancs és mentés tiltott; validált betöltés vagy új világ helyreállítja a futást. Hibainjektálásos teszt. |
| Kieső szimulációs idő mérése | Megvalósítva. A fix óra számlálja a cap és munkakeret miatt kihagyott időt; a fejlesztői panel megjeleníti. |
| Hét CS8600 figyelmeztetés | Javítva. A tesztsegéd nullable változói helyesen jelöltek; a teljes Release buildben 0 figyelmeztetés. |
| Fokozatos kezdeti geometria és korlátozott LOD-cache | Megvalósítva és natívan ellenőrizve. Kamera szerinti Far-kezdés, időkeretes generálás és lapozott feltöltés; nem látható munka megszakítása; LRU-ürítés 128 MiB erdő- és 32 MiB terep-adatkerettel. A látható munkakészlet túllépése külön mérve. A 64/128-as nagy térképes terhelés, tranzakciós betöltési csúcs és két kameraút P95 értéke rögzített profilú kapuval ellenőrzött; részletek: [large-world-performance.md](large-world-performance.md). |
| Futtatási szabályok verziója a mentésben | Megvalósítva. A v12 mentés explicit szimulációs runtime-azonosítót tárol. Hiányzó vagy eltérő azonosító elutasítva az élő világ és a függő parancsok módosítása nélkül. |
| Assetimport és megosztott modellerőforrások | Rendezve. Előzetes GLB-határ-, típus-, hierarchia- és szemantikaellenőrzés; fájl- és dekódoltadat-keret; kiválasztott scene; független pózok egy GPU-renderelővel. Natív feltöltési hiba takarítása, újrapróbálás és valódi handle-törlés ellenőrizve. A régi teherautó-importer közös előellenőrzést és atomi bufferfeltöltést használ. |
| Effekt-erőforrások | Rendezve. Kemény részecske-, felhőlépés- és jelölőkorlát, méretkorlátos magasságmező és ködmélységadat, korlátos ködforrás-cache. CPU-idő/cél és CPU/GPU-adat mérése. Natív szélsőérték-, cachecsere-, felszabadítási és újrapróbálási teszt. |
| Több rendernézet tulajdonlása | Rendezve. Explicit, szálhoz kötött `RenderEnvironment`, külön frame-állapot, shader-cache és backendgyárak. GPU-backendek, batch-ek, UI és profiler tulajdonosi ellenőrzéssel működnek. A játék, preview és editor saját környezeti hostot használ; felváltva és beágyazva futó ablakok kontextus-visszaállítása, külön bezárása és hibás betöltésének takarítása natívan ellenőrzött. |
| Automatizált platform-, kép- és teljesítményellenőrzés | A közös PowerShell ellenőrző, Windows/Linux build–teszt és Linux Mesa/Xvfb natív CI elkészült. A glibc-eltérés és a Mesa által elutasított shader javítva. A végső automatikus platform-CI és a helyi runner Windows natív CI-je sikeres; a futási bizonyítékok fent hivatkozva. A rögzített GPU-s teljesítménykapu, háttérterhelés-védelem és tíz helyi szerződésteszt elkészült. A legutóbbi kapu tiszta GPU-s ismétlése a felhasználó kérésére későbbre halasztva, ezért ez a kör nem ad új teljesítmény-minősítést. |

A v10 és korábbi checkpointok nem tartalmazták az útöregedés részidejét; ezek betöltése továbbra is támogatott, a hiányzó érték 0. A pontos, részidőt is megőrző folytatás az új mentésekre biztosított.

A renderkörnyezet-alap ellenőrzése: 1581 sikeres Release teszt, 0 buildhiba és figyelmeztetés. A teljes Windows natív csomag sikeres; a lapozott feltöltés tulajdonosi ellenőrzésének utolsó módosítása után a teljes headless tesztkészlet, a kétkontextusos próba és az erdő natív regressziója is újra sikeres. A környezeti állapot, tulajdonosi szerződés, natív bizonyíték és hátralévő integráció: [render-environments.md](render-environments.md).

Az ablakhost integrációjának ellenőrzése: 1583 sikeres Release teszt, 0 buildhiba és figyelmeztetés, sikeres teljes Windows natív csomag. A többablakos próba feltárta és javítás után regresszióként ellenőrzi a megosztott, felszabadítás után érvénytelen ImGui-címfontpointer problémáját. A host a natív és UI-kontextust is visszaállítja, az új ablak konstrukciója és a játék callbackjén belül futó editor életciklusa sem változtatja meg a szülő környezetét. A legutóbbi konstruktor- és szálvédelmi módosítások után a többablakos életciklus, a kétkontextusos jelenetpróba és az önálló játék újra sikeres; a profiler query-törlése szintén driverrel ellenőrzött. A nagy térképes teljesítménykapu azóta elkészült; a távoli platform-CI és a Windows natív CI sikeres. A legutóbbi kapuváltozat tiszta GPU-s mérése a felhasználó kérésére későbbre halasztva.

A közvetlen GPU-backendek tulajdonosi ellenőrzésének bővítése: 1582 sikeres Release teszt, 0 buildhiba és figyelmeztetés; a teljes Windows natív csomag sikeres. A kétkontextusos próba a játékvilágot, modelleket, batch-et, esőt, felhőt, postprocesst és UI-t is kétszer futtatja. Az elutasított idegen törlés után az első jelenet újra működik, majd a modellek és az időjárás GPU-erőforrásainak törlését driverlekérdezések igazolják. A grafika nélküli világ tesztjei továbbra is natív kontextus nélkül futnak. A csomag óta az ablakhost és a teljesítménykapu is elkészült; a még hiányzó futási bizonyítékokat a státusztáblázat jelöli.

A v4–v11 mentések runtime-azonosító nélkül továbbra is a meglévő migrációs viselkedéssel tölthetők be. Ez nem garantálja egy korábbi, eltérő motorverzió minden natív szabályának reprodukálását. A v12 azonosítója `forestycoon-simulation/2026-10-10.1`; natív szimulációs szemantika változtatásakor a `CurrentRuntimeRulesVersion` értékét növelni kell, és támogatott migrációról külön dönteni. A szerkeszthető gráf és tuning saját állapota továbbra is a checkpointban/parancsnaplóban szerepel.

A második csomag ellenőrzése: 1513 sikeres Release teszt, 0 buildfigyelmeztetés; sikeres natív erdő-, időjárás-, növekedés-, checkpoint-, játék- és editor-ellenőrzés. A cache számlálói a tárolt CPU/GPU-adatokat mérik, nem a teljes processzmemóriát vagy a grafikus driver belső költségét. A látható réteg és az egyetlen folyamatban lévő csere nem üríthető ki a keret kedvéért; a túllépés látható a fejlesztői panelen. A 2/8 ms generálási keret kooperatív: egy fa generálása vagy egy GPU-lap feltöltése nem szakítható félbe.

A Windows natív hidegvilág-próba 64×64 és 128×128 térképen egyaránt 8 látható chunkhoz kért geometriát, 16, illetve 64 összes chunkból. A mért konstruktoridő 82–207 ms, az első rajzolás 31–65 ms volt több helyi futásban; a szálon mért allokáció 12,6, illetve 37,2 MiB. Ezek tájékoztató gépfüggő minták, nem kiadási sebességküszöbök vagy csúcsmemória-mérések. A 33×33 csomópontos erdő látható Far-rétege 20–31 rajzolási lépésben jelent meg; a teszt nem követel rögzített lépésszámot.

Ismételhető ellenőrzés a repó gyökeréből: `pwsh -File tools/verify-engine.ps1` a buildhez és headless tesztekhez; `pwsh -File tools/verify-engine.ps1 -Native` OpenGL környezetben a natív ellenőrzésekhez is. Az eredmények a gitből kizárt `artifacts/` könyvtárba kerülnek. A CI a TRX eredményeket és a natív képeket hiba esetén is feltölti. A script bármely részellenőrzés hibájánál hibával leáll.

A harmadik csomag modellkezelése: 1557 sikeres Release teszt, 0 buildhiba és figyelmeztetés. A telepített 13 GLB, köztük a helyileg licencelt járművek és épületek, véges pózt és konzisztens csúcselrendezést ad. Célzott tesztek fedik a hibás normál-/pozícióméretet, bufferview- és indexhatárokat, skinhivatkozást, kvaterniót, animációcsatornát, mély hierarchiát és scene-kiválasztást. A natív modellpróba egy feltöltési hiba után újrapróbál, majd a driver `IsProgram/IsBuffer/IsVertexArray/IsTexture` lekérdezéseivel ellenőrzi a felszabadítást. A bound program törlése előbb leválasztja azt, hogy a driver valóban azonnal megszüntethesse az objektumot. A részhalmaz és a tulajdonlási szerződés: [model-import-contract.md](model-import-contract.md).

A negyedik csomag eső/felhő részének ellenőrzése: 1571 sikeres Release teszt, 0 buildhiba és figyelmeztetés. A kis esőkeretnél korábban nem befejeződő rácsméretezés javítva. Azonos revíziójú másik felület és új dimenzió feltöltést kér; a magasságadat 16 MiB CPU- és 16 MiB GPU-payloadra korlátozott. A minőségi szintek részecskeszáma, felhőlépése és CPU-célja, valamint a tényleges CPU-idő és memória látszik a fejlesztői panelen. A részletes korlátok és a célértékek értelmezése: [effect-budgets.md](effect-budgets.md).

A köd/jelölő kiegészítés ellenőrzése: 1576 sikeres Release teszt, 0 buildhiba és figyelmeztetés. A köd mélységtextúrája 16/32/64 MiB keretet kap; túllépéskor a natív textúra és framebuffer törlődik, kisebb nézetben újra létrehozható. A driver lekérdezései ellenőrzik a törlést. A forrás-cache 16 384 csempés térképen is legfeljebb 4096 bejegyzés. Az akciójelölők fix 4096 elemű körpuffert és minőségfüggő rajzolási keretet használnak. A régi túlméretes checkpointokból a legújabb 4096 vizuális jelölő marad meg, teljes bemenetvalidáció után; a játék gazdasági és ökológiai állapota nem változik. Telítődés, lejárat, sorrend, migráció és bemelegítés utáni allokációmentes létrehozás tesztelt. A teljes Windows natív ellenőrzési csomag minden eleme sikeresen lefutott; az új ködteszt ültetési előfeltételének javítása után a grafikai próba és az összes további ellenőrzés is sikeres.

Az első javítási csomag ellenőrzése: 1509 sikeres Release teszt, teljes Release build 0 hibával és 0 figyelmeztetéssel; sikeres játék- és editor-smoke teszt Windows OpenGL környezetben. A natív `--checkpoint-smoke-test` szintén sikeres a migráció, a 31 tickes folytatás, a függő parancsok és az atomi elutasítás ellenőrzésével. A v10 checkpoint hiányzó részidejének kompatibilitási kezelése és az erőforrás-létrehozási hiba takarítása célzott teszttel ellenőrzött. A teljes cél még nyitott a táblázatban jelzett feladatok miatt.

## Javítandó hibák

### P1 Az útöregedés időállapota elveszik betöltéskor

Forrás: `ForesTycoon/World/GameWorld.cs:292`, `ForesTycoon/World/Persistence/GameWorld.Checkpoint.cs:7`, `ForesTycoon/World/Persistence/WorldCheckpointData.cs:44`.

A `WeatherRoads` a `roadWeatherSeconds` részidőt gyűjti, és 0,5 másodpercnél alkalmazza az utak kopását és a közelítő nyomok öregedését. Ez az érték nem része a checkpointnak, és a `Load` sem veszi át a betöltéshez létrehozott világ értékét. Ezért a mentés folytatása eltér a megszakítás nélküli futástól. Meglévő világba betöltve még a korábbi világ maradékideje is megmaradhat.

Célzott, rejtett OpenGL ablakban futó próba: 0,3 másodperc frissítés után az eredeti világ részideje 0,3, ugyanannak a mentésnek a betöltött világában 0. A teszt közvetlenül a belső részidőt ellenőrizte; a hosszú távú eltérés nagyságát nem mérte.

Javítás: a részidő legyen verziózott és validált checkpointmező; a világ tulajdonának átadásakor is kerüljön át. A regresszióteszt mentse a világot az időhatár két oldalán, majd hasonlítsa össze az útállapotot és a nyomok fennmaradását több további lépés után.

### P1 Új világban megmaradnak a régi kiadások

Forrás: `ForesTycoon/World/GameWorld.cs:413`.

A `Regenerate` törli a rendszereket és a parancsnaplót, de nem nullázza az `Expenses` értékét. Az új logisztika bevétele és működési költsége újraindul, az építési kiadás viszont átjön az előző játékból. Így az új világ egyenlege hibás. A `roadWeatherSeconds` és a korábbi területművelet összesítése szintén megmarad.

Célzott életcikluspróbában a beállított 123 eFt kiadás regenerálás után is 123 maradt; a 0,3 másodperces útöregedési részidő is megmaradt. A kiadást a próba közvetlenül állította be, nem útépítéssel hozta létre.

Javítás: minden világállapot egyetlen, újonnan létrehozott állapotegységből induljon. A megőrzendő beállítások köre legyen explicit, a pénzügyi és szimulációs állapot nullázása legyen tesztelt.

### P2 Teherautóváltáskor kimarad üzemanyag a költségből

Forrás: `ForesTycoon/World/Cargo/ForestryLogistics.Fleet.cs:113`, `:135`.

A telephelyről a munkához érkező járművet a `StartShuttle` új `Vehicle` példányra cseréli. Az új példány `FuelUsed` számlálója nulla, a flottateherautó `FuelCharged` értéke viszont a régi jármű fogyasztását őrzi. A különbség addig negatív, amíg az új jármű túl nem lépi a korábbi számlálót; ezalatt nincs üzemanyag-terhelés.

Célzott próba: a munkafázis indítása után az új jármű fogyasztása 0, a korábban beállított elszámolási alap továbbra is 12 liter. Javítás: cserekor számoljuk el a régi példány maradékfogyasztását, majd az új számlálóhoz igazítsuk az alapot. Tesztelendő a teljes indulás–munka–hazatérés fogyasztási mérlege.

### P2 A dokkok közötti útválasztás felülírja a súlyozott költséget

Forrás: `ForesTycoon/World/Cargo/ForestryLogistics.Fleet.cs:77`, `ForesTycoon.Map/Terrain/TerrainMap.Network.cs:17`, `:79`.

A `FindNetworkPath` súlyozott keresést végez: aszfalt 1, makadám 1,15, nyom legalább 3,5 egység. A `TruckRoute` az egyes dokkpárok eredményeit mégis csempeszám alapján hasonlítja össze. Több lehetséges dokk esetén így egy rövidebb, drága nyomvonal megelőzhet egy hosszabb, olcsóbb közúti útvonalat. Ez forráskódból igazolt döntési hiba; külön térképi reprodukció nem készült.

Javítás: a kereső adja vissza az út összköltségét is, és ezt használja a dokkpárok összehasonlítása. Az azonos költségekhez legyen stabil döntési szabály.

### P2 A regenerálás hibája használhatatlanná teheti az élő világot

Forrás: `ForesTycoon/World/GameWorld.cs:413`, `:493`.

A regenerálás előbb törli a parancsokat és rendszereket, majd a `ReplaceTerrain` felszabadítja a renderelőt és a terepet. Csak ezután ellenőrzi a null beállítást és építi az új világot. Ha a létrehozás hibát dob, a korábbi állapot már elveszett, és a mezők részben felszabadított objektumokra mutathatnak. A betöltés már izolált jelöltvilággal dolgozik; a regenerálásnál ugyanez a minta hiányzik.

Javítás: teljes jelöltállapot létrehozása és sikeres ellenőrzése után történjen a csere. Hibainjektálásos teszt ellenőrizze, hogy sikertelen új világ után a régi tovább frissíthető, rajzolható és menthető.

## Motorarchitektúra és skálázás

### A világ létrehozása az egész térkép minden fa részletességét felépíti

Forrás: `ForesTycoon/Rendering/Scene/TerrainRenderer.cs:50`, `ForesTycoon/Terrain/Forest/Terrain.ForestIndividuals.cs:78`.

A `TerrainRenderer` konstruktora felmelegíti a statikus geometriát, majd minden chunk minden `ForestLod` értékéhez szinkron elkészíti a fákat. A cache a világ teljes élettartama alatt tárolja ezeket; méretkeret és használat szerinti eltávolítás nincs. A későbbi, időkeretes frissítés jó irány, de az indulás és a betöltés nem ezt használja. Betöltéskor ráadásul először az induló erdő renderelője épül meg, majd a checkpoint helyreállítása új renderelőt hoz létre.

Javasolt cél: először a látható chunkok távoli geometriája készüljön el; a további részletességek és a GPU-feltöltések kapjanak képkockánkénti keretet. A cache méretét CPU- és GPU-bájtokban mérjük, és a nem használt részletességek legyenek felszabadíthatók. Nagy térképen mérendő az első képig eltelt idő, a betöltési csúcsmemória és a képkockaidők felső percentilisei. A jelen review nem állapít meg nagy térképes FPS-értéket.

### A teljes játékállapot még grafikai környezethez kötött

Forrás: `ForesTycoon/World/GameWorld.cs:16`, `:59`, `ForesTycoon/Rendering/Scene/TerrainRenderer.cs:33`.

A Map és Ecology külön is futtatható, de a `GameWorld` a térképet a `Terrain` jeleneten keresztül éri el, és mindig renderelőt, valamint feltöltött geometriát hoz létre. Ezért egy teljes játék mentésének és gazdaságának teszteléséhez jelenleg grafikai környezet is szükséges. A világ állapotának legyen közvetlen `TerrainMap` tulajdona, és az állapot létrehozása legyen elválasztva a megjelenítés csatlakoztatásától. Az Editor ugyanennek a grafika nélküli állapotnak egy külön nézetét használhatja.

## Projektenkénti értékelés

| Projekt | Motor szempontból értékelt állapot és következő feladat |
| --- | --- |
| Engine | Jó, kis hatókörű alap: fix időlépés, sorrendtartó rendszerek, háttérmunka publikálása. A teljes világ hibás frissítés utáni folytathatóságát és a kieső szimulációs idő mérését tovább kell specifikálni. |
| Ecology | Erős elkülönítés, explicit folyamatfázisok, több időskála, checkpoint-validáció és hibás runtime lezárása. A teljes játék determinisztikáját is ilyen szigorral kell ellenőrizni. |
| Map | Jó adatmodell és renderfüggetlenség, közös közlekedési hálózat. Az útkeresési költségnek végig meg kell maradnia a felsőbb döntésekben. |
| TreeModels | CPU-geometria elkülönítve, ökológiai alakleírásból épít. A generálás eredménye legyen megosztható, megszakítható és mérhető a betöltési feladatokban. |
| Rendering | Backend-szerződések, rendezetten futó passok és állapotscope-ok jó alapot adnak. A statikus eszközállapot egy renderkörnyezetet feltételez; több nézethez explicit környezettulajdon szükséges. |
| Models | A CPU-póz és GPU-renderelő szétválasztása jó. Az import formátumrészhalmazát, a hibás assetek kezelését és a megosztott erőforrások tulajdonát további tesztekkel kell rögzíteni. |
| Effects | A felületi szerződés és időjárási állapot leválasztja a tereptől. Az effektminőségi szintekhez mérhető részecske-, memória- és időkeret szükséges. |
| Rules | Privát fordított gráf, kör- és egységellenőrzés, értéktartomány-validáció jó alap. A kiadás reprodukálhatóságához a mentés a futtatási szabályok verzióját is egyértelműen rögzítse. |
| Editor | Külön alkalmazás, a játék nem függ tőle. A próbaszimulációt grafika nélküli játékállapotra érdemes építeni. |
| ForesTycoon | Itt összpontosulnak a bizonyított életciklus- és elszámolási hibák, valamint a megjelenítéshez kötött világfelépítés. Ez az első javítási célterület. |
| Tests | 1494 átment teszt és architekturális ellenőrzések. A mentés utáni jövőazonosságot, az új világ teljes nullázását és a fázisokon átívelő gazdasági mérlegeket bővíteni kell. |

## Ellenőrzések

- `dotnet test ForesTycoon.sln --no-restore --verbosity minimal`: 1494 sikeres, 0 hibás, 0 kihagyott teszt.
- `dotnet build ForesTycoon.sln --no-restore -c Release --verbosity minimal`: mind a 11 projekt lefordult, 0 hiba, 7 CS8600 figyelmeztetés a `TreePreviewDump.cs` tesztsegédben.
- Két ideiglenes célzott próba igazolta az útöregedési részidő, az új világ kiadása és a teherautó üzemanyag-alapja körüli hibákat. Az ideiglenes tesztforrás eltávolítva; a próbák a jelenlegi hibás viselkedést ellenőrizték.
- A játék `--smoke-test` futása sikeresen befejeződött Windows alatt, natív OpenGL környezetben. Ez indulási és rajzolási ellenőrzés; nem nagy térképes teljesítménymérés vagy képi minősítés.

## Javasolt megvalósítási sorrend

1. Mentés teljes időállapotának javítása, új világ nullázása, üzemanyag-mérleg és dokkválasztási költség javítása; mindegyikhez célzott regresszióteszt.
2. Grafika nélküli játékállapot és tranzakciós világcsere bevezetése. Sikertelen betöltés, regenerálás és erőforrás-létrehozás után ellenőrzött állapot maradjon.
3. Láthatóság szerint ütemezett geometriaépítés, memóriahatáros LOD-cache és első képkockáig mért betöltési költség.
4. Automatizált kiadási ellenőrzés: build, tesztek, támogatott platformok natív smoke tesztjei, rögzített képi minták és nagy térképes teljesítménykeretek.

A meglévő ökológiai és backend-határokat érdemes megtartani. A következő fejlesztési szakasz a pontos állapotfolytatásra, a világ biztonságos cseréjére és a korlátozott erőforrás-használatra koncentráljon.
