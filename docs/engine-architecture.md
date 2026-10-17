# Engine architecture

> A szerelvényekre bontott, egyirányú rétegezést az [architecture.md](architecture.md) írja le (Engine ← Ecology ← TreeModels ← játék). Az alábbi szöveg a futási folyamatot és a teljesítménypolitikát tartalmazza; a `Simulation` mappa neve azóta `World`, az erdő/víz/időjárás a `ForesTycoon.Ecology` szerelvényben van.

The engine is organized around one rule: simulation state is deterministic and independent from the window and renderer.

## Egyenletes szimulációs munkavégzés (2026-10-16)

Motoralapelv: minden növekvő szimulációs munka folytatható, determinisztikusan
szétosztott végrehajtást kapjon. Ez a gráfszabályokra, natív motorprimitívekre,
erdőhónapra, környezetre, logisztikára és útkeresésre egyaránt vonatkozik.
Az időérzékeny, korlátos mozgáslépés továbbra is minden fix tickben futhat.
Az előkészítés és publikálás sem rejthet korlátlan teljesvilág-ciklust; lejáró
határidő vagy projektcsere nem indokol korlátlan „fejezd be most” végrehajtást.

A szeletek determinisztikus munkaegységeken alapuljanak, menthető folyamatállapottal,
stabil objektumazonosítókkal és megőrzött függőségi sorrenddel. A milliszekundumos
profilozás ellenőrzésre szolgál; nem teheti gépsebességtől függővé a visszajátszást.
A szétosztás nem dobhat el szimulációs időt és nem változtathatja meg észrevétlenül
a szabály jelentését vagy a fizika időlépését.

A folyamatos szimuláció ezt most `GameWorld.ContinueUpdate` munkafolyamatként
valósítja meg: globális dinamika, ökológia, logisztika, szabálykönyv, vadak és
regisztrált rendszerek. A raszterek, havi erdőmunka, objektumlista-előkészítés,
egyedhatások, flotta, automatikus útkeresés és útvonalgeometria külön
felfüggesztési pontokat kaptak. Egy részlet legfeljebb 64 munkaegység; a host
képkockánként több részletet futtathat az időkeretében.

A logikai tick csak az egész munkafolyamat végén lép tovább. Közben más
szimulációs rendszer és világmódosító parancs nem előzheti meg a függő munkát;
így a fa- és tönktömbök indexei nem tolódhatnak el egy másik tick miatt.
Nincs „nem stabil objektum: fusson egészben” kivétel és esedékességhez vagy
projektcseréhez kötött korlátlan befejezés. A renderelés közben olvashatja a
világot, de nem módosíthatja. A gráfos előnézet a függő tick alatt várakozik.

A mentési kérés (`AtTickBoundary`) a következő kész ticknél készít pillanatképet;
nem próbál C# iteratorállapotot menteni és nem fejezi be azonnal a számítást.
A tick alatt érkező parancs a következő tick sorszámát kapja. A szabálycsere
utáni függő erdőráta-frissítést külön checkpointmező őrzi. A direkt `Save` és
`CaptureCheckpoint` a félkész tick pillanatképezését visszautasítja; az interaktív
mentési felületek a lépéshatárra ütemeznek.

`FixedStepClock.AdvanceWork` megőrzi az időhátralékot, és szüneteltetéskor is
befejezheti a már megkezdett tick munkaszeleteit. A különböző gépsebesség csak
a képkockák közti elosztást változtatja; a műveletek sorrendjét és a fix időlépést
nem. A szinkron `Update`/`SimulationWork.Complete` adapterek a fej nélküli
teszteléshez, visszajátszáshoz és CLI-eszközökhöz maradtak meg;
a játék és az Editor szimulációs gombjai a folytatható hostot használják.

Minden `IWorldSystem` kötelezően megvalósítja az `UpdateWork` metódust;
nincs automatikus, egész `Update`-et egy munkának tekintő alapadapter. A
faanyagkönyvelés O(1), az effektkészlet elemenként szüneteltethető.
A pályagenerálás és a nagy szerkesztések külön folytatható munkafolyamatot
kaptak. A részleteket a következő szakasz írja le; a fájlkiírás és a teljes
rajzolási képkocka továbbra sem kap ettől kemény valós idejű garanciát.

### A szeletelés ellenőrzött határa

A `SimulationWork.MoveNext` a tick, a szabályfuttatás, a parancscsomag és a
pályagenerálás munkaszeletének belsejében tiltja a `SimulationWork.Complete`
szinkron adapterét. Az `Update`, `ExecutePendingCommands`, `Regenerate`,
`StartScenario` és `RuleEngine.Run` adapterei is ellenőrzik ezt a határt.
A guard kivétel után is visszaáll; az izolált háttérmunka külön szálon fut.
Ez a védelem a szinkron adapterek véletlen visszahívását fogja meg; nem bizonyítja
önmagában minden `MoveNext` belső ciklusának vagy memóriafoglalásának időkorlátját.
Ezt kódvizsgálat és p95/p99/max mérés egészíti ki.

Az algoritmusfuttató-csere megvárja, hogy a host szeletekben végrehajtsa a
még el nem kezdett parancsokat is; a fájlfigyelő nem üríti ki a sort.
Az algoritmusváltás teljes, függő parancs nélküli checkpointot kap. A régebbi editor-mentési gomb is
`AtTickBoundary`-ra ütemez. A mentésbe a még el nem kezdett parancsok is bekerülnek.
Az összehasonlító próba megvárja a tesztvilág műveletének végét; nem zárja azt
korlátlanul a renderhívásból. A futtatócsere utáni induló projektparancsok a
betöltőképernyő 4 ms-os munkakeretében haladnak.

A fájlok sorosítása, tömörítése, IO-ja és a közvetlen, teljes mentésbetöltés
külön költség: ezekre ez a munkaszeletelési védelem nem ad valós idejű garanciát.
A mentési konzisztencia és a parancsok sorrendje ettől függetlenül kötelező.
A fix 16×16-os induló bootstrap korlátos; a kért pályamérettel növő rész a
folytatható generálási munkában fut.

### Pályagenerálás és nagy szerkesztések

A játék és az Editor `RequestRegenerate` / `RequestScenario` kérést ad;
a `ContinueCommands` ugyanabból a 4 ms-os képkockakeretből folytatja az
előtérben végzett munkát, mint a szimulációt. Szüneteltetett világban is halad.
A generálás nem futtat szimulációs tickeket: egy külön jelölt világon dolgozik,
és csak elkészült állapotot publikál. A régi világ addig látható marad.

A rács/topológia, zajminták, folyóakkumuláció, magasságkaszkád,
hidrológiai BFS/Dijkstra, talaj, erdőegyedek és kezdeti ráták kis egységekben
épülnek fel. A magasságtervező újrahasználja a saját munkatárait, a topológia
lapos tömböket használ a cellánkénti átmeneti listák helyett. A rögzített
kiindulójelenet visszaállítása is folytatható. A jelenet beolvasása,
szabályfordítás és a fa tisztán CPU-s geometria-előállítása izolált
háttérmunkán történik; az élő világot és a GL-t csak a tulajdonos szál kezeli.
A fajparaméterek háttérmunkánként külön kontextust kapnak. A lombkorona egyetlen
cellájának lekérdezése nem frissíthet teljes rasztert és nem módosít
szimulációs gyorsítótárat; a teljes publikálás explicit munkafázis.

A nagy ecset, magasságfestés, út/útjavítás/nyom, területi és egyedi ültetés,
kitermelési kijelölés és járműrendelés útkeresése folytatható.
A parancs a sor elején marad a teljes befejezéséig. Új parancs és tick nem
előzheti meg; a napló egyetlen felhasználói műveletet tárol, nem szeleteket.
A közben érkező bemenet megmarad. Generáláskor az új bemenet a publikált
világra kerül, megszakításkor a régi világ parancssorában marad.

A mentési határ most a teljes tick **és a teljes megkezdett parancscsomag /
pályapublikálás** határa. Az `AtTickBoundary` ide ütemez; közvetlen félkész
checkpoint továbbra sem engedélyezett. A mentés nem üríti ki korlátlanul a
munkasort. A még el nem kezdett bemenetet a checkpoint külön megőrzi.
A `.2026-10-16.2` futtató a `.2026-10-16.1` teljes célállapotát is importálja,
régi történeti naplóvéget azonban nem értelmez át az új futtatóval.

A GPU-geometria és az anyagtextúrák előkészítése, a környezeti színraszter
frissítése és a lecserélt pálya gyorsítótárainak
felszabadítása is részletekben halad. Útépítés nem indíthat teljes
szinkron erdő-újraépítést a rajzolásból: az elavult, útra lógó részlet
ideiglenesen rejtett, amíg a cseréje elkészül. A szinkron erdőépítési kapcsoló
csak explicit diagnosztikai/fixture célra maradt meg, az első játék-képkocka
sem kapcsolja be.

A kezdeti korlátokat és a még beégetett játékszabályokat az
[audit](rule-runtime-audit.md), a méréseket az
[engine-performance.md](engine-performance.md) sorolja fel.

The current raster/ecosystem plan and local terrain-edit semantics are documented in
[ecosystem-simulation-design.md](ecosystem-simulation-design.md). Terrain edits preserve unmodified
trees and environmental water stores; terrain/grid resource caches use per-chunk surface revisions.

## Runtime flow

1. The cross-platform OpenTK `GameWindow` host collects platform input.
2. `WorldInteractionController` converts editing gestures into world commands.
3. `WorldCommandQueue` applies a stable command batch at the frame boundary.
4. `SimulationFrameRunner` and `FixedStepClock` advance `GameWorld` at deterministic 30 Hz ticks.
5. `WorldSystemCollection` updates registered simulation systems in stable order.
6. `RenderPipeline` draws the current state and uses interpolation between fixed ticks.

This separation keeps input responsive, makes commands replayable, and lets simulation tests run without an OpenGL context.

## Responsibility boundaries

| Area | Owns | Must not own |
| --- | --- | --- |
| `App` | OS window, GL context, raw input and UI composition | forestry/economy rules |
| `Camera` | isometric camera state, easing, zoom and tilt | OS input types, simulation mutation |
| `Interaction` | tool state and conversion of gestures to commands | WinForms/OpenTK event types, rendering |
| `Simulation` | game time, world lifetime, commands, ordered systems and forest state | GL calls, UI widgets |
| `Animation` | allocation-free time sampling and playback modes | entity-specific visuals |
| `Terrain` / `Roads` | authoritative map topology and construction rules | window lifecycle |
| `Rendering` | render passes, GPU resources, visual interpolation | authoritative simulation mutation |
| `Diagnostics` | frame/tick/GC/draw-call measurements | gameplay decisions |

`ForestSystem` is the first gameplay simulation system: it owns per-tile species, age, biomass and health, while `Terrain` only provides habitat data through `IForestHabitat`. Future harvesting, depots, industries and economy should follow the same boundary and implement `IWorldSystem`. Player actions should enter through immutable `IWorldCommand` implementations.

## Forestry simulation

- Forest state is stored in contiguous arrays indexed by tile ID; no entities or temporary collections are allocated per tick.
- Growth runs in deterministic monthly steps. A normal world uses 1200 simulation seconds per forest year; isolated diagnostics may use the faster 30-second tempo.
- Spruce, birch, oak and beech have distinct maturity ages, biomass limits, growth rates, site preferences, shade tolerance and timber value.
- Terrain moisture, elevation, standing water, rivers and roads determine habitat availability.
- Mature, healthy neighbouring stands can seed empty suitable tiles naturally.
- The renderer reads immutable stand snapshots and scales/colours trees without owning forest rules.
- Save replay reconstructs the same forest because initialization and monthly random decisions are seed/tick based.
- Planting and harvesting enter through replayable world commands. Harvesting converts stand biomass into `TimberCargoSystem` inventory without coupling forestry to vehicles.
- Trucks load timber at the first route endpoint, unload it at the other endpoint and visually expose their fill level. Removing a broken route returns onboard cargo to the stockpile, preserving resources.

## Performance policy

- Simulation uses fixed ticks and never scales gameplay by render FPS.
- Recurring work uses bounded resumable units. Prefer indexed/reused collections; iterator state allocations are measured rather than assumed to be free.
- Terrain topology caches expose `ReadOnlySpan<T>` or caller-provided `Span<T>` buffers.
- Rendering may interpolate but must not mutate authoritative state.
- Large map work should be chunk-dirty and incremental; avoid full terrain/hydrology rebuilds for local edits.
- Add measurements before optimization: frame CPU time, simulation tick time, visible chunks, draw calls, uploaded bytes and GC allocations/frame.
- CPU-heavy background jobs publish immutable results through `BackgroundJobScheduler` only at frame boundaries.
- Saves serialize versioned DTOs and a tick-stamped command journal, never runtime objects.

## Review findings and next milestones

The current foundation has deterministic ticking, command batching, chunk culling, cross-platform execution, replay saves, performance counters, an OpenGL 3.3 core renderer and partial terrain updates. The highest remaining technical risks are:

1. Dynamic terrain overlays, water and roads use streamed core-profile batches. Promote stable chunk geometry to persistent dirty-chunk VBOs when profiling shows upload bandwidth is the bottleneck.
2. Picking still reads OpenGL matrices and refreshes hover continuously. Keep matrices CPU-side and recompute only when pointer, camera or terrain changes.
3. Hydrology and road planning are synchronous. The background scheduler is ready, but dirty-chunk calculation still needs immutable job payloads.
4. Version 8 saves restore validated dynamic checkpoints and replay only the subsequent journal. Periodic checkpoint retention, compression and large-world loading benchmarks remain future work.
5. Split ImGui panel composition out of `Viewport` as the tool count grows.

Recommended delivery order: placeable depots and route assignment, immutable hydrology jobs, economy/cargo graph, dirty-chunk GPU caches, then replay checkpoints.

## Forest GPU cache and LOD (2026-09-13)

Forest wood and crowns now live in persistent triangle VBOs per visible terrain chunk. Stable frames only submit cached draw calls; they do not regenerate stems, expand primitives or upload forest vertices. CPU scratch lists are reused during rebuilds. Each visited chunk retains one LOD, released with its terrain.

`ForestSystem.Revision` signals potentially changed state. On a new revision, each visible chunk compares compact visual snapshots (species plus maturity, stocking, health and neighbour pressure rounded to 1/32). A changed visual snapshot, a terrain Props dirty flag, or a LOD transition rebuilds that chunk. Neighbour pressure is included so harvesting across a chunk boundary also updates the edge canopy. Previously invisible chunks validate their state on re-entry. Simulation/save precision is unchanged.

LOD uses framebuffer pixels per world unit with hysteresis:

- Near: enter at 9, leave below 7; full existing detail for sufficiently large stems and undergrowth.
- Medium: enter from far at 3.5, leave below 2.5; no branches, secondary lobes or undergrowth.
- Far: simplified crowns without wood. Stem positions are retained across LODs to preserve canopy coverage.

The diagnostics panel reports `Forest rebuild/frame`. Expected value on a stable view is zero. A cold view or LOD switch still rebuilds its visible chunks synchronously; time-budgeted uploads and GPU instancing remain possible follow-ups. Far LOD currently preserves per-stem crowns rather than replacing an entire stand with one canopy mesh.

Validation:

```sh
dotnet test ForesTycoon.sln
dotnet run --project ForesTycoon/ForesTycoon.csproj -- --forest-smoke-test
dotnet run --project ForesTycoon/ForesTycoon.csproj -- --smoke-test
```

The dedicated forest smoke test creates a hidden OpenGL window and checks stable-frame reuse, LOD transitions, plant/harvest invalidation, terrain edits, clear, and GL errors. On its deterministic 33-node test map: near 173,808; medium 77,952; far 30,300 submitted forest vertices (82.6% fewer at far than near). These are geometry counts, not an FPS benchmark.

## 30 FPS performance correction

Run the repeatable rendering benchmark with:

```sh
dotnet run --project ForesTycoon/ForesTycoon.csproj -- --forest-benchmark
```

The benchmark uses 1280×720, 4× MSAA, ten warm-up frames and ninety measured frames per view. GL.Finish includes GPU completion in the elapsed time; it is used only by diagnostics. Simulation, UI, input and swap are excluded, and cold cache/terrain generation costs are outside the steady-state measurements.

On the available RTX 5060, the unoptimized 64×64 world took approximately 79–87 ms median in the first run. Pass profiling identified repeated foundation classification and terrain submissions as the dominant cost, not the forest shader. Surface classification now caches results until terrain/road changes, and terrain plus grid geometry use persistent chunk VBOs with the original baked colours and triangles.

After the change: world64 zoom10 median 8.39 ms / p95 16.84 ms; zoom5 median 10.20 ms / p95 18.14 ms. Fixture16 zoom10 median 1.23 ms / p95 4.89 ms. These are measurements of the test scenes, not a guarantee for every map size or editing workload. The steady-state results fit inside the 33.3 ms render budget for 30 FPS without reducing forest detail.

Cache invalidation is checked by the forest GL smoke test after road construction, road removal and terrain elevation changes. A map-wide visual revision currently rebuilds visible static chunks after such edits; incremental revision propagation is a future improvement if editing spikes are material.

## Camera-motion stutter correction

`--camera-benchmark` runs continuous rotation and a sinusoidal zoom crossing all LOD boundaries, while advancing the forest simulation at 30 Hz. This exercises both level changes and a monthly growth boundary. The ordinary benchmark remains a stationary render measurement.

Changes:

- Terrain and all three forest LOD buffers are prepared when the terrain renderer is created, before interactive frames. Previously only one forest LOD survived, so repeated zooming repeatedly regenerated the same geometry.
- Prepared LODs are retained independently. Forest buffers release their CPU vertex copies after upload, retaining only their GPU geometry and vertex counts. This trades more GPU memory and loading work for predictable camera movement; very large maps still need streaming/instancing before this eager policy scales well.
- Crown grid vertices and their numerical normals are evaluated once and reused by neighbouring triangles.
- Monthly visual updates use a cooperative 2 ms CPU budget, preserving the previous mesh until the replacement is complete. Work includes wood, floor and undergrowth as well as crowns. At most one buffer group uploads per frame. The final allocation/upload is indivisible, so 2 ms is a work budget, not a strict wall-clock guarantee.
- User edits remain immediately visible. Obsolete queued growth work is cancelled when the simulation revision or terrain changes. Road/elevation changes still invalidate cached terrain correctly.

RTX 5060, 1280×720, 4× MSAA camera benchmark: before, world64 p95 about 103 ms and maximum 470 ms. After, with simulation advancing, world64 median 5.0–6.5 ms, p95 7.1–8.7 ms, maximum 11.3 ms in the measured runs. Dense fixture maximum 31.0 ms. These exclude initial loading and do not guarantee the same result on larger maps or different hardware.

Regression coverage includes returning to a previously used LOD without a rebuild, completing incremental growth updates, cancellation after edits, and drawing buffers after releasing CPU geometry copies.

## Individual-tree rendering and transactional loading (2026-10-04)

The individual-tree renderer retains a mesh per chunk and LOD. Monthly physical dimensions, growth rates and health are separate per-tree GPU data. Only topology, terrain, model style or visual life-stage changes replace mesh geometry. Natural changes are built cooperatively from copied tree state while the last complete mesh remains visible; user edits invalidate immediately. The details and measured limits are in `engine-performance.md`.

`GameWorld.Load` validates the command journal and terrain settings before constructing a candidate world. Replay runs in that candidate. The active runtime is exchanged only after successful replay, preserving current graphics preferences and rebinding the vehicle route factory to the adopted terrain. Failed validation or replay leaves the live world and its pending commands intact. Quicksave writes and flushes a sibling temporary file before replacing the existing save.

## Coupled environment and individual growth (2026-10-04)

`ForestEnvironmentCoordinator` now owns the explicit order of water intervals, forest months and environmental accumulator resets. `WeatherSystem` owns seeded atmospheric events, while `EnvironmentSystem` owns conserved water stores and `SoilProperties` separates soil capacity/fertility from current moisture. Living leaf area determines demand; actual root uptake is limited by available water and supplies the same growth/stress model that reads weather-derived radiation. Harvesting immediately releases neighbouring light and transfers intercepted water to the ground without losing it.

Replay save version is now 4. Earlier versions are rejected because the changed growth rules cannot reproduce their original world from the journal. Details, equations, tests, measured water-step costs and remaining model limits: [environment and forest coupling](environment-forest-coupling.md).

`ForestMonthlyPreparation` now computes next-month geometric competition in deterministic batches across environmental steps. It owns separate reusable snapshot/result buffers and publishes no partial tree state. Water and radiation are applied only when the actual month closes. Known local edits enqueue deduplicated snapshot and observer-resource repairs; global or untracked revision changes restart preparation. A missing result uses the synchronous calculation. The completed competition snapshot transfers to forestry through a buffer exchange, preserving seedling evaluation order. See [local repairs and remaining limits](engine-performance.md#helyi-változások-javítási-sorai).

The same preparation also owns projected boundary dimensions, volume increments, geometry-only growth-curve terms and pre-update stand totals. `ForestGrowthShape.Apply` preserves the original arithmetic order; final environmental response, health changes and annual increment accounting still occur at the month boundary. Optional `ForestMonthProfile` diagnostics separate monthly phases. The simulation benchmark now includes coupled 30 Hz weather/water/forestry measurements against the synchronous reference, in addition to isolated forest timings.

## OpenGL-first rendering and replaceable backend boundaries (2026-10-08)

The runtime animation implementation now lives in `AnimatedGlbModel.Animation.cs`, apart from GLB parsing.
`AnimationPath` and `AnimationInterpolation` replace numeric path IDs and runtime string comparisons.
Sampling remains CPU-only, binary-searches sorted keyframes, uses shortest-arc quaternion interpolation
and interval-scaled cubic Hermite tangents, and reuses pose arrays. Crossfade endpoints evaluate one clip.
The loader owns format validation; the pose owns sampling and hierarchy transforms; the renderer owns
GPU resources, materials and submissions. Keep these responsibilities when adding animation features.

Names should expose meaning and units: use enum values for closed choices, `Seconds`/`Years`/`WorldWidth`
for quantities where ambiguity affects correctness, and verb names for state-changing operations.
`RenderStateScope.ThinLines()` explicitly requests the supported one-pixel lines; the former width
argument was ignored. Optional GL state is captured only when a scope changes it, and nested polygon
offsets restore their actual parent values. Consecutive wildlife and mill instances share a state scope.

**OpenGL remains the primary and default backend.** Production native graphics operations and
GLSL now live exclusively inside `OpenGl/` implementation directories. CPU animation, geometry
emitters, terrain/forest ownership, scene pass orchestration and UI input use backend contracts.
Diagnostic programs intentionally retain native operations to verify the OpenGL implementation.

| Responsibility | Boundary | OpenGL implementation |
| --- | --- | --- |
| Geometry resources, frame operations and primitive submission | `IGraphicsBackend`, `IGeometryBufferBackend`, `RenderStateScope` | `ForesTycoon.Rendering/OpenGl/` |
| Forest instance growth/state uploads | `IForestStateBuffer`, geometry resource contract | `OpenGlForestStateBuffer`, `OpenGlVertexBuffer` |
| Imported/animated models and batching | `IModelRenderBackend`, `IModelRenderBatch`, `ModelRenderFrame` | `ForesTycoon.Models/OpenGl/` |
| Clouds and precipitation | effect backend contracts/factory | `ForesTycoon.Effects/OpenGl/` |
| Surface materials and shadow passes | `ISurfaceRenderBackend` | game `OpenGlSurfaceVisualRenderer` |
| Forest materials, fog and lightning | `IForestMaterialBackend`, `IForestWeatherBackend` | game `OpenGlForestMaterial`, `OpenGlForestWeatherRenderer` |
| Diorama render targets and composition | `IPostProcessBackend` | `OpenGlPostProcess` |
| ImGui font upload and draw data | `IUiRenderBackend` | `OpenGlImGuiRenderer` |
| Graphics window settings, context activation and presentation | `IRenderWindowPlatform` | `OpenGlWindowPlatform` |

`Program` selects `RenderBackendSelection.UseOpenGl()` before constructing windows or render resources.
An alternative backend supplies one coherent `RenderBackendBundle` and installs it with `Configure`.
This changes core resources, model creation, effects, scene/UI factories and the window platform together.
Configuration is rejected after initialization or first resource acquisition, before any other factory
changes. This is a startup choice. Dispose render resources while their backend/context is alive,
then dispose `RenderDevice`; only after shutdown can another bundle be installed. Resource acquisition
also locks selection before shader initialization, and disposal releases that lock.

The contracts group resource and pass ownership rather than translating every native function.
`VertexBuffer`, `DioramaPostProcess`, material/effect facades and `ImGuiController` delegate to owned
implementations. No native buffer/texture identifiers or graphics enums cross their boundaries.
ImGui texture IDs are opaque tokens interpreted by the UI backend. Renderer capability flags also
belong to that implementation. The controller owns and releases its ImGui context and pinned font ranges.
Shader compilation/linking and uniform lookup are consolidated in the OpenGL `GlProgram` helper.

`AnimatedGlbModel` and `Pose` remain CPU data/sampling. `ModelDrawOrder` prepares reusable ordering,
and `AnimatedModelRenderer` submits explicit frame values and borrowed spans to its backend.
`DynamicPrimitiveBatch` expands quads/line loops on the CPU and submits packed coloured vertices.
Borrowed spans must be consumed or copied before the backend call returns. `RenderTransformState`
keeps camera/model state in Engine, independently of native contexts.

A replacement must preserve or adapt the existing CPU transform conventions, winding, clip-space depth,
texture orientation, colour space and shadow-depth comparisons. Framebuffer readback is packed RGBA8
with rows starting at the bottom left. Forest growth/state layout and static/paged upload completion
must preserve publication semantics. Model, surface and geometry implementations in a bundle must
share compatible shadow/material resources; OpenGL currently shares its shadow texture binding.

The host still uses OpenTK's window/event toolkit. Graphics settings and context/presentation policy
are isolated in the window platform, but a Metal implementation must provide an appropriate native
surface and presentation path, potentially adapting the host bridge. GLSL programs also need equivalent
Metal shaders and pipeline state. **No Metal backend is implemented or hardware-validated.**

Architecture tests prevent native graphics imports, calls, shaders and enums outside production backend
directories. Headless tests exercise primitive/frame/resource submission, startup selection and rejected
live reconfiguration, plus the UI controller through injected implementations. Native smoke tests cover
forest upload/growth, materials, shadows, weather, animated wildlife, UI glyphs and the complete window.

For another backend, first validate an asymmetric mesh, animation, mask/blend materials and shadows,
then terrain/forest state, effects, composition and UI. Require native resize/disposal checks, image
regressions and completed-frame benchmarks on each enabled platform. The next performance priorities
remain bounded forest LOD residency and correct chunk-local terrain invalidation, with explicit dependency
tracking and continuity guarantees. Current measurements are in [engine-performance.md](engine-performance.md).


## Az 1–3. alapcsomag ellenőrzése és kapacitásmérése

Mérés: 2026-10-16, Release, Intel Core i7-8700 @ 3.20 GHz, képernyő és GPU
nélkül. A táblák kizárólag a CPU-munkát mérik, nem a teljes megjelenített
képkockát. A p95/p99 nem felső időgarancia; a JIT, GC és operációs rendszer
miatt a legrosszabb minta nagyobb lehet a 4 ms-os hostkeretnél.

### Szimuláció, 64 munkaegységes szelet

`dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --rule-benchmark`

| Pálya / időjárás | Fák | Teljes tick p99 / max, ms | Szelet p99 / max, ms | Azonos teljes állapot |
| --- | ---: | ---: | ---: | --- |
| 64×64, eső | 15 797 | 55,749 / 684,462 | 0,074 / 1,347 | igen |
| 64×64, hó | 15 797 | 57,049 / 567,809 | 0,048 / 1,309 | igen |
| 128×128, eső | 65 086 | 216,320 / 2573,854 | 0,052 / 4,357 | igen |
| 128×128, hó | 65 086 | 230,095 / 2426,306 | 0,046 / 9,609 | igen |

### Tényleges hostkeret és munkahátralék

`--rule-host-benchmark`: 4 ms CPU-keret képkockánként, virtuális 60 Hz-es
időbeérkezés, 900 s/erdőév, 120 szimulációs másodperc egy hónaphatárral.
Nincs alvás vagy rajzolás a mérésben. A lemaradás szimulációs idő, nem egyetlen
hosszú képkocka. A végén új idő hozzáadása nélkül is megmérjük a ledolgozást.

| Pálya | Host CPU p95 / p99 / max, ms | Legnagyobb hátralék, s | Hátralék a mérés végén, s | Plusz ledolgozó képkockák |
| --- | ---: | ---: | ---: | ---: |
| 64×64 | 4,021 / 4,046 / 12,347 | 3,2833 | 0,0333 | 0 |
| 128×128 | 4,037 / 4,052 / 10,957 | 10,1500 | 0,0333 | 25 |

Az időveszteség minden esetben 0. A lebegőpontos kerekítésből megmaradó,
egy ticknél kisebb idő a következő képkockára marad; nem dobjuk el.
128×128-on a plusz ledolgozás után a hátralék 0 lett.

**Kapacitáshatár:** a gyorsított, 120 s/erdőév stresszpróbán a 128×128-as pálya
4 ms/60 Hz mellett nem tartja a kért tempót: a 15 másodperces hostmérés végén
8,0333 s a hátralék, esőben 972, hóban 1037 további munkaképkocka kellett hozzá.
Ez nem korlátlan befejezés és nem időeldobás, de a gyorsítás kiszolgálásához
kevés a rendelkezésre álló CPU-keret. Az alaptempó hónaphatári, 10,15 s-os
csúcshátraléka szintén valós szimulációs késleltetés: a rövid képkockákból nem
következik azonnali állapotfrissítés. A következő motoroptimalizálás célja a havi
munka összköltsége és ez a késleltetés, a determinisztikus sorrend megtartásával.

### Generálás és nagy szerkesztési parancsok

`--foreground-benchmark`, 4 ms-os hostkeret. Az ecset, út, nyom, területi
ültetés és kitermelési kijelölés közös parancscsomag.

| Pálya / művelet | Egészben, ms | Host CPU p95 / p99 / max, ms |
| --- | ---: | ---: |
| 128×128 generálás | 596,420 | 4,031 / 7,564 / 12,065 |
| 128×128 parancscsomag | 227,328 | 4,039 / 14,261 / 14,261 |
| 256×256 generálás | 1071,983 | 4,178 / 14,085 / 16,037 |
| 256×256 parancscsomag | 119,785 | 4,008 / 4,678 / 4,678 |

Mindkét méretnél pontosan egyezett az egészben és szeletelten elkészült
checkpoint, a mentés/betöltés és a folytatás. A számok egyszeri helyi mérésből
származnak; a 256-os generálás rövidebb aktív ideje nem általános skálázódási
állítás, a háttérfeladatok várakozása és a bemelegedés eltérhet.

A szöveges nyers eredmények az ignorált `artifacts/foundation-*-benchmark.txt`
fájlokba kerülnek. A fenti parancsok újra előállítják őket.
Ellenőrzések: `SimulationWorkTests`, `ForegroundWorkTests`,
`BoundedWorkGuardTests`, `RuleProjectTests`, `BehaviorCurveTests`,
`ResponseRuleCardTests`, `RuleFlowTests`, `DynamicsRuntimeTests`,
`RuleCoverageTests`, valamint a teljes csomag és az editor két füsttesztje.


Lezárási ellenőrzés: a teljes Release tesztcsomag **1887/1887** eredménnyel
átment (`artifacts/foundation-final-tests.txt`). A művelethatár és a fájlfigyelő
módosításának célzott tesztjei is zöldek; az editor füsttesztje és az önálló
editorablak füsttesztje sikeres (`artifacts/foundation-editor-smoke.txt`).
A Release editor- és játékfordítás 0 hibát és 0 figyelmeztetést adott.
A CI a tesztek előtt a `python tools/update-rule-audit.py --check` paranccsal
a generált leltár frissességét is ellenőrzi.


## Hónaphatári számítás gyorsítása, 2026-10-16

A `--month-work-benchmark` a tényleges GameWorldöt méri, a havi egészség-,
stressz-, elhalás- és növekedési gráfokkal. A korábbi külön erdő-fixture ezek
nélkül nem jelzi a játék teljes havi költségét. A profil 310 fix ticken,
esővel, 120 s/erdőév mellett, egy hónaphatárt fog át. A havi profilozás és
szabályonkénti időmérés be van kapcsolva; ezért a számok a diagnosztikai
ráfordítást is tartalmazzák.

A 128×128-as pályán a változás előtt a havi növekedés/életciklus 550,216 ms,
az újulat és az egészségváltozás utáni erőforrás-frissítés 1974,525 ms volt.
A domináns rész a szomszédos fapárok újbóli gráfos kiértékelése.

### Megvalósítás és szemantikai határ

- A `GraphZeroGate` az aktuális gráf **feltételes, nulla egyéb ágából** képez
  konzervatív tartományvizsgálatot. Nem ismer fafajt, koronahatótávot vagy
  beégetett árnyékolási képletet. Csak akkor hagy ki egy szomszédcsoportot,
  ha mindkét párhatásról bizonyított, hogy minden tagjára nulla.
- A csempe pillanatképe a tényleges minták befoglaló pozícióit és méret-/egészség-
  tartományait is tárolja gyorsítótárként. Az összegek és szorzatok tartományai
  kifelé kerekítettek; a távolságkorlát ugyanazt a float aritmetikát követi,
  mint a párvizsgálat, szintén konzervatív kerekítéssel.
- Nem támogatott feltétel, ismeretlen bemenet, nem véges köztes tartomány vagy
  túlcsordulás esetén az eredeti teljes gráf fut. A nem kiválasztott ág hibáját
  továbbra sem értékeljük ki. A két gráf azonos feltételét csak egyszer vizsgáljuk.
- Egy egyedi gráf továbbra is hathat távoli szomszédra. Nincs natív koronatávolság-
  levágás, és a megmaradó hozzájárulások összeadási sorrendje sem változott.
- A korlátok származtatott adatok: mentéskor nem kerülnek új checkpointmezőbe,
  betöltéskor a megőrzött mintákból, mintánként szeletelten újraépülnek.
- A RuleEngine szabályonként újrahasználja a bemeneti és sajátmező-referenciatömböt.
  Minden használt bemenetet és saját mezőt újraolvas az aktuális elemről;
  ezek a tömbök nem helyettesítik a mentendő állapotot. Egy motoron nem lehet
  egymásba ágyazott szabálylépést futtatni.
- Az átadási összesítő csak tényleges átadás után írja újra a nyilvános
  összesítőt. A pusztán növekedést végző szeletek ugyanazokat a vízadatokat
  nem másolják át újra. A részleges lépés minden megtörtént átadását
  a következő felfüggesztési ponton azonnal közzéteszi.
- Növekedési facsoportonként az eltérő időzítésű szabályok nem fogyasztanak
  külön munkaszeletet. Az egyszerű metaadat-bejárást a szabálykönyv 256 tételes
  korlátja határolja; a tényleges elem- és fapármunka továbbra is szeletelt.
  A checkpoint szabályindexe az eredeti listában marad.

Nem változott a szabályok sorrendje, képlete, órája vagy a mentési formátum.
A teljes checkpoint SHA-256 lenyomata a régi és új végrehajtásban bitre azonos:

- 64×64: `75F17BD1B78F6378FB7EB81AB5E6055E7927495166D9F3AE90EFA87D70FB7EF4`
- 128×128: `A3E82249533AF4FAAEE694E44E6CAEFFC91BFD95B0A28CBD7831AE06558ED79A`

### Előtte / utána

Ugyanazon az i7-8700 gépen, Release, GPU és rajzolás nélkül. Egy-egy helyi
futás eredménye, nem általános hardverfüggetlen időgarancia.

| Méret | Hónaphatári teljes tick, előtte → utána | Csökkenés | Allokáció a 310 tick alatt, előtte → utána |
| --- | ---: | ---: | ---: |
| 64×64, 15 797 fa | 835,391 → 619,342 ms | 25,9% | 60 148 608 → 45 358 064 B |
| 128×128, 65 086 fa | 2616,309 → 1727,391 ms | 34,0% | 240 354 584 → 182 418 560 B |

Ezek **egészben mért logikai tickek**, a játék továbbra is munkaszeletekben
futtatja őket. A 128-as havi életciklus-rész 328,245 ms-ra, a regeneráció/
erőforrás-frissítés 1350,542 ms-ra csökkent.

Az alaptempós `--rule-host-benchmark` 4 ms-os CPU-kerettel, virtuális 60 Hz-es
időbeérkezéssel 120 szimulációs másodpercet futtatott:

| Méret | Korábbi → új legnagyobb hátralék | Új host CPU p95 / p99 / max |
| --- | ---: | ---: |
| 64×64 | 3,2833 → 2,5167 szimuláció-s | 4,021 / 4,068 / 22,556 ms |
| 128×128 | 10,1500 → 7,0500 szimuláció-s | 4,034 / 4,047 / 13,384 ms |

A p99 keret lényegében változatlan. A legrosszabb minták nem javultak.
A mérés nem választja szét a JIT, a GC és az operációs rendszer ütemezésének
költségét; a képkocka-kiugrások megszűnését ebből nem állítjuk. A mérés végén mindkét méretnél egy ticknél kisebb idő maradt,
extra ledolgozó képkocka és eldobott idő nélkül. A hónaphatári késleltetés
csökkent, de a 7,05 s-os csúcshátralék továbbra is fennmaradó motorkorlát.

Nyers helyi eredmények: `artifacts/month-work-before.txt`,
`artifacts/month-work-final-profile.txt`, `artifacts/month-work-final-host.txt`.
A reprodukáló parancsok: `--month-work-benchmark` és `--rule-host-benchmark`.

Ellenőrzés: `GraphZeroGateTests` (véletlen tartományok sarkai és belső pontjai,
kerekítés, túlcsordulás és bizonytalan feltételek), `ForestCompetitionTraversalTests`
(független teljes párbejárás, checkpointból újraépített korlátok, helyi törlés,
float határértékek), `ForestCompetitionGraphTests` (távoli egyedi hatások,
mentés és naplós visszajátszás), `RuleStepTests` (újrahasznált bemeneti tömb,
elemenkénti saját állapot és megszakított lépés visszatöltése), `RuleFlowTests`
(részleges lépés közben is azonnali és megmaradó átadási összesítő), teljes tesztcsomag.


A módosítás utáni teljes Release futás **1903/1903** teszttel sikeres
(`artifacts/month-work-final-tests.txt`), a célzott átadási, forgalmi,
szeletelési és korlátbizonyítási csomag **38/38** teszttel átment
(`artifacts/month-work-final-targeted.txt`). A leltár frissességellenőrzése
is sikeres. A forgalmi tesztek a tényleges munkakeretet és a félkész esemény
eldobását ellenőrzik; nem követelik meg az elhagyott, más időzítésű szabályok
miatti mesterséges felfüggesztéseket.

## Környezeti gráfok szeletelt futása – 2026-10-16

Az időjárás, helyi klíma, párologtató képesség, lomb-víz visszacsatolás,
hóhordás és járhatóság 31 tiszta projektgráfot használ. A lombok összegzése
fánként is szeletelt; a hófluxus előkészítése, vizsgálata és publikálása
csempénként folytatható. A félkész hópublikálás közbeni betöltés tesztelt:
a megszakított világ munkája eldobódik, a visszatöltött világ pontosan folytatódik.
A száraz és csapadékos események sorsolási streamje és a redukciós sorrend rögzített.

Az újramért 128²/65 086 fás alaptempós host p99 értéke 4,081 ms,
maximuma 13,048 ms; a csúcshátralék továbbra is 7,05 szimuláció-s.
A 64 egységes eső-/hószeletek p99 értéke 0,090/0,084 ms; a ritka maximum
6,031/3,516 ms. A teljes és szeletelt checkpoint mind a négy 64²/128²
eső-/hóesetben bitre azonos, nincs eldobott idő.
Ez a csomag a környezeti döntések szerkeszthetőségét zárja le; a hónaphatári
hátralékot nem szünteti meg. [Részletes protokoll és eredmények](environment-rules.md).

## Járművek és fenntartás: közös válaszprogram – 2026-10-16

Az 5/a csomag 17 tiszta projektgráfból számolja a járműfizika és a közös
fenntartás döntéseit. A fizikai integrátor, a készlet- és költségkönyvelés,
a visszaszámláló és a seedelt sorsolás marad motorfeladat. A járműadatok
cseréje közös `VehicleSystem`-referencián keresztül azonnali; az alkalmazás
nem másolja végig a specifikációt minden teherautóra. A visszatöltés a
meglévő szeletelt járműépítés közben köti vissza ezt a kapcsolatot.

A járműgráfok cseréje nem érvényteleníti az erdei ráta-előkészítést.
Az általános alapadatok nem tartanak globális módosítható kiértékelőpéldányt;
az alapértelmezett program szálankénti, az aktív projekt programja világonkénti.

10 000 fizikai járműnél a 64 egységes szeletek p99/max értéke 0,057/0,189 ms,
a teljes és szeletelt járműállapot bitre azonos. Az 1000-es eset ritka maximuma
3,083 ms; ezeket a méréseket nem általános időgaranciaként kezeljük.
A külön 128²-es erdővilág hostpróbájának p99/max értéke 4,080/12,913 ms,
csúcshátraléka 7,0667 szimuláció-s. A havi kapacitáskorlát megmaradt.
[Részletes protokoll, korlátok és ellenőrzések](vehicle-response-rules.md).

## Gépparaméterek publikálása – 2026-10-17

Az 5/b előkészítéseként a `ForestryLogistics.Tuning` alkalmazása már nem
járja végig a gépparkot. A telephelyen létrehozott és a mentésből visszaépített
gépek a tulajdonos logisztika közös, változtathatatlan `GameTuning` példányát
olvassák. A publikálás egy referenciacsere, a gépszámtól függetlenül. A
visszakötés a már meglévő, gépenként megszakítható visszaépítésben történik.
A különálló gép továbbra is saját paraméterpéldányt használhat.

A váltás nem indítja újra a megkezdett rakodási ciklust: annak időtartama,
megfogási/elengedési pontjai és a megfogott fa továbbra is a mentett állapot
részei. Az új ciklusidőt a következő ciklus kezdetén számolja ki; például
a kapacitás lekérdezése viszont azonnal az új paramétert adja. A regressziós
próba megfogott rönkkel ment, paramétert vált, visszatölt, majd összehasonlítja
a két folytatás teljes logisztikai checkpointját. Ez önmagában nem a rakodási
szabályok gráfos átvezetése; a malom, rakodás és gépi döntések 5/b munkája
még nyitott. Ehhez a változtatáshoz nem készült új időmérés, és nem módosul
a mentés formátuma vagy a szabályok matematikája.

Ellenőrzés: 31 célzott gépi/rakodási teszt és a teljes 1926 teszt sikeres.
A teljes futás első kísérletét a tesztfigyelő az algoritmus-fordító kimenetére
várakozva megszakította; a változatlan forrás újrafuttatása
`MSBUILDDISABLENODEREUSE=1` mellett sikeres (2,85 perc).
Nyers naplók: `artifacts/machine-tuning-tests.txt`,
`artifacts/machine-tuning-full-tests-retry.txt`.
