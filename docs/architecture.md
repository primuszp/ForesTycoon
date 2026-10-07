# Architektúra: négy réteg, egyirányú függőség

```
 ┌───────────────────────────────────────────────────────────────────────────┐
 │ ForesTycoon  (a játék)                                                    │
 │   App · UI · Interaction · Camera │ World (GameWorld, parancsok, mentés,  │
 │   járművek, rönkszállítás) │ Terrain │ Rendering (GL, pass-ok, shaderek)  │
 └───────────────┬──────────────────────┬─────────────────────┬──────────────┘
                 │                      │                     │
 ┌───────────────▼────────────┐ ┌───────▼───────────────┐     │
 │ ForesTycoon.TreeModels     │ │ ForesTycoon.Ecology   │     │
 │ fajszintű modellgenerálás: │ │ víz · talaj · időjárás│     │
 │ Arbaro-készletek → váz →   │ │ · fafajok · erdő      │     │
 │ törzs/ág/korona háló, LOD  │ │ egyedek, versengés    │     │
 └───────────────┬────────────┘ └───────┬───────────────┘     │
        TreeShapeSpec ◄────────────────┘ (szerződés)         │
                 │                      │                     │
 ┌───────────────▼──────────────────────▼─────────────────────▼──────────────┐
 │ ForesTycoon.Engine: fix lépéses idő, rendszerek, háttérmunkák, animáció-idő,│
 │ csúcspont-formátum. Nem tud erdőről, terepről, OpenGL-ről.                 │
 └────────────────────────────────────────────────────────────────────────────┘
```

A függőség csak lefelé mehet. Ezt a fordító (projekthivatkozások) és az `ArchitectureTests` is
kikényszeríti: a három könyvtár nem hivatkozhat a játékra, a grafikai/ablakozó/UI csomagokra, és forrásaikban
nem szerepelhet `OpenTK.Graphics`, `OpenTK.Windowing`, `ImGuiNET`.

| Szerelvény | Névtér | Felelőssége | Nem tudhat |
|---|---|---|---|
| `ForesTycoon.Engine` | `ForesTycoon.Engine` | `FixedStepClock`, `SimulationFrameRunner`, `IWorldSystem`/`WorldSystemCollection`, `BackgroundJobScheduler`, `AnimationTimeline`, `Vertex` | erdő, terep, GL |
| `ForesTycoon.Ecology` | `ForesTycoon.Ecology` | a természeti rendszer: időjárás, talaj, vízmérleg, fajok, erdőállomány és egyedek | terepháló, render, a játékvilág |
| `ForesTycoon.TreeModels` | `ForesTycoon.TreeModels` | fa- és cserjemodellek fajszinten: Arbaro-készlet → váz → háló | a világ, a render, a GL |
| `ForesTycoon` | `ForesTycoon` | játékvilág, terep, megjelenítés, felület, diagnosztika | – |

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

## 3. A játék – világ és megjelenítés

* `World/`: `GameWorld` (a játékállapot határa), parancsok és visszajátszható mentés, járművek, rönkszállítás,
  hatások, vadállomány. Itt vannak azok a szabályok, amelyek a terepet és a járműveket is ismerik.
* `Terrain/World` (adat, generálás, hidrológia, utak, az `IForestHabitat` illesztése), `Terrain/Forest` (erdő-
  és telepítésmegjelenítés), `Terrain/Render` (felületek, víz, út, kellékek).
* `Rendering/Gpu` (eszköz, pass-ok, puffer), `Rendering/Forest` (fa-GPU állapot, LOD, régi/importált modellek),
  `Rendering/Weather`, `Rendering/Models`, `Rendering/Scene`.
* A render csak olvas: `ForestSystem`-ből egyedeket, `TreeShapeSpec`-et épít (`TreeShapeSpec.From`), kéri a modellt, és
  feltölti. Soha nem módosítja a szimulációt. A fa újraépítése sávhatárhoz (`ShapeKey`) és fázis-/lombváltáshoz kötött.

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
* A `Terrain` osztály még adatot és GL-rajzolást is tartalmaz (partial fájlokban). A rajzoló részek
  (`Terrain/Render`, `Terrain/Forest`) külön `ForesTycoon.Rendering` szerelvénybe vihetők, ha a
  `Terrain` egy GL-mentes `ForesTycoon.World` szerelvényre és egy rajzoló-adapterre válik szét.
* A parancs- és mentésréteg (`World/Commands`, `World/Persistence`) ugyanígy kivehető `ForesTycoon.World`-be.
* Az `Ecosystem` mentés-pillanatképe (ellenőrzött snapshot + naplórészlet) a hosszú játékokhoz.
