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

Az első további prioritás a korlátozott erdőcache és a tömeges jármű-instancing. Ezek még nincsenek megvalósítva; a motor nem tekinthető korlátlanul skálázhatónak. A jelenlegi változtatások mérhetően javítják a meglévő funkciók működését, és állítható terhelési keretet adnak az effekteknek.

## Ellenőrzés

- 130 sikeres automatizált teszt, köztük kamerakivágás és importált modell.
- Grafikai smoke: eredeti mód visszaállítása, textúra/fény/árnyék kapcsolók, eső/vihar/felhő, köd, villám, szünet, ablakméret-váltás, alacsony/közepes/magas minőség és magas minőség visszaállítása.
- Erdő smoke: LOD, változatlan kép cache-újrahasználata, terep/út/erdőművelés érvénytelenítése, növekedési építési sor befejezése.
- Terhelésmérés minden minőségi szinten 1/25/100 járművel; OpenGL-hiba ellenőrzése.
- Kanyarbeli teherautó képi ellenőrzése; a kormányzás és rugózás megmaradt.

A futó játék és Visual Studio miatt zárolt normál build helyett a legutolsó változat külön, `artifacts/engine-validation` mappában is le lett fordítva és tesztelve. Innen a `run-game.ps1` indítja el. A normál projektbuild a futó játék bezárása után használható.
