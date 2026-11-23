# Kettős-gerinc – pálya és fejlesztői áttekintés

A [pályafájl](../ForesTycoon/Content/Scenes/kettos-gerinc.project.json) a Kőris-patak és a Kettős-gerinc 64 × 64 csempés, szerkeszthető jelenete. A domborzatot, a talajtípusokat, a forrásokat, az induló tisztásokat és az építményeket a jelenet tárolja. A terep és a talaj alapján a játék maga állítja elő a tesztvilágot, majd a jelenet két induló erdőgráfja választja ki a fásodás sűrűségét és a fajokat.

```powershell
dotnet run --project ForesTycoon.Editor -c Release -- --project ForesTycoon/Content/Scenes/kettos-gerinc.project.json
dotnet run --project ForesTycoon -c Release -- --scene-project ForesTycoon/Content/Scenes/kettos-gerinc.project.json
```

Az editor **Jelenet… → Új generált pálya** művelete külön fájlútvonalat állít be. A **Mentés másként** művelettel saját nevet és helyet adhatsz a jelenetnek. Az **Erdő** rétegen a tisztás ecset üres kezdőterületet készít a telephelyhez vagy malomhoz. A kivágás ecset faanyagot és tuskókat hagy. A **Magassági övek** és **Biomok** módosítása után a tesztvilág újraépítése frissíti a kezdő erdőt.

## Élőhelyek és úthálózat

| Élőhely | Induló állapot |
| --- | --- |
| Tó- és patakpart | Nyílt víz, szerves és agyagos talajfoltok, kőris–nyír a szárazabb csempéken. |
| Kőrises völgyalj | Magas kőris, kocsányos tölgy és mogyoró, folytonosabb fás borítással. |
| Völgyi rét és erdőszegély | Ritkás mogyoró, galagonya és tölgy. |
| Domboldal | Kocsánytalan tölgy, csertölgy és mogyoró, összefüggő fás foltokkal. |
| Hűvös hegyoldal | Bükk, hegyi juhar és elszórt jegenyefenyő. |
| Gerinc | Nyílt gyep és sziklafoltok, boróka, erdeifenyő és nyír. |

A vízfolyást két forrás táplálja. A telephely és a fűrészmalom közös hálózatra csatlakozik. Az aszfaltos bekötőút makadám főútra vált, amelyből két kiszolgáló ág és egy erdei közelítőnyom indul. A telephelyen egy harvester, egy forwarder és egy rönkszállító áll. A járművek a kiadott fuvarozási és erdészeti feladatokra indulnak.

A két kezdőerdő-gráf a Biomok oldalon kijelzett `tile.biome` indexet használja. Biom hozzáadása vagy törlése után ellenőrizd a gráfokhoz rendelt indexeket, és építsd újra a tesztvilágot.

A társulások közép-európai élőhelyek játékbeli közelítései. A pálya a rendelkezésre álló fajkészletet és a játék szintjeit, termőhelyi válaszait és időlépéseit használja. A készlet- és áramlásmérleg ellenőrizhető. A hosszú távú állományfejlődés és az egyes talajok vízforgalmának terepi kalibrációja további feladat. [Európai élőhelytípusok](https://eur-lex.europa.eu/eli/reg/2024/1991/oj) · [FAO gyökérzóna-vízmérleg](https://www.fao.org/4/X0490E/x0490e0e.htm)

Az induló erdő a jelenet övei és biomjai szerint vált társulást. A futó klíma és több növekedési szabály a közös, 0–1 közé szorított `tile.elevation` értéket olvassa, ami már a 6. terepszinten eléri a felső határát. A felső övek klímája és növekedése emiatt külön kalibrációt igényel. [A terep ökológiai magassága](../ForesTycoon.Map/Terrain/TerrainMap.cs)

## Osztályok és felelősségek

| Osztály | Megállapítás és módosítás |
| --- | --- |
| `RuleEditorView`, `EditorWindow` | A `--project` útvonal a sikeresen megnyitott jelenethez tartozik. Az új jelenet és a nyitójelenet másolata önálló célfájlt kap. |
| `MapUiState`, `MapEditorView` | A domborzatcsúszka megtartja a húzás értékét a gesztus végéig. Az ecset kitölti a képkockák közötti csempéket, és a térképen indított húzásból fest. A térképmód és a dokumentum váltása törli az érintett kijelöléseket. |
| `SceneSession`, `EditorSceneCommands`, `EditorDocumentHistory` | A tisztás és a kivágás ugyanazon dokumentumelőzményen keresztül szerkeszthető. Méretváltáskor a korábbi csempe- és csomópontazonosítókhoz kapcsolt adatok, köztük az utak és épületek, együtt törlődnek. A biomok és az övhatárok változása újraépítést jelez. |
| `BiomeEditorView`, `WorldScenario` | A megjelenítés és az induló erdő ugyanabból az öv- és biomdefinícióból dolgozik. A térképen a besorolás azonnal látszik, a szimulációs példány a pálya újraépítésekor veszi át. |
| `ProjectWorkflow`, `ProjectPersistence`, `SimulationProjectWorkflowRuntime` | A mentés a dokumentum pillanatképén futtatja a közös jelenetbetöltőt. Ha egy építmény hiányzik a tesztvilágból, a mentés konkrét csempeszámmal jelez hibát. A piszkozat és a célfájl atomikus, külön művelettel menthető. |
| `TerrainSettings`, `TerrainGenerator`, `TerrainElevationPlanner` | A Diamond–Square mintarácsát a generátor a kívánt rácsra képezi. A folyóvízgyűjtő és a szintkorlát ellenőrzése már ezen fut. A generátor natív méreteinél az eredeti mintarács marad. |
| `TerrainMap`, `RoadNetwork`, `RoadPathfinder` | Az editor a játékkal közös építési tervet és terepkorlátot használja. A szállítás a tényleges hálózati éleken keres útvonalat. A külön demonstrációs keresés csak a grafikai és terhelési próbákhoz választ közúti útvonalat. |
| `SimulationLoader`, `WorldLoader`, `SimulationWorld` | A szinkron játékbetöltés, a grafika nélküli alkalmazás és a szakaszos editor-előnézet ugyanazt a jelenetépítési folyamatot használja. A friss pályán a domborzat a talaj, klíma és erdő inicializálása előtt készül el. A rögzített jelenetek a mentett állapotból indulnak, majd megkapják az új festést. Az építési hibák a kész tesztvilággal együtt jelennek meg. |
| `Ecosystem`, `ForestEnvironmentCoordinator`, `EnvironmentSystem`, `ForestSystem` | A környezeti lépés továbbra is a havi erdőfrissítés előtt fut. A forrás, csapadék, tározás és kifolyás a meglévő vízmérleget használja. A terep átépítésekor az induló erdő az új termőhelyből számolódik. |
| `SoilLandscape`, `EnvironmentCheckpoint` | A mentés az ellenőrzött talajrasztert és vízkészleteket állítja vissza. A talajprofilok határai és a vízmérleg zárása a visszatöltéskor is ellenőrzöttek. |
| `ForestryLogistics`, `VehicleSystem`, `RuleEngine` | Az induló útépítés ugyanazt a terep- és anyagkezelést használja, mint a játékos útépítése. A költségellenőrzés a játék közbeni beruházáshoz tartozik. Az eső és az áthaladás a meglévő szabályok szerint módosítja az útállapotot. |

## Regressziós ellenőrzés

- A `MountainSceneTests` az élőhelyek faji elegyét, az utak folytonosságát, a telephely és malom kapcsolatát, valamint az esőben futó, menthető fuvarozást ellenőrzi.
- A `SceneStructureTests` a megépíthető pályát és a meglévő úttól induló, akadályba ütköző szakasz hibajelzését ellenőrzi.
- A `WorldLoaderTests`, `SceneDocumentTests` és `SceneSoilEditingTests` az eredeti nyitójelenet állapotát, a rögzített pályák visszatöltését és a talajfestés vízmérlegét ellenőrzik.

A Windows Release build figyelmeztetés és hiba nélkül készült. Az 53 célzott teszt sikeres. A rönkszállító esőben 8 m³ faanyagot vitt a malomba, a közbeni checkpoint visszatöltése után a folyamatos és a szakaszos futás állapota egyezett. Az editor natív próbája és a pálya 120 képkockás játékpróbája lefutott. A nyitójelenet és az alapszabályfájl SHA-256 lenyomata megegyezett a munka előtt rögzített értékkel.

A teljes, 2050 tesztes futásban 2048 teszt átment. A két hibás tesztben egy korábbi forrásvíz-számlálót és egy kiváltott editori belépési pontot kellett frissíteni. A végleges builden mindkettő átment a 18 tesztes újraellenőrzésben. A kézi ablakvezérlés az Escape lenyomásakor megszakadt, az ellenőrzés a beépített natív próbákkal és az automatizált regressziókkal folytatódott.
