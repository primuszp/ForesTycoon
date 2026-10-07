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
    sealed class GameWorld : IDisposable, IWorldCommandTarget, IWorldInteractionTarget
    {
        private Terrain terrain;
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
        private ForestryActionResult lastForestryAction;
        private ForestryAreaSummary lastForestryArea;
        internal GraphicsSettings Graphics { get; }

        public GameWorld(TerrainSettings settings, double forestYearSeconds = EcologyTime.DefaultGameSecondsPerYear)
            : this(settings, new GraphicsSettings { AutomaticWeather = true }, forestYearSeconds) { }

        private GameWorld(TerrainSettings settings, GraphicsSettings graphics, double forestYearSeconds)
        {
            // Validate before allocating terrain/GPU resources, including direct diagnostic callers.
            if (!EcologyTime.IsValidForestYearSeconds(forestYearSeconds))
                throw new ArgumentOutOfRangeException(nameof(forestYearSeconds));
            Graphics = graphics;
            terrain = new Terrain(settings ?? throw new ArgumentNullException(nameof(settings)));
            ecosystem = new Ecosystem(terrain, forestYearSeconds);
            timberCargo = systems.Add(new TimberCargoSystem());
            vehicles = systems.Add(new VehicleSystem(timberCargo, route => terrain.CreateVehicleRoadRoute(route)));
            effects = systems.Add(new WorldEffectSystem());
            InitializeLogistics();
            terrainRenderer = new TerrainRenderer(terrain, vehicles, effects, forest, Graphics, Environment, wildlife, Logistics);
        }

        public Tile HoveredTile => terrain.HoveredTile;
        public int HoveredTileId => terrain.HoveredTile?.Id ?? -1;
        public int SelectedNodeId => terrain.SelectedNodeId;
        public int RoadCount => terrain.RoadCount;
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
            return terrain.TryGetTileCenter(bestU * rows + bestV, out centre);
        }
        public int VisibleChunkCount => terrain.VisibleChunkCount;
        public int ForestChunkRebuilds => terrain.ForestChunkRebuilds;
        public int TotalChunkCount => terrain.TotalChunkCount;
        public int TileWidth => terrain.TileWidth;
        public int TileHeight => terrain.TileHeight;
        public int MapTileColumns => terrain.Settings.TileColumns;
        public ForestPattern InitialForestPattern => terrain.Settings.ForestPattern;
        public ulong SimulationTick => worldTick;
        internal bool ProfileUpdates { get; set; }
        internal WorldUpdateProfile LastUpdateProfile { get; private set; }

        public void Update(double fixedDeltaSeconds)
        {
            long start = ProfileUpdates ? Stopwatch.GetTimestamp() : 0;
            ecosystem.Update(fixedDeltaSeconds);
            long environmentUpdated = ProfileUpdates ? Stopwatch.GetTimestamp() : 0;
            Logistics?.Update(fixedDeltaSeconds);
            long logisticsUpdated = ProfileUpdates ? Stopwatch.GetTimestamp() : 0;
            wildlife.Update(fixedDeltaSeconds, terrain, forest, Environment);
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
            terrain.EditElevationAtNode(nodeId, delta, radius, strength);
            forest.RefreshHabitat();
            Environment?.RefreshRouting();
            if (terrain.TryGetNodePosition(nodeId, out Vector3 position))
                effects.Spawn(WorldEffectKind.TerrainChanged, position);
        }

        void IWorldCommandTarget.ExecuteRoadPath(int startTileId, int endTileId, bool remove)
        {
            if (remove) terrain.RemoveRoadTilePath(startTileId, endTileId);
            else terrain.BuildRoadTilePath(startTileId, endTileId);
            forest.RefreshHabitat();
            Environment?.RefreshRouting();
            if (remove) vehicles.RemoveInvalidRoutes(terrain.IsRoadTile);
            else vehicles.RefreshLogisticsRoutes(terrain.IsRoadTile);
            if (terrain.TryGetRoadTileCenter(endTileId, out Vector3 position))
                effects.Spawn(WorldEffectKind.RoadChanged, position);
        }

        void IWorldCommandTarget.ExecuteSpawnVehicle()
        {
            Logistics.Dispatch(vehicles);
        }

        void IWorldCommandTarget.ExecutePlantForest(int tileId, ForestSpecies species)
        {
            lastForestryAction = forest.Plant(tileId, species);
            if (terrain.TryGetTileCenter(tileId, out Vector3 position))
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
            Span<int> tileIds = stackalloc int[Terrain.MaximumAreaTiles];
            int count = terrain.GetTileRectangle(startTileId, endTileId, tileIds);

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
            if (terrain.TryGetTileCenter(startTileId, out Vector3 start))
                effects.Spawn(kind, start);
            if (endTileId != startTileId && terrain.TryGetTileCenter(endTileId, out Vector3 end))
                effects.Spawn(kind, end);
        }

        public void Draw(RenderContext context)
        {
            terrainRenderer.Draw(context);
        }
        public void GetWorldBounds(out Vector3 min, out Vector3 max) => terrain.GetWorldBounds(out min, out max);
        public bool TryGetSurfaceZ(double x, double y, out float z) => terrain.TryGetSurfaceZ(x, y, out z);
        public bool TryRaycastTerrain(Vector3 rayNear, Vector3 rayFar, out Vector3 hit) =>
            terrain.TryRaycast(rayNear, rayFar, out hit);
        public bool SearchScreenPoint(double x, double y, double radius, double[] model, double[] projection, int[] viewport) =>
            terrain.SearchScreenPoint(x, y, radius, model, projection, viewport);
        public bool SearchTile(double x, double y) => terrain.SearchTile(x, y);
        public void ClearHover() => terrain.ClearHover();
        public void ClearTileHover() => terrain.ClearTileHover();
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
                Tick = worldTick,
                Terrain = TerrainSettingsData.From(terrain.Settings),
                Commands = new List<WorldCommandRecord>(commandJournal)
            });
        }

        public void Load(Stream source)
        {
            WorldSaveData save = WorldSaveSerializer.Read(source);
            save.ValidateReplay();
            var settings = save.Terrain.ToSettings();
            // Replay into an isolated world. Failure leaves the live world and queued commands intact.
            using var candidate = new GameWorld(settings, Graphics, save.ReplayForestYearSeconds);
            candidate.Replay(save);

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
            vehicles.RoadRouteFactory = route => terrain.CreateVehicleRoadRoute(route);
            commands.Clear();
            commandJournal.Clear();
            commandJournal.AddRange(save.Commands);
            worldTick = candidate.worldTick;
            lastForestryAction = candidate.lastForestryAction;
            lastForestryArea = candidate.lastForestryArea;
        }

        private void Replay(WorldSaveData save)
        {
            int commandIndex = 0;
            double fixedDelta = 1.0 / save.TickRate;
            for (ulong tick = 0; ; tick++)
            {
                while (commandIndex < save.Commands.Count && save.Commands[commandIndex].Tick == tick)
                    commands.Enqueue(WorldCommandFactory.Create(save.Commands[commandIndex++]));
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
            ecosystem.Reset(terrain, forestYearSeconds);
            wildlife = new WildlifeSystem();
            InitializeLogistics();
            terrainRenderer = new TerrainRenderer(terrain, vehicles, effects, forest, Graphics, Environment, wildlife, Logistics);
        }

        private void DesignateHarvest(int start,int end)
        {
            Span<int> ids=stackalloc int[Terrain.MaximumAreaTiles];
            int count=terrain.GetTileRectangle(start,end,ids);
            int applied=Logistics.Designate(ids[..count]);
            lastForestryAction=applied>0?ForestryActionResult.Designated:ForestryActionResult.NoForest;
            lastForestryArea=new ForestryAreaSummary(count,applied,Logistics.Remaining);
        }
        private void InitializeLogistics()
        {
            Logistics=new ForestryLogistics(terrain,forest);
            vehicles.SourceLoader = Logistics.Load;
            vehicles.DestinationReceiver = Logistics.Deliver;
            vehicles.RouteValidator = Logistics.RouteConnected;
        }

        public void Dispose()
        {
            backgroundJobs.Dispose();
            terrainRenderer?.Dispose();
            terrain?.Dispose();
        }
    }
}
