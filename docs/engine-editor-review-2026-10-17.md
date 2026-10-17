# Engine / editor review and monthly work — 2026-10-17

Status: sustained backlog clears in the measured paced 256-square run. Residual month-boundary latency remains a P1 finding; this is not a claim that every pause is eliminated. The retained source passes all 1969 tests and the native editor, window-lifecycle and rendered startup/retirement checks.

## Scope and method

Reviewed the simulation, rule evaluator, discovered object bindings, editor test-world paths, commands, generation, save/replay and project installation across the 12 first-party projects. Searched synchronous completion adapters, native/legacy fallbacks, update entry points and the rule coverage inventory. This is an architecture and hotspot review, not a claim that every line is defect-free.

The editor sandbox uses the same GameWorld and SimulationFrameRunner as the game. Offline trials also create actual project worlds. The standalone AddSpring synchronous helper is called by the editor smoke test, not its interactive controls. Rendering, geometry, route search, inventory conservation and persistence remain engine mechanisms. The rule coverage inventory still records gameplay areas awaiting migration; they are not deleted merely because they are native.

## Corrections in this package

- World behavior, ecology and vehicle query bindings share one compiled behavior model per world. Controllers validated during compilation are reused instead of being compiled and discarded, then rebuilt.
- Delegates can be prepared without executing graphs or reading mutable world values. Graph operations, lazy choices, finite-value checks and authored bounds remain the source of behavior.
- Forest competition recognizes a static graph or a static coefficient multiplied by one health input. It caches only values derived from that graph. Unrecognized/nonlinear health expressions and health-dependent crowding use the original evaluator. Rule revision, species context, own geometry and every dependency geometry version invalidate a cache row. Health-dependent pruning cannot omit a contribution that becomes active after health changes.
- Cache rows and coefficient buffers are reused, and the second prediction buffer is reserved in sliced loading. Removed-tree rows are retired on subsequent snapshot cycles. Nothing in these caches is saved game state.
- Dirty resource neighborhoods are expanded once per source tile, not once for every tree whose health changed. Sources and observers use separate sets so an observer is not mistaken for an already-expanded source.
- Rule traversal and pair reduction use bounded batches, preserving logical operation order. Nested synchronous completion remains forbidden. Entity lists are prepared once per scope until an action invalidates them.
- Relation getters use compiled property access instead of reflection invocation for each field read. Cached object views belong to their world and static bindings release disposed worlds.
- Tile excess water, availability, canopy fill and downhill head use shared helpers for the editor object view and fast runtime reads.
- Species edits invalidate forest rates and vegetation inputs. Applying the default project retains its existing species-reset semantics. Reinstalling identical species content preserves its immutable context.
- WorldSystemCollection's synchronous entry point consumes the same sliced update implementation. Sawmill placement and scenario installation consume a sliced placement scan rather than hiding a full mill-list scan in a command unit.
- Removed the ecological runtime's reflection-based choice between separate synchronous and sliced schedulers. Every ecological process must now implement resumable work; the synchronous entry point consumes that same work. Iterator allocation is bounded (the warm scheduler test permits 256 bytes per step); the stock-network commit still allocates zero bytes.
- The native graph compiler preserves shared DAG nodes rather than expanding them exponentially or falling back to interpretation for a large shared expression. Constant folding uses the reference evaluator and never reads world values. Lazy branches, error order, finite checks and exact arithmetic order have regression coverage, including randomized DAGs and concurrent prepared evaluations.
- Weather queries are cached until event, time, restore or rule-program changes. Local climate and potential evaporation are cached per climate epoch. Bounded pure-query jobs use private scratch and publish in deterministic order; errors are deferred until the corresponding field is actually read.
- Independent own-cell stock flows may prepare fixed 1024-row blocks in a bounded ring. Both ordered and prepared execution use the same transfer kernel and canonical stock normalization; cross-cell flows remain ordered. Only committed rows advance the saved cursor. Flow ledger publication updates only touched slots, retaining original first-use dictionary order.
- Living/dead tree and stump membership lists survive steps until topology changes. A separate nonserialized storage epoch invalidates same-store restoration and tile removal. World adoption also clears object views, avoiding stale bindings after a same-size map replacement.
- Monthly preparation uses fixed cost weights so cheap snapshots finish early and expensive resource queries occupy more of the month. Water and radiation still come from completed month integrals. The transient saved preparation cursor can therefore differ from the previous scheduler; the logical biological rules and save format are unchanged.
- The game and editor now use one interactive scheduling factory: 4 ms base budget, rising with simulation debt to a hard 12 ms catch-up cap. This is an explicit CPU-budget tradeoff, not a claim of threefold computation speedup. No clock time is discarded and no graph decision depends on wall time.
- Old behavior-change commands prepare their replacement program in a background job and publish it atomically through the same resumable command path. Validation-only program creation skips executable preparation.

## Measurements

Release, seed 42, LargeMixed, rain, normal year length 900 s, no rendering. Times are elapsed main-thread simulation work per host frame, including OS scheduling; they exclude rendering/GPU work and do not measure all background CPU time. A paced run gives pure-query workers wall time between 60 Hz frames. A tight virtual run deliberately does not, so the two modes must not be presented as equivalent throughput measurements.

| Run | Trees | Duration | Main-thread p99 / maximum | Peak / end debt | Recovery frames |
| --- | ---: | ---: | ---: | ---: | ---: |
| 64², tight virtual, earlier feedback ramp | 15,795 | 240 s | 4.849 / 15.851 ms | 0.6833 / 0.0333 s | 0 |
| 128², paced, restored, earlier feedback ramp | 65,086 | 160 s | 12.007 / 18.613 ms | 1.8500 / 0.0333 s | 0 |
| 256², paced, retained final scheduling | 247,237 | 160 s | 12.034 / 16.473 ms | 5.9500 / 0.0333 s | 0 |

All runs dropped zero simulation time. The first two runs used a 0.5-second feedback ramp; the final runner reaches its same 12 ms cap after 0.1 seconds of excess debt. The 256² run crosses two monthly boundaries. Its peak after the first 30 seconds was **3.2333 simulation seconds**, so the improvement does not eliminate transient monthly latency. At 30/60/90/120/150 seconds, debt was 0.1667/0.3000/0.0333/0.0333/0.1667 seconds. The end needs no recovery frames: sustained debt is not accumulating in this paced workload.

The 12 ms cap is an explicit increase over the former fixed 4 ms CPU allowance. A tight virtual 256² stress run still accumulated debt (53.0333 s at the end of a 240 s intermediate run). Do not describe the engine as meeting a strict 4 ms capacity target or arbitrary workloads as guaranteed real time. Cache buffers also consume additional memory proportional to tree/candidate counts.

Reproduction (PowerShell, after Release build; run without concurrent builds/tests):

```powershell
$env:FORESTYCOON_PACED_HOST='1'
$env:FORESTYCOON_HOST_SECONDS='160'
dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --rule-host-benchmark 256
# Set FORESTYCOON_RESTORED_HOST=1 for the cold restored-world variant.
# Omit PACED_HOST for the conservative tight-loop stress test.
```

## Open review findings

1. **P1 — residual month transaction latency.** The 256² warm run still reaches 3.2333 seconds of debt around the monthly transaction. Fixed ticks wait for that transaction, although the host keeps yielding and rendering. `ForestSystem.Individuals.cs:StepIndividualMonthWork` preserves ordered authored writes, mortality, regeneration and publication. Removing the remaining delay requires reducing these ordered costs or defining snapshot/transaction semantics; moving arbitrary graph effects earlier changes their meaning. A further post-health parallel-query experiment was measured and removed because it increased both debt and frame spikes. No unused alternate rate-processing implementation is retained.
2. **P2 — unbounded world-total field reads.** `EnvironmentSystem.cs:SnowWater` uses a full-array sum and `StoredWater` scans all stores; `RuleEntities.cs:WorldView.Snow/Water` and named world readers expose these to graphs. Lazy named caching avoids repeated reads in a step, but the first read still contains a map-sized loop in one unit. Prepare demanded aggregates through resumable work or a deterministic ordered reduction before claiming that every graph field read has a size-independent bound.
3. **P2 — command fallthrough and fleet/site scans.** `GameWorld.CommandWork.cs` still has a default `command.Execute(this)` branch. `ForestryLogistics.Stacks.cs:StackAt/PlaceStack/RemoveStack`, and fleet lookup sites scan growing lists. The sawmill's mill-list traversal is now sliced, but its `ContainsTile` call still scans work sites and their tile arrays. These lookups need sliced work or maintained indices. Do not claim that every command unit is already bounded.
4. **P2 — synchronous project metadata validation.** `QueueRuleProject`/`ValidateRuleProject` and editor draft validation still construct graph metadata on the calling thread. Validation now skips executable JIT preparation, and actual project/behavior installation prepares in a background job, but metadata validation is not fully asynchronous.
5. **Capacity limit.** The tested normal paced workload recovers at the shared 4–12 ms budget. A strict 4 ms allowance, arbitrary graph complexity, larger/denser worlds and rendered FPS remain separate capacity questions. The measurements are not a universal no-stall guarantee.

## Validation

The final retained source passed **1969/1969 tests**. The editor Release build has zero warnings and errors. Native editor, independent-window lifecycle, and rendered startup/retirement checks all passed. The rule audit covers all 84 catalog entries plus two additional areas; diff whitespace checks pass.

An earlier complete rerun had one intermittent allocation-test failure in the unchanged effect ring. Its isolated five-test class and the subsequent full 1969-test run both passed; that production code and its zero-allocation assertion were not changed.

## Review interpretation

The rule inventory is still [rule-runtime-audit.md](rule-runtime-audit.md), generated from 84 catalog entries and two additional areas. It lists 14 gameplay areas awaiting migration, two areas with engine/profile work, and one partial override. This package does not silently replace those native rules or claim that all gameplay is already graph-authored. Terrain geometry, routing, stock conservation and persistence remain engine responsibilities.


The compiler/interpreter, typed field bindings, and optimized transfer backend execute the same authored definitions. They are generic execution mechanisms, not independently maintained gameplay formulas. Exact backend/preview comparisons guard these boundaries. The editor sandbox and game both run `GameWorld`; offline trial loops call its synchronous adapter on a background task.
