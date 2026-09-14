# Engine review — 2026-09-13

## Corrected

- Forest variation used right-shifted hashes before conversion to a unit float. This reduced the range by 2^shift: yaw, tint, size, and shrub placement were nearly constant. Independent deterministic hash channels now cover the intended range.
- Stem placement was evaluated separately for wood and foliage. Reusable frame lists now resolve each visible stand once and feed both passes, preserving draw order and the two forest draw calls.
- Expiring effects repeatedly removed list entries, causing quadratic movement for mixed lifetimes. Stable compaction now updates and removes effects in linear time without per-update allocations.
- Nonfinite fixed-clock inputs could poison the accumulator or generate invalid simulation deltas. They are rejected before state changes. Effect updates also reject nonfinite time even for an empty effect list.
- Background scheduler shutdown could dispose synchronization resources while jobs still used them; pre-start task cancellation could permanently inflate PendingCount. Workers now always finish accounting, resource cleanup waits for the last worker, and result enqueueing is synchronized with shutdown. Work exceptions surface at the frame publication boundary instead of becoming unobserved task faults.

## Validation

- Existing baseline: 77 passing tests.
- After fixes: 88 passing tests, including variation range/determinism, mixed effect expiry, invalid clock values, queued/running job cancellation, and background failure reporting.
- Native OpenGL smoke test: 120 frames, exit code 0.
- No FPS improvement is claimed without comparative profiling. The smoke test verifies startup and rendering execution, not visual quality.

## Remaining architecture work

1. Retain forest geometry per chunk, invalidate only on terrain/stand visual changes. The frame lists eliminate duplicate placement work, but mesh construction and GPU upload still happen every frame.
2. Select forest LOD by projected pixel size/zoom with hysteresis. Current DetailLevel uses world-space crown radius, so zooming out does not reduce detail. Add simplified canopy geometry for distant stands.
3. Initial forest occupancy is a per-tile hash filter (roughly 20–33% of eligible tiles). Spatially correlated density and species fields would produce larger coherent woodland patches and natural transitions.
4. Introduce weather as explicit simulation state, with batched/bounded precipitation effects and shader-driven wind. No weather system was added in this bug-fix pass.
5. SurfacePoint currently uses bilinear height interpolation while the ground consists of triangles. Reuse the actual terrain triangle sampling before extending slope-dependent forest placement.

Road and terrain appearance were not modified.
