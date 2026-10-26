# Biome, Elevation and Diorama Ground Plan

## Goal

The diorama should read as a real landscape cut out of the ground:

- the land falls into **elevation zones** — coast, lowland plain, hills, mountains — and each zone
  looks, grows and is managed differently;
- a **biome** ties a zone to its ground look, soils and species, so a coast is not just
  "low green tiles";
- the four **sides of the diorama show the soil profile** (the horizons of the soil under the edge
  tile) instead of one flat brown wall;
- **tile textures vary** by biome, soil and moisture, not only by height band;
- the **editor supports every part of it** from the first version: zones, biomes, soil horizons and
  textures are scenario data, edited and previewed in the editor, and checked by the coverage test.

## Current State (2026-10-25)

| Area | Today | Where |
|---|---|---|
| Height | Integer levels 0–6 (`MaxHeight`), 2 m each (`HeightScale`), sea level 3 m. 12 m of relief cannot hold hills and mountains. | `TerrainSettings.Default`, `TerrainGenerator` |
| Ground colour | Hard-coded colour pair per height level (`low <= 1` sand, 2–3 green, 4–5 olive, 6+ rock/snow); soil type is not used. | `Terrain.Mesh.cs` `GetTerrainColorRange` |
| Ground texture | One shared detail layer for all ground (`kind == 1`), plus a 24 m patch noise and the season tint. | `OpenGlSurfaceVisualRenderer` |
| Diorama sides | One flat colour per side and a dark rim strip, 16 units deep. | `Terrain.TerrainRender.cs` `DrawSkirts` |
| Soils | Four soils (sand, loam, clay, organic) with water and fertility values, no horizons. | `profiles.json`, `SoilTypeDefinition` |

### Parallel parts this plan removes

- **Soil definitions — checked, not parallel.** `profiles.json` is the only source of the built-in
  soils. `SoilTypeDefinition` is the editable form stored in the scene, `SoilCatalog` the compiled,
  hashed runtime form that saves refer to; both conversions sit side by side in
  `GameWorld.Scenario.cs` (`EditableSoils`, `SoilModel`). Merging the two formats would change the
  catalog hash and break existing saves and the opening-scene fingerprints, so they stay layered.
  Soil horizons (phase 2) are added to `SoilTypeDefinition` and carried through `SoilModel`.
- **Tile shading — done (phase 1).** The terrain mesh, the flat road foundation and the foundation
  slope faces each had their own copy of the lambert shade; `TileShading.Shade` now serves all three
  (and guards the degenerate face the mesh copy turned into NaN).
- **Colour decided in two places.** Height bands in C# pick the colour, the shader then adds its own
  detail. After this plan the CPU writes only *material ids and weights*; the shader owns the look.

## Design

### 1. Elevation zones

Heights stay integer levels (the road, foundation and water rules depend on them), but the range
grows and each level gets a meaning through zone thresholds:

```
TerrainSettings: MaxHeight 6 → 16, HeightScale 2 m (unchanged) → 32 m of relief
ElevationZoneDefinition { Id, Name, Color, MaxLevel }   // ordered low → high; a zone reaches from
                                                        // the zone below up to MaxLevel, the last is open
default zones (MaxHeight 6):  coast ≤ 1 · plain ≤ 3 · hills ≤ 5 · mountain above
phase 5 (MaxHeight 16):       thresholds move up with the taller relief
```

- The generator gets a **relief profile per zone mix** (`ReliefProfile`: share of the map per zone,
  roughness, terrace strength) instead of one `TerraceMap`, so a scenario can be "coastal lowland"
  or "hill country". Existing maps keep their heights: a level-6 map is all coast/plain/low hills.
- Zones are **derived, not stored per tile**: `ElevationZones.Of(tileLow)` is a pure lookup, so
  terrain editing changes the zone with the height and there is nothing to keep in sync.
- Slope is a second axis (`TileSlope`: flat / gentle / steep from the tile code). Steep tiles in
  hills and mountains show rock and limit machine access — one rule for both look and gameplay.

### 2. Biomes

A biome is the per-zone look and ecology, authored as scenario data:

```
BiomeDefinition {
  Id, Name, Zone                 // which elevation zone it belongs to
  GroundMaterials[]              // material id + weight by moisture (dry → wet) and slope
  SoilWeights{soilId: weight}    // what the soil generator prefers here
  SpeciesWeights{speciesId: w}   // initial forest and natural regeneration preference
  Undergrowth                    // density and type, used by the forest floor
}
```

- **One biome per zone by default** (dune coast, lowland meadow, oak–hornbeam hills, beech–spruce
  mountains); a scenario may define several per zone and the generator picks by a low-frequency
  climate noise, so the map has regions, not stripes.
- The biome is **resolved per tile** once at generation and after terrain edits
  (`BiomeMap`, a byte per tile, saved with the checkpoint). It is the only input the ground
  material, soil generation and species preference read — no system re-derives "is this coast".
- Rules can read it: `tile.biome` and `tile.zone` become rule fields, so the editor's rule book can
  write "regeneration on coast: ×0.5" without code.

### 3. Ground materials and texture variety

- A **material palette** (texture array layers) replaces the colour pairs: dune sand, wet sand,
  meadow grass, dry grass, forest floor, moss, gravel scree, bare rock, alpine grass, snow.
- The mesh writes per vertex **two material ids and a blend weight** (from the biome's
  `GroundMaterials`, the soil, the moisture of the tile and the slope); the shader blends the two
  layers with a height-aware mask so edges between materials are ragged, not tile-square.
- Tile-scale repetition is broken by **world-space macro variation** (a 40–80 m noise on hue and
  brightness) and per-tile hashed rotation of the detail UV.
- The grid stays a decal: drawn only in build/inspect tools at full strength, faint otherwise
  (separate change, listed in the follow-ups).

### 4. Soil profile on the diorama sides

- Each soil gets **horizons**:

```
SoilHorizon { Name, Depth (m), Color, Texture (material id), Stones (0–1) }
default loam: O 0.05 dark brown litter · A 0.25 humus · B 0.6 brown · C 1.5 pale parent material
```

- `DrawSkirts` is replaced by `DioramaSides`: for every edge tile it stacks the horizons of that
  tile's soil from the surface down, **following the terrain surface** (horizon boundaries are
  offsets below the surface, so they rise and fall with hills), then bedrock to the plinth base.
- Under water the side shows water, then a mud layer; in mountains the profile thins to rock
  (horizon depths scale by the zone's `SoilDepth`).
- Depths are exaggerated for the miniature look by one `ProfileScale` (default ×6), so a 0.25 m
  A horizon is visible at diorama scale; the scale is a scenario value, not a constant.
- The sides are static geometry rebuilt only when an edge tile's height or soil changes, using the
  existing chunk cache.

### 5. Editor support (part of every phase, not a follow-up)

| Data | Editor page | Preview |
|---|---|---|
| Elevation zones, relief profile, `MaxHeight` | Térkép → Generálás (new "Domborzat" section) | Map layer "Magassági övek" |
| Biomes | New page **Világ → Biomok** | Map layer "Biom"; ground material swatches |
| Ground materials | Biomok → Talajfelszín | Live material ball / tile strip in the test world |
| Soil horizons, `ProfileScale` | Talajtípusok → Rétegrend | Drawn soil column next to the soil card |
| `tile.zone`, `tile.biome` rule fields | Szabályok (field picker) | Rule card sentence |

- **One source of truth**: all of the above live in `WorldScenario` and travel with the scene file;
  the game reads only the scenario. No hard-coded copy remains in C#.
- **Coverage is enforced**: `EditorCoverage` (the `--check-editor-coverage` gate) is extended to
  scenario data — every `WorldScenario` property and every rule field must have an editor page,
  otherwise the check fails. A new game feature without an editor surface cannot ship.
- Migration: an older scenario without zones/biomes/horizons gets the defaults on load through the
  shared `GameWorld.MigrateRuleProject` path, so the game and the editor see the same document.

## Phases

1. **Foundations — done 2026-10-25.** `TileShading.Shade`; `ElevationZoneDefinition`
   (`WorldScenario.ElevationZones`, built-in coast ≤1 · plain ≤3 · hills ≤5 · mountain, a scene
   without zones reads the built-in ones), `WorldScenario.ZoneOf`, `TerrainSlope` from the tile
   corners, `GameWorld.TileLevel/TileSlope/TileZone`; editor map layer **Magassági övek** with
   zone editing, zone and slope in the tile facts, one wrapping map legend; `EditorCoverage` checks
   every scene field (`WorldScenario` and the item types it lists) against `SceneSurfaces`. The
   check found and fixed one gap: the species description had no editor field.
   Found on the way: the crown lattice hull (`LobeCrownMesh.Sphere`) was O(n⁴) under a global lock —
   5.8 s for the 384-point Near lattice on an idle machine, far more under load — stalling every
   tree build (and the editor's life-course preview test) the first time a large crown was built.
   `LobeCrownMesh.HullFaces` builds the identical hull from local neighbourhoods in ~25 ms.
2. **Soil profile sides — done 2026-10-25.** `SoilHorizon` (name, metres, colour) on
   `SoilTypeDefinition.Horizons`; empty means the soil's built-in O/A/B/C profile
   (`SoilHorizon.Defaults`: sand, loam, clay, organic, and a generic one for authored soils);
   `WorldScenario.ProfileScale` (default ×6). Horizons are presentation only — they stay out of
   the soil catalog, so its hash, saves and the opening fingerprints are unchanged.
   `GameWorld.SideProfile` resolves a tile's layers (cached per soil); `Terrain.DrawSkirts` draws
   the bands following the surface, bedrock to the plinth. Editor: Talajtípusok → *Rétegrend*
   (drawn column with a depth scale, horizon list, scale slider); editing horizons does not
   rebuild the test world. Found on the way: `Adopt` handed the scene to the adopting world but
   left its callbacks on the retired one (the side profile and the existing flood depth read the
   bootstrap world); `BindRendering` now rebinds them on attach and on adopt.
3. **Biomes — part 1 done 2026-10-25.** `BiomeDefinition` (id, name, colour, zone, share,
   description) in `WorldScenario.Biomes`, one built-in biome per zone. The biome is *derived*,
   not stored: `WorldScenario.BiomeOf(level, column, row)` takes the zone's biomes and, with
   several, picks by share from a smooth 16-tile noise of the map seed — so terrain edits move it
   and saves need nothing new (this replaces the planned stored `BiomeMap`). Rule fields
   `tile.zone`, `tile.biome` (indices) and `tile.slope` (enum). Editor: **Biomok** page, map layer
   **Biom**, biome in the tile facts; editing biomes does not rebuild the test world.
   **Part 2 — done 2026-10-25: biomes steer species through rules.** Fixed biome weights were
   rejected: a save without a checkpoint regenerates from `SoilLandscapeDefinition`, so weights
   applied only at generation would not survive a replay. Instead the site-suitability graph
   (`forest.site`) gained the inputs `tile.zone`, `tile.biome`, `tile.slope` and `species.kind`
   (appended, so existing graphs are unchanged; the default graph ignores them and the opening
   fingerprints are identical). The forest reads them through `IForestHabitat.GetLandscape`; the
   map gets its zone/biome classifier from the owning world (`BindWorldQueries`, also on adopt).
   The suitability drives the initial forest, natural regeneration, planting advice and tree
   resources alike, and lives in the project, so saves replay it. Soil generation is not
   biome-driven; soils stay painted or generated by moisture/texture.
4. **Ground materials — done 2026-10-25.** `GroundMaterialDefinition` (name, colour, pattern) in
   `WorldScenario.GroundMaterials`; each biome names its dry, wet and steep ground.
   `WorldScenario.Ground(biome, moisture, slope)` is the one source for the game and the editor.
   Terrain: corner colours average the meeting tiles plus a 60/23-unit brightness and warmth
   noise (`Terrain.Ground.cs`); a per-tile ground map (unit 6) carries the texture layers of the
   ground and steep patterns, blended bilinearly across tiles in the surface shader; five new
   procedural layers (sand, rock, moss, gravel, snow). Editor: Biomok → *Felszínanyagok*, the three
   grounds per biome, map layer *Felszín*.
   Two lessons recorded: the first shader version mapped pattern → layer with integer ternaries in
   a four-times-inlined function, which macOS took minutes to compile — the map now stores the
   layers; and the loading screen could stay up forever when the final preparation pass needed
   more than one 4 ms slice after a generated world arrived already prepared (`Viewport.RenderScene`
   now keeps the loading frame until `isLoaded`).
5. **Relief — done 2026-10-26.** `WorldScenario.MaxHeight` (4–16 levels, default 6, so existing
   scenes and saves are unchanged; it travels in the saved terrain settings) feeds
   `ScenarioSettings`; the generator already scales its relief to it. Default zones follow the
   height (`ElevationZoneDefinition.Defaults(maxHeight)`: 16 levels → plain ≤4, hills ≤8, about a
   tenth mountains). Editor: Térkép → *Domborzat magassága*, zones on their defaults follow it.
   Measured on a 128-tile map, 6 vs 16 levels: generation 1.2 vs 2.0 s, 40 terrain edits 1.4 vs
   1.5 s, one game minute 12.1 vs 12.0 s. The UI font now loads the arrows block (← ↑ → ↓).
   Ecology reads elevation against a fixed reference (`TerrainMap.EcologyReferenceLevels` = 6,
   clamped to 1), so a level means the same on every map; editor maps still shade within the
   map's own relief. Measured: this was *not* the cause of sparse forest on generated tall maps —
   the generator lays down sparse forest on any relief (seed 42, 64 tiles: 59 forested tiles on six
   levels); the opening scene's forest is painted.
   **Sparse forest — fixed 2026-10-26.** Cause: the map moisture is a coarse class (0 away from water,
   0.35f on lowland, 0.45 by streams, 0.6 by lakes), and the default `forest.initialAllow` rule
   required moisture ≥ 0.35; 0.35f is just below 0.35, so all lowland failed and only water edges
   got trees, then one tile in three or five at random. The rule now has a smooth `noise.patch`
   input (`ForestPatchNoise`, ~8-tile patches with 3-tile edges) and reads: no forest on standing
   water or the lowest coastal level, otherwise woods where the patch noise exceeds
   0.58 − 0.25 × moisture. Generated maps now carry 30–48 % forest on suitable tiles, 93–98 % of it
   in connected woods, on low and tall relief alike (`InitialForestCoverTests`).

Each phase ends with the editor smoke test capturing the new page and a game capture
(`--smoke-test --capture-frame`) before/after, kept under `artifacts/`.

## Open Questions

- Should mountains allow forestry machines on steep tiles at a cost, or block them? (Proposed: a
  `tile.slope` rule field with a default block above "steep", editable in the rule book.)
- Is snow a biome material (permanent above a height) or only the seasonal weather layer?
  (Proposed: both — `alpine` biome adds permanent snow above its top level; weather adds seasonal.)
- `MaxHeight` 16 multiplies terrain edit and hydrology work; phase 5 needs the large-world
  benchmark run before it lands.
