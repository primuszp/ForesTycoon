# Laptopos renderelés a Kettős-gerinc pályán

Windows Release, Core i7-11800H, Intel UHD és NVIDIA T1200 Laptop GPU. A valódi játékablak 1280 × 720-as kezdő nézetén, bekapcsolt diorámával és magas grafikai minőséggel mértem. A mérőfutás 120 képkockás. Az összesítés a 31–120. képkockából készült. A GPU-időbélyegek közé parancsbeküldési várakozás is kerülhet. A diagnosztika minden képkockán megvárja a GPU-t, így a kapott idők azonos mérési mód mellett hasonlíthatók össze.

| Adapter | Medián előtte → utána | P95 előtte → utána | Erdő fő passza | Árnyéktérkép |
| --- | ---: | ---: | ---: | ---: |
| Intel UHD | 384,48 → 101,88 ms | 540,66 → 118,66 ms | 165,65 → 50,17 ms | 159,01 → 10,19 ms |
| NVIDIA T1200 | 44,57 → 19,63 ms | 50,96 → 26,74 ms | 18,02 → 5,90 ms | 14,17 → 1,08 ms |

A javított nyitójelenet a T1200-on 17,49 ms mediánt és 24,84 ms P95-öt mért. A Kettős-gerinc utolsó mintájában a beküldött csúcsok száma 71,36 millióról 11,85 millióra, az erdő GPU-buffereinek összmérete 2953,5 MiB-ról 587,2 MiB-ra csökkent. A bufferadat az erdő könyvelt mérete. A látható munkakészlet túllépheti a gyorsítótár puha keretét.

## A módosítások

- A játék a kezdő kamera által látott erdőhöz készíti elő a kiválasztott részletességi szintet. Az egész pályára betölti a távoli erdő egyszerű, példányosított geometriáját. A közeli modellek szeletekben épülnek, és a gyorsítótárból törölhetők. Távolításkor az újonnan látható részek első képkockája már a távoli réteget használja. A finomítás a kamera megállapodása után folytatódik. [Bruneton és Neyret](https://onlinelibrary.wiley.com/doi/full/10.1111/j.1467-8659.2012.03016.x) · [Losasso és Hoppe](https://hhoppe.com/geomclipmap.pdf)
- A Zoom alapján mód 10 pixel/világegység sűrűségű nézeten közepes modellt rajzol. Ebben a módban a legaprólékosabb szint 16-nál kapcsol be és 13-nál lép ki. Az előző modell látható marad, amíg az új változat feltöltése befejeződik, majd a motor nagyjából 220 ms alatt vált át. A két modell egymást kiegészítő, stabil képpontmaszkot használ, így a takarás működik az átmenet közben is. Ha a cél közben az előző szintre változik, az átmenet ugyanonnan halad visszafelé. A párhuzamos átmenetek 1,5 millió többletcsúcsnyi kereten osztoznak a két résztvevő modell közül a drágábbhoz képest. Egy kivételesen sűrű erdőszakasz önállóan túllépheti ezt a keretet. [SpeedTree, GPU Gems](https://developer.nvidia.com/gpugems/gpugems3/part-i-geometry/chapter-4-next-generation-speedtree-rendering) · [Unity LOD-átmenetek](https://docs.unity.com/en-us/engine/6000.5/manual/analysis/graphics-performance-profiling/lod/lod-group/lod-transitions-lod-group)
- Az árnyékpassz a mélységtextúra saját texelsűrűségéhez választ erdőmodellt. A kamera részletességi állapotát a fő erdőpassz kezeli. Az álló és kidőlt holtfa mindegyik szinten közös geometriával őrzi a megjelenési állapotát.

A mérést az alábbi parancs ismétli meg a `ForesTycoon.exe` natív ablakában. Az `exe` közvetlen indítása lehetővé teszi a programonkénti Windows GPU-hozzárendelést. Ezen a gépen az optimalizált mérőbuild saját `ForesTycoon.exe` fájlja a nagy teljesítményű adapterhez van rendelve. Másik exe-útvonalhoz külön Windows-beállítás tartozik. [Microsoft útmutató](https://support.microsoft.com/en-au/windows/hardware/display-graphics/optimizations-for-windowed-games-in-windows-11) · [NVIDIA útmutató](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/3D%20Settings/Setting_the_Preferred_Graphics_Processor.htm)

```powershell
& .\ForesTycoon\bin\Release\net8.0\ForesTycoon.exe `
    --scene-project ForesTycoon/Content/Scenes/kettos-gerinc.project.json `
    --profile-frames artifacts/laptop-render-review/rerun.json
```

A nyers eredmények a helyi `artifacts/laptop-render-review` mappában vannak. A JSON tartalmazza az OpenGL-adaptert, a képkockaidőket, a GPU- és CPU-passzidőket, a beküldött csúcsok számát és az erdő bufferadatait.

## Ellenőrzés

A Release build figyelmeztetés nélkül fordult. Mind a 23 célzott automatizált teszt és az öt natív grafikai próba lefutott. A natív próbák ellenőrizték a betöltést, az erdő részletességváltását és vizuális folytonosságát, az útépítést, az alpha anyagokat, a grafikai környezetek elkülönítését, az időjárást és az árnyékokat.

A hideg gyorsítótáras, 128 × 128 mezős stresszteszt nyolc erdőszakaszt kért a legnagyobb részletességgel. Az első képkocka 94 ms alatt elkészült. A teljes közeli geometria fokozatosan, 194 másodperc alatt jelent meg. A próba minden képkockán megvárja a GPU-t. Ez a mérés a hideg közeli geometria teljes felépülési idejét mutatja, külön a kezdő nézet képkockaidejétől.

A `--forest-lod-smoke-test` natív ablakban, rögzített kameránál hasonlítja össze a közvetlen és az átmenetes váltást. A szomszédos képkockák között mért legnagyobb összesített RGB-eltérés az azonnali váltás 11,6%-a volt egyszerű grafikával és 11,9%-a textúrákkal és árnyékokkal. A próba ellenőrzi a gyors irányváltást és a hideg gyorsítótárat is. Az átmenet bevezetésekor ismételt Kettős-gerinc mérésben az álló kezdő nézet erdőpasszának medián GPU-ideje 7,29 ms-ról 7,27 ms-ra változott. A két futás teljes képkockaidejének mediánja 63,9 és 61,4 ms volt. Ennek a visszamérésnek a nyers profiljai az `artifacts/forest-lod-review` mappában vannak.

A gyors távolítás próbájában egy közeli erdőszakaszról 16 szakaszra nőtt a látható terület. Az újonnan látható 15 szakasz fái már az első képkocka visszaolvasott képén szerepeltek, egy bájtos részletgyorsítótár-keret mellett. A próba azt is ellenőrzi, hogy egy új út helyi változást okoz a távoli megjelenítésben. A tölgy, bükk, nyír és juhar koronájának regressziós tesztje a közepes háló minden pontját megtalálja a közeli hálóban. A több minta a két szint közös burkolatán finomítja a koronát.

Az állandó távoli réteg élő fánként 112 bájt példányadatot tárol. A Kettős-gerinc azonos kezdő nézetének újabb, egymást követő T1200-as mérése 17,21 és 16,58 ms-os képkocka-mediánt adott, az erdő GPU-pufferei 587,2 és 446,0 MiB-ot foglaltak. A P95 21,27-ről 22,17 ms-ra változott. A profilok az `artifacts/forest-coverage-review` mappában vannak.

## Adaptív famodell

A Grafika ablak Famodell beállításának alapértéke az Adaptív. Közepes szintről indul, és ugyanazt a célszintet tartja zoomoláskor és pásztázáskor. Körülbelül egy másodpercig fennálló, 25 ms feletti renderterhelésnél visszalép egy szintet, ha az erdőrajzolás mérhető része a terhelésnek. A minőség emeléséhez legalább négy másodpercnyi becsült tartalék, kész erdőgeometria és megállapodott kamera kell. Terhelés miatti visszalépés után 30 másodpercig vár az újabb emelési próbával. A GPU-időbélyegek eredményét a játék akkor olvassa ki, amikor az OpenGL elkészült velük. [Kuth és mtsai., automatikus fa-LOD](https://gpuopen.com/download/Real-Time_GPU_Tree_Generation.pdf) · [OpenGL időmérés](https://registry.khronos.org/OpenGL/extensions/ARB/ARB_timer_query.txt)

A Rögzített közepes és Rögzített magas mód kézzel választ célszintet. A magas szint a becsült videomemória-keretbe illeszkedő nézetben aktív. A háttérben épülő magas modell tényleges pufferméretét a motor feltöltés előtt is ellenőrzi. Az elérhető GPU-memóriát a motor ott méri, ahol a driver jelenti, egyébként konzervatív kerettel számol. Memóriatartalék esetén a játék megőrzi a kamerából nemrég kikerült részletes modelleket. Új területen az állandó távoli réteg biztosítja a megjelenítést a hideg geometriapufferek feltöltése alatt.

A Kettős-gerinc 1280 × 720-as mérőfutásán a T1200 közepes szinten maradt mind a 120 képkockán, 17,36 ms állandósult mediánnal. Az Intel UHD a 18. képkockán távoli szintre kért váltást, és 40,47 ms állandósult mediánt mért. Az Intel első 17, közepes célszintű képkockájának mediánja 148,77 ms volt. A két adapter profilja az `artifacts/adaptive-forest-review` mappában található.

## A Visual Studio indítása

A ForesTycoon indítási profil az eredeti nyitópályát használja. A ForesTycoon – Kettős-gerinc külön indítási profil. A Grafika panel megmutatja az aktuális OpenGL-adaptert, a famodell célszintjét és a még készülő látható erdőrészek állapotát. A menüben választott famodellmód a helyi alkalmazásadat-könyvtárban marad.

Ezen a gépen a Visual Studio Debug kimenetét külön kellett a T1200-hoz rendelni. A nyitópályát az Intel UHD a távoli szintig egyszerűsítette. A T1200-on a magas modell teljes előállítása a rajzolás első képkockájától számolva rögzített magas módban 19,79-ről 5,38 másodpercre csökkent. Az Adaptív futásban a magas célszint 5,20 másodpercnél jelent meg, és az összes látható erdőrész 9,95 másodpercnél érte el. A hosszabb profilok az `artifacts/forest-icon-review` mappában találhatók.
