# Nyitójelenet-referencia – 2026-10-12

A közös betöltő bevezetése **előtt** rögzített, jó nyitójelenet. Kiinduló Git HEAD: `f3826bc`; a munkapéldány tartalmazta a korábbi ImGui buffer-/átméretezési javításokat is.

- `opening.world.json.gz`: a teljes, nulladik szimulációs lépésben mentett világ; kicsomagolva normál világmentésként betölthető. Terep, talaj, egyedi fák, víz, logisztika és szabályállapot is szerepel benne.
- `fingerprints.json`: a checkpoint részeinek és az összes grafikai beállításmezőnek SHA-256 lenyomata. Csak az algoritmusok fordítási revíziója és gépfüggő elérési útjai maradnak ki az összehasonlításból.
- `opening.png`: 1280 × 720 képernyőkép a valódi játék 60. renderelt képkockájáról, alapkamerával, aktív diorámával. Élő animációt tartalmaz, ezért nem pixelazonossági teszt.

A generálás változatlan `TerrainSettings.Default`: seed 42, 64 × 64 csempe, természetes erdő. A játék eredeti talajmodelljét használjuk; az editor kerekített talajértékeire történő átállítás még nem része ennek a lépésnek. High grafika, dioráma, tilt-shift (0,45), AO (0,8), színkorrekció és rács bekapcsolva.

Rögzítés: `dotnet run --project ForesTycoon -- --record-opening-reference <új-könyvtár>`; kép: `dotnet run --project ForesTycoon -- --smoke-test --capture-frame <új-könyvtár>`. A rögzítő parancs nem olvas editorprojektet. A képernyőképezés a normál játékutat járja be, beleértve az esetleges helyi projektkapcsolatot. A referenciát teszthiba miatt ne generáljuk automatikusan újra.

Az első átállási lépésben a `WorldLoader.CreateOpening` az eredeti indulási útvonalat őrzi. A `CreateProject` az editor eső-/világpróbája és a parancssori futtatók közös belépése: generálás és talajmodell → festett magasságok/víz → festett erdő → projekt szabályai → opcionális megjelenítés. A meglévő `StartScenario` ugyanazt a jelenet-inicializálást használja. Mentés folytatásakor a mentett terepet nem generáljuk újra a projekt alapján.

Ellenőrzés: `WorldLoaderTests` összeveti az induló világot a fenti lenyomatokkal, visszatölti az archivált mentést, teszteli a mentés körútját, és összehasonlítja a festett projekt két betöltési útját induláskor és 30 szimulációs lépés után. A kapcsolódó terep-, festés- és szabálytesztekkel együtt 17 teszt sikeres. A játék OpenGL indulási próbája és az editor fordítása sikeres; az előtte/utána képek vizuálisan ellenőrizve.

## Második lépés: hordozható editor-jelenet

Az `opening.project.json` már megnyitható jelenetprojekt. A `World.InitialState` verziózott, tömörített világállapotot tartalmaz; a projekt a terep-, erdő- és forrásfestést, valamint a szabályokat külön őrzi. Nem függ a mentést készítő gép elérési útjaitól, és nem ágyazza újra magába a korábbi projektek parancselőzményeit.

Megnyitás az editorban:

```powershell
dotnet run --project ForesTycoon.Editor -- --project docs/references/opening-scene/opening.project.json
```

A játék ugyanennek a dokumentumnak a jelenetét és szabályait tölti be:

```powershell
dotnet run --project ForesTycoon -- --scene-project docs/references/opening-scene/opening.project.json
```

Az editor Pálya lapján a **Nyitójelenet átvétele** visszahozza az eredeti kezdővilágot. A **Jelenet mentése** és **Megnyitás** a mellettük megadott fájlt használja. A normál Ctrl+S továbbra is a meglévő munkapéldány-mentés. Új referenciaprojekt a `--export-opening-scene <fájl>` editorparanccsal készíthető; az archivált referenciát nem kell felülírni.

A terep és az erdő festhető, források és szabályok módosíthatók. Egy erdőcsempe festése megőrzi minden érintetlen fa azonosítóját, helyét, korát, méretét és növekedési állapotát. A festés törlése az eltárolt kiinduló állapotot állítja vissza. A rögzített jelenet talajmodellje, seedje és mérete ebben a lépésben még nem szerkeszthető; a felület ezt jelzi, a betöltő pedig visszautasítja az ilyen módosításokat. A korábbi generált projektek továbbra is szerkeszthetők.

Ellenőrzés: 45 célzott teszt sikeres, beleértve a régi referenciát, a projekt JSON-körútját, a festetlen fák egyezését, a festés visszavonását, a sérült jelenet elutasítását és a mentés utáni továbbfuttatást. Az editor OpenGL-próbája az induló terep, ökológia és grafikai beállítások egyezését is ellenőrzi. A jelenetprojekt játékbeli, 120 képkockás indítási próbája sikeres; a dioráma képe vizuálisan összevetve a referenciával.

## Harmadik lépés: a rögzített talajmodell szerkesztése

A korábbi talajzár feloldva. A **Pálya → Talaj** rétegen festhető a rögzített jelenet, a **Típusok → Talajtípusok** lapon pedig módosíthatók a tulajdonságok és új típus is létrehozható. Használatban lévő típust nem lehet törölni. A seed és a méret továbbra is rögzített.

A talajfoltok azonosító szerint maradnak a helyükön, katalógus-átrendezés és generátorpreferencia-módosítás esetén is. Csak az ecsettel kijelölt csempék kapnak másik típust. A terep és az egyedi fák állapota megmarad. Változatlan projekt, illetve az eredeti típus önmagára festése esetén a betöltő pontosan az eredeti állapotot hagyja meg.

A víztartalom nem indul újra: alacsonyabb telítettségnél az új talajba nem férő víz a felszíni készletbe kerül. Nagyobb kapacitás nem teremt új vizet. A mentés külön őrzi a talajszerkesztés előtti induló vízmérleget; betöltéskor a teljes vízmérleg ellenőrzése továbbra is kötelező. A régi talajhoz készített havi növekedési előszámítás törlődik, a fák jelenlegi állapota megmarad.

Ellenőrzés: 78 célzott teszt sikeres. Ezek lefedik a referencia egyezését, a helyi talajfestést, az új típusokat és törlésvédelmet, a kapacitáscsökkentés vízmegőrzését, a mentés-visszatöltést, az újrarögzítést és a havi növekedési határon túli determinisztikus továbbfuttatást. A szerkesztett projekt parancssori futtatása és az editor OpenGL-próbája is sikeres.

Következő külön lépés lehet a küldetéscélok és eseményszabályok felvétele. Az editor jelenleg térképrétegeken szerkeszt; a diorámás 3D megjelenítés továbbra is a játékban ellenőrizhető.

## Negyedik lépés: a játék a jelenetdokumentumot futtatja, küldetésekkel

A játék alapindulása már nem a beépített generátor, hanem a `ForesTycoon/Content/Scenes/opening.project.json` jelenetprojekt (`WorldLoader.CreateStart`). Forráskönyvtárból futtatva a játék és az editor is ezt a forrásfájlt használja, így az editorban mentett jelenet újrafordítás nélkül indul; telepített futásnál a kimenet mellé másolt példány töltődik be. Ha egyik sem létezik, a régi `CreateOpening` útvonal marad. A `--scene-project` továbbra is felülírja.

Az editor induló tesztvilága ugyanebből a fájlból épül, a jelenetfájl mezője alapból erre mutat, a **Nyitójelenet átvétele** pedig ezt nyitja meg.

Új **Küldetések** lap: a `WorldScenario.Missions` célokat tárol (mérőszám: `world.*` mező, `world.deliveredTimber` vagy `stock.*` készlet; legalább/legfeljebb; cél; opcionális határidő erdőévben). A játék az Erdőnapló tetején mutatja az állásukat, és naplózza a teljesítést vagy a lejártat. Az állapot a futó világ értékeiből számolódik, külön mentett küldetésállapot nincs (egy már teljesült, de később visszaeső cél újra folyamatban lesz). Küldetés szerkesztése nem építi újra a tesztvilágot. Világ nélküli szabályprojekt (pl. a helyi `project.json`) alkalmazása már nem törli a futó jelenet térképét és küldetéseit.

Ellenőrzés: a teljes tesztsor (1726) sikeres, köztük a `MissionTests` (a szállított jelenet terep-, ökológia- és grafikai lenyomata egyezik a referenciával; küldetések körútja és kiértékelése; hibás küldetések elutasítása). A játék és az editor OpenGL-próbája sikeres.

## Ötödik lépés: a játék számai az editorban, egységes mentés

A **Szabályok** lap négy részre bomlik: **Képletek** (a korábbi képletszerkesztő), **Gazdaság** (faár-szorzó, hitelkeret, dízelár, építési és javítási költségek), **Gépek és járművek** (kapacitás, tempó, fogyasztás, kopás, javítás, járműfizika) és **Összes szám** (mind a kereshető hangolólistában). A számok szabályonként kártyán jelennek meg, egységgel, alapértékkel és visszaállítással. Elengedés után a tesztvilágban azonnal érvényesek, és a jelenetfájlba kerülnek. Az útburkolatok számai továbbra is a Típusok › Burkolatok lapon vannak.

A fejléc **Mentés · Ctrl+S** gombja a munkapéldányt és a jelenetfájlt is menti. Az állapotsor jelzői: a jelenetfájl neve; *mentve / mentetlen módosítás* (a fájl tényleges tartalmával összevetve); *tesztvilág naprakész / újraépítés kell* (kattintásra újraépít); a játék átvételi állapota.

Ha a játék a szerkesztett nyitójelenetből indul, a helyi `project.json`-kapcsolat már nem írja felül induláskor a jelenet szabályait. Csak az indulás után közzétett projektet veszi át („Alkalmazás a játékban”).

Ellenőrzés: 1727 teszt sikeres (új: `SceneRulesTests`), az editor és a játék OpenGL-próbája is sikeres.

## Hatodik lépés: Jelenet menü, visszavonás, képlet–szám kapcsolat

- **Jelenet… (Ctrl+O)** a fejlécben: aktív fájl, mentés, megnyitás és mentés másként tetszőleges útvonalra, a játék nyitójelenete, az eredeti generált nyitójelenet. Mentetlen módosításnál figyelmeztet. A Pálya lap bal oszlopában ennek helyén most a **Generálás** szakasz áll.
- **Vissza / Újra (Ctrl+Z, Ctrl+Y vagy Ctrl+Shift+Z)**: 100 lépés a pályafestésre, a talajtípusokra, a fafajokra, a forrásokra, a küldetésekre és a játék számaira. Egy lépés a lezárt szerkesztés (felengedett ecset, elhagyott mező). A tooltip megnevezi a lépést. Pályát érintő lépésnél a tesztvilág újraépül, szám- és küldetéslépésnél nem. Másik jelenet megnyitása törli az előzményt. A képletszövegek szerkesztése még nincs benne.
- **Képlet ↔ szám**: a képletnél a hozzá tartozó szabály számai szerkeszthetők („A szabály számai”). A számkártyákon pedig ugrógomb vezet a szabály képletéhez, ha van ilyen.

Ellenőrzés: 1727 teszt sikeres. Az editor smoke-próbája valódi ImGui-képkockákon visszavonja, majd újra végrehajtja egy küldetés felvételét és egy számmódosítást.

## Hetedik lépés: utak, fűrészmalmok és telephely a jelenetben

A `WorldScenario` új mezői: `Roads` (szakaszok: kezdő- és végcsempe, `Asphalt`/`Macadam` burkolat), `Sawmills` és `Depots` (a 2×2-es hely bal felső csempéje). A betöltő a festés után, a játékos építésének sorrendjében építi fel őket. Az építés ingyenes, a játék nulla egyenleggel indul. Épület alól az erdő kiirtódik, és az első telephely kapja a kezdő járműparkot (processzor, forwarder, rönkszállító). Ami nem épülhet meg (víz, lejtő, foglalt hely, út nélküli telephely), az a `SceneProblems` listába kerül, és az editor kiírja.

Editor: a Pálya lap **Utak és épületek** rétege szerkeszthető.
- **Út:** kattintás a kezdő-, majd a végcsempére. A további kattintások folytatják az utat, jobb gomb vagy Esc fejezi be. Az utat a játék útkeresése vezeti.
- **Malom és telephely:** egy kattintás. Előtte zöld vagy piros keret mutatja, hova építhető.
- **Építménylista:** rámutatásra kiemeli az elemet a térképen, és innen törölhető.
- Az építmények szerkesztése is visszavonható.

A rögzített nyitójelenet meglévő útjai és épületei változatlanok, a referencia-lenyomatok egyeznek.

Ellenőrzés: 1729 teszt sikeres. Új a `SceneStructureTests`: ingyenes építés járműparkkal, determinisztikus betöltés, hibás elhelyezés jelzése, érvénytelen út elutasítása. A játék és az editor OpenGL-próbája is sikeres.

## Nyolcadik lépés: a vízháztartás a projekt szabályai

A vízmérleg (csapadék, lombkorona, beszivárgás, párolgás, fák vízfelvétele, mélybe szivárgás, alaplefolyás, lefolyás, hóolvadás) a projekt szerkeszthető áramlásszabályaiból fut; a natív vízlépés és a hozzá tartozó régi képletcsatlakozások megszűntek. A lenyomatok közül emiatt a `Behaviors` (a megszűnt csatlakozások gráfjai kimaradnak) és az `Ecology` (a fák igény-/felvételi rasterei nélkül) változott; a terep, az erdő, a logisztika és a grafika lenyomata változatlan. Az archivált `opening.world.json.gz` megmaradt: betöltéskor ugyanerre az állapotra vezetődik át, ezt a `WorldLoaderTests` ellenőrzi.

## Kilencedik lépés: korábbi szabályalakok kivezetése – 2026-10-16

A szállított és archivált projektben, valamint a tömörített kezdeti világállapotokban
eltávolítottuk a hat megszűnt vízformula-kötést és az útromlás külön formula-kötését.
A vízállapotból kikerült a négy már nem használt igény-/felvételi raster. A betöltő ezekhez
már nem tart külön szabályátvezető vagy elhagyott mezőket elfogadó kódot.
A referencia-lenyomatok közül kizárólag a `Behaviors` változott az útromlási kötés törlése miatt;
a terep, az ökológiai állapot, a logisztika és a grafika egyezését külön összevetés ellenőrizte.

## Tizedik lépés: közös áthaladási kopás – 2026-10-16

Az út- és nyomkopás most a közös szabálykönyv `traffic.wear` eseménygráfja.
Eltávolítottuk a külön útkopási modellt, a `road.trafficWear` viselkedéskötést
és az öt hozzá tartozó régi hangolási kötést a projektadatokból is. A beágyazott
kezdőállapot és az archivált teljes mentés az új `.2026-10-16.3` futtatóhoz készült.

A frissítést először külön könyvtárba rögzítettük, majd összevetettük az előző
lenyomatokkal. Kizárólag a `Rules` kulcs szűnt meg és a `Behaviors` lenyomat változott;
a terep, az ökológia, az erdő, a logisztika és a grafika változatlan. A szállított
jelenet saját küldetéseit és pályaadatait megőriztük; csak a beágyazott runtime-állapot
és a megszűnt programkötések frissültek. A nyitójelenetben nincs áthaladás, ezért
a közös eseményszabály nem módosítja a kezdőállapotot.


## Tizenegyedik lépés: havi faéletciklus-gráfok – 2026-10-16

A `forest.health` natív viselkedéskötést eltávolítottuk a projektből és a beágyazott
világból. Az egészség, stressz, elhalás és holtfa-eltűnés a projekt havi gráfjai.
A futtató verziója `forestycoon-simulation/2026-10-16.4`.

Külön könyvtárban újrarögzített referencia alapján kizárólag az `Ecology` és
`Behaviors` lenyomat változott. A natív induló egészségközelítés megszűnt: a generált
fák az eredeti egészségükkel indulnak, azt az első havi gráf módosítja. A terep,
logisztika, grafika és minden más lenyomat egyezik. A jelenet küldetéseit és
szerkesztett pályáját megőriztük; csak a kezdeti runtime-állapot és a megszűnt
alapértelmezett programkötés frissült. Nincs betöltéskori régi szabályátvezetés.


## Tizenkettedik lépés: növekedési ráták a projektgráfból – 2026-10-16

A `forest.growth` natív eredményfelülírás megszűnt a kódban, a szállított
projektben és a kezdeti világállapotban is. A növekedési alapszabály a közös
szabálykönyv `growth` időzítésű gráfja. A futtató verziója
`forestycoon-simulation/2026-10-16.5`; nincs régi szabályt átvezető betöltési ág.

A külön mappában rögzített új referencia és az előző összevetése alapján csak
az `Ecology`, `Behaviors`, `PendingForestRateRefresh` lenyomat változott. A
natív induló rátaszámítás helyett az első logikai tick elején, szeletelten fut
le a növekedési gráf. Az évszakválasz a projekt szerkeszthető, kilencpontos
görbéje. Az előkészített geometria már nem tárol natív növekedési képlettagokat.
A terep, logisztika, grafika és minden más lenyomat egyezik. A szállított
jelenet küldetéseit, szerkesztett pályáját megőriztük.

## Tizenharmadik lépés: termőhely és vízstressz gráfok – 2026-10-16

Az alkalmasság, fafajfüggő vízválasz, aszály- és pangóvízsimítás szerkeszthető
projektgráfból fut. A natív Fitness és WaterResponse, a külön fajajánló-szorzó
és a régi `water.stress / tile` felülírás törölve. A futtató
`forestycoon-simulation/2026-10-16.6`; nincs régi szabályátvezető betöltési ág.

A külön könyvtárba újrarögzített referencia összevetése alapján csak az
`Ecology` és `Behaviors` változott. A kezdeti alkalmasság is a közös gráfból
származik, ezért a korábbi, nedvességet külön beszorzó induló egészség eltér.
A terep, a logisztika, a grafika és minden más lenyomat egyezik. A projekt
saját küldetéseit és szerkesztett pályáját megőriztük.

## Tizennegyedik lépés: fény- és térverseny a gráfokból – 2026-10-16

A szomszédok árnyékolása és térterhelése, az összegzett fény- és térválasz,
a fafaj fényválasza és az erőforráskorlát a projekt hat szerkeszthető gráfja.
A korábbi natív képletek, koronatávolság szerinti levágás és külön
gyökérverseny-ág törölve. A futtató `forestycoon-simulation/2026-10-16.7`.

Külön mappába újrarögzített és összevetett referencia alapján csak az
`Ecology` és `Behaviors` lenyomat változott. A minták a fafajt is tárolják,
a fény-/térszámítás a közös gráffuttató számértékeiből készül. A terep,
logisztika, grafika és minden más lenyomat egyezik. A szállított jelenet
saját küldetéseit és szerkesztett pályáját megőriztük; nincs régi
versenyszabályt vagy előkészítési értéket átvezető betöltési ág.


## Tizenötödik lépés: természetes erdőmegújulási gráfok – 2026-10-16

A projekt hét szerkeszthető gráfot kapott: magadó képesség, fajkiválasztás,
lombterhelési válasz, csírázás, csemeteszám, kezdő életkor és kezdő egészség.
A natív újulati feltételek, esélyképlet és biomassza-alapú csemeteszám
az újulat futási útvonalán megszűnt. Közös havi forráspillanatkép és szeletelt
csempe-/szomszédvizsgálat akadályozza meg a hónapon belüli továbbterjedést.
A futtató `forestycoon-simulation/2026-10-16.8`.

Külön mappába újrarögzített referencia alapján csak a `Behaviors` lenyomat
változott. Minden más kezdeti lenyomat, a saját pálya és a küldetések megmaradtak.
A referencia mentés az új gráfokat tartalmazza; nincs régi újulatszabályt
átalakító betöltési kód.


## Tizenhatodik lépés: fák kezdő méretgörbéi – 2026-10-16

A generált, festett, ültetett és természetes újulati fák kezdő magasságát,
törzsátmérőjét és koronasugarát a projekt három szerkeszthető gráfja adja.
A régi `ForestTreeGrowth.Initial` matematikai ága törölve; az adapter ugyanazt
a gráfmotort hívja. A létrehozási munka egyedenként is szeletelt.
A futtató `forestycoon-simulation/2026-10-16.9`.

A külön mappába újrarögzített és összevetett referencia alapján csak az
`Ecology` és `Behaviors` lenyomat változott: a double gráfkiértékelésből float
méretet tárolunk, így kis numerikus eltérés is lehetséges. A terep, logisztika,
grafika és minden más lenyomat egyezik. A szállított jelenet saját pályája,
küldetései és eddigi szerkesztett gráfjai megmaradtak. A helyi szerkesztett
projektben a már élő fák méretét nem írjuk át. Nincs legacy méretképlet vagy
régi futtatót fenntartó betöltési ág.


## Tizenhetedik lépés: egységes szabálykártyák és explicit kikapcsolás – 2026-10-16

A válaszgráfok kikapcsoláskor egy felhasználó által kiválasztott, mentett fix
értéket adhatnak; visszakapcsoláskor az eredeti gráf marad meg. A futtató
`forestycoon-simulation/2026-10-16.10`. Az aktív alapgráfok szerializált
készlete és végrehajtása változatlan: a külön újrarögzített nyitóvilág minden
lenyomata egyezik a tizenhatodik lépésével. A beágyazott és referencia mentés
verziójele frissült, az állapotadatok és a saját jelenetszerkesztések megmaradtak.
Nincs korábbi kikapcsolási jelentést átvezető betöltési ág.


## Tizennyolcadik lépés: környezeti projektgráfok – 2026-10-16

Az időjárási események és légköri válaszok, a helyi klíma, a párologtató
képesség, a lombkorona vízháztartási visszacsatolása, a hóhordás és a
burkolatlan talaj járhatósága a projekt 31 válaszgráfjából fut. A korábbi
járhatósági Native-kötést valódi bemeneti gráf váltja. A futtató
`forestycoon-simulation/2026-10-16.11`.

A külön újrarögzített nyitóvilággal összevetve csak a `Behaviors` lenyomat
változott. Az ökológiai indulóállapot, a terep, logisztika és minden más
lenyomat egyezik. A szállított jelenet saját beállításai megmaradtak.
A referencia és a beágyazott állapot az új futtatóval készült; nincs régi
környezeti képletet fenntartó futtatási ág. [Részletek](../../environment-rules.md).


## Tizenkilencedik lépés: járművek és fenntartás gráfjai – 2026-10-16

A tömeg-, gördülési, menetellenállási, vonóerő-, fogyasztási és sebességválaszok,
valamint a közös kopás/meghibásodás/javítás/karbantartás 17 projektgráfból fut.
A futtató `forestycoon-simulation/2026-10-16.12`. A frissen rögzített világot
összevetve csak a `Behaviors` lenyomat változott; a többi kezdeti állapot egyezik.
A jelenet saját szerkesztései megmaradtak. A fizikai integrátor és könyvelés
motor marad, a régi párhuzamos válaszszámítások törölve.
[Részletek, az 5/a és 5/b határa](../../vehicle-response-rules.md).
