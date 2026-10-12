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
