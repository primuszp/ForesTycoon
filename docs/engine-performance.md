# Motoráttekintés és teljesítmény – 2026. október 3.

## Egyedi fák renderfrissítése – 2026. október 4.

A havi növekedés már nem generálja újra az eljárásos fák meshét. Chunkenként két RGBA32F texel/fa tárolja a meshhez viszonyított méretet, az éves növekedési sebességet és az egészséget. A törzs és a korona saját vízszintes skálát használ; a textúrázott, eredeti színalapú, kontúr- és árnyékmenet ugyanazt a növekedési transzformációt alkalmazza. A nem erdészeti rajzolások törlik az erdőspecifikus shaderállapotot.

A növekedési revízió különvált a topológiai revíziótól. Új egyed, eltávolítás vagy lebomlott holtfa megváltoztatja a topológiát; egy havi méret-, ráta- vagy egészségfrissítés csak a kis állapotpuffert tölti fel. A topológiai revízió a tárolón belül egyedi, így az azonos darabszámmal újra létrehozott patch sem használhat régi geometriát.

A közeli, közepes és távoli eljárásos erdőgeometria külön cache-ben marad, a meglévő hiszterézises LOD-választással. Betöltéskor előkészülnek. A távoli változat nem rajzol élő törzseket vagy talpdecalokat. A statikus terep előkészítése most ténylegesen a megadott chunklistát dolgozza fel.

Az életfázisváltás és a természetes állományváltozás egyetlen, megszakítható meshépítési feladatot használ. A feladat másolt faállapotból dolgozik, körülbelül 2 ms CPU-munkát kap a fő render menetben, és befejezésig a korábbi geometriát őrzi. A pufferfeltöltések között is átadja a vezérlést. Szerkesztés, új világ vagy elavult topológia megszakítja a feladatot; a felhasználói szerkesztés azonnali frissítés marad. Egyetlen fa feldolgozása, a tömb létrehozása és egy GPU-feltöltés oszthatatlan, ezért a keret nem kemény 2 ms-os időkorlát.

Ugyanazon RTX 5060-on, Debug buildben, 1280×720, 4× MSAA mellett, a kameramozgásos benchmark:

| Jelenet / futam | Előtte medián / p95 / maximum | Utána medián / p95 / maximum |
|---|---:|---:|
| Sűrű fixture16 / 1 | 6,02 / 1160,09 / 1288,19 ms | 6,97 / 10,33 / 71,93 ms |
| Sűrű fixture16 / 2 | 5,09 / 1095,17 / 1129,14 ms | 6,87 / 8,50 / 27,84 ms |
| world64 / 1 | 8,03 / 14,52 / 946,13 ms | 8,56 / 13,87 / 18,73 ms |
| world64 / 2 | 8,22 / 15,30 / 1211,67 ms | 10,33 / 15,66 / 25,41 ms |

A mozgó kamera mindkét futamban ugyanazon zoomtartományt járja be; a futam neve nem állandó zoomot jelent. A benchmark gyorsított, 30 másodperces erdőévet használ. A normál játék éve 1200 szimulációs másodperc. Az eredmény renderelést, GPU-befejezést és erdőfrissítést mér, nem a teljes UI-s játékot. A GL-próba közeli/közepes/távoli vertexszáma: 6 509 352 / 3 865 032 / 1 045 152.

Ellenőrzések: `dotnet test ForesTycoon.sln --no-restore`, `--forest-smoke-test`, `--tree-growth-smoke-test`, `--graphics-smoke-test`, `--logistics-smoke-test`, `--camera-benchmark`. A forest próba a havi mesh-újraépítés elmaradását, a halasztott életfázis-frissítés befejezését és a világ törlése utáni megszakítását is ellenőrzi.

Megmaradó feladat: nagy térképen az előkészített LOD-cache-ek memória- és betöltési költsége, valamint a nagy pufferfeltöltések ritka tüskéi. Ezekhez chunk-streaming, kisebb feltöltési egységek és később instancing szükséges. Az importált, eredeti GLB-k továbbra is egyedenként rajzolódnak.

A motor funkcióinak áttekintése után a renderelés ismételt CPU-munkáját csökkentettük, és explicit effektkereteket adtunk hozzá. Az eredeti színes mód, a kapcsolható textúrázás, a járművek kormányzása és rugózása megmaradt. A minőségbeállítás nem változtatja meg az erdőgazdálkodást, a szállítást vagy a mentések visszajátszását.

## Megvalósított optimalizálás

- Shader-paraméterek helyének gyorsítótára, a program törlésekor érvénytelenítéssel: az OpenGL újrahasznosított programazonosítói sem kaphatnak régi paraméterhelyet.
- A napfény, időjárás, árnyékmátrix és egyéb közös paraméterek feltöltése egyszer történik képkockánként. A kamera csak változáskor kerül újra feltöltésre; a modellmátrix és az anyag az egyes rajzolásokhoz igazodik.
- A teherautó 33 része 13 rajzolási csoportba kerül. Azonos forgáspontú kerékanyagok összevonva; a karosszéria egy csoport. A hat rakományrész és a hat kerék külön kezelhető maradt. A háromszögszám és a csúcspontszínek változatlanok.
- Konzervatív gömbalapú kamerakivágás a járművekhez: a fő menet kihagyja a teljesen képernyőn kívüli modelleket és ezek vizuális mozgásszámítását. A képernyőszélt átfedő modellek megmaradnak. Az árnyékmenet továbbra is tartalmazhat képernyőn kívüli árnyékvetőket.
- A köd élőhelyadatai csempénként gyorsítótárba kerülnek. A kulcs a kvantált erdőállapot, a terepverzió és a LOD; erdőművelés, növekedés és terepváltozás érvényteleníti az érintett adatot. A lombkorona-célpontok csak új villámeseménynél készülnek el.
- A köd feltöltési tömbje kapacitást újrahasználó puffer. Ha nincs kirajzolható részecske, mélységmásolás sem történik.
- Árnyék nélküli képkockán megszűnt a látható terep ismételt kiválasztása. Az erdő növekedési modellépítési sora képkockánként egyszer dolgozik a meglévő 2 ms-os együttműködő keretben. Egyetlen fa feldolgozása és a végső GPU-feltöltés továbbra is oszthatatlan művelet.

## Minőségbeállítás

A **Nézet** menüben az **Effektek minősége** választó működés közben állítható. A magas beállítás őrzi a korábbi részletességet.

| Keret | Alacsony | Közepes | Magas |
|---|---:|---:|---:|
| Árnyéktérkép oldalhossza | 512 | 1024 | 2048 |
| Ködforrások felső kerete | 256 | 512 | 768 |
| Részecskék ködforrásonként | 2 | 3 | 3 |
| Esőrészecskék célkerete | 1500 | 3000 | 6000 |
| Felhőtérfogat mintái | 6 | 8 | 12 |

Az eső világkoordinátákhoz rögzített cellákkal és a látómező alapján változó cellamérettel tartja a keretet. A köd forrásai az erdőhöz, vízhez és völgyekhez kötődnek, az időjárási viselkedés változatlan. Az árnyéktérkép méretváltása újrafoglalja ugyanazt az erőforrást; a szűrés a tényleges textúraméretet használja. A felhő integrálási lépéshossza és fényelnyelése a mintaszámhoz igazodik.

## Mérések

Gép: NVIDIA GeForce RTX 5060, OpenGL 3.3, Debug build, 1280×720, 4× MSAA, rejtett tesztablak. Az idő a CPU-munkát és a `GL.Finish()` által megvárt renderelési befejezést is tartalmazza: nem külön GPU-idő, és nem általános FPS-garancia. A `GL.Finish()` kizárólag a diagnosztikában szerepel. Memóriafoglalás: az aktuális renderelő szál managed foglalása, nem teljes RAM/VRAM.

Az összehasonlítható alapmérésben az időjárás ideje 0, ezért a presetből még nem épült fel eső vagy felhőzet; a természetes talajköd aktív. Ugyanaz a seed, kamera, geometria és magas minőség szerepel előtte és utána.

| Alapjelenet | Előtte medián / p95 | Utána medián / p95 | Foglalás előtte → utána |
|---|---:|---:|---:|
| 16×16 teszt, zoom 10 | 2,22 / 4,18 ms | 1,30 / 1,68 ms | 6840 → 2816 B/kép |
| 64-es világ, zoom 10 | 9,65 / 10,38 ms | 5,31 / 5,97 ms | 36504 → 3840 B/kép |
| 64-es világ, zoom 5 | 11,00 / 11,64 ms | 6,33 / 6,85 ms | 38648 → 3936 B/kép |

Külön terhelésmérés: 32×32 sűrű vegyes erdő, kifejlődött vihar 40 másodperces bemelegítéssel, mozgó teherautók. 60 mért kép; járműszimuláció is benne van, az erdő havi növekedése ebben a mérésben nem fut. A teherautók egy közös útvonalon közlekednek, ez renderelési terhelést vizsgál, nem forgalmi ütközésmodellt.

| Minőség | 1 teherautó medián | 25 teherautó medián | 100 teherautó medián / p95 |
|---|---:|---:|---:|
| Alacsony | 2,67 ms | 2,98 ms | 7,94 / 10,32 ms |
| Közepes | 2,36 ms | 2,91 ms | 8,03 / 9,70 ms |
| Magas | 2,82 ms | 3,11 ms | 8,18 / 8,77 ms |

A minőségek közötti kis eltérést mérési zaj is befolyásolja; ezen a jeleneten a 100 jármű rajzolásának CPU-költsége dominál. A közös shader-paraméterek képenkénti feltöltése előtt a már összevont modellekkel ugyanennek a magas minőségű, 100 járműves jelenetnek 16,41 ms volt a mediánja. A végső 100 járműves jelenet 2640 rajzolást és körülbelül 10 KB managed foglalást jelent képenként. A renderer előtöltése a sűrű 32×32 pályán körülbelül 0,9–1,1 másodperc.

Újrafuttatás a lefordított alkalmazással:

```powershell
dotnet ForesTycoon/bin/Debug/net8.0/ForesTycoon.dll --engine-benchmark
dotnet ForesTycoon/bin/Debug/net8.0/ForesTycoon.dll --engine-stress-benchmark
dotnet ForesTycoon/bin/Debug/net8.0/ForesTycoon.dll --camera-benchmark
```

## Áttekintett alrendszerek és fennmaradó korlátok

| Alrendszer | Jelenlegi alap / módosítás | Következő skálázási feladat |
|---|---|---|
| Terep és utak | Darabolt statikus geometria, kamerakivágás, verziózott érvénytelenítés | Lokális terepváltozások érvénytelenítésének további szűkítése |
| Erdők | Három LOD, hiszterézis, kvantált vizuális állapot, időkeretes növekedési frissítés | Minden chunk mindhárom LOD-jának teljes előtöltése helyett korlátozott, igény szerinti cache; nagy térképnél ez a fő indulási/memóriakockázat |
| Víz | Látható csempékre épülő dinamikus hullámgeometria | Stabil topológia GPU-n, hullámzás vertex shaderben; az alapmérésben a vízmenetek még jelentősek |
| Modellek | Egy közösen betöltött teherautó, összevont részek, kamerakivágás | Tömeges GPU instancing; nagyobb modellkönyvtárhoz referenciaalapú assetcache, távoli modell-LOD és CPU-másolatok elengedése |
| Járműanimáció | Fix szimulációs lépésből interpoláció, analitikus kormányzás/rugózás | Sok járműnél közös útvonal/pose-cache; csontvázas modellek esetén láthatósághoz kötött animációs frissítés |
| Köd/eső/felhő/villám | GPU instanced részecskék, élőhely-cache, minőségi keretek, eseményalapú villám | Nagy felbontáson félfelbontású köd/felhő kompozitálás, GPU-idő alapján szabályozott keretek |
| Erdőszimuláció | Tömbök újrahasználata, havi frissítés, csak releváns regenerációs jelöltek | Külön nagy térképes havi tick mérés; a mostani renderbenchmark nem igazol 256/512-es teljes szimulációt |
| Események és animációidő | Értéktípusú timeline, helyben tömörített lejárt effektlista | Nagy effektburstnél külön megjelenítési keret, a játékmeneti események megőrzésével |
| Háttérfeladatok | CPU-feladatok elkülönítve, korlátozott párhuzamosság és publikálás | A beadott feladatok sorhosszának korlátozása; modellimport CPU-részének ide helyezése, OpenGL-műveletek a renderelő szálon |
| Mentés/visszajátszás | Determinisztikus parancsnapló és fix tick | Hosszú játékokhoz ellenőrzőpont/snapshot; a betöltés jelenleg az eltelt tickeket visszajátssza |

További prioritás a korlátozott erdőcache és a tömeges jármű-instancing. A havi szimuláció külön mérése és első optimalizálása az alábbi új szakaszban szerepel. A motor nem tekinthető korlátlanul skálázhatónak.

## Havi erdőszimuláció és helyi kitermelés (2026-10-04)

Új, OpenGL és ablak nélküli mérés:

```powershell
dotnet build ForesTycoon/ForesTycoon.csproj -p:UseAppHost=false
dotnet ForesTycoon/bin/Debug/net8.0/ForesTycoon.dll --simulation-benchmark
```

A benchmark a 42-es seeddel, 16×16 méteres sík csempékkel, természetes vegyes és teljesen telepített érett tölgyállományokkal fut. Két hónap bemelegítés után hét havi frissítést és hét külön helyi kitermelést mér. A környezeti kapcsolat aktív, de az időjárás és víz nem lép: ez a havi erdőfrissítés költségét izolálja. Render, UI, betöltés, vízlépés és teljes világfrissítés nincs a mért időben. A „sűrű” jelenet minden csempén tartalmaz erdőt; nem 36 egyedes csemeteültetvény.

Változtatások:

- A versengés nem számol korona- és magasságválaszt olyan fa-párra, amelynek sem koronája, sem az önálló erdőmodellben használt gyökértere nem érintkezik.
- Egy pillanatképezett csempe törzskoordinátáinak befoglaló téglalapja és legnagyobb koronája konzervatív előszűrést ad: a teljesen távoli csempét nem kell egyedenként bejárni. A minimális kölcsönhatási sugár és a nagyobb gyökérsugár is benne van a korlátban.
- Ültetés, elhalás és kivágás utáni rátafrissítés a ténylegesen frissített egyedek valamennyi kétgyűrűs függőségét pillanatképezi. Ez legfeljebb négy gyűrű a módosítás körül. Az eltávolított állományok mintái törlődnek; az üres csempékhez nem foglalunk külön mintatárat.
- A hozzájáruló fa-párok összeadási sorrendje változatlan. A globális havi pillanatkép továbbra is megelőzi a ráták módosítását.

Debug build, ugyanazon gép, mediánok egy összehasonlítható mérési sorozatban (ms):

| Jelenet | Havi frissítés előtte → utána | Helyi kitermelés előtte → utána |
| --- | ---: | ---: |
| Természetes 32² | 4,12 → 2,96 | 0,17 → 0,06 |
| Természetes 64² | 17,86 → 12,61 | 0,64 → 0,13 |
| Természetes 128² | 77,38 → 54,51 | 2,45 → 0,38 |
| Érett tölgy 32² | 18,23 → 10,82 | 0,62 → 0,19 |
| Érett tölgy 64² | 75,59 → 45,00 | 2,47 → 0,34 |

A futások között van mérési zaj. A páronkénti előszűrés ezekben a jelenetekben körülbelül 30–40%-kal gyorsította a havi frissítést. A következő szakasz az időbeli felosztás külön eredményeit mutatja.

Az öt jelenet egyedazonosság-, méret-, egészség-, növekedés- és erőforrás-ujjlenyomata változatlan. `ForestCompetitionPruningTests` ezen felül az eredeti, szűrés nélküli páronkénti számítással ellenőrzi az eredményt: nagyon kis és túlméretezett koronákkal, mindkét vízmodellel, majd kivágás és időben előrehaladó növekedés után a helyi és teljes pillanatkép egyezését. 357 automatizált teszt és az egyednövekedési, illetve logisztikai grafikus próbák sikeresek. A mentésverzió változatlanul 4.

## Havi versengés fokozatos előkészítése

Az éles világ `ForestEnvironmentCoordinator` rendszere félmásodperces szimulációs lépésekben előkészíti a következő hónap határán várható geometriai versengést. A `ForestMonthlyPreparation` előbb minden állomány jövőbeli méretét pillanatképezi, majd egy második menetben számítja a fényt és növőteret. A munkamennyiség az állományok számából és a határig hátralévő lépésekből adódik; nem a gép sebességéből vagy faliórából. A képkockacsoportosítás és visszajátszás eredménye változatlan.

Az előkészítés nem módosít egyedeket, egészséget, havi vízösszegzést vagy publikált revíziót. A hónap végén a kész geometriai eredmény a tényleges havi besugárzással és vízellátással együtt kerül alkalmazásra. A kész versengési pillanatkép két újrahasználható puffer cseréjével kerül az erdőhöz; az új csemeték is a megfelelő, frissítés előtti állapotot olvassák. Az egy csempén azonos fajhoz tartozó termőhely- és vízválaszt nem számoljuk újra minden egyednél.

Globális vagy nem követett erdőrevízió-változás után az előkészítés újraindul. A helyi módosítások kezelését az alábbi javítási sorok szakasza részletezi. Hiányzó vagy érvénytelen eredménynél a szinkron számítás megmarad. Az egyedazonosító, index, revízió és célhónap együttes ellenőrzése megakadályozza a régi eredmény alkalmazását kivágás, újratelepítés vagy törlés után. Az önálló `ForestSystem.Update` továbbra is használható előkészítés nélkül.

Ugyanazon `--simulation-benchmark` hét mért hónapja, Debug build; a szinkron és előkészített változat azonos egyedujjlenyomattal:

| Jelenet | Szinkron havi medián | Előkészített hónapváltás medián / max | Minden előkészítési és havi lépés p95 |
| --- | ---: | ---: | ---: |
| Természetes 64² | 12,07 ms | 5,20 / 5,55 ms | 0,09 ms |
| Természetes 128² | 50,96 ms | 22,33 / 23,06 ms | 0,34 ms |
| Érett tölgy 64² | 43,32 ms | 14,85 / 18,65 ms | 0,40 ms |

A mérés a geometriai előkészítés és havi erdőfrissítés idejét együtt tartalmazza, de továbbra sem teljes világfrissítés: vízlépés, logisztika és render nincs benne. A növekedési ráták, egészség, elhalás, regeneráció és statisztikák alkalmazása még egyetlen hónapvégi lépésben történik. A két pillanatképpuffer és az egyedenkénti eredménytár több memóriát használ; a tömbök újrahasználhatók és világcserekor felszabadulnak.

Az első előkészítési változat a hónap utolsó félmásodpercében végzett kivágás/újratelepítés után teljes újraindulással 55,68 ms-os hónapváltást mért a 128²-es próbában (egy minta). Ezt a helyi szerkesztések alábbi részleges érvénytelenítése javítja. Nincs szigorú időkeret: egy állomány feldolgozása, a globális sor újraindítása és a hónapvégi alkalmazás továbbra is oszthatatlan.

`ForestMonthlyPreparationTests` a szinkron referenciával a teljes fa-, holtfa-, tönk-, készlet- és környezeti állapotot hasonlítja össze: normál és tört hónaphatárokkal, viharral, elhalással, közbenső és közvetlenül hónap végi kivágással/újratelepítéssel, törléssel és szünettel. 362 automatizált teszt, valamint a környezeti, egyednövekedési és logisztikai grafikus próbák sikeresek. Az új előkészítés a mentésformátumot nem változtatja meg.

## Helyi változások javítási sorai

Fakivágás és ültetés után az előkészítés megtartja a változatlan térképrészek pillanatképét és eredményét. A ténylegesen módosult növekedési ráták csempéi a pillanatkép-javítási sorba kerülnek. Ezek két szomszédgyűrűjének erőforrásszámítása külön javítási sort kap: a módosult ráta megváltoztatja a hónap végére előrejelzett koronát, ezért a közvetlen szerkesztési területnél távolabbi megfigyelők is érintettek. Egyetlen csempe művelésénél a rátafrissítés legfeljebb két, az erőforrásjavítás legfeljebb négy gyűrűre terjed ki.

A sorok csempeazonosítónként deduplikálnak, és a tényleges aktuális állományt olvassák. Eltávolított vagy újra létrehozott állományra nem támaszkodnak régi objektumreferenciára. A teljes alap-pillanatkép után először minden esedékes pillanatkép-javítás fut, aztán folytatódik az alap-erőforrásszámítás és a javítások. Új szerkesztés ismét előreveszi a pillanatkép-javítást. Amíg bármelyik sorban munka marad, nincs kész állapot és publikálás.

A részleges javítás kezeli az előkészítés elején, közben és végén végzett kivágást, az új állomány hozzáadását, az ismételt teherautós kitermelést és a halasztott területi ültetést. A végső revíziókönyvelés csak már ismert helyi módosításhoz kapcsolható. Globális frissítés, diagnosztikai geometriaátírás, törlés vagy célhónapváltás továbbra is teljes újrakezdést igényel. A már publikált pillanatképet helyi javítás nem módosíthatja.

A hónap utolsó félmásodpercében végzett kivágás/újratelepítés 128²-es próbája: **55,68 ms → 22,25 ms**. Az új változat utolsó lépése 9 pillanatképcsempét és 54 javítandó vagy még hátralévő erőforráscsempét dolgozott fel; nem indította újra a teljes térképet. Ez egy célzott mintamérés, nem teljes képkocka vagy általános FPS-garancia. A változatlan 128²-es jelenet hónapváltási mediánja 21,53 ms, maximuma 22,25 ms maradt.

367 automatizált teszt sikeres. Új ellenőrzések: szerkesztés a pillanatképépítés és erőforrásszámítás alatt, késői javítás munkamennyiségének térképmérettől független korlátja, ismételt kitermelés és területi ültetés összevetése a teljes szinkron állapottal, valamint a globális változások teljes újraindulása. A környezeti, egyednövekedési és logisztikai grafikus próbák is sikeresek; mentésverzió továbbra is 4.

Megmaradó korlátok: nagy szerkesztési terület sok helyi javítást is jelenthet, a globális változások teljes munkát okoznak. A hónapvégi alkalmazás következő előkészítési lépését az alábbi szakasz részletezi.

## Növekedési görbék és hónapvégi állomány előkészítése

A bontott havi profil szerint a 128²-es természetes jelenetben az előkészített versengés mellett a növekedés/egészség/elhalás még körülbelül 14,3 ms-ot, a kezdeti állományösszegzés 1,8 ms-ot igényelt. A `ProfileMonthlyWork` kapcsoló alapból kikapcsolt; a benchmark bekapcsolja, és külön méri az állományzárást, pillanatkép/magforrás előkészítést, növekedést/elhalást és regeneráció/publikálást. Ezek az utolsó hónap fázisai, nem a teljes futás fázisonkénti mediánjai.

Az előkészítés most egyedenként tárolja a hónaphatáron várható méretet, a térfogatnövekményt, a geometriai növekedési görbe tagjait és a növőtér négyzetgyökös válaszát. Csempénként előre összegzi a hónap kezdeti kor-, biomassza- és egészségadatait is. Mindez a meglévő, javítható második előkészítési menetben történik. Egy helyi rátafrissítés ezeket az adatokat is érvényteleníti; a javítás friss méretből és ütemből számol.

Hónapváltáskor a lezárt környezeti időszak tényleges víz- és fényadatai adják a végső szorzót. A fény fajspecifikus válaszát egyedenként egyszer számoljuk, a szezonális tagot egy rátafrissítéshez egyszer. Az eredeti lebegőpontos szorzási/osztási sorrend megmarad. A térfogatnövekmény csak a havi alkalmazáskor kerül az éves könyvelésbe; az előkészítés nem módosít élő fát, statisztikát vagy vízkészletet.

Mérés azonos benchmarkkal, hét hónap, Debug build:

| Jelenet | Hónapváltás medián előtte → utána | Új maximum |
| --- | ---: | ---: |
| Természetes 64² | 5,00 → 3,71 ms | 4,14 ms |
| Természetes 128² | 22,75 → 16,02 ms | 17,02 ms |
| Érett tölgy 64² | 14,27 → 10,46 ms | 12,79 ms |

A 128²-es jelenet előkészítési lépéseinek p95 ideje 0,37 ms; a késői helyi szerkesztés próbája 17,31 ms-os hónapváltást adott. A külön futások közötti zaj miatt a mért 128²-es havi medián 15,5–16,0 ms között változott. Az új geometriai/görbe-adattár kapacitásonként további 40 bájtot tárol egyedenként, a tömbfejléceken és csempeösszegzéseken felül; a tár újrahasználható, világcserekor elengedhető.

A benchmark ezután **valóban együtt futtatja a vizet, időjárást és erdőt** 900 szimulációs másodpercig, 30 Hz-es hívásokkal. Az első 200 másodperc bemelegítés; 700 másodperc mért tick. A teljes szinkron változat más képkockacsoportosítással ugyanazt a faállapotot, vízkészletet és transzspirációt adja.

| Együtt futó környezet/erdő | Tick p95 | Tick maximum | Hónapváltás maximum |
| --- | ---: | ---: | ---: |
| 64² | 0,83 ms | 14,27 ms | 8,45 ms |
| 128² | 3,35 ms | 20,31 ms | 20,31 ms |

Ez még nem teljes játék-képkocka: a render, UI, terepszerkesztés, vadállatok és logisztika nincsenek benne. A ráták végső szorzása, egészség/stressz/elhalás és regeneráció a hónaphatáron továbbra is egyetlen állapotfrissítésben fut. Tömeges elhalás, nagyobb térkép vagy globális változás más költséget adhat; nincs szigorú képkockaidő-garancia.

372 automatizált teszt sikeres. A fagyasztott régi növekedési képlet 2000 faj/méret/év/erőforrás esete pontosan egyezik az új görbefelbontással. Külön teszt igazolja, hogy az előkészítés nem publikál élő állapotot. Az öt korábbi benchmark állapotlenyomata változatlan; a környezeti, egyednövekedési és logisztikai grafikus próbák mentés/visszajátszás ellenőrzése is sikeres. A mentésverzió változatlanul 4.

## Játékvilág és havi élőhely-ellenőrzés

Az új `--world-benchmark` a valódi `GameWorld.Update` és `GameWorld.Draw` útvonalat méri, a GPU befejezését is megvárva (`GL.Finish`, csak a diagnosztikában). A kikapcsolható `ProfileUpdates` külön bontja a környezet/erdő, logisztika, vadállatok és egyéb rendszerek frissítési idejét. Normál játékban a profilozás kikapcsolt.

A mérés 64²-es alapértelmezett, seed 42-es természetes világot használ 1887 fával, magas grafikai minőségen, 1280×720 felbontásban, MSAA4 mellett. Külön napsütéses és viharos futás készül. A 90 másodperces szimulációs bemelegítést 30 kirajzolt bemelegítő képkocka és 600 mért képkocka követi, 30 Hz-es szimulációval; a mért szakasz egy hónaphatárt tartalmaz. A képkockánkénti JSON-minták az `artifacts/world-benchmark/sunny.json` és `storm.json` fájlokba kerülnek.

A havi profilban a szarvasok élőhelykeresése önmagában körülbelül 5 ms-ot igényelt. A teljes rangsor építésekor most csempénként egyszer gyűjtjük össze a törzspozíciókat, és ugyanazt a mintát használjuk a kilenc jelölt helyhez. A pozíciókeresés nem számolja ki a fa modelljét, méretét és megjelenését. A csempe-, szomszéd- és összehasonlítási sorrend megmarad, a kiválasztott legfeljebb 16 születési hely változatlan.

Már létező állatoknál a korábbi rendszer is csak az üres eredményt használta: megszűnt-e minden élőhely. Ez az ellenőrzés most az első megfelelő hely után befejeződik. Üres világ benépesítése továbbra is a teljes rangsorból történik. A havi változás nem cseréli le az állatokat és nem módosítja mozgásukat.

Debug build, NVIDIA RTX 5060, összehasonlítható futások (ms):

| Jelenet | Havi vadállat-frissítés előtte → utána | Teljes havi világfrissítés előtte → utána | Új update + render havi képkocka | Új képkocka medián / p95 / max |
| --- | ---: | ---: | ---: | ---: |
| Napsütés | 5,18 → 0,12 | 11,74 → 6,97 | 28,00 | 15,38 / 17,90 / 29,62 |
| Vihar | 5,07 → 0,07 | 8,31 → 3,12 | 22,39 | 15,52 / 18,27 / 27,29 |

A kirajzolás továbbra is jelentős költség: a havi képkockán 21,03 és 19,27 ms. Az új futások leglassabb képkockái a hónaphatár után jelentkeztek, 0,04 ms-os frissítéssel és 27–30 ms-os renderrel. Más futásban 50 ms-os rendercsúcs is előfordult. Ezek egy-egy futás mintái, nincs általános FPS-garancia. A mérés nem tartalmaz host inputot, UI-t, diorama utófeldolgozást, swapot vagy képkockaütemezést; járművek száma 0, ezért aktív logisztikai terhelést sem igazol. Világlétrehozás és kezdeti cache-feltöltés kívül esik a mért szakaszon.

372 automatizált teszt sikeres. A vadállatok grafikus próbája az eredeti, diagnosztikába fagyasztott élőhelyalgoritmussal pontosan összeveti a teljes helylistát, rangsort és az élőhely-létezést: kezdeti erdőben, növekedés, kitermelés, törlés és újratelepítés után. Ellenőrzi a meglévő állatok megőrzését hónapváltáskor, az élőhely nélküli világ kiürítését és a szokásos animációs/megjelenítési viselkedést. A mentésformátum változatlan.

## Erdőmesh feltöltése adagokban és renderfázisok mérése

A `--world-benchmark` most a renderfázisok CPU-idejét és OpenGL-időbélyegek közötti GPU-időt is rögzíti a JSON-mintákban (`RenderPasses`). A pipeline lépésein túl külön méri a képkocka előkészítését, az árnyéktérképet és a felhőket. A query-k csak a diagnosztikai futásban jönnek létre, eredményüket a benchmark meglévő `GL.Finish` hívása után olvassuk; normál játékban nincs query vagy GPU-várakozás. A GPU-időbélyeg-intervallum tartalmazhat parancsbeküldési szünetet is, a CPU-idő pedig driver-várakozást: a két idő nem összeadható. Az instrumentált benchmark kis többletterhelést okoz.

A napsütéses referenciafutás hónapváltás utáni leglassabb képkockájában a `props` erdőfázis CPU-ideje 14,40 ms volt. Az előkészítés már fokozatosan építette a fákat, de a feltöltés még teljes mesh- és növekedésitömb-másolatokat készített, majd egészben adta át őket a drivernek. A feltöltés most közvetlenül az építési listákból, legfeljebb 16 384 csúcsos adagokban történik. Ez adagonként legfeljebb 448 KiB csúcsadatot vagy növekedési metaadatot jelent. A két újrahasznált átmeneti tömb a teljes mesh méretétől függetlenül legfeljebb összesen 896 KiB; a meglévő építési listák és teljes GPU-tárak továbbra is szükségesek.

Az adagok között az építő visszaadja a vezérlést a meglévő, 2 ms-os együttműködő ütemezésnek. A csúcs- és növekedési attribútumok beállítását közös segédfüggvény végzi a közvetlen és az adagolt feltöltésben. Részben feltöltött buffer nem rajzolható; a régi erdőgeometria csak mindhárom új buffer és az aktuális egyedállapot elkészülte után cserélődik. Közbenső módosításnál a meglévő revízióellenőrzés eldobja a helyettesítő mesht. A növekedés, faazonosítók és mentésformátum változatlanok.

Ugyanazon instrumentált benchmark, Debug build, RTX 5060, 64²-es világ; külön futások mintái:

| Jelenet | Erdőfázis CPU maximum előtte → utána | Teljes képkocka maximum előtte → utána | Képkocka medián előtte → utána |
| --- | ---: | ---: | ---: |
| Napsütés | 14,40 → 6,50 ms | 30,43 → 27,09 ms | 15,82 → 16,17 ms |
| Vihar | 12,82 → 5,42 ms | 53,48 → 25,90 ms | 16,02 → 16,05 ms |

A mérés az erdőfázis csúcsának csökkenését mutatja; az átlagos képkocka nem gyorsult. A teljes maximum zajos, ezért a táblázatból nem következik általános 50%-os rendergyorsulás. A terep, víz és halak továbbra is folyamatos terhelést adnak. A teljes GPU-buffer lefoglalása, egy adag feltöltése és egy fa meshének előállítása oszthatatlan; nagyobb térképen vagy más driverrel továbbra is lehet megakadás. A kisebb adagok több GL-hívást jelentenek, a kezdeti feltöltés ára nincs ebben a mérésben. UI, diorama, aktív járműforgalom és más térképméretek továbbra sincsenek lefedve.

372 automatizált teszt sikeres. Az erdő grafikus próbája üres, kis, egy adag határán túlnyúló és többadagos bufferrel ellenőrzi a forrással byte-ra azonos GPU-csúcsadatot, a közvetlen feltöltéssel pixelre egyező növekedést három időpontban és a részleges mesh rajzolhatatlanságát. A halasztott publikálás/törlés próbája, az egyednövekedés mentés-visszajátszás próbája és a grafika/időjárás megjelenítési próbája is sikeres.

## Interaktív időgyorsítás és vékony rács

A felület 4×–256× fokozatokat kínál. A korábbi képkockánkénti 8 tickes korlát gyors gépen is korlátozta volna a nagyobb fokozatokat. Az interaktív futtató most legfeljebb 2048 fix, 30 Hz-es tickre jogosult képkockánként, de 8 ms-os együttműködő munkakeretben. Az időkeret ellenőrzése teljes tickek között történik; legalább egy esedékes tick mindig lefut. A szimulációs idő és a mentés tick-számlálója csak a ténylegesen végrehajtott lépésekkel halad, a környezet/erdő/szállítás sorrendje megmarad. A keret miatt bent maradó teljes időadósság eldobódik, a tört tick megmarad; ezért visszalassításkor nincs elnyújtott gyorsított felzárkózás. Terhelés alatt az elért gyorsítás kisebb a kiválasztottnál. Egy nehéz havi tick továbbra is átlépheti a 8 ms-ot.

A rács alapértelmezésben bekapcsolt. Minden zoomnál egyetlen 1 pixeles GL-vonalmenet fut, nincs közeli/távoli vastagságváltás vagy eltolás a kameramátrixon. Ez a közeli rács két korábbi rajzolási menetét egyre csökkenti. A korábbi benchmark-táblázatok a régi, kikapcsolt alapértelmezett rács mellett készültek; új alapbeállítású méréssel nem közvetlenül összehasonlíthatók.

381 automatizált teszt sikeres: a 8×/32×/128×/256× ütemezés, változatlan fix delta, budget miatti leállás, legalább egy tick előrehaladása és a visszalassítás utáni adósságmentes működés ellenőrzött. A grafikus erdőpróba három zoomértéknél egyetlen rácsmenetet és változatlan kameramátrixot igazol. A grafika/időjárás próbája a bekapcsolt alapértelmezést és a ki/be kapcsolás képi eredményét ellenőrzi. Kezelőfelület-képek: `artifacts/grid-speed-hud`; validációs build: `artifacts/grid-speed-validation/ForesTycoon.dll`.

## Ellenőrzés

- 130 sikeres automatizált teszt, köztük kamerakivágás és importált modell.
- Grafikai smoke: eredeti mód visszaállítása, textúra/fény/árnyék kapcsolók, eső/vihar/felhő, köd, villám, szünet, ablakméret-váltás, alacsony/közepes/magas minőség és magas minőség visszaállítása.
- Erdő smoke: LOD, változatlan kép cache-újrahasználata, terep/út/erdőművelés érvénytelenítése, növekedési építési sor befejezése.
- Terhelésmérés minden minőségi szinten 1/25/100 járművel; OpenGL-hiba ellenőrzése.
- Kanyarbeli teherautó képi ellenőrzése; a kormányzás és rugózás megmaradt.

A futó játék és Visual Studio miatt zárolt normál build helyett a legutolsó változat külön, `artifacts/engine-validation` mappában is le lett fordítva és tesztelve. Innen a `run-game.ps1` indítja el. A normál projektbuild a futó játék bezárása után használható.
