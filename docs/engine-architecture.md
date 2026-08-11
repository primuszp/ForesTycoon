# Engine architecture

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
| `Simulation` | game time, world lifetime, commands, ordered systems | GL calls, UI widgets |
| `Animation` | allocation-free time sampling and playback modes | entity-specific visuals |
| `Terrain` / `Roads` | authoritative map topology and construction rules | window lifecycle |
| `Rendering` | render passes, GPU resources, visual interpolation | authoritative simulation mutation |
| `Diagnostics` | frame/tick/GC/draw-call measurements | gameplay decisions |

Future systems such as forest growth, harvesting, depots, industries and economy should implement `IWorldSystem` and be registered by `GameWorld`. Player actions should enter through immutable `IWorldCommand` implementations.

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

Recommended delivery order: immutable hydrology jobs, forestry simulation, economy/cargo graph, dirty-chunk GPU caches, then replay checkpoints.
