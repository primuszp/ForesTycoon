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
        private TerrainMap map;
        private WildlifeSystem wildlife = new WildlifeSystem();
        internal ForestryLogistics Logistics { get; private set; }
        private TerrainRenderer terrainRenderer;
        private WorldCommandQueue commands = new WorldCommandQueue();
        private WorldSystemCollection systems = new WorldSystemCollection();
        private BackgroundJobScheduler backgroundJobs = new BackgroundJobScheduler();
        private List<WorldCommandRecord> commandJournal = new List<WorldCommandRecord>();
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
        /// <summary>
        /// Vehicle time per calendar time. As in Transport Tycoon, vehicles move at a natural pace whatever the calendar
        /// does: at the viewport's slow 1× (a quarter simulated second per real second, a year an hour) they still drive in
        /// real time, while the year runs slowly enough to plan.
        /// </summary>
        internal const double VehicleTimeScale = 4;
        private ForestryAreaSummary lastForestryArea;
        internal GraphicsSettings Graphics { get; }
        internal TerrainMap Map => map;
        internal bool HasPresentation => terrainRenderer != null;
        internal int ReadyVisibleForestChunks(ForestLod lod) => terrain?.ReadyVisibleForestChunks(lod) ?? 0;
        private bool disposed, faulted;
        internal bool IsFaulted => faulted;

        private void EnsureAvailable(bool allowFaulted = false)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted && !allowFaulted)
                throw new InvalidOperationException("The world update failed; load a validated save or regenerate before continuing.");
        }

        public GameWorld(TerrainSettings settings, double forestYearSeconds = EcologyTime.DefaultGameSecondsPerYear,
            SoilLandscapeDefinition soils = null, ClimateDefinition climate = null, bool enableRendering = true)
            : this(settings, new GraphicsSettings { AutomaticWeather = true }, forestYearSeconds, soils ?? SoilLandscapeDefinition.Default,
                climate ?? ClimateDefinition.Default, enableRendering) { }

        private GameWorld(TerrainSettings settings, GraphicsSettings graphics, double forestYearSeconds, SoilLandscapeDefinition soilModel,
            ClimateDefinition climate, bool enableRendering = false)
            : this(CreateMap(settings, forestYearSeconds), graphics, forestYearSeconds, soilModel, climate, enableRendering) { }

        private static TerrainMap CreateMap(TerrainSettings settings, double forestYearSeconds)
        {
            if (!EcologyTime.IsValidForestYearSeconds(forestYearSeconds))
                throw new ArgumentOutOfRangeException(nameof(forestYearSeconds));
            return new TerrainMap(settings ?? throw new ArgumentNullException(nameof(settings)));
        }

        // Direct model injection also supports flat and otherwise controlled simulation fixtures.
        internal GameWorld(TerrainMap map, double forestYearSeconds = EcologyTime.DefaultGameSecondsPerYear)
            : this(map, new GraphicsSettings { AutomaticWeather = true }, forestYearSeconds,
                SoilLandscapeDefinition.Default, ClimateDefinition.Default, false) { }

        private GameWorld(TerrainMap map, GraphicsSettings graphics, double forestYearSeconds, SoilLandscapeDefinition soilModel,
            ClimateDefinition climate, bool enableRendering)
        {
            Graphics = graphics;
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            try
            {
                ecosystem = new Ecosystem(map, forestYearSeconds, soilModel, climate);
                timberCargo = systems.Add(new TimberCargoSystem());
                vehicles = systems.Add(new VehicleSystem(timberCargo, route => VehicleRoadRoute.Create(this.map, route)) { TimeScale = VehicleTimeScale });
                effects = systems.Add(new WorldEffectSystem());
                InitializeLogistics();
                if (enableRendering) AttachRendering();
            }
            catch { Dispose(); throw; }
        }

        internal void AttachRendering()
        {
            EnsureAvailable();
            if (terrainRenderer != null) { terrainRenderer.VerifyAccess(); return; }
            var scene = new Terrain(map);
            try
            {
                var renderer = new TerrainRenderer(scene, vehicles, effects, forest, Graphics, Environment, wildlife, Logistics);
                terrain = scene;
                terrainRenderer = renderer;
            }
            catch { scene.Dispose(); throw; }
        }

        public Tile HoveredTile => map.HoveredTile;
        public int HoveredTileId => map.HoveredTile?.Id ?? -1;
        public int SelectedNodeId => map.SelectedNodeId;
        public int RoadCount => map.RoadCount;
        public int RoadPreviewCount => terrain?.RoadPreviewCount ?? 0;
        public int VehicleCount => vehicles.Count;
        internal int FishCount=>terrainRenderer?.FishCount ?? 0;
        internal int WildlifeCount => terrainRenderer?.WildlifeCount ?? 0;
        internal bool TryGetWildlifePosition(out Vector3 position)
        {
            if (terrainRenderer != null) return terrainRenderer.TryGetWildlifePosition(out position);
            position = default; return false;
        }
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
        internal void SetManagementOverlay(uint[] colours) => terrain?.SetManagementOverlay(colours);

        /// <summary>
        /// Review-capture helper: finds the densest 5×5 block of forest, clears its western half
        /// and half-loads the eastern half, so stumps and regrowth can be inspected. Bypasses
        /// the command log, so it must never run in a game that will be saved.
        /// </summary>
        internal bool DiagnosticFellForestBlock(out Vector3 centre)
        {
            int rows = map.Settings.TileRows, columns = map.Settings.TileColumns;
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
        public int VisibleChunkCount => terrain?.VisibleChunkCount ?? 0;
        public int ForestChunkRebuilds => terrain?.ForestChunkRebuilds ?? 0;
        public long ForestGpuPayloadBytes => terrain?.ForestGpuPayloadBytes ?? 0;
        public long ForestCpuPayloadBytes => terrain?.ForestCpuPayloadBytes ?? 0;
        public long ForestBudgetExcessBytes => terrain?.ForestBudgetExcessBytes ?? 0;
        public int ForestResidentLods => terrain?.ForestResidentLods ?? 0;
        public int ForestCacheEvictions => terrain?.ForestCacheEvictions ?? 0;
        public long StaticGpuPayloadBytes => terrain?.StaticGpuPayloadBytes ?? 0;
        public long StaticCpuPayloadBytes => terrain?.StaticCpuPayloadBytes ?? 0;
        public long StaticBudgetExcessBytes => terrain?.StaticBudgetExcessBytes ?? 0;
        public int WeatherParticleCount => terrainRenderer?.WeatherParticleCount ?? 0;
        public int WeatherCloudSteps => terrainRenderer?.WeatherCloudSteps ?? 0;
        public long WeatherCpuPayloadBytes => terrainRenderer?.WeatherCpuPayloadBytes ?? 0;
        public long WeatherGpuPayloadBytes => terrainRenderer?.WeatherGpuPayloadBytes ?? 0;
        public double WeatherCpuMilliseconds => terrainRenderer?.WeatherCpuMilliseconds ?? 0;
        public int FogParticleCount => terrainRenderer?.FogParticleCount ?? 0;
        public bool FogDepthFallback => terrainRenderer?.FogDepthFallback ?? false;
        public int MarkerRenderedCount => terrainRenderer?.MarkerRenderedCount ?? 0;
        public int ActiveMarkerCount => effects.Count;
        public long DroppedMarkers => effects.DroppedEffects;
        public int TotalChunkCount => map.Chunks.Chunks.Count;
        public int TileWidth => map.Settings.TileWidth;
        public int TileHeight => map.Settings.TileHeight;
        public int MapTileColumns => map.Settings.TileColumns;
        public ForestPattern InitialForestPattern => map.Settings.ForestPattern;
        public ulong SimulationTick => worldTick;
        internal ulong LastLoadReplayedTicks { get; private set; }
        internal bool ProfileUpdates { get; set; }
        internal WorldUpdateProfile LastUpdateProfile { get; private set; }

        public void Update(double fixedDeltaSeconds)
        {
            EnsureAvailable();
            terrainRenderer?.VerifyAccess();
            if (!double.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
            try
            {
                long start = ProfileUpdates ? Stopwatch.GetTimestamp() : 0;
                ecosystem.Update(fixedDeltaSeconds);
                long environmentUpdated = ProfileUpdates ? Stopwatch.GetTimestamp() : 0;
                // Vehicles and machines keep their own clock, faster than the calendar (see VehicleTimeScale).
                Logistics?.Update(fixedDeltaSeconds * Tuning[Tune.VehicleTimeScale]);
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
            catch { faulted = true; throw; }
        }

        public int ExecutePendingCommands()
        {
            EnsureAvailable();
            terrainRenderer?.VerifyAccess();
            try
            {
                backgroundJobs.PublishCompleted();
                return commands.ExecutePending(this);
            }
            catch { faulted = true; throw; }
        }
        public void QueueElevationEdit(int nodeId, int delta, int radius, int strength) =>
            Enqueue(new EditElevationCommand(nodeId, delta, radius, strength));
        public void QueueRoadPath(int startTileId, int endTileId, bool remove) =>
            Enqueue(new RoadPathCommand(startTileId, endTileId, remove));
        public void QueueRoadPath(int startTileId, int endTileId, bool remove, RoadPaving surface) =>
            Enqueue(new RoadPathCommand(startTileId, endTileId, remove, surface));
        public void QueueRoadRepair(int startTileId, int endTileId) => Enqueue(new RoadRepairCommand(startTileId, endTileId));
        public void QueueSkidTrailPath(int startTileId, int endTileId, bool remove) =>
            Enqueue(new SkidTrailPathCommand(startTileId, endTileId, remove));
        public void SetSkidTrailPreview(int startTileId, int endTileId, bool remove) =>
            terrain?.SetSkidTrailPreview(startTileId, endTileId, remove);
        internal bool IsSkidTrail(int tileId) => map.IsSkidTrail(tileId);
        internal float GetSkidTrailWear(int tileId) => map.GetSkidTrailWear(tileId);
        internal int SkidTrailCount => map.SkidTrailCount;
        /// <summary>Order-tool marks drawn on the ground (targets, cursor tile, routes).</summary>
        internal OrderOverlay Orders => terrain?.Orders ?? throw new InvalidOperationException("Attach rendering before using visual overlays.");
        public void SetRoadRepairPreview(int startTileId, int endTileId) => terrain?.SetRoadRepairPreview(startTileId, endTileId);
        internal RoadPaving GetRoadPaving(int tileId) => map.GetRoadPaving(tileId);
        internal float GetRoadCondition(int tileId) => map.GetRoadCondition(tileId);
        internal bool IsRoadTile(int tileId) => map.IsRoadTile(tileId);
        /// <summary>Cost of repairing the worn tiles along a → b, thousand forints.</summary>
        internal double RoadRepairCost(int startTileId, int endTileId)
        {
            double cost = 0;
            foreach (var (tile, damage) in map.PlanRoadRepair(startTileId, endTileId))
                cost += RoadCosts.Repair(map.GetRoadPaving(tile), damage, Tuning);
            return cost;
        }
        public void QueuePlaceSawmill(int tileId) => Enqueue(new PlaceSawmillCommand(tileId));
        public void QueuePlaceDepot(int tileId) => Enqueue(new PlaceDepotCommand(tileId));
        internal void QueueSendVehicle(int vehicleId, int tileId, int destination, bool truck) =>
            Enqueue(new SendVehicleCommand(vehicleId, tileId, destination, truck));
        public void QueueStackSite(int tileId, bool remove) => Enqueue(new StackSiteCommand(tileId, remove));
        void IWorldCommandTarget.ExecuteStackSite(int tileId, bool remove)
        {
            if (remove) Logistics.RemoveStack(tileId); else Logistics.PlaceStack(tileId);
        }
        /// <summary>Money earned at the mills, thousand forints.</summary>
        internal double Income => Logistics?.Income ?? 0;
        /// <summary>Everything spent: building, repairs, fuel, thousand forints.</summary>
        internal double Spending => Expenses + (Logistics?.RunningCosts ?? 0);
        internal double Balance => Income - Spending;
        internal void QueueSendHome(int vehicleId, bool truck) => Enqueue(new SendHomeCommand(vehicleId, truck));
        void IWorldCommandTarget.ExecutePlaceDepot(int tileId) { Logistics?.PlaceDepot(tileId); }
        void IWorldCommandTarget.ExecuteSendVehicle(int vehicleId, int tileId, int destination, bool truck)
        {
            if (truck)
            {
                var t = Logistics.Trucks.Find(x => x.Id == vehicleId);
                var source = Logistics.StackAt(tileId);
                if (t == null) return;
                if (source == null) { Logistics.Status = "A rönkszállító forrása egy sarang legyen."; return; }
                Logistics.AssignTruck(t, source, destination);
                return;
            }
            var m = Logistics.Machines.Find(x => x.Id == vehicleId);
            if (m == null) return;
            if (m.Kind == ForestMachineKind.Harvester)
            {
                var stack = Logistics.StackAt(tileId);
                if (stack == null) { Logistics.Status = "A processzort egy sarangra küldd: oda hordja a fát."; return; }
                Logistics.AssignProcessor(m, stack);
            }
            else
            {
                var source = Logistics.StackAt(tileId);
                if (source == null) { Logistics.Status = "A forwarder forrása egy sarang legyen."; return; }
                Logistics.AssignForwarder(m, source, destination);
            }
        }
        void IWorldCommandTarget.ExecuteSendHome(int vehicleId, bool truck)
        {
            if (truck) { var t = Logistics.Trucks.Find(x => x.Id == vehicleId); if (t != null) Logistics.SendHome(t); }
            else { var m = Logistics.Machines.Find(x => x.Id == vehicleId); if (m != null) Logistics.SendHome(m); }
        }
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
            terrain?.SetForestryPreview(startTileId, endTileId, removal);
        public void ClearForestryPreview() => terrain?.ClearForestryPreview();
        public int ForestryPreviewCount => terrain?.ForestryPreviewCount ?? 0;

        private void Enqueue(IWorldCommand command)
        {
            EnsureAvailable();
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
                Expenses += RoadCosts.Repair(map.GetRoadPaving(tile), damage, Tuning);
            if (map.TryGetRoadTileCenter(endTileId, out Vector3 position)) effects.Spawn(WorldEffectKind.RoadChanged, position);
        }

        /// <summary>Rain and wet ground wear the roads, and skid-trail ruts fade; applied twice a second of game time.</summary>
        private void WeatherRoads(double seconds)
        {
            roadWeatherSeconds += seconds;
            if (roadWeatherSeconds < 0.5) return;
            float rain = (float)Math.Clamp(Environment.RainRate / 20, 0, 1);
            float years = (float)(roadWeatherSeconds / ecosystem.ForestYearSeconds);
            map.WeatherRoads(years, rain);
            if (map.AgeSkidTrails(years).Length > 0)
            {
                Logistics?.TrailsChanged();
                vehicles.RemoveInvalidRoutes(map.IsNetworkTile);
            }
            roadWeatherSeconds = 0;
        }

        void IWorldCommandTarget.ExecuteSkidTrailPath(int startTileId, int endTileId, bool remove)
        {
            int[] changed = remove ? map.RemoveSkidTrailPath(startTileId, endTileId) : map.MarkSkidTrailPath(startTileId, endTileId);
            if (changed.Length == 0) return;
            if (!remove) Expenses += changed.Length * Tuning[Tune.TrailCost];
            Logistics?.TrailsChanged();
            if (remove) vehicles.RemoveInvalidRoutes(map.IsNetworkTile); else vehicles.RefreshLogisticsRoutes(map.IsNetworkTile);
            if (map.TryGetTileCenter(endTileId, out Vector3 position)) effects.Spawn(WorldEffectKind.RoadChanged, position);
        }

        void IWorldCommandTarget.ExecuteRoadPath(int startTileId, int endTileId, bool remove, RoadPaving surface)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int[] changed = remove ? map.RemoveRoadTilePath(startTileId, endTileId)
                : map.BuildRoadTilePath(startTileId, endTileId, surface);
            if (changed.Length == 0) return;
            if (!remove) Expenses += changed.Length * RoadCosts.Build(surface, Tuning);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            forest.RefreshHabitat(changed);
            long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            Environment?.RefreshRouting(changed);
            long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (remove) vehicles.RemoveInvalidRoutes(map.IsNetworkTile);
            else vehicles.RefreshLogisticsRoutes(map.IsNetworkTile);
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
            EnsureAvailable();
            if (terrainRenderer == null) throw new InvalidOperationException("Attach rendering before drawing a headless world.");
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
            terrain?.SetRoadPreview(startTileId, endTileId, remove);
        public void ClearRoadPreview() => terrain?.ClearRoadPreview();

        public void Regenerate(TerrainSettings settings)
        {
            EnsureAvailable(allowFaulted: true);
            terrainRenderer?.VerifyAccess();
            using var candidate = new GameWorld(settings, Graphics, EcologyTime.DefaultGameSecondsPerYear,
                SoilLandscapeDefinition.Default, ClimateDefinition.Default);
            candidate.ApplyTuning(Tuning);
            if (HasPresentation) candidate.AttachRendering();
            Adopt(candidate);
            LastRuleSample = "Még nem történt közúti áthaladás.";
            LastLoadReplayedTicks = 0;
        }

        public void Save(Stream destination, double tickRate = 30.0)
        {
            EnsureAvailable();
            WorldSaveSerializer.Write(destination, new WorldSaveData
            {
                TickRate = tickRate,
                ForestYearSeconds = ecosystem.ForestYearSeconds,
                SoilModel = SoilModelData.From(Soils.Definition),
                Climate = Environment.Climate.Definition,
                Tick = worldTick,
                Terrain = TerrainSettingsData.From(map.Settings),
                Commands = new List<WorldCommandRecord>(commandJournal),
                Checkpoint = CaptureCheckpoint()
            });
        }

        public void Load(Stream source)
        {
            EnsureAvailable(allowFaulted: true);
            terrainRenderer?.VerifyAccess();
            WorldSaveData save = WorldSaveSerializer.Read(source);
            save.ValidateReplay();
            var settings = save.Terrain.ToSettings();
            // Replay into an isolated world. Failure leaves the live world and queued commands intact.
            using var candidate = new GameWorld(settings, Graphics, save.ReplayForestYearSeconds, save.ReplaySoilModel, save.ReplayClimate);
            ulong startTick = save.Checkpoint?.Tick ?? 0;
            if (save.Checkpoint == null) candidate.Replay(save);
            else { candidate.RestoreCheckpoint(save.Checkpoint); candidate.ReplayTail(save); }

            foreach (var record in save.Commands) candidate.commandJournal.Add(save.ReplayCommand(record));
            if (HasPresentation) candidate.AttachRendering();
            ulong replayedTicks = candidate.worldTick - startTick;
            Adopt(candidate);
            LastRuleSample = "Betöltött szabálymodell; várakozás a következő áthaladásra.";
            LastLoadReplayedTicks = replayedTicks;
        }

        private void Adopt(GameWorld candidate)
        {
            (map, candidate.map) = (candidate.map, map);
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
            (commands, candidate.commands) = (candidate.commands, commands);
            (commandJournal, candidate.commandJournal) = (candidate.commandJournal, commandJournal);
            // Route creation must follow this world's terrain after the ownership transfer.
            vehicles.RoadRouteFactory = route => VehicleRoadRoute.Create(map, route);
            roadRule = candidate.roadRule;
            Expenses = candidate.Expenses;
            roadWeatherSeconds = candidate.roadWeatherSeconds;
            BindRoadRules();
            ApplyTuning(candidate.Tuning);
            foreach (var vehicle in vehicles.Vehicles) { vehicle.RoadState = vehicles.RoadState; vehicle.RoadWear = vehicles.RoadWear; }
            worldTick = candidate.worldTick;
            lastForestryAction = candidate.lastForestryAction;
            lastForestryArea = candidate.lastForestryArea;
            LastUpdateProfile = default;
            LastRoadBuildProfile = "";
            faulted = candidate.faulted;
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
            Logistics=new ForestryLogistics(map,forest){MachinesEnabled=true};
            vehicles.SourceLoader = Logistics.Load;
            vehicles.DestinationReceiver = Logistics.Deliver;
            vehicles.RouteValidator = Logistics.RouteConnected;
            Logistics.Vehicles = vehicles;
            // A skid trail is bare, rutted ground: trucks crawl and pitch on it, the deeper the ruts the worse.
            BindRoadRules();
            ApplyTuning(Tuning);
        }

        public void Dispose()
        {
            if (disposed) return;
            terrainRenderer?.VerifyAccess();
            disposed = true;
            backgroundJobs.Dispose();
            terrainRenderer?.Dispose();
            terrain?.Dispose();
        }
    }
}
