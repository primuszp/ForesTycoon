# Engine architecture

> A szerelvényekre bontott, egyirányú rétegezést az [architecture.md](architecture.md) írja le (Engine ← Ecology ← TreeModels ← játék). Az alábbi szöveg a futási folyamatot és a teljesítménypolitikát tartalmazza; a `Simulation` mappa neve azóta `World`, az erdő/víz/időjárás a `ForesTycoon.Ecology` szerelvényben van.

The engine is organized around one rule: simulation state is deterministic and independent from the window and renderer.

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
- Per-tick systems iterate indexed collections without LINQ or temporary allocations.
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
4. Replay currently rebuilds from tick zero. Add periodic validated checkpoints for long-running worlds.
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
