using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ForesTycoon
{
    /// <summary>World-editor scenarios: a seed, the soil types and the painted soil map become a new world.</summary>
    sealed partial class GameWorld
    {
        /// <summary>The built-in soil types, as an editable scenario on the given seed.</summary>
        internal static WorldScenario DefaultScenario(int seed = 42, int size = 64) => new() {
            Name = "Új pálya", Seed = seed, Size = size,
            SoilTypes = SoilCatalog.Default.Profiles.ToArray().Select(p => new SoilTypeDefinition {
                Id = p.Id, Name = p.Name, Color = $"#{p.Color & 255:X2}{(p.Color >> 8) & 255:X2}{(p.Color >> 16) & 255:X2}",
                PreferredMoisture = p.PreferredMoisture, PreferredTexture = p.PreferredTexture,
                Saturation = p.Properties.Saturation, FieldCapacity = p.Properties.FieldCapacity, WiltingPoint = p.Properties.WiltingPoint,
                InfiltrationPerHour = p.Properties.InfiltrationPerHour, DrainagePerHour = p.Properties.DrainagePerHour,
                Fertility = p.Properties.Fertility, BearingCapacity = Math.Round(EnvironmentSystem.BearingCapacity(p.Properties), 2)
            }).ToList(),
            Species = ForestSpeciesTraits.Playable.Select(DefaultSpecies).ToList() };

        private static SpeciesDefinition DefaultSpecies(ForestSpecies s)
        {
            var p = ForestSpeciesProfile.Builtin(s); var t = ForestSpeciesTraits.Builtin(s);
            return new SpeciesDefinition {
                Id = s.ToString(), Name = t.Name, Description = t.Description,
                MaximumAgeYears = p.MaximumAgeYears, MatureAgeYears = p.MatureAgeYears, MaximumBiomass = Math.Round(p.MaximumBiomass, 3),
                AnnualGrowthRate = Math.Round(p.AnnualGrowthRate, 4), PreferredMoisture = Math.Round(p.PreferredMoisture, 3),
                MoistureTolerance = Math.Round(p.MoistureTolerance, 3), PreferredElevation = Math.Round(p.PreferredElevation, 3),
                ElevationTolerance = Math.Round(p.ElevationTolerance, 3), ShadeTolerance = Math.Round(p.ShadeTolerance, 3),
                WoodDensity = Math.Round(p.WoodDensity, 3), DrySensitivity = Math.Round(t.DrySensitivity, 3),
                MatureHeight = Math.Round(t.MatureHeight, 2), MaxHeight = Math.Round(t.MaxHeight, 2), Capacity = t.Capacity,
                TimberPrice = ForestryLogistics.BuiltinTimberPrice(s) };
        }

        /// <summary>Only the numbers that differ from the built-in species become edits.</summary>
        internal static Dictionary<ForestSpecies, ForestSpeciesEdit> SpeciesEdits(WorldScenario scenario)
        {
            var edits = new Dictionary<ForestSpecies, ForestSpeciesEdit>();
            foreach (var d in scenario?.Species ?? new()) {
                if (!Enum.TryParse(d.Id, out ForestSpecies s) || s == ForestSpecies.None) continue;
                var b = DefaultSpecies(s);
                float? F(double edited, double builtin) => Math.Abs(edited - builtin) > 1e-9 ? (float)edited : null;
                var edit = new ForestSpeciesEdit(d.Name != b.Name ? d.Name : null, d.Description != b.Description ? d.Description : null,
                    F(d.MaximumAgeYears, b.MaximumAgeYears), F(d.MatureAgeYears, b.MatureAgeYears), F(d.MaximumBiomass, b.MaximumBiomass),
                    F(d.AnnualGrowthRate, b.AnnualGrowthRate), F(d.PreferredMoisture, b.PreferredMoisture), F(d.MoistureTolerance, b.MoistureTolerance),
                    F(d.PreferredElevation, b.PreferredElevation), F(d.ElevationTolerance, b.ElevationTolerance), F(d.ShadeTolerance, b.ShadeTolerance),
                    F(d.WoodDensity, b.WoodDensity), F(d.DrySensitivity, b.DrySensitivity), F(d.MatureHeight, b.MatureHeight), F(d.MaxHeight, b.MaxHeight),
                    d.Capacity != b.Capacity ? d.Capacity : null, F(d.TimberPrice, b.TimberPrice));
                if (edit != new ForestSpeciesEdit()) edits[s] = edit;
            }
            return edits;
        }

        /// <summary>The soil model of a scenario: its types as a catalog, its paint as tile overrides.</summary>
        internal static SoilLandscapeDefinition SoilModel(WorldScenario scenario)
        {
            scenario.Validate();
            var data = new SoilCatalogData { Version = 1, Profiles = scenario.SoilTypes.Select(s => new SoilProfileData {
                Id = s.Id, Name = s.Name, Color = s.Color, PreferredMoisture = s.PreferredMoisture, PreferredTexture = s.PreferredTexture,
                Properties = new SoilProperties(s.Saturation, s.FieldCapacity, s.WiltingPoint, s.InfiltrationPerHour, s.DrainagePerHour, (float)s.Fertility)
                    { BearingCapacity = s.BearingCapacity }
            }).ToArray() };
            var catalog = new SoilCatalog(JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            var index = scenario.SoilTypes.Select((s, i) => (s.Id, i)).ToDictionary(p => p.Id, p => (byte)p.i, StringComparer.Ordinal);
            return new SoilLandscapeDefinition(1, catalog, scenario.SoilPaint.ToDictionary(p => p.Key, p => index[p.Value]));
        }

        internal static TerrainSettings ScenarioSettings(WorldScenario scenario) => TerrainSettings.Default.WithNodeSize(scenario.Size + 1, scenario.Seed);

        /// <summary>Replaces this world with a new one built from the scenario; the scenario stays the project's map.</summary>
        internal void StartScenario(WorldScenario scenario)
        {
            // A new world starts with default rules; the project's rules and formulas carry over as one journaled change.
            var project = RuleProjectDocument;
            project.World = RuleProject.FromJson(new RuleProject { World = scenario }.ToJson()).World;
            Regenerate(ScenarioSettings(scenario), SoilModel(scenario));
            // The painted map is the initial state: terrain (and so water) first, then the forest that grows on it.
            ForestSpeciesBook.Apply(SpeciesEdits(scenario));
            int[] changed = map.ApplyHeightTargets(scenario.HeightPaint);
            if (changed.Length > 0) ecosystem.ApplyTerrainEdit(changed);
            forest.PaintInitialForest(scenario.ForestPaint.ToDictionary(p => p.Key,
                p => p.Value.Length == 0 ? ForestSpecies.None : Enum.TryParse(p.Value, out ForestSpecies s) ? s : ForestSpecies.None));
            QueueRuleProject(project); ExecutePendingCommands();
        }

        /// <summary>
        /// Terrain targets that make the tiles a lake: water fills closed hollows up to their rim (at most sea level), so the
        /// tiles' corners go to the water height and the corners of the surrounding tiles become a shore one step higher.
        /// Corners already painted as water are not raised, so neighbouring lakes join.
        /// </summary>
        internal Dictionary<int, int> LakeTargets(IEnumerable<int> lakeTiles, IReadOnlyDictionary<int, int> painted)
        {
            var (columns, rows) = TileGrid; int water = map.WaterHeight;
            var tiles = lakeTiles.ToHashSet();
            var bed = tiles.SelectMany(map.TileCorners).ToHashSet();
            var targets = bed.ToDictionary(n => n, _ => water);
            foreach (int tile in tiles)
                for (int dc = -1; dc <= 1; dc++)
                    for (int dr = -1; dr <= 1; dr++) {
                        int c = tile / rows + dc, r = tile % rows + dr;
                        if (c < 0 || r < 0 || c >= columns || r >= rows || tiles.Contains(c * rows + r)) continue;
                        foreach (int node in map.TileCorners(c * rows + r))
                            if (!bed.Contains(node) && !(painted.TryGetValue(node, out int h) && h <= water))
                                targets[node] = Math.Max(map.NodeHeight(node), water + 1);
                    }
            return targets;
        }
        /// <summary>Terrain targets that lift the tiles above the water.</summary>
        internal Dictionary<int, int> LandTargets(IEnumerable<int> tiles) =>
            tiles.SelectMany(map.TileCorners).Distinct().ToDictionary(n => n, n => Math.Max(map.NodeHeight(n), map.WaterHeight + 1));

        /// <summary>Column-major tile grid of the soil raster: tile ID = column × rows + row.</summary>
        internal (int Columns, int Rows) TileGrid => Soils == null ? (0, 0) : (Soils.Grid.Columns, Soils.Grid.Rows);
        internal int SoilIndex(int id) => Soils?.ProfileIndices[id] ?? 0;
        internal float TileElevation(int id) => ((IForestHabitat)map).GetNormalizedElevation(id);
        internal bool IsWaterTile(int id) => map.IsWaterTile(id);
        /// <summary>A tile's current values under the names the water and track rule formulas use.</summary>
        internal Dictionary<string, double> TileRuleInputs(int id)
        {
            var result = new Dictionary<string, double>(StringComparer.Ordinal);
            if (Environment == null || id < 0 || id >= Environment.CellCount) return result;
            var cell = Environment.Cell(id); var p = Soils?.Profile(id).Properties ?? SoilProperties.Standard;
            var (wetness, bearing) = Environment.Trafficability(id);
            result["gyökérzónavíz"] = cell.Soil; result["felszíni_víz"] = cell.Surface; result["mélyvíz"] = cell.Deep;
            result["többletvíz"] = Math.Max(0, cell.Soil - p.FieldCapacity);
            result["talaj.beszivárgás"] = p.InfiltrationPerHour; result["talaj.telítettség"] = p.Saturation;
            result["talaj.vízkapacitás"] = p.FieldCapacity; result["talaj.hervadáspont"] = p.WiltingPoint; result["talaj.drénezés"] = p.DrainagePerHour;
            result["óra"] = result["lépés_órái"] = Environment.HoursPerStep;
            result["burkolt"] = map.IsRoadTile(id) || map.IsBuildingTile(id) ? 1 : 0; result["lombborítás"] = Environment.Cover(id);
            result["átnedvesedés"] = wetness; result["talaj.teherbírás"] = bearing;
            result["vízellátottság"] = Math.Clamp(cell.Soil <= p.WiltingPoint ? 0 : p.Availability(cell.Soil), 0, 1); result["pangóvíz"] = cell.Waterlogging;
            return result;
        }
        /// <summary>Game seconds per environment hour, and the area of one tile in square metres.</summary>
        internal double SecondsPerEnvironmentHour => Environment == null ? 45 : EnvironmentSystem.StepSeconds / Environment.HoursPerStep;
        internal double TileArea { get { var g = ((IForestHabitat)map).GetForestTileGeometry(0); return g.Width * g.Height; } }
        internal double TileSurfaceWater(int id) => Environment == null ? 0 : Environment.Cell(id).Surface;
        internal double TileWater(int id) => Environment == null ? 0 : Environment.Cell(id).Soil / Math.Max(1, Environment.Cell(id).Capacity);
        internal (double Wetness, double Bearing) TileTrafficInputs(int id) => Environment?.Trafficability(id) ?? (0, 1);
        internal double TileForest(int id) => forest.TryGetStand(id, out var stand) && stand.Species != ForestSpecies.None ? ForestSystem.TimberCubicMetres(stand) : 0;
        internal bool IsUnpaved(int id) => !map.IsRoadTile(id);
        internal ForestSpecies TileSpecies(int id) => forest.TryGetStand(id, out var stand) ? stand.Species : ForestSpecies.None;
        internal bool IsTrackTile(int id) => map.IsRoadTile(id) || map.IsSkidTrail(id);
    }
}
