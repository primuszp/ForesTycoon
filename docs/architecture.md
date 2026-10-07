# Architektúra: rétegek és modulok, egyirányú függőség

```
 ┌────────────────────────────────────────────────────────────────────────────────────┐
 │ ForesTycoon  (a játék)                                                             │
 │  App · UI · Interaction · Camera │ World (GameWorld, parancsok, mentés, járművek)  │
 │  Terrain (a terep JELENETE: GPU) │ Rendering/* (jelenet, erdő-GPU, ragasztó)    │
 └──┬────────────┬───────────────┬──────────────┬───────────────┬─────────────────────┘
    │            │               │              │               │
    │     Map: a talaj (TerrainMap), Engine+Ecology fölött, GL nélkül – a játék köti
 ┌──▼──────────┐ │ ┌─────────────▼───┐ ┌────────▼──────────┐ ┌──▼──────────────────┐
 │ TreeModels  │ │ │ Effects         │ │ Models            │ │ (a játék közvetlenül│
 │ fajszintű   │ │ │ eső, hó, felhő, │ │ glTF/GLB betöltés,│ │  is használja)      │
 │ modell-     │ │ │ köd, villám,    │ │ animáció, kép-    │ └─────────────────────┘
 │ generálás   │ │ │ akciójelzők     │ │ és jelenet-eszköz │
 └──┬──────────┘ │ └──┬──────┬───────┘ └────────┬──────────┘
    │            │    │      │                  │
 ┌──▼────────────▼─┐  │   ┌──▼──────────────────▼────────────┐
 │ Ecology         │  │   │ Rendering (GPU-mag)              │
 │ víz · talaj ·   │◄─┘   │ eszköz, shader, pass-ok, puffer, │
 │ időjárás · fajok│      │ batch, utófeldolgozás, mérők     │
 │ · erdő          │      └──┬───────────────────────────────┘
 └──┬──────────────┘         │
 ┌──▼────────────────────────▼───────────────────────────────────────────────────────┐
 │ Engine: fix lépéses idő, rendszerek, háttérmunkák, animáció-idő, csúcspont-formátum│
 └────────────────────────────────────────────────────────────────────────────────────┘
```

A függőség csak lefelé mehet. Ezt a fordító (projekthivatkozások) és az `ArchitectureTests` is
kikényszeríti: a szimulációs és modellezési könyvtárak (Engine, Ecology, TreeModels) nem használhatnak
grafikai, ablakozó vagy UI csomagot; a render-modulok (Rendering, Models, Effects) GL-t igen, ablakot és
UI-t nem, és nem hivatkozhatnak a játékra vagy egymás fölötti rétegre.

| Szerelvény | Névtér | Felelőssége | Nem tudhat |
|---|---|---|---|
| `ForesTycoon.Engine` | `ForesTycoon.Engine` | `FixedStepClock`, `SimulationFrameRunner`, `IWorldSystem`/`WorldSystemCollection`, `BackgroundJobScheduler`, `AnimationTimeline`, `Vertex` | erdő, terep, GL |
| `ForesTycoon.Ecology` | `ForesTycoon.Ecology` | a természeti rendszer: időjárás, talaj, vízmérleg, fajok, erdőállomány és egyedek | terepháló, render, a játékvilág |
| `ForesTycoon.TreeModels` | `ForesTycoon.TreeModels` | fa- és cserjemodellek fajszinten: Arbaro-készlet → váz → háló | a világ, a render, a GL |
| `ForesTycoon.Map` | `ForesTycoon.Map` | a talaj mint adat és szabály: magasságrács, generálás, hidrológia, utak, szerkesztés, picking (`TerrainMap`); az erdő élőhelye (`IForestHabitat`) | GL, render, jármű, játékvilág |
| `ForesTycoon.Rendering` | `ForesTycoon.Rendering` | a render GPU-magja: `RenderDevice`, `GlProgram`, `RenderPipeline`/pass-ok, `VertexBuffer`, `DynamicPrimitiveBatch`, `DioramaPostProcess`, `RenderMetrics`, `ISurfaceVisuals` | erdő, terep, modellek, effektek |
| `ForesTycoon.Models` | `ForesTycoon.Models` | modellbetöltés és -rajzolás: `AnimatedGlbModel` (glTF/GLB), `AnimatedModelRenderer`, `ImportedSceneAsset` | járművek, erdő, terep |
| `ForesTycoon.Effects` | `ForesTycoon.Effects` | vizuális effektek: `WeatherRenderer`, `CloudRenderer`, `FogHabitat`, `WeatherVisualState`, `WorldEffectSystem` + `EffectRenderer` | terep, erdő (csak `IWeatherSurface`-en át látja a talajt) |
| `ForesTycoon` | `ForesTycoon` | játékvilág, a terep jelenete (`Terrain`), jelenet-ragasztó, felület, diagnosztika | – |

A render-modulok a játékkal **interfészeken** csatlakoznak, amelyeket a játék valósít meg:
`IShadingSettings` / `IPostProcessSettings` / `IWeatherSettings` (a `GraphicsSettings` mindhármat),
`IWeatherSurface` (a `TerrainWeatherSurface` adja a terep- és lombkorona-magasságmezőt), `ISurfaceVisuals`
(a `SurfaceVisualRenderer`). Új modul így a játék ismerete nélkül tesztelhető.

## 1. Ecology – a szimulációs egység

Mappák: `Climate/` (időjárás, `WeatherPreset`), `Soil/` (`SoilProperties`), `Water/`
(`EnvironmentSystem`: lombkorona-, felszíni, talaj- és mélyvíz; `EcologyTime`; `IForestCanopy`,
`IForestEnvironment`), `Species/` (`ForestSpecies`, `ForestSpeciesProfile` ökológia,
`ForestSpeciesTraits` katalógus, növekedés, életfázisok, fenológia), `Forest/` (`ForestSystem`,
egyedek és versengés, telepítés, kitermelés, `IForestHabitat`), `Shape/` (`TreeShapeSpec` szerződés).

* **Belépési pont:** `Ecosystem`. Összeköti az erdőt és a víz/időjárás modellt, és a helyes többütemű sorrendben
  lépteti őket (víz → erdőhónap → összegzők törlése). A játék ezt példányosítja, nem a részeket.
* **A világhoz kötés:** csak `IForestHabitat` (csempék, nedvesség, magasság, talaj, szomszédság,
  kezdeti erdőminta). A `Terrain` ezt valósítja meg; tesztben bármilyen apró megvalósítás elég.
* **A két alrendszer egymás felé** interfészen át lát: az `EnvironmentSystem` a növényzetet az
  `IForestCanopy`-n át (interceptió, levélfelület, faj), az erdő a vizet és fényt az `IForestEnvironment`-en át
  (sugárzás, vízválasz). Így az időjárás- vagy vízmodell cserélhető anélkül, hogy az erdőhöz nyúlnánk.
* **Determinizmus:** csak seedből és időlépésekből számol, nincs óra, nincs GL – fejetlenül tesztelhető
  (`ArchitectureTests.EcosystemRunsHeadlessWithoutTerrainOrRendering`).

### Új fajt felvenni
1. `ForestSpecies` új érték (`Species/ForestSpecies.cs`).
2. Ökológia: egy sor a `ForestSpeciesProfile.For`-ban (kor, növekedés, nedvesség, magasság, árnyéktűrés, faanyag).
3. Katalógus: egy sor a `ForestSpeciesTraits`-ban (név, méretek, koronaarány, ráták, színek, shader-család).
4. Fenológia (ha lombhullató): naptár a `TreePhenology.For`-ban.
5. Modell: egy Arbaro-formátumú XML a `ForesTycoon.TreeModels/Architecture/Presets`-ben, a katalógus `Preset` mezőjével.
A UI-lista a `ForestSpeciesTraits.Playable`-ből épül. Tesztek (`ForestSpeciesCatalogTests`) minden fajt végigpróbálnak.

### Új időjárás-/vízmodell
Valósítsd meg az `IForestEnvironment`-et (és kapd az `IForestCanopy`-t); `Ecosystem.Attach` cseréli be.

## 2. TreeModels – fajszintű modellgenerálás

Bemenet: `TreeShapeSpec` (faj, mag, életfázis, méret, életerő, termőhely, lombállapot). Kimenet:
`DendroTreeGenerator.Generate(spec, lod)` → törzs/ág/korona csúcspontok, vagy `TreeShapeModel.Measure(spec)` →
`TreeShapeMetrics` (faanyag, alaktényező, koronamutatók).

* `Architecture/`: `TreeArchitecture` (Arbaro-készlet + életfázis/fény módosítók, váz-gyorsítótár),
  `TreeSkeleton` (topológiailag pontos váz, pipe-model vastagság, `Validate()`), `Presets/*.xml`.
* `Shape/`: `TreeForm` (feloldott forma: méretezés, torzítás, szín, életerő), `TreeShapeModel`.
* `Meshing/`: `TreeWoodMesh` (törzs, gyökér, ágak, csupasz ágrendszer), `DendroCrownMesh` (koronafelhő),
  `DendroTreeGenerator`, `ForestLod`.
* `TreeScale` az egyetlen hely, ahol a méter→világegység arány és a shader-anyagcsalád kódja él.
Részletek: [tree-shape-system.md](tree-shape-system.md), [arbaro-parameter-sets.md](arbaro-parameter-sets.md).

## 3. Rendering, Models, Effects – a render három modulja

* **Rendering (GPU-mag):** egyetlen helyen él a GL-állapot, a shaderfordítás, a kamera/modellmátrix, a pass-sorrend
  (`RenderLayer`, `RenderPipeline`), a puffer- és batch-kezelés és a kép utófeldolgozása. Más modul ide épít, ide nem ér vissza.
* **Models:** csak azt tudja, hogyan lesz fájlból rajzolható modell: glTF/GLB elemzés, csontváz-animáció mintavétel,
  anyag- és alfa-mód, árnyék- és kontúr-rajzolás. Az eszközkiválasztás (melyik fához melyik modell) a játék dolga.
* **Effects:** minden, ami „időjárás és visszajelzés”: eső/hó részecskék, felhő, köd-szabályok, villám, és az
  akciójelző körök (`WorldEffectSystem`, az Engine `IWorldSystem`-je). Az időjárás állapotát az Ecology-ból olvassa
  (`WeatherPreset`, `EnvironmentSystem`), a talajt az `IWeatherSurface`-en át.
Új effekt: egy új osztály az `Effects`-ben, a beállításai egy kis interfészen át; a játék köti be egy `RenderLayer`-re.

## 4. A játék – világ és megjelenítés

* `World/`: `GameWorld` (a játékállapot határa), parancsok és visszajátszható mentés, járművek, rönkszállítás,
  hatások, vadállomány. Itt vannak azok a szabályok, amelyek a terepet és a járműveket is ismerik.
* `Terrain/Render` és `Terrain/Forest`: a terep **jelenete** (lásd az 5. pontot) – csak rajzol.
* `Rendering/Gpu` (beállítások, ImGui-vezérlő), `Rendering/Forest` (fa-GPU állapot, LOD, régi/importált fa-modellek),
  `Rendering/Models` (jármű-modell), `Rendering/Scene` (jelenet-rajzolók, amelyek a modulokat összekötik).
* A render csak olvas: `ForestSystem`-ből egyedeket, `TreeShapeSpec`-et épít (`TreeShapeSpec.From`), kéri a modellt, és
  feltölti. Soha nem módosítja a szimulációt. A fa újraépítése sávhatárhoz (`ShapeKey`) és fázis-/lombváltáshoz kötött.

## 5. A terep felelősségi köre: `ForesTycoon.Map` és a `Terrain` jelenet

A terep két, egyértelműen elkülönített dolog, nem egy osztály:

| | `TerrainMap` (`ForesTycoon.Map`) | `Terrain` (játék, `Terrain/Render`, `Terrain/Forest`) |
|---|---|---|
| Szerepe | a talaj **ténye és szabálya** | a talaj **megjelenítése** |
| Birtokolja | magasságrács (node/tile), hidrológia és nedvesség, úthálózat és a befagyasztott úttest-magasság, épületlábnyomok, a szerkesztő-kurzor, hover | csúcspont-pufferek, látható chunkok, víz-/út-/kellék-/erdő-/időjárás-rajzolás, útelőnézet |
| Műveletek | generálás, domborzat-szerkesztés (atomi, út- és épület-ellenőrzéssel), útépítés/bontás, útelhelyezés-elemzés, felszín-osztályozás (`TileSurface`: fű / földmű), sugár- és képernyőpont-keresés, felszíni magasság, élőhely-lekérdezések (vadállomány-helyek, halélőhelyek), az időjárás magasságmezője és határai | `UpdateVisibleTiles`, `Draw*`, GPU feltöltés |
| GL | **soha** (csak `OpenTK.Mathematics`) | igen |
| Függ | Engine, Ecology (ő az `IForestHabitat`) | a `TerrainMap`-től, nem fordítva |

A térképet a render **követi**, nem módosítja: `NodesChanged` (magasságok változtak), `EditsFlushed` (egy
szerkesztés-köteg kész → GPU feltöltés), `RoadDiagonalsChanged` (új kanyar-átlók), és a `SurfaceVersion`
(gyorsítótárak érvénytelenítése). A játéklogika (`GameWorld`, `ForestryLogistics`, `VehicleRoadRoute.Create`,
`Ecosystem`) a `TerrainMap`-et kapja; `Terrain.Map` adja a jelenet mögötti talajt. Így a terep szabályai
fejleszthetők és tesztelhetők (`TerrainMapTests`) kijelző nélkül, és a renderer cserélhető.

**Hova tegyek új dolgot?** Új terepszabály, mező, lekérdezés, szerkesztés → `TerrainMap`. Új vizuális
(szín, anyag, rajzolási sorrend, GPU-puffer) → `Terrain`. Ha a rajzolónak új tény kell, a térkép kapjon
egy lekérdezést vagy eseményt – a térkép sosem hív rajzolót.

## Adatfolyam egy fára

```
 ForestSystem (egyed: faj, kor, méret, egészség, erőforrások)
      │  TreeShapeSpec.From(tree, év, TreeSite)   ← ForestTreeSites: rés-irány, szél
      ▼
 TreeModels.DendroTreeGenerator.Generate(spec, LOD)  →  Vertex[] törzs / ág / korona
      ▼
 Terrain.ForestIndividuals: csempe-gyorsítótár, VBO-k, GPU-állapot (méret, egészség)
```

## Következő lépések
* A rajzoló részek (`Terrain/Render`, `Terrain/Forest`, `Rendering/Scene`) külön `ForesTycoon.Scene` szerelvénybe
  vihetők; a térkép-oldali szétválasztás ehhez már megvan.
* A parancs- és mentésréteg (`World/Commands`, `World/Persistence`) ugyanígy kivehető.
* A `GlbTruckModel` és a fa-GPU állapot (`Rendering/Forest`) általánosítható és a `Models`-be vihető.
* Az `Ecosystem` mentés-pillanatképe (ellenőrzött snapshot + naplórészlet) a hosszú játékokhoz.
