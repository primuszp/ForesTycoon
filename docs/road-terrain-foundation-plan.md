# Road Terrain Editing and Foundation Plan

## Goal

Implement OpenTTD-style road terrain rules for ForesTycoon:

- road geometry stays as it is now;
- road placement decides whether the road can sit on the natural terrain or needs a foundation;
- later terrain editing under a road does not deform the road surface;
- invalid terrain edits fail atomically instead of leaving road/foundation state inconsistent.

The current road rendering already has the right high-level split:

- `roadSurfaceW` stores the frozen road surface height per node;
- `RoadSurface(...)` renders the road against `roadSurfaceW`;
- `DrawRoadFoundations()` fills the gap between terrain and road surface.

The missing layer is the placement/terraform rule model that decides what `roadSurfaceW` should be and whether a road/terraform operation is legal.

## OpenTTD Reference Model

Relevant upstream files:

- `terraform_cmd.cpp`
  - `TerraformerState` stores pending height changes before execution.
  - `TerraformTileHeight(...)` changes one corner, then cascades to edge-neighbouring corners while the height difference is greater than 1.
  - `CmdTerraformLand(...)` computes the pending landscape first, then asks each affected tile type if the new slope is valid via `terraform_tile_proc`.
- `road_cmd.cpp`
  - `_invalid_tileh_slopes_road` encodes slope/road-bit combinations that are not valid.
  - `CheckRoadSlope(...)` validates road bits on the current slope and accounts for foundation cost.
  - `TerraformTile_Road(...)` allows autoslope/foundation only if the road's effective surface slope remains unchanged.
- `landscape.cpp`
  - `ApplyFoundationToSlope(...)` separates the raw terrain slope from the effective top surface slope after foundation application.

Translated to ForesTycoon: terrain height, road top surface, and foundation walls should be separate concepts.

## Current Local State

Relevant files:

- `ForesTycoon/Terrain/Terrain.cs`
  - `roadSurfaceW`: frozen road top-surface height per node.
  - `IsRoadBuildable(Tile t, RoadEdge edges)`: currently only allows planar tiles with `W + E == S + N`.
  - `BuildRoadTilePath(...)`: currently adds road edges and captures current terrain corner heights.
  - `DrawRoadFoundations()` and `FoundationWall(...)`: render vertical walls on non-road neighbouring sides.
  - `ElevationManager(...)`: already computes terrain edits atomically through a pending map.
- `ForesTycoon/Roads/RoadNetwork.cs`
  - stores road edge bits per tile.
- `ForesTycoon/Core/Tile.cs`
  - stores corner nodes, relative code, and low height.

The current blocker is `IsRoadBuildable(...)`: it conflates road placement validity with "all four corners are planar". That blocks valid foundation cases and does not model OpenTTD road slope rules.

## Design

### 1. Add a Local Slope Description

Add private helper types inside `Terrain.cs` first. Move to separate files only if the code grows.

Suggested types:

```csharp
private enum TileSlopeKind
{
    Flat,
    OneCornerRaised,
    TwoAdjacentRaised,
    TwoOppositeRaised,
    ThreeCornersRaised,
    Steep
}

private enum TileCorner
{
    W,
    S,
    E,
    N
}

private readonly struct TileSlopeInfo
{
    public readonly TileSlopeKind Kind;
    public readonly int Min;
    public readonly int Max;
    public readonly bool WRaised;
    public readonly bool SRaised;
    public readonly bool ERaised;
    public readonly bool NRaised;
}
```

Rules:

- `Min = min(W, S, E, N)`.
- `Max = max(W, S, E, N)`.
- if `Max - Min > 1`, treat as `Steep`.
- raised flags mean `corner.W > Min`.
- classify by raised count and adjacency.

This should replace ad hoc checks such as `t.W.W + t.E.W == t.S.W + t.N.W` in road build logic.

### 2. Add Road Placement Analysis

Replace `IsRoadBuildable(Tile t, RoadEdge edges)` with analysis, then keep `IsRoadBuildable` as a wrapper.

Suggested result type:

```csharp
private enum RoadPlacementKind
{
    Invalid,
    NaturalSurface,
    FoundationSurface
}

private readonly struct RoadPlacement
{
    public readonly RoadPlacementKind Kind;
    public readonly int W;
    public readonly int S;
    public readonly int E;
    public readonly int N;
}
```

Suggested API:

```csharp
private RoadPlacement AnalyzeRoadPlacement(Tile t, RoadEdge requestedEdges)
```

Inputs:

- current terrain corner heights;
- existing road edges on the tile;
- requested new road edges;
- existing frozen road surface at shared nodes, if any.

Output:

- invalid placement;
- natural terrain placement with current corner heights;
- foundation placement with computed top-surface heights.

`IsRoadBuildable(...)` becomes:

```csharp
return AnalyzeRoadPlacement(t, edges).Kind != RoadPlacementKind.Invalid;
```

### 3. Road Slope Rules

Start with a pragmatic OpenTTD-inspired subset, not every special case.

Allowed natural surface:

- flat tile: any road edge combination;
- planar two-adjacent raised slope: straight road along the slope-compatible axis;
- existing frozen road surface exactly matches the newly requested surface.

Allowed foundation surface:

- one-corner raised or three-corners-raised slopes where a level top can be formed at `Max`;
- straight road on supported simple slopes, with the top surface computed at the valid level;
- only if every involved node can use the computed surface without conflicting with already frozen neighbouring road nodes.

Invalid:

- water tile;
- steep tile (`Max - Min > 1`);
- two-opposite-raised saddle;
- road bit combination that would require different top-surface slopes from already existing road edges;
- any computed surface below terrain at a road corner.

This is intentionally stricter than full OpenTTD. It gives correct foundation behavior first, then can be expanded.

### 4. Capture Computed Road Surface, Not Raw Terrain

Change:

```csharp
CaptureRoadSurface(Tile t)
```

to:

```csharp
CaptureRoadSurface(Tile t, RoadPlacement placement)
```

Behavior:

- for `NaturalSurface`, store current `W/S/E/N`;
- for `FoundationSurface`, store computed `placement.W/S/E/N`;
- if a node already has `roadSurfaceW`, require the same value, otherwise placement is invalid.

Change `BuildRoadTilePath(...)`:

1. analyze placement;
2. skip invalid tiles;
3. add road edges;
4. capture the analyzed surface.

This prevents the common bug where a foundation-capable tile is accepted but the road is frozen to the wrong raw terrain shape.

### 5. Preview Uses the Same Analysis

Change preview bad/ok checks from:

```csharp
!IsRoadBuildable(tiles[step.TileId], step.Edges)
```

to:

```csharp
AnalyzeRoadPlacement(tiles[step.TileId], step.Edges).Kind == RoadPlacementKind.Invalid
```

Optional visual enhancement:

- white preview: natural surface;
- pale yellow/blue preview: foundation surface;
- red preview: invalid.

This will make foundation cases visible before build.

### 6. Terraform Under Roads

Current `ElevationManager(...)` already has the right atomic pending-change shape.

Next step: after pending heights are computed but before applying them, validate affected road tiles.

Suggested flow:

1. Build `pending` as today.
2. Collect all tiles touching changed nodes.
3. For every tile with road edges:
   - compute the terrain slope using pending heights;
   - keep the road surface from `roadSurfaceW`;
   - validate that the road can still exist above that new terrain;
   - if invalid, reject the whole terrain edit.
4. Apply pending heights only after validation passes.

Important distinction:

- road build computes and freezes `roadSurfaceW`;
- later terrain editing validates against the frozen surface;
- later terrain editing must not recapture or deform the road surface.

### 7. Foundation Rendering Follow-up

The current `FoundationWall(...)` is usable for first pass.

Refinement after placement rules work:

- draw a wall only if road surface is above the visible neighbouring terrain edge;
- do not draw a wall between adjacent road tiles if both share the same frozen edge height;
- if adjacent road tiles share the side but have different surface heights, draw a retaining wall or mark the placement invalid. Prefer invalid first.

### 8. Tests / Verification

Because this is currently a WinForms/OpenGL prototype, start with deterministic helper tests where possible.

Minimum manual scenarios:

- build road on flat tile;
- build road on simple ramp;
- build road on one-corner-raised tile and verify foundation appears;
- reject road on saddle tile;
- reject road on steep tile;
- build road, lower terrain under one side, verify road surface stays fixed and foundation wall grows;
- try terrain edit that would put terrain above the frozen road surface, verify edit is rejected.

If adding automated tests later, extract slope and placement helpers into a non-OpenGL class so they can be tested without a rendering context.

## Implementation Order

1. Add slope classification helpers in `Terrain.cs`.
2. Add `RoadPlacement` and `AnalyzeRoadPlacement(...)`.
3. Change `IsRoadBuildable(...)`, preview validation, and `BuildRoadTilePath(...)` to use placement analysis.
4. Change `CaptureRoadSurface(...)` to capture analyzed surface heights.
5. Add pending-road validation to `ElevationManager(...)`.
6. Tune foundation wall rendering only after the placement rules are stable.

## Non-goals For First Pass

- full OpenTTD rail/tram/depot/tunnel/bridge rules;
- road ownership and authority checks;
- costs;
- exact OpenTTD sprite foundation variants;
- automatic terrain clearing around roads.

The first pass should make road terrain editing correct and predictable before matching every OpenTTD edge case.
