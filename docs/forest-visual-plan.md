# Forest Planting and 3D Appearance Plan

## Goal

Make a planted forest read like the reference illustration: stands built from **many
trees**, not one tree per tile — dense blocks with a visible edge, scattered solitary
trees on the open grass, low undergrowth, a species/season colour per stand, and a
marked management parcel. The simulation model stays as it is; this is a placement,
level-of-detail and tooling plan.

## Current State (2026-09-13)

- Multi-stem deterministic placement, size tiers, crowding response, undergrowth and area planting are implemented.
- Forest wood and crowns use persistent per-chunk GPU geometry, rebuilt on quantized visual changes, terrain edits or LOD changes.
- Three zoom-dependent LOD bands use hysteresis. Far views retain simplified per-stem crowns; stand-wide canopy meshes are still planned.
- Initial forest occupancy is still a per-tile hash filter. Spatially correlated woodland patches, species mixing and seasonal colour remain future work.
- See `engine-architecture.md` for cache invalidation, thresholds and the dedicated OpenGL regression test.

The design below records the broader visual direction; it includes both implemented and planned features.
## Design

### 1. Stand → many stems (`ForestStandVisual`)

Keep `ForestStand` as the authority; derive the drawn population from it.

```
readonly record struct StemPlacement(
    float X, float Y, float Z,     // tile-space, Z sampled on the tile surface
    float Scale, float Yaw, float Tint,
    ForestSpecies Species, byte Tier);   // Tier: dominant / co-dominant / suppressed
```

Population rule, evaluated per tile:

- `targetStems = Lerp(SaplingStems, MatureStems, stand.Maturity) * StockingFactor`,
  where `StockingFactor = stand.Biomass / profile.MaximumBiomass` clamped to
  `[0.25, 1]`. Suggested per-species `MatureStems` (a new field on
  `ForestSpeciesProfile`): spruce 14, birch 12, beech 9, oak 6 — a wide oak crown
  fills the tile with fewer stems.
- Placement is a **jittered sub-grid**, not free random: split the tile into an
  `n × n` lattice (`n = ceil(sqrt(targetStems))`), keep the first `targetStems`
  cells in hash order, and jitter each stem inside its cell by ±40 %. This gives an
  even, non-clumping, allocation-free, fully deterministic layout — no Poisson
  rejection loop.
- Height: bilinear interpolation of the four tile corner heights at the stem's
  `(u, v)`, so stems follow the slope instead of all sitting on the tile-centre plane
  (today's `centerZ`).
- Reuse the existing hash: `TreeHash(tileId, salt)` with a stem index folded into the
  salt keeps determinism and needs no per-tile storage.

### 2. Age and shape variation inside a stand

- **Tiers.** ~20 % dominants (`scale × 1.15`), ~55 % co-dominants (`× 1.0`), ~25 %
  suppressed (`× 0.62`, fewer crown lobes, no branches). Drawn from the same hash,
  so a stand shows a real height profile rather than a flat green plane.
- **Edge response.** `ForestSystem.GetCrowding(tileId)` is already available. Feed it
  into the crown: interior (high crowding) → crown radius × 0.8, crown height × 1.15
  (drawn up towards the light); edge (low crowding) → radius × 1.2, height × 0.9.
  This alone makes stand boundaries read the way they do in the reference image.
- **Species mixing.** With probability `1 - purity` (say 0.12) a stem takes the
  species of a hash-picked neighbouring stand. Mixed stands stop looking like flat
  colour fields; pure plantations still read as pure.
- **Seasonal / health colour.** Extend `Weather(...)` with a season parameter and
  give each broadleaf species an autumn crown colour (oak rust, beech copper, birch
  yellow); conifers stay. The illustration's red and gold blocks are exactly this.

### 3. Undergrowth and open-field trees

- **Undergrowth.** Below a stand, 3–5 shrub blobs per tile: one `LobeOutline` sweep,
  5 sides, height ~0.6 m, colour a darkened crown colour. Drawn only in the near LOD
  band. Cheap, and it removes the "trees standing on bare grass" look.
- **Scattered trees.** The reference has solitary trees on the open slopes. The
  seeding pass in `ForestSystem` already creates such stands; with the stocking rule
  above a young, low-biomass stand naturally renders as 1–3 stems. No new mechanism.

### 4. Level of detail and caching (the enabling work)

Density multiplies vertex count by roughly 10×, so this part comes first in the
implementation order even though it is invisible.

- **Chunked cache.** Build stem geometry per `TerrainChunk` into a retained
  `VertexBuffer` (wood + foliage), rebuilt only when a stand in that chunk changes
  (plant, harvest, monthly growth step crossing a visual threshold) or the terrain
  under it is edited. Add a `chunkForestDirty` flag set from `GameWorld` on those
  events. Frame cost becomes two draw calls per visible chunk, independent of stem
  count.
- **Three LOD bands**, chosen from camera zoom, applied when a chunk is built:
  - *near*: today's full model — bole, branches, lobes, 8 sides, undergrowth.
  - *mid*: 5 sides, no branches, no lobes, undergrowth dropped, stems reduced ~50 %
    (drop suppressed tier first — they are hidden under the canopy anyway).
  - *far*: no stems; one flattened canopy blob per tile, sized from the stand's crown
    radius and coloured by species, plus a species tint blended into the ground.
    This is what makes the wide-zoom view read as coloured forest blocks.
- Threshold the visual scale so growth does not rebuild every chunk every month:
  quantise `TreeVisualScale` to ~8 steps and rebuild only on a step change.

### 5. Planting tools

Single-tile planting cannot produce the parcel shapes in the image.

- **Brush and rectangle planting.** Extend the forestry interaction to a drag
  operation producing a tile set; emit one `PlantForestAreaCommand(tileIds, species)`
  (or a batched sequence) so replay/persistence stays deterministic. Reuse the
  existing terrain-edit drag machinery in `Interaction/WorldInteractionController.cs`.
- **Parcel overlay.** A dashed outline around the pending or selected planting area,
  drawn in the existing overlay pass — the white dashed boundary in the reference.
  Purely visual, no simulation state.
- Failure feedback: an area plant reports counts (planted / occupied / unsuitable)
  instead of one `ForestryActionResult`.

## Implementation Order

1. Chunked forest geometry cache + dirty flags (no visual change, enables the rest).
2. LOD bands.
3. Multi-stem placement (`StemPlacement`, sub-grid, slope-correct Z, `MatureStems`
   on the species profile).
4. Tiers, crowding-driven crown shape, species mixing.
5. Undergrowth, seasonal colours.
6. Area planting tool + parcel overlay.

Each step is independently shippable and testable; steps 3–4 are pure functions of
`(tileId, stand, crowding)` and can be unit-tested for determinism in
`ForesTycoon.Tests`.

## Art Direction Read from the Reference Image

The density plan above fixes the geometry, but the reference's look comes from a
handful of separable decisions. Listed with where they land in this codebase.

### What the image actually does

1. **Paper diorama, not a game world.** Cream background, soft drop shadow under the
   block, matte flat colours, faint paper grain. Nothing is glossy or specular.
   → `BG_COLOR` in `App/Viewport.cs:211` is dark blue-grey (44,53,64); the reference
   is roughly (240,232,214).
2. **Cut-away earth block with geological strata.** The side walls show 4–6 distinct
   horizontal bands (topsoil, pale sand, brown clay, grey rock, dark bedrock), and
   the rock band under the cliff is drawn with a different, blockier texture.
   → `DrawSkirts()` currently draws exactly two bands (`colorRim` + `colorFront`).
   A banded palette by absolute Z is a small, self-contained change.
3. **Silhouette-first trees.** Individual trees are *tiny* — a mature conifer is maybe
   a fifth of a tile — flat-shaded, two tones at most, with a clearly readable
   triangular or round outline. There is no per-face lighting detail on them at all;
   they read purely as shape plus colour.
   → Our trees are proportionally far too large and too internally shaded. Reducing
   the visual scale is what makes room for the multi-stem density in section 1: the
   canopy effect comes from *many small* silhouettes, never from few large ones.
4. **Canopy as texture.** Inside a stand the crowns overlap enough that the ground is
   completely hidden, and the block reads as one grain of repeated shapes with a
   slightly ragged edge. The stand's identity is its **colour field**, not its
   individual trees.
5. **The ground is tinted under a stand.** The red plateau and the gold block have
   red/gold *ground*, not just red/gold crowns, and the tint stops at the stand
   boundary with a crisp line. This is what makes the blocks read from far away.
   → Species/season tint blended into the terrain tile colour, strength scaled by
   stocking. Cheap, and it doubles as the far LOD in section 4.
6. **Feathered edges, hard parcels.** Two different kinds of boundary coexist: the
   *natural* forest edge is ragged and thins out into scattered solitary trees on the
   grass, while the *managed* parcel edge is a crisp white dashed line (and a solid
   red line on the plateau). Do not confuse them — the dashed line is UI, the ragged
   edge is simulation.
7. **Ground has drawn detail, not texture.** Faint lighter contour/erosion lines run
   across the grass, following the slope. They give scale and terrain readability
   without any texture map.
8. **Contact shadows only.** No cast shadow volumes; each object has a small soft
   dark ellipse under it. Light reads as coming from the upper left, consistently.
   → We already have a fixed sun (`TreeLight`); a per-stem contact ellipse in the
   near/mid LOD is enough and costs a few triangles.
9. **Working machines are the focal points.** The forwarder and the cable yarder with
   its hanging log bundle are the only saturated dark objects on screen; everything
   else is muted. Contrast, not size, makes them read.
10. **Muted, desaturated palette with a few accents.** Grass is olive rather than
    green; the accent colours (rust red, gold) appear only on managed stands. Our
    current crown colours are more saturated than the reference across the board.

### Cheap wins vs. structural work

Immediate, independent of the density work — these three alone move the look most:

- background colour + a soft ground shadow under the block;
- banded strata in `DrawSkirts()`;
- desaturated palette pass, and a smaller `TreeVisualScale` baseline.

Structural, and already covered above: multi-stem density (1), ground tint under
stands (5, = far LOD), ragged stand edges (6, from crowding), contact shadows (8),
contour lines on the grass (7, terrain render pass).
