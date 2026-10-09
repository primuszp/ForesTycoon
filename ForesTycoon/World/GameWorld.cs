using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>
    /// Owns game-state lifetime and is the boundary used by input, simulation and rendering.
    /// Viewport code must not own or replace individual world systems directly.
    /// </summary>
    sealed partial class GameWorld : IDisposable, IWorldCommandTarget, IWorldInteractionTarget
    {
        private Terrain terrain;
        /// <summary>The ground model; the <see cref="Terrain"/> scene only draws it.</summary>
        private TerrainMap map => terrain.Map;
        private WildlifeSystem wildlife = new WildlifeSystem();
        internal ForestryLogistics Logistics { get; private set; }
        private TerrainRenderer terrainRenderer;
        private readonly WorldCommandQueue commands = new WorldCommandQueue();
        private WorldSystemCollection systems = new WorldSystemCollection();
        private BackgroundJobScheduler backgroundJobs = new BackgroundJobScheduler();
        private readonly List<WorldCommandRecord> commandJournal = new List<WorldCommandRecord>();
        private VehicleSystem vehicles;
        private WorldEffectSystem effects;
        private Ecosystem ecosystem;
        private ForestSystem forest => ecosystem.Forest;
        private TimberCargoSystem timberCargo;
        private ulong worldTick;
        internal EnvironmentSystem Environment => ecosystem.Environment;
        internal SoilLandscape Soils => ecosystem.Soils;
        private ForestryActionResult lastForestryAction;
        /// <summary>Money spent on building and repairing, thousand forints.</summary>
        internal double Expenses { get; private set; }
        private double roadWeatherSeconds;
        private ForestryAreaSummary lastForestryArea;
        internal GraphicsSettings Graphics { get; }

        public GameWorld(TerrainSettings settings, double forestYearSeconds = EcologyTime.DefaultGameSecondsPerYear,
            SoilLandscapeDefinition soils = null, ClimateDefinition climate = null)
            : this(settings, new GraphicsSettings { AutomaticWeather = true }, forestYearSeconds, soils ?? SoilLandscapeDefinition.Default,
                climate ?? ClimateDefinition.Default) { }

        private GameWorld(TerrainSettings settings, GraphicsSettings graphics, double forestYearSeconds, SoilLandscapeDefinition soilModel,
            ClimateDefinition climate)
        {
            // Validate before allocating terrain/GPU resources, including direct diagnostic callers.
            if (!EcologyTime.IsValidForestYearSeconds(forestYearSeconds))
                throw new ArgumentOutOfRangeException(nameof(forestYearSeconds));
            Graphics = graphics;
            terrain = new Terrain(settings ?? throw new ArgumentNullException(nameof(settings)));
            ecosystem = new Ecosystem(map, forestYearSeconds, soilModel, climate);
            timberCargo = systems.Add(new TimberCargoSystem());
            vehicles = systems.Add(new VehicleSystem(timberCargo, route => VehicleRoadRoute.Create(map, route)));
            effects = systems.Add(new WorldEffectSystem());
            InitializeLogistics();
            terrainRenderer = new TerrainRenderer(terrain, vehicles, effects, forest, Graphics, Environment, wildlife, Logistics);
        }

        public Tile HoveredTile => map.HoveredTile;
        public int HoveredTileId => map.HoveredTile?.Id ?? -1;
        public int SelectedNodeId => map.SelectedNodeId;
        public int RoadCount => map.RoadCount;
        public int RoadPreviewCount => terrain.RoadPreviewCount;
        public int VehicleCount => vehicles.Count;
        internal int FishCount=>terrainRenderer.FishCount;
        internal int WildlifeCount => terrainRenderer.WildlifeCount;
        internal bool TryGetWildlifePosition(out Vector3 position) => terrainRenderer.TryGetWildlifePosition(out position);
        internal System.Collections.Generic.IReadOnlyList<Vehicle> Vehicles => vehicles.Vehicles;
        public ForestStatistics ForestStatistics => forest.Statistics;
        internal int ForestTreeCount => forest.IndividualTreeCount;
        internal float LastAnnualForestGrowth => forest.LastAnnualGrowthCubicMetres;
        public float TimberStockpile => Logistics.Remaining;
        public float DeliveredTimber => timberCargo.Delivered;
        public ForestryActionResult LastForestryAction => lastForestryAction;
        public ForestryAreaSummary LastForestryArea => lastForestryArea;
        public bool TryGetForestStand(int tileId, out ForestStand stand) => forest.TryGetStand(tileId, out stand);
        internal bool TryGetPlantationStatus(int tileId, out PlantationStatus status) => forest.TryGetPlantationStatus(tileId, out status);
        /// <summary>Changes whenever the forest publishes a new state; the management view rebuilds on it.</summary>
        internal ulong ForestRevision => forest.Revision;
        internal ForestTileSurvey[] BuildManagementSurvey() => ForestManagementSurvey.Build(forest, Environment, Soils);
        internal (ForestSpecies Species, float Suitability)[] RecommendSpecies(int tileId) =>
            ForestManagementSurvey.Recommend(forest.Habitat, tileId);
        internal bool TryGetTileCenter(int tileId, out Vector3 centre) => map.TryGetTileCenter(tileId, out centre);
        /// <summary>Per-tile RGBA tint of the active management lens drawn onto the terrain; null clears it.</summary>
        internal void SetManagementOverlay(uint[] colours) => terrain.SetManagementOverlay(colours);

        /// <summary>
        /// Review-capture helper: finds the densest 5×5 block of forest, clears its western half
        /// and half-loads the eastern half, so stumps and regrowth can be inspected. Bypasses
        /// the command log, so it must never run in a game that will be saved.
        /// </summary>
        internal bool DiagnosticFellForestBlock(out Vector3 centre)
        {
            int rows = terrain.Settings.TileRows, columns = terrain.Settings.TileColumns;
            int bestU = -1, bestV = -1, bestScore = 0;
            for (int u = 3; u < columns - 3; u++)
                for (int v = 3; v < rows - 3; v++)
                {
                    int score = 0;
                    for (int du = -2; du <= 2; du++)
                        for (int dv = -2; dv <= 2; dv++)
                            if (forest.TryGetStand((u + du) * rows + v + dv, out ForestStand stand) && stand.Maturity > 0.5f) score++;
                    if (score > bestScore) { bestScore = score; bestU = u; bestV = v; }
                }
            centre = default;
            if (bestScore == 0) return false;
            for (int du = -2; du <= 2; du++)
                for (int dv = -2; dv <= 2; dv++)
                {
                    int id = (bestU + du) * rows + bestV + dv;
                    if (du < 0) forest.Harvest(id, out _);
                    else if (du == 0 && forest.TryGetStand(id, out ForestStand stand))
                        forest.ExtractTimber(id, ForestSystem.TimberCubicMetres(stand) * 0.55f);
                }
            return map.TryGetTileCenter(bestU * rows + bestV, out centre);
        }
        public int VisibleChunkCount => terrain.VisibleChunkCount;
        public int ForestChunkRebuilds => terrain.ForestChunkRebuilds;
        public int TotalChunkCount => terrain.TotalChunkCount;
        public int TileWidth => terrain.TileWidth;
        public int TileHeight => terrain.TileHeight;
        public int MapTileColumns => terrain.Settings.TileColumns;
        public ForestPattern InitialForestPattern => terrain.Settings.ForestPattern;
        public ulong SimulationTick => worldTick;
        internal ulong LastLoadReplayedTicks { get; private set; }
        internal bool ProfileUpdates { get; set; }
        internal WorldUpdateProfile LastUpdateProfile { get; private set; }

        public void Update(double fixedDeltaSeconds)
        {
            long start = ProfileUpdates ? Stopwatch.GetTimestamp() : 0;
            ecosystem.Update(fixedDeltaSeconds);
            long environmentUpdated = ProfileUpdates ? Stopwatch.GetTimestamp() : 0;
            Logistics?.Update(fixedDeltaSeconds);
            WeatherRoads(fixedDeltaSeconds);
            long logisticsUpdated = ProfileUpdates ? Stopwatch.GetTimestamp() : 0;
            wildlife.Update(fixedDeltaSeconds, map, forest, Environment);
            long wildlifeUpdated = ProfileUpdates ? Stopwatch.GetTimestamp() : 0;
            systems.Update(fixedDeltaSeconds);
            worldTick++;
            if (ProfileUpdates)
                LastUpdateProfile = new(Stopwatch.GetElapsedTime(start, environmentUpdated).TotalMilliseconds,
                    Stopwatch.GetElapsedTime(environmentUpdated, logisticsUpdated).TotalMilliseconds,
                    Stopwatch.GetElapsedTime(logisticsUpdated, wildlifeUpdated).TotalMilliseconds,
                    Stopwatch.GetElapsedTime(wildlifeUpdated).TotalMilliseconds);
        }

        public int ExecutePendingCommands()
        {
            backgroundJobs.PublishCompleted();
            return commands.ExecutePending(this);
        }
        public void QueueElevationEdit(int nodeId, int delta, int radius, int strength) =>
            Enqueue(new EditElevationCommand(nodeId, delta, radius, strength));
        public void QueueRoadPath(int startTileId, int endTileId, bool remove) =>
            Enqueue(new RoadPathCommand(startTileId, endTileId, remove));
        public void QueueRoadPath(int startTileId, int endTileId, bool remove, RoadPaving surface) =>
            Enqueue(new RoadPathCommand(startTileId, endTileId, remove, surface));
        public void QueueRoadRepair(int startTileId, int endTileId) => Enqueue(new RoadRepairCommand(startTileId, endTileId));
        public void SetRoadRepairPreview(int startTileId, int endTileId) => terrain.SetRoadRepairPreview(startTileId, endTileId);
        internal RoadPaving GetRoadPaving(int tileId) => map.GetRoadPaving(tileId);
        internal float GetRoadCondition(int tileId) => map.GetRoadCondition(tileId);
        internal bool IsRoadTile(int tileId) => map.IsRoadTile(tileId);
        /// <summary>Cost of repairing the worn tiles along a → b, thousand forints.</summary>
        internal double RoadRepairCost(int startTileId, int endTileId)
        {
            double cost = 0;
            foreach (var (tile, damage) in map.PlanRoadRepair(startTileId, endTileId))
                cost += RoadCosts.Repair(map.GetRoadPaving(tile), damage);
            return cost;
        }
        public void QueuePlaceSawmill(int tileId) => Enqueue(new PlaceSawmillCommand(tileId));
        void IWorldCommandTarget.ExecutePlaceSawmill(int tileId) { Logistics?.PlaceMill(tileId); }
        public void QueueSpawnVehicle() => Enqueue(new SpawnVehicleCommand());
        internal void QueueWeather(WeatherPreset preset,int intensity,int duration) => Enqueue(new SetWeatherCommand(preset,intensity,duration));
        void IWorldCommandTarget.ExecuteWeather(WeatherPreset preset,int intensity,int duration)
        {
            Environment.ForceWeather(preset,intensity,duration);
        }
        public void QueuePlantForest(int tileId, ForestSpecies species) =>
            Enqueue(new PlantForestCommand(tileId, species));
        public void QueueHarvestForest(int tileId) => Enqueue(new HarvestForestCommand(tileId));
        public void QueuePlantForestArea(int startTileId, int endTileId, ForestSpecies species) =>
            Enqueue(new PlantForestAreaCommand(startTileId, endTileId, species));
        public void QueueHarvestForestArea(int startTileId, int endTileId) =>
            Enqueue(new HarvestForestAreaCommand(startTileId, endTileId));

        public void SetForestryPreview(int startTileId, int endTileId, bool removal) =>
            terrain.SetForestryPreview(startTileId, endTileId, removal);
        public void ClearForestryPreview() => terrain.ClearForestryPreview();
        public int ForestryPreviewCount => terrain.ForestryPreviewCount;

        private void Enqueue(IWorldCommand command)
        {
            commandJournal.Add(command.ToRecord(worldTick));
            commands.Enqueue(command);
        }

        void IWorldCommandTarget.ExecuteElevationEdit(int nodeId, int delta, int radius, int strength)
        {
            int[] changedTiles = map.EditElevationAtNode(nodeId, delta, radius, strength);
            if (changedTiles.Length == 0) return;
            ecosystem.ApplyTerrainEdit(changedTiles);
            if (map.TryGetNodePosition(nodeId, out Vector3 position))
                effects.Spawn(WorldEffectKind.TerrainChanged, position);
        }

        void IWorldCommandTarget.ExecuteLegacyElevationEdit(int nodeId, int delta, int radius, int strength)
        {
            // Only historical commands use the old rule; new edits in loaded worlds use the fixed rule.
            map.EditElevationAtNode(nodeId, delta, radius, strength);
            forest.RefreshHabitat();
            Environment.RefreshRouting();
            if (map.TryGetNodePosition(nodeId, out Vector3 position)) effects.Spawn(WorldEffectKind.TerrainChanged, position);
        }

        void IWorldCommandTarget.ExecuteRoadPath(int startTileId, int endTileId, bool remove) =>
            ((IWorldCommandTarget)this).ExecuteRoadPath(startTileId, endTileId, remove, RoadPaving.Asphalt);

        void IWorldCommandTarget.ExecuteRoadRepair(int startTileId, int endTileId)
        {
            foreach (var (tile, damage) in map.RepairRoadTilePath(startTileId, endTileId))
                Expenses += RoadCosts.Repair(map.GetRoadPaving(tile), damage);
            if (map.TryGetRoadTileCenter(endTileId, out Vector3 position)) effects.Spawn(WorldEffectKind.RoadChanged, position);
        }

        /// <summary>Rain and wet ground wear the roads; applied twice a second of game time.</summary>
        private void WeatherRoads(double seconds)
        {
            roadWeatherSeconds += seconds;
            if (roadWeatherSeconds < 0.5) return;
            float rain = (float)Math.Clamp(Environment.RainRate / 20, 0, 1);
            map.WeatherRoads((float)(roadWeatherSeconds / ecosystem.ForestYearSeconds), rain);
            roadWeatherSeconds = 0;
        }

        void IWorldCommandTarget.ExecuteRoadPath(int startTileId, int endTileId, bool remove, RoadPaving surface)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int[] changed = remove ? map.RemoveRoadTilePath(startTileId, endTileId)
                : map.BuildRoadTilePath(startTileId, endTileId, surface);
            if (changed.Length == 0) return;
            if (!remove) Expenses += changed.Length * RoadCosts.Build(surface);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            forest.RefreshHabitat(changed);
            long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            Environment?.RefreshRouting(changed);
            long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (remove) vehicles.RemoveInvalidRoutes(map.IsRoadTile);
            else vehicles.RefreshLogisticsRoutes(map.IsRoadTile);
            long t4 = System.Diagnostics.Stopwatch.GetTimestamp();
            static double Ms(long a, long b) => System.Diagnostics.Stopwatch.GetElapsedTime(a, b).TotalMilliseconds;
            LastRoadBuildProfile = $"[{changed.Length} tiles: map {Ms(t0, t1):F1}, habitat {Ms(t1, t2):F1}, routing {Ms(t2, t3):F1}, vehicles {Ms(t3, t4):F1}]";
            if (map.TryGetRoadTileCenter(endTileId, out Vector3 position))
                effects.Spawn(WorldEffectKind.RoadChanged, position);
        }

        void IWorldCommandTarget.ExecuteSpawnVehicle()
        {
            Logistics.Dispatch(vehicles);
        }

        void IWorldCommandTarget.ExecutePlantForest(int tileId, ForestSpecies species)
        {
            lastForestryAction = forest.Plant(tileId, species);
            if (map.TryGetTileCenter(tileId, out Vector3 position))
                effects.Spawn(lastForestryAction == ForestryActionResult.Planted
                    ? WorldEffectKind.TreePlanted
                    : WorldEffectKind.ForestryRejected, position);
        }

        void IWorldCommandTarget.ExecuteHarvestForest(int tileId)
        {
            DesignateHarvest(tileId, tileId);
        }

        void IWorldCommandTarget.ExecutePlantForestArea(int startTileId, int endTileId, ForestSpecies species)
        {
            Span<int> tileIds = stackalloc int[TerrainMap.MaximumAreaTiles];
            int count = map.GetTileRectangle(startTileId, endTileId, tileIds);

            int planted = 0;
            int areaId = forest.AllocatePlantationId();
            for (int i = 0; i < count; i++)
            {
                lastForestryAction = forest.PlantInArea(tileIds[i], species, areaId);
                if (lastForestryAction == ForestryActionResult.Planted) planted++;
            }

            lastForestryArea = new ForestryAreaSummary(count, planted, 0f);
            if (planted > 0) forest.FinishPlantingArea(areaId);
            SpawnAreaEffect(startTileId, endTileId,
                planted > 0 ? WorldEffectKind.TreePlanted : WorldEffectKind.ForestryRejected);
        }

        void IWorldCommandTarget.ExecuteHarvestForestArea(int startTileId, int endTileId)
        {
            DesignateHarvest(startTileId, endTileId);
        }

        /// <summary>
        /// One effect per area gesture. Spawning per tile would bury the effect system under a
        /// single drag and tell the player nothing a marker at the corners does not.
        /// </summary>
        private void SpawnAreaEffect(int startTileId, int endTileId, WorldEffectKind kind)
        {
            if (map.TryGetTileCenter(startTileId, out Vector3 start))
                effects.Spawn(kind, start);
            if (endTileId != startTileId && map.TryGetTileCenter(endTileId, out Vector3 end))
                effects.Spawn(kind, end);
        }

        public void Draw(RenderContext context)
        {
            terrainRenderer.Draw(context);
        }
        public void GetWorldBounds(out Vector3 min, out Vector3 max) => map.GetWorldBounds(out min, out max);
        internal int TilesPerSide => map.TilesPerSide;
        /// <summary>Per-step timings of the last road edit (diagnostics).</summary>
        internal string LastRoadBuildProfile { get; private set; } = "";
        public bool TryGetSurfaceZ(double x, double y, out float z) => map.TryGetSurfaceZ(x, y, out z);
        public bool TryRaycastTerrain(Vector3 rayNear, Vector3 rayFar, out Vector3 hit) =>
            map.TryRaycast(rayNear, rayFar, out hit);
        public bool SearchScreenPoint(double x, double y, double radius, double[] model, double[] projection, int[] viewport) =>
            map.SearchScreenPoint(x, y, radius, model, projection, viewport);
        public bool SearchTile(double x, double y) => map.SearchTile(x, y);
        public void ClearHover() => map.ClearHover();
        public void ClearTileHover() => map.ClearTileHover();
        public void SetRoadPreview(int startTileId, int endTileId, bool remove) =>
            terrain.SetRoadPreview(startTileId, endTileId, remove);
        public void ClearRoadPreview() => terrain.ClearRoadPreview();

        public void Regenerate(TerrainSettings settings)
        {
            vehicles.UseRoadPhysics = true; vehicles.UseCargoStops = true;
            commands.Clear();
            commandJournal.Clear();
            systems.Clear();
            worldTick = 0;
            lastForestryAction = ForestryActionResult.None;
            ReplaceTerrain(settings, EcologyTime.DefaultGameSecondsPerYear);
        }

        public void Save(Stream destination, double tickRate = 30.0)
        {
            WorldSaveSerializer.Write(destination, new WorldSaveData
            {
                TickRate = tickRate,
                ForestYearSeconds = ecosystem.ForestYearSeconds,
                SoilModel = SoilModelData.From(Soils.Definition),
                Climate = Environment.Climate.Definition,
                Tick = worldTick,
                Terrain = TerrainSettingsData.From(terrain.Settings),
                Commands = new List<WorldCommandRecord>(commandJournal),
                Checkpoint = CaptureCheckpoint()
            });
        }

        public void Load(Stream source)
        {
            WorldSaveData save = WorldSaveSerializer.Read(source);
            save.ValidateReplay();
            var settings = save.Terrain.ToSettings();
            // Replay into an isolated world. Failure leaves the live world and queued commands intact.
            using var candidate = new GameWorld(settings, Graphics, save.ReplayForestYearSeconds, save.ReplaySoilModel, save.ReplayClimate);
            ulong startTick = save.Checkpoint?.Tick ?? 0;
            if (save.Checkpoint == null) candidate.Replay(save);
            else { candidate.RestoreCheckpoint(save.Checkpoint); candidate.ReplayTail(save); }

            (terrain, candidate.terrain) = (candidate.terrain, terrain);
            (terrainRenderer, candidate.terrainRenderer) = (candidate.terrainRenderer, terrainRenderer);
            (ecosystem, candidate.ecosystem) = (candidate.ecosystem, ecosystem);
            (wildlife, candidate.wildlife) = (candidate.wildlife, wildlife);
            (Logistics, candidate.Logistics) = (candidate.Logistics, Logistics);
            (vehicles, candidate.vehicles) = (candidate.vehicles, vehicles);
            (effects, candidate.effects) = (candidate.effects, effects);
            (timberCargo, candidate.timberCargo) = (candidate.timberCargo, timberCargo);
            (systems, candidate.systems) = (candidate.systems, systems);
            (backgroundJobs, candidate.backgroundJobs) = (candidate.backgroundJobs, backgroundJobs);
            // Route creation must follow this world's terrain after the ownership transfer.
            vehicles.RoadRouteFactory = route => VehicleRoadRoute.Create(map, route);
            commands.Clear();
            foreach (var pending in candidate.commands.Snapshot()) commands.Enqueue(pending);
            commandJournal.Clear();
            foreach (var record in save.Commands) commandJournal.Add(save.ReplayCommand(record));
            worldTick = candidate.worldTick;
            lastForestryAction = candidate.lastForestryAction;
            lastForestryArea = candidate.lastForestryArea;
            LastLoadReplayedTicks = candidate.worldTick - startTick;
        }

        private void Replay(WorldSaveData save)
        {
            int commandIndex = 0;
            double fixedDelta = 1.0 / save.TickRate;
            for (ulong tick = 0; ; tick++)
            {
                while (commandIndex < save.Commands.Count && save.Commands[commandIndex].Tick == tick)
                    commands.Enqueue(WorldCommandFactory.Create(save.ReplayCommand(save.Commands[commandIndex++])));
                commands.ExecutePending(this);
                if (tick == save.Tick) break;
                Update(fixedDelta);
            }
        }
        private void ReplaceTerrain(TerrainSettings settings, double forestYearSeconds)
        {
            terrainRenderer.Dispose();
            terrain.Dispose();
            terrain = new Terrain(settings ?? throw new ArgumentNullException(nameof(settings)));
            ecosystem.Reset(map, forestYearSeconds, SoilLandscapeDefinition.Default, ClimateDefinition.Default);
            wildlife = new WildlifeSystem();
            InitializeLogistics();
            terrainRenderer = new TerrainRenderer(terrain, vehicles, effects, forest, Graphics, Environment, wildlife, Logistics);
        }

        private void DesignateHarvest(int start,int end)
        {
            Span<int> ids=stackalloc int[TerrainMap.MaximumAreaTiles];
            int count=map.GetTileRectangle(start,end,ids);
            int applied=Logistics.Designate(ids[..count]);
            lastForestryAction=applied>0?ForestryActionResult.Designated:ForestryActionResult.NoForest;
            lastForestryArea=new ForestryAreaSummary(count,applied,Logistics.Remaining);
        }
        private void InitializeLogistics()
        {
            Logistics=new ForestryLogistics(map,forest);
            vehicles.SourceLoader = Logistics.Load;
            vehicles.DestinationReceiver = Logistics.Deliver;
            vehicles.RouteValidator = Logistics.RouteConnected;
            vehicles.RoadState = id => (map.GetRoadPaving(id) == RoadPaving.Asphalt ? RoadSurface.Asphalt : RoadSurface.Gravel,
                map.GetRoadCondition(id));
            vehicles.RoadWear = (id, amount) => map.WearRoad(id, amount * (map.GetRoadPaving(id) == RoadPaving.Macadam ? 1f : 0.2f));
        }

        public void Dispose()
        {
            backgroundJobs.Dispose();
            terrainRenderer?.Dispose();
            terrain?.Dispose();
        }
    }
}
