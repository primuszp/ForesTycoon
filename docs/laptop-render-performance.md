# Laptopos renderelés a Kettős-gerinc pályán

Windows Release, Core i7-11800H, Intel UHD és NVIDIA T1200 Laptop GPU. A valódi játékablak 1280 × 720-as kezdő nézetén, bekapcsolt diorámával és magas grafikai minőséggel mértem. A mérőfutás 120 képkockás. Az összesítés a 31–120. képkockából készült. A GPU-időbélyegek közé parancsbeküldési várakozás is kerülhet. A diagnosztika minden képkockán megvárja a GPU-t, így a kapott idők azonos mérési mód mellett hasonlíthatók össze.

| Adapter | Medián előtte → utána | P95 előtte → utána | Erdő fő passza | Árnyéktérkép |
| --- | ---: | ---: | ---: | ---: |
| Intel UHD | 384,48 → 101,88 ms | 540,66 → 118,66 ms | 165,65 → 50,17 ms | 159,01 → 10,19 ms |
| NVIDIA T1200 | 44,57 → 19,63 ms | 50,96 → 26,74 ms | 18,02 → 5,90 ms | 14,17 → 1,08 ms |

A javított nyitójelenet a T1200-on 17,49 ms mediánt és 24,84 ms P95-öt mért. A Kettős-gerinc utolsó mintájában a beküldött csúcsok száma 71,36 millióról 11,85 millióra, az erdő GPU-buffereinek összmérete 2953,5 MiB-ról 587,2 MiB-ra csökkent. A bufferadat az erdő könyvelt mérete. A látható munkakészlet túllépheti a gyorsítótár puha keretét.

## A módosítások

- A játék a kezdő kamera által látott erdőhöz készíti elő a kiválasztott részletességi szintet. A tágabb térképen a költségkeretbe illeszkedő távoli modelleket tárolja. A többi teljes modell kameramozgáskor és zoomoláskor épül fel szeletekben.
- A 10 pixel/világegység sűrűségű kezdő nézet közepes modellt rajzol. A legaprólékosabb szint 16-nál kapcsol be és 13-nál lép ki. A látható modell használható marad, amíg az új változat feltöltése befejeződik.
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
