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

## Ellenőrzés

- 130 sikeres automatizált teszt, köztük kamerakivágás és importált modell.
- Grafikai smoke: eredeti mód visszaállítása, textúra/fény/árnyék kapcsolók, eső/vihar/felhő, köd, villám, szünet, ablakméret-váltás, alacsony/közepes/magas minőség és magas minőség visszaállítása.
- Erdő smoke: LOD, változatlan kép cache-újrahasználata, terep/út/erdőművelés érvénytelenítése, növekedési építési sor befejezése.
- Terhelésmérés minden minőségi szinten 1/25/100 járművel; OpenGL-hiba ellenőrzése.
- Kanyarbeli teherautó képi ellenőrzése; a kormányzás és rugózás megmaradt.

A futó játék és Visual Studio miatt zárolt normál build helyett a legutolsó változat külön, `artifacts/engine-validation` mappában is le lett fordítva és tesztelve. Innen a `run-game.ps1` indítja el. A normál projektbuild a futó játék bezárása után használható.
