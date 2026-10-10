# ForesTycoon

**Erdőgazdálkodási játék egy élő, izometrikus diorámában.** Nevelj változatos erdőt, kövesd az évszakokat, építs utakat, és szervezd meg a kitermelést és a faanyag szállítását.

A ForesTycoon aktív fejlesztés alatt álló, később **fizetős játéknak tervezett** projekt. A GitHub a fejlesztés, a hibajelentések és a technikai dokumentáció helye; a megjelenés időpontja és ára még nincs meghirdetve. A pénzügyi támogatási lehetőség előkészítés alatt áll.

![A ForesTycoon jelenlegi nyári diorámája](images/preview/current-summer.png)

| Őszi lombszínek | Téli erdő |
| --- | --- |
| ![Fafajonként eltérő, tömör őszi koronák](images/preview/current-autumn.png) | ![Csupasz lombhullatók, havas örökzöldek és szürke rács](images/preview/current-winter.png) |

A képek a valódi játékablakból, ugyanarról a közeli nézetről származnak, 256× időgyorsítás mellett. A dioráma megjelenítése aktív.

## Jelenlegi játék és szimuláció

- **Élő erdő:** 16 fa- és cserjefaj, egyedi faazonosítók, kor, törzsátmérő, magasság, koronaméret, fény- és vízellátás. Növekedés, lombkorona-versengés, természetes újulat, önritkulás, tönkök és holtfa.
- **Évszakos dioráma:** fafajspecifikus koronák és felületi minták, eltérő arany, okker, réz és vöröses őszi lombszínek. Az egészséges tavaszi és őszi korona tömör marad; lombhullás végén eltűnik a lomb, télen a részletes ágrendszer látszik. Az örökzöldek megtartják tűleveleiket, a vörösfenyő lombhullató.
- **Időjárás és hó:** ősszel gyakoribb, tartós eső; nyáron rövid záporok és erős viharok; télen szállingózó, széllel sodródó hópelyhek. Csempénként változó hókészlet, szél által átrendezett hó és olvadásból származó víz.
- **Terep és víz:** procedurális térkép, folyók és állóvizek, helyi talaj- és klímaviszonyok, terepszerkesztés. A csemperács alapból bekapcsolt, télen és havas tájon hűvös szürke.
- **Erdészet és szállítás:** kitermelési terület kijelölése, soros telepítések, rönkdepók, fűrészmalom, rakodás és teherautós szállítás. A járművek a valódi úthálózaton haladnak; útmegszakadáskor megőrzik rakományukat.
- **Utak:** aszfalt, makadám és közelítő nyom, csatlakozások, kopás és javítás, nedves és havas útfelületek.
- **Vadállatok:** élőhelyhez igazodó szarvasmozgás, járási és legelési animáció; a vízben úszó halak.
- **Megfigyelés és mentés:** birtoktérkép, gazdálkodási nézetek, környezeti adatok, játékmentés és determinisztikus szimulációs folytatás.

Az alap 1× időben a szarvasok és járművek természetes mozgási ütemet kapnak. Új világban egy erdőév 900 szimulációs másodperc: a játék 1× naptárórájával ez körülbelül egy valós óra, 256× mellett elméletileg 14,06 másodperc. A tényleges előrehaladást a gép terhelése is befolyásolhatja.

Közeli nézetben a háttérben készülő új famodell nem cserélheti le a látható részletes modellt egy durvább LOD-ra. Az évszakos lombszín és lombtalanság a GPU-n követi az aktuális évet, modell-újraépítésre várakozás nélkül. A legutóbbi játékbeli próba két teljes év és 1633 képkocka alatt minden látható chunkot Near részletességen tartott 256× mellett.

## Fejlesztői indítás

Szükséges: **.NET 8 SDK**, OpenGL 3.3 core megjelenítés és Windows, Linux vagy macOS. Linuxon a használt ImGui.NET natív könyvtárhoz glibc 2.38 vagy újabb kell, például Ubuntu 24.04. A jelenlegi automatizált platformellenőrzések Windowsra és Linuxra készülnek.

```sh
dotnet restore ForesTycoon.sln
dotnet run --project ForesTycoon -c Release
```

A solution Visual Studióban is megnyitható. A megvásárolt, helyileg telepített modellek opcionálisak: nélkülük a játék a repóban szereplő modellekkel vagy helyettesítő geometriával fut. A megvásárolt modellek nincsenek a nyilvános repóban; részletek: [helyi assetek](docs/logging-facility-assets.md).

## Kezelés

| Művelet | Vezérlés |
| --- | --- |
| Kamera forgatása megfigyelő módban | Bal egérhúzás; bal/jobb nyíl |
| Kamera mozgatása / nagyítás | Jobb egérhúzás / egérgörgő |
| Kameradöntés / alapnézet | Fel/le nyíl / Home |
| Szünet / időgyorsítás | Space / a HUD sebességválasztója, 1×–256× |
| Megfigyelés / gondozás / építés | Q / W / R |
| Termelés / szállítás | E / T |
| Járművek / erdészet | V / F |
| Környezet / gazdálkodás / grafika | K / M / G |
| Mentés / betöltés | Ctrl vagy Cmd + S / L |
| Súgó / fejlesztői eszközök | F1 / F12 |

A gyorsmentés az operációs rendszer helyi alkalmazásadat-könyvtárának `ForesTycoon/quicksave.json` fájljába kerül. A részletes eszközök és gyorsbillentyűk a játék súgójában találhatók.

## Motor és szabályeditor

C# és .NET 8, OpenTK 4.9.4, OpenGL 3.3 core, GLSL és ImGui.NET. A fix lépéses szimuláció grafika nélkül is futtatható; a megjelenítés külön erőforrás-környezetet és háttérben épülő chunkgeometriát használ.

| Projekt | Feladat |
| --- | --- |
| [ForesTycoon](ForesTycoon) | Játékablak, HUD, interakciók, világ, mentés, erdészet és szállítás |
| [ForesTycoon.Engine](ForesTycoon.Engine) | Órák, fix lépések, rendszerek, feladatütemezés és animációs időzítés |
| [ForesTycoon.Ecology](ForesTycoon.Ecology) | Erdő, fajok, növekedés, talaj, víz, regionális klíma és időjárás |
| [ForesTycoon.Map](ForesTycoon.Map) | Terepadatok, generálás, hidrológia, utak és szerkesztési szabályok |
| [ForesTycoon.TreeModels](ForesTycoon.TreeModels) | Fafajspecifikus ágváz, törzs, korona és részletességi szintek |
| [ForesTycoon.Rendering](ForesTycoon.Rendering) | GPU-eszközök, shaderek, bufferek és renderelési környezetek |
| [ForesTycoon.Models](ForesTycoon.Models) | glTF/GLB modellek, csontvázak, animáció és megjelenítés |
| [ForesTycoon.Effects](ForesTycoon.Effects) | Csapadék, felhőzet, köd, villámlás és vizuális effektek |
| [ForesTycoon.Rules](ForesTycoon.Rules) | Verziózott szabálykatalógus és szerkeszthető szabálymodellek |
| [ForesTycoon.Editor](ForesTycoon.Editor) | Önálló szabályrendszer-editor |
| [ForesTycoon.Tests](ForesTycoon.Tests) | Szimulációs, architektúra-, mentési és erőforrás-tesztek |

```sh
dotnet run --project ForesTycoon.Editor -c Release
```

Az editor a játék szabályait, képleteit és kapcsolatainak katalógusát mutatja. Az útkopási modell paraméterei és kapcsolatai szerkeszthetők, saját tesztvilágban kipróbálhatók és JSON-ként menthetők. A játékban **F12 → Szabálymodell → Szabálymodell alkalmazása** tölti be a modellt. [Editor használata](docs/rule-editor.md).

## Ellenőrzés és dokumentáció

Az [Engine validation](https://github.com/primuszp/ForesTycoon/actions/workflows/engine-validation.yml) Windows/Linux buildet, egységteszteket és Linux Mesa/Xvfb natív grafikai próbákat futtat. A külön [hardveres ellenőrzés](https://github.com/primuszp/ForesTycoon/actions/workflows/engine-hardware-validation.yml) Windows natív és teljesítményvizsgálatokra szolgál.

```sh
dotnet build ForesTycoon.sln -c Release -warnaserror
dotnet test ForesTycoon.Tests -c Release
dotnet run --project ForesTycoon -c Release -- --smoke-test
```

PowerShellben a teljes ellenőrzőcsomag:

```powershell
./tools/verify-engine.ps1 -Native
```

A közeli, két erdőéves, valódi 256× játékbeli évszakpróba képkockánként ellenőrzi a részletességet és képeket ment:

```sh
dotnet run --project ForesTycoon -c Release -- --capture-seasons artifacts/seasonal-256x
```

A legutóbbi helyi ellenőrzésben **1589 egységteszt és a teljes Windows natív csomag sikeres**, a Release build figyelmeztetés és hiba nélkül készült. A tiszta GPU-n futó új teljesítménykapu-mérés még hátralévő feladat; a vizuális próba nem helyettesíti azt.

- [Architektúra és projektfüggőségek](docs/architecture.md)
- [Játékmotor code review és javítások](docs/engine-review-2026-10-10.md)
- [Nagy térképek teljesítményterve és mérési kapuk](docs/large-world-performance.md)
- [Ökoszisztéma és regionális folyamatok](docs/ecosystem-simulation-design.md)
- [Faegyedek életciklusa](docs/tree-individual-lifecycle-plan.md)
- [Fagenerálási irodalom és fajmodellek](docs/tree-generation-literature.md)
- [Időjárás, vihar és hó kutatási alapjai](docs/rain-storm-cloud-research.md)
- [Modellimport és GPU-erőforrások](docs/model-import-contract.md)

## Támogatás és visszajelzés

A pénzügyi támogatási oldal még nincs létrehozva; ide kerül a hivatalos link, amikor elérhetővé válik. Addig a projekt követésével, GitHub-csillaggal, valamint reprodukálható [hibajelentésekkel és ötletekkel](https://github.com/primuszp/ForesTycoon/issues) segítheted a fejlesztést. Hibajelentéshez add meg az operációs rendszert, a videókártyát, a használt revíziót és a reprodukálási lépéseket.

## Licenc és a tervezett fizetős kiadás

A cél az, hogy a ForesTycoon saját kódját mások ne használhassák engedély nélkül kereskedelmi termékben vagy bevételszerző szolgáltatásban. Ehhez a kiválasztott **PolyForm Noncommercial 1.0.0** licenc [változatlan tervezete](docs/licensing/PolyForm-Noncommercial-1.0.0.md) elkészült. **Ez még nem a teljes játék hatályos licence.**

A jelenlegi fagenerátor közvetlenül befordított DendroKit/Arbaro GPL-kódot és GPL-es származtatott paraméterkészleteket használ. A teljes összekapcsolt játékra ezért nem vezethető be egyszerűen kereskedelmi tiltás: előbb a GPL-függőséget kell megfelelően kiváltani vagy külön jogosultsággal rendezni. A harmadik felek licencei megmaradnak; a tervezet nem korlátozza az általuk megadott jogokat.

Az aktuális jogállás, a kivételek és a fizetős kiadás előfeltételei: **[LICENSING.md](LICENSING.md)**. A nyilvános forrás önmagában nem jelent szabad kereskedelmi felhasználási engedélyt.

## English summary

ForesTycoon is a forestry management game in development, built around a living isometric diorama, individual trees, seasonal weather, terrain and water simulation, and timber logistics. A paid release is planned; financial support options are not yet available. See the licensing status above and [LICENSING.md](LICENSING.md): the proposed noncommercial license is not yet in force for the integrated game because GPL components remain linked into it.
