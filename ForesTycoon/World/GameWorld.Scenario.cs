using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ForesTycoon
{
    /// <summary>World-editor scenarios: a seed, the soil types and the painted soil map become a new world.</summary>
    sealed partial class GameWorld
    {
        /// <summary>The built-in soil types, as an editable scenario on the given seed.</summary>
        internal static WorldScenario DefaultScenario(int seed = 42, int size = 64) => new() {
            Name = "Új pálya", Seed = seed, Size = size,
            SoilTypes = EditableSoils(SoilCatalog.Default, rounded: true),
            Species = ForestSpeciesTraits.Playable.Select(DefaultSpecies).ToList() };

        internal static List<SoilTypeDefinition> EditableSoils(SoilCatalog catalog, bool rounded = false) =>
            catalog.Profiles.ToArray().Select(p => new SoilTypeDefinition {
                Id = p.Id, Name = p.Name, Color = $"#{p.Color & 255:X2}{(p.Color >> 8) & 255:X2}{(p.Color >> 16) & 255:X2}",
                PreferredMoisture = p.PreferredMoisture, PreferredTexture = p.PreferredTexture,
                Saturation = p.Properties.Saturation, FieldCapacity = p.Properties.FieldCapacity, WiltingPoint = p.Properties.WiltingPoint,
                InfiltrationPerHour = p.Properties.InfiltrationPerHour, DrainagePerHour = p.Properties.DrainagePerHour,
                Fertility = p.Properties.Fertility, BearingCapacity = rounded ? Math.Round(EnvironmentSystem.BearingCapacity(p.Properties), 2)
                    : EnvironmentSystem.BearingCapacity(p.Properties)
            }).ToList();

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

        /// <summary>
        /// A species' authored tree form for the tree generator; null when it has none. Unknown parameter names are an error
        /// of the project, not something to drop silently.
        /// </summary>
        internal static TreeShapeOverride ShapeOverride(SpeciesDefinition species)
        {
            if (species.Shape == null || species.Shape.IsEmpty) return null;
            IReadOnlyDictionary<string, double> Checked(Dictionary<string, double> values)
            {
                foreach (string name in values.Keys)
                    if (!ForesTycoon.TreeModels.TreeShapeParameters.Known(name))
                        throw new System.IO.InvalidDataException($"{species.Name}: ismeretlen famegjelenési paraméter: {name}.");
                return new Dictionary<string, double>(values, StringComparer.Ordinal);
            }
            var phases = new Dictionary<TreeLifePhase, IReadOnlyDictionary<string, double>>();
            foreach (var (name, values) in species.Shape.Phases ?? new())
                if (values is { Count: > 0 } && Enum.TryParse(name, out TreeLifePhase phase)) phases[phase] = Checked(values);
            return TreeShapeOverride.Of(Checked(species.Shape.All ?? new()), phases);
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
                    d.Capacity != b.Capacity ? d.Capacity : null, F(d.TimberPrice, b.TimberPrice), ShapeOverride(d));
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
        internal void StartScenario(WorldScenario scenario, RuleProject definition = null)
        {
            SimulationWork.RequireSynchronousContext();
            // A new world starts with default rules; the project's rules and formulas carry over as one journaled change.
            var project = definition?.Clone() ?? RuleProjectDocument;
            project.World = RuleProject.FromJson(new RuleProject { World = scenario }.ToJson()).World;
            ValidateRuleProject(project);
            RequireAlgorithmRuntime(project.Algorithms);
            if (scenario.InitialState != null) {
                using var candidate = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), Graphics,
                    EcologyTime.DefaultGameSecondsPerYear, SoilLandscapeDefinition.Default, ClimateDefinition.Default);
                WorldLoader.RestoreInitialState(candidate, project.World);
                candidate.InitializeProjectScene(project);
                if (HasPresentation) candidate.AttachRendering();
                Adopt(candidate);
                return;
            }
            Regenerate(ScenarioSettings(scenario), SoilModel(scenario), ForesTycoon.Rules.EcologyResponseProgram.FromModel(project.Behaviors));
            InitializeProjectScene(project);
        }

        internal void InitializeProjectScene(RuleProject project) => SimulationWork.Complete(InitializeProjectSceneWork(project));

        internal IEnumerable<int> InitializeProjectSceneWork(RuleProject project)
        {
            var scenario = project.World;
            // Painting and construction use the authored graphs before journal publication below.
            var preparing = Task.Run(() => PrepareRuleProject(project, ReadWorldValue));
            while (!preparing.IsCompleted) yield return 64;
            var prepared = preparing.GetAwaiter().GetResult();
            forest.ResponseRules = Environment.ResponseRules = prepared.Policy.Responses;
            Logistics.Behaviors = prepared.Policy;
            Logistics.Gameplay = wildlife.Gameplay = map.Gameplay = prepared.Policy.Gameplay;
            // The painted map is the initial state: terrain (and so water) first, then the forest that grows on it.
            if (scenario != null) {
                ForestSpeciesBook.Apply(SpeciesEdits(scenario));
                var changed = new WorkResult<int[]>();
                foreach (int unit in map.ApplyHeightTargetsWork(scenario.HeightPaint, changed)) yield return unit;
                foreach (int unit in forest.ClearTerrainTilesWork(changed.Value)) yield return unit;
                foreach (int unit in forest.PaintInitialForestWork(scenario.ForestPaint.ToDictionary(p => p.Key,
                    p => p.Value.Length == 0 ? ForestSpecies.None : Enum.TryParse(p.Value, out ForestSpecies s) ? s : ForestSpecies.None),
                    preserveUnpainted: scenario.InitialState != null)) yield return unit;
                foreach (int unit in BuildSceneStructuresWork(scenario)) yield return unit;
            }
            var command = Task.Run(() => new SetRuleProjectCommand(project.ToJson()));
            while (!command.IsCompleted) yield return 64;
            Enqueue(command.GetAwaiter().GetResult());
            do { ContinueCommands(16); yield return 16; } while (commands.WorkPending);
        }

        /// <summary>What the scene asked for but the map did not allow, e.g. a mill on a slope; shown in the editor.</summary>
        internal List<string> SceneProblems { get; } = new();

        /// <summary>
        /// The scene's roads, sawmills and depots, built in that order like a player would, but free: they are the starting
        /// estate, not spending. Forest under a building's footprint is cleared first.
        /// </summary>


        private IEnumerable<int> BuildSceneStructuresWork(WorldScenario scenario)
        {
            SceneProblems.Clear();
            if (scenario.Roads.Count + scenario.Sawmills.Count + scenario.Depots.Count == 0) yield break;
            double expenses = Expenses; string status = Logistics?.Status;
            foreach (var road in scenario.Roads) {
                var surface = road.Surface == "Asphalt" ? RoadPaving.Asphalt : RoadPaving.Macadam;
                int before = RoadCount;
                foreach (int unit in RoadCommandWork(road.From, road.To, false, surface)) yield return unit;
                if (RoadCount == before && !map.IsRoadTile(road.From)) SceneProblems.Add($"Az út #{road.From} → #{road.To} nem épülhetett meg (víz vagy meredek).");
            }
            foreach (int tile in scenario.Sawmills) {
                if (map.TryGetSawmillFootprint(tile, out var footprint, out _))
                    foreach (int unit in forest.PaintInitialForestWork(footprint.ToDictionary(t => t, _ => ForestSpecies.None), preserveUnpainted: true)) yield return unit;
                var placed = new WorkResult<bool>();
                if (Logistics != null) foreach (int unit in Logistics.PlaceMillWork(tile, placed)) yield return unit;
                if (!placed.Value) SceneProblems.Add($"Fűrészmalom #{tile}: {Logistics?.Status ?? "nincs logisztika."}");
            }
            foreach (int tile in scenario.Depots) {
                if (map.TryGetSawmillFootprint(tile, out var footprint, out _))
                    foreach (int unit in forest.PaintInitialForestWork(footprint.ToDictionary(t => t, _ => ForestSpecies.None), preserveUnpainted: true)) yield return unit;
                var placed = new WorkResult<bool>();
                if (Logistics != null) foreach (int unit in Logistics.PlaceDepotWork(tile, placed)) yield return unit;
                if (!placed.Value) SceneProblems.Add($"Telephely #{tile}: {Logistics?.Status ?? "nincs logisztika."}");
            }
            Expenses = expenses;
            if (Logistics != null) Logistics.Status = status;
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
