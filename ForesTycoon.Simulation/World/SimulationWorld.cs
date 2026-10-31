using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>
    /// Owns deterministic world state, commands, transport, economy and persistence.
    /// Hosts attach presentation through optional lifecycle hooks.
    /// </summary>
    partial class SimulationWorld : IDisposable, IWorldCommandTarget
    {
        /// <summary>The ground model, independent of its presentation.</summary>
        private TerrainMap map;
        private WildlifeSystem wildlife = new WildlifeSystem();
        internal ForestryLogistics Logistics { get; private set; }
        private WorldCommandQueue commands = new WorldCommandQueue();
        private WorldSystemCollection systems = new WorldSystemCollection();
        private BackgroundJobScheduler backgroundJobs = new BackgroundJobScheduler();
        private List<WorldCommandRecord> commandJournal = new List<WorldCommandRecord>();
        private VehicleSystem vehicles;
        private WorldEffectSystem effects;
        private Ecosystem ecosystem;
        // Immutable species snapshot used by this world's update and presentation.
        private object worldSpecies;
        private ForesTycoon.Rules.SpeciesDefinition[] worldSpeciesDefinitions;
        internal T WithSpecies<T>(Func<T> action) => ForestSpeciesBook.WithContext(worldSpecies, action);
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
        internal TerrainMap Map => map;
        internal virtual bool HasPresentation => false;
        private bool disposed, faulted;
        internal bool IsFaulted => faulted;

        protected void EnsureAvailable(bool allowFaulted = false)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted && !allowFaulted)
                throw new InvalidOperationException("The world update failed; load a validated save or regenerate before continuing.");
        }

        internal SimulationWorld(TerrainSettings settings, double forestYearSeconds = EcologyTime.DefaultGameSecondsPerYear,
            SoilLandscapeDefinition soils = null, ClimateDefinition climate = null,
            EcologyResponseProgram responseRules = null, WorldScenario scene = null)
            : this(CreateMap(settings, forestYearSeconds), forestYearSeconds, soils ?? SoilLandscapeDefinition.Default,
                climate ?? ClimateDefinition.Default, initialResponses: responseRules, scene: scene) { }

        private static TerrainMap CreateMap(TerrainSettings settings, double forestYearSeconds)
        {
            if (!EcologyTime.IsValidForestYearSeconds(forestYearSeconds))
                throw new ArgumentOutOfRangeException(nameof(forestYearSeconds));
            return new TerrainMap(settings ?? throw new ArgumentNullException(nameof(settings)));
        }

        // Direct model injection also supports flat and otherwise controlled simulation fixtures.
        internal SimulationWorld(TerrainMap map, double forestYearSeconds = EcologyTime.DefaultGameSecondsPerYear)
            : this(map, forestYearSeconds, SoilLandscapeDefinition.Default, ClimateDefinition.Default) { }

        internal SimulationWorld(TerrainMap map, double forestYearSeconds, SoilLandscapeDefinition soilModel,
            ClimateDefinition climate, Ecosystem preparedEcosystem = null, bool deferRules = false,
            EcologyResponseProgram initialResponses = null, WorldScenario scene = null)
        {
            ruleValues = new(this);
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            // The initial forest is grown here, so it already reads the scene's zones and biomes.
            ruleProjectWorld = scene;
            worldSpeciesDefinitions = scene?.Species?.ToArray();
            worldSpecies = ForestSpeciesBook.Compose(SpeciesEdits(scene));
            BindWorldQueries();
            try
            {
                ecosystem = preparedEcosystem ?? ForestSpeciesBook.WithContext(worldSpecies,
                    () => new Ecosystem(map, forestYearSeconds, soilModel, climate, initialResponses ?? behaviorPolicy.Responses));
                timberCargo = systems.Add(new TimberCargoSystem());
                vehicles = systems.Add(new VehicleSystem(timberCargo, route => VehicleRoadRoute.Create(this.map, route)) { TimeScale = VehicleTimeScale });
                vehicles.RoadRouteWorkFactory = (route, result) => VehicleRoadRoute.CreateWork(this.map, route, result);
                effects = systems.Add(new WorldEffectSystem());
                InitializeLogistics();
                if (!deferRules) WithSpecies(() => { InitializeRuleProject(); return 0; });
            }
            catch { Dispose(); throw; }
        }

        private void BindWorldQueries()
        {
            map.LandscapeClass = id => Landscape(ruleProjectWorld ?? BuiltinZones, map, id);
            BindPresentationQueries();
        }

        public Tile HoveredTile => map.HoveredTile;
        public int HoveredTileId => map.HoveredTile?.Id ?? -1;
        public int SelectedNodeId => map.SelectedNodeId;
        public int RoadCount => map.RoadCount;
        public int VehicleCount => vehicles.Count;
        internal System.Collections.Generic.IReadOnlyList<Vehicle> Vehicles => vehicles.Vehicles;
        public ForestStatistics ForestStatistics => forest.Statistics;
        internal ForestSystem Forest => forest;
        internal WildlifeSystem Wildlife => wildlife;
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
            ForestManagementSurvey.Recommend(forest, tileId);
        internal bool TryGetTileCenter(int tileId, out Vector3 centre) => map.TryGetTileCenter(tileId, out centre);
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
        internal Dictionary<string, double> EcologyWorkProfile { get => ecosystem.WorkProfile; set => ecosystem.WorkProfile = value; }
        internal WorldUpdateProfile LastUpdateProfile { get; private set; }
        /// <summary>With <see cref="ProfileUpdates"/>: what the rule step of the last update cost (null when none ran).</summary>
        internal double? LastRuleStepMilliseconds { get; private set; }

        // Synchronous simulation adapter for headless replay/tests. Interactive hosts use ContinueUpdate.
        public void Update(double fixedDeltaSeconds)
        {
            SimulationWork.RequireSynchronousContext();
            while (!ContinueUpdate(fixedDeltaSeconds, 256)) { }
        }

        private IEnumerator<int> updateWork;
        private double updateDelta;
        private int workPhase;
        private bool pendingForestRateRefresh;
        private readonly double[] workCpu = new double[6];
        private static readonly string[] WorkPhaseNames = { "dynamics", "ecology", "logistics", "rules", "wildlife", "systems" };
        internal string ActiveWorkProcess => WorkPhaseNames[workPhase];
        internal bool UpdatePending => updateWork != null;
        private readonly Queue<Action> tickBoundaryActions = new();
        internal int LastUpdateWorkUnits { get; private set; }

        /// <summary>Runs at most budget deterministic work units. The logical tick commits only on completion.</summary>
        internal bool ContinueUpdate(double fixedDeltaSeconds, int budget = 64)
            => ForestSpeciesBook.WithContext(worldSpecies, () => ContinueUpdateCore(fixedDeltaSeconds, budget));

        private bool ContinueUpdateCore(double fixedDeltaSeconds, int budget)
        {
            EnsureAvailable(); VerifyPresentationAccess();
            if (!double.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds < 0 || budget <= 0)
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
            if (updateWork != null && fixedDeltaSeconds != updateDelta)
                throw new InvalidOperationException("A pending tick must retain its time step.");
            if (generationWork != null || commands.WorkPending || RuleExportPending) return false;
            updateDelta = fixedDeltaSeconds;
            if (updateWork == null) { Array.Clear(workCpu); workPhase = 0; updateWork = UpdateWork(fixedDeltaSeconds).GetEnumerator(); }
            LastUpdateWorkUnits = 0;
            int measuredPhase = workPhase;
            long phaseStarted = ProfileUpdates ? Stopwatch.GetTimestamp() : 0;
            try {
                while (LastUpdateWorkUnits < budget) {
                    bool more = SimulationWork.MoveNext(updateWork, budget - LastUpdateWorkUnits);
                    if (ProfileUpdates && measuredPhase != workPhase) {
                        workCpu[measuredPhase] += Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
                        measuredPhase = workPhase; phaseStarted = Stopwatch.GetTimestamp();
                    }
                    if (!more) {
                        updateWork.Dispose(); updateWork = null;
                        if (ProfileUpdates) {
                            workCpu[measuredPhase] += Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
                            LastUpdateProfile = new(workCpu[0] + workCpu[1], workCpu[2] + workCpu[3], workCpu[4], workCpu[5]);
                            LastRuleStepMilliseconds = workCpu[3] > 0 ? workCpu[3] : null;
                        }
                        PublishBoundaryActions();
                        return true;
                    }
                    LastUpdateWorkUnits += Math.Max(1, updateWork.Current);
                }
                if (ProfileUpdates) workCpu[measuredPhase] += Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
                return false;
            }
            catch when (updateWork != null) { updateWork.Dispose(); updateWork = null; faulted = true; throw; }
        }

        /// <summary>Requests a snapshot/operation at a complete tick, without draining unfinished work.</summary>
        internal void AtTickBoundary(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (BoundaryPending) tickBoundaryActions.Enqueue(action); else action();
        }

        private IEnumerable<int> UpdateWork(double fixedDeltaSeconds)
        {
            LastRuleStepMilliseconds = null;
            if (pendingForestRateRefresh) {
                foreach (int unit in forest.RefreshEnvironmentRatesWork()) yield return unit;
                pendingForestRateRefresh = false;
            }
            foreach (int unit in dynamics.StepWork(fixedDeltaSeconds, forest.ForestYear * ecosystem.ForestYearSeconds)) yield return unit;
            workPhase = 1;
            foreach (int unit in ecosystem.UpdateWork(fixedDeltaSeconds)) yield return unit;
            workPhase = 2;
            // Both fleets use the same sampled clock for this logical tick, including dynamic formula hooks.
            vehicles.TimeScale = Tuning[Tune.VehicleTimeScale];
            vehicles.Spec = Tuning.SampleTruck();
            foreach (int unit in Logistics.UpdateWork(fixedDeltaSeconds * vehicles.TimeScale)) yield return unit;
            workPhase = 3;
            foreach (int unit in TrafficWork()) yield return unit;
            foreach (int unit in Environment.RefreshVegetationWork()) yield return unit;
            foreach (int unit in WeatherRoadsWork(fixedDeltaSeconds)) yield return unit;
            workPhase = 4;
            double wildlifeTimeScale = behaviorPolicy.Gameplay.Evaluate("wildlife.timeScale", forest.ForestYear, wildlife.Animals.Count);
            foreach (int unit in wildlife.UpdateWork(fixedDeltaSeconds * wildlifeTimeScale, map, forest, Environment)) yield return unit;
            workPhase = 5;
            foreach (int unit in systems.UpdateWork(fixedDeltaSeconds)) yield return unit;
            foreach (int unit in TrafficWork()) yield return unit;
            foreach (int unit in Logistics.PublishInventoryWork()) yield return unit;
            var motion = new System.Collections.Generic.List<(RigPresentation Target, RigPresentationSample Sample)>();
            foreach (var v in vehicles.Vehicles) {
                if (v.Kinematics != null) motion.Add((v.Presentation, new(v.Kinematics.CapturePresentation(), v.CurrentSpeed > .0001 && !v.RouteBlocked && !v.Broken)));
                yield return 1;
            }
            foreach (var m in Logistics.Machines) {
                if (m.Kinematics != null) motion.Add((m.Presentation, new(m.Kinematics.CapturePresentation(), m.State == ForestMachineState.Driving && !m.Upkeep.Broken)));
                yield return 1;
            }
            // The entire set is committed without a yield, before the host advances its logical tick.
            foreach (var item in motion) item.Target.Publish(item.Sample);
            worldTick++;
        }

        public int ExecutePendingCommands()
        {
            SimulationWork.RequireSynchronousContext();
            EnsureAvailable(); VerifyPresentationAccess();
            while (RuleExportPending) ContinueCommands(256);
            if (UpdatePending || RulesBlockCommands) return 0;
            if (generationWork != null) throw new InvalidOperationException("Use ContinueCommands while generating a world.");
            int completed = 0, count = commands.Count;
            do { completed += ContinueCommands(256); } while (commands.WorkPending && completed < count);
            return completed;
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
        internal bool IsSkidTrail(int tileId) => map.IsSkidTrail(tileId);
        internal float GetSkidTrailWear(int tileId) => map.GetSkidTrailWear(tileId);
        internal int SkidTrailCount => map.SkidTrailCount;
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
        internal double Balance => behaviorPolicy.Gameplay.Evaluate("economy.balance", Income, Expenses, Logistics?.RunningCosts ?? 0);
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

        private void Enqueue(IWorldCommand command)
        {
            EnsureAvailable();
            if (GenerationPending) generationInputs.Add(command);
            commandJournal.Add(command.ToRecord(worldTick + (UpdatePending ? 1UL : 0UL)));
            commands.Enqueue(command);
        }

        void IWorldCommandTarget.ExecuteElevationEdit(int nodeId, int delta, int radius, int strength) =>
            SimulationWork.Complete(ExecuteCommandWork(new EditElevationCommand(nodeId, delta, radius, strength)));

        void IWorldCommandTarget.ExecuteLegacyElevationEdit(int nodeId, int delta, int radius, int strength) =>
            SimulationWork.Complete(ExecuteCommandWork(new EditElevationCommand(nodeId, delta, radius, strength, true)));

        void IWorldCommandTarget.ExecuteRoadPath(int startTileId, int endTileId, bool remove) =>
            ((IWorldCommandTarget)this).ExecuteRoadPath(startTileId, endTileId, remove, RoadPaving.Asphalt);

        /// <summary>The credit line gates new spending; the refusal is part of the deterministic command result.</summary>
        private bool Fundable()
        {
            if (behaviorPolicy.Gameplay.Evaluate("economy.invest", Balance, Tuning[Tune.CreditLimit]) >= .5) return true;
            if (Logistics != null) Logistics.Status = Economy.NoFunds;
            return false;
        }

        void IWorldCommandTarget.ExecuteRoadRepair(int startTileId, int endTileId) =>
            SimulationWork.Complete(ExecuteCommandWork(new RoadRepairCommand(startTileId, endTileId)));

        /// <summary>Game seconds between rule steps; each logical step can suspend between bounded work units.</summary>
        internal const double RuleStepSeconds = 0.5;

        /// <summary>Rain and wet ground wear the roads, and skid-trail ruts fade; applied twice a second of game time.</summary>
        private IEnumerable<int> WeatherRoadsWork(double seconds)
        {
            roadWeatherSeconds += seconds;
            if (!ruleEngine.StepPending && roadWeatherSeconds >= RuleStepSeconds) {
                float years = (float)(roadWeatherSeconds / ecosystem.ForestYearSeconds);
                Environment.RefreshLocalClimate();
                ruleEngine.Begin(this, years, forest.ForestYear);
                roadWeatherSeconds = 0;
            }
            if (ruleEngine.StepPending) {
                foreach (int unit in ruleEngine.ContinueWork(this)) yield return unit;
                foreach (int unit in forest.CompleteRuleEditsWork(forest.ForestYear)) yield return unit;
                if (ruleEngine.NetworkChanged) {
                    Logistics?.TrailsChanged();
                    foreach (int unit in vehicles.RemoveInvalidRoutesWork(map.IsNetworkTile, map.AreNetworkNeighbours)) yield return unit;
                }
            }
        }

        void IWorldCommandTarget.ExecuteSkidTrailPath(int startTileId, int endTileId, bool remove) =>
            SimulationWork.Complete(ExecuteCommandWork(new SkidTrailPathCommand(startTileId, endTileId, remove)));

        void IWorldCommandTarget.ExecuteRoadPath(int startTileId, int endTileId, bool remove, RoadPaving surface) =>
            SimulationWork.Complete(RoadCommandWork(startTileId, endTileId, remove, surface));

        void IWorldCommandTarget.ExecuteSpawnVehicle()
        {
            Logistics.Status = "A rönkszállítót a Járművek ablakból küldd: forrás sarang, majd cél.";
        }

        void IWorldCommandTarget.ExecutePlantForest(int tileId, ForestSpecies species) =>
            SimulationWork.Complete(ExecuteCommandWork(new PlantForestCommand(tileId, species)));

        void IWorldCommandTarget.ExecuteHarvestForest(int tileId)
        {
            DesignateHarvest(tileId, tileId);
        }

        void IWorldCommandTarget.ExecutePlantForestArea(int startTileId, int endTileId, ForestSpecies species) =>
            SimulationWork.Complete(ExecuteCommandWork(new PlantForestAreaCommand(startTileId, endTileId, species)));

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
        public void Regenerate(TerrainSettings settings) => Regenerate(settings, SoilLandscapeDefinition.Default);
        /// <summary>A new world from a seed and a soil model, for example a world-editor scenario with painted soil.</summary>
        internal void Regenerate(TerrainSettings settings, SoilLandscapeDefinition soils, ForesTycoon.Rules.EcologyResponseProgram responseRules = null, WorldScenario scene = null)
        {
            SimulationWork.RequireSynchronousContext();
            EnsureAvailable(allowFaulted: true);
            VerifyPresentationAccess();
            using var candidate = CreateCandidate(CreateMap(settings, EcologyTime.DefaultGameSecondsPerYear), EcologyTime.DefaultGameSecondsPerYear,
                soils ?? SoilLandscapeDefinition.Default, ClimateDefinition.Default, initialResponses: responseRules, scene: scene);
            candidate.ApplyTuning(Tuning);
            if (HasPresentation) candidate.AttachPresentation();
            Adopt(candidate);
            LastRuleSample = "Még nem történt közúti áthaladás.";
            LastLoadReplayedTicks = 0;
        }

        public void Save(Stream destination, double tickRate = 30.0)
        {
            EnsureAvailable();
            if (BoundaryPending) throw new InvalidOperationException("Save must be requested at a completed tick (AtTickBoundary).");
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

        public void Load(Stream source, bool allowAlgorithmMigration = false)
        {
            EnsureAvailable(allowFaulted: true);
            VerifyPresentationAccess();
            WorldSaveData save = WorldSaveSerializer.Read(source);
            save.ValidateReplay();
            if (!allowAlgorithmMigration) RequireAlgorithmRuntime(save.Checkpoint?.Algorithms);
            else if (save.Checkpoint == null || save.Checkpoint.Tick != save.Tick || save.Checkpoint.PendingCommands != 0)
                throw new InvalidOperationException("Algoritmusváltáshoz aktuális, függő parancs nélküli állapotmentés szükséges.");
            var settings = save.Terrain.ToSettings();
            // Replay into an isolated world. Failure leaves the live world and queued commands intact.
            using var candidate = CreateCandidate(CreateMap(settings, save.ReplayForestYearSeconds), save.ReplayForestYearSeconds, save.ReplaySoilModel, save.ReplayClimate);
            ulong startTick = save.Checkpoint?.Tick ?? 0;
            if (save.Checkpoint == null) candidate.Replay(save);
            else { candidate.RestoreCheckpoint(save.ReplayCheckpoint); candidate.ReplayTail(save); }
            if (allowAlgorithmMigration) candidate.algorithmGraphs = ForesTycoon.RuleCompiler.AlgorithmBuild.CurrentReference;

            foreach (var record in save.Commands) candidate.commandJournal.Add(save.ReplayCommand(record));
            if (HasPresentation) candidate.AttachPresentation();
            ulong replayedTicks = candidate.worldTick - startTick;
            Adopt(candidate);
            LastRuleSample = "Betöltött szabálymodell; várakozás a következő áthaladásra.";
            LastLoadReplayedTicks = replayedTicks;
        }

        private void Adopt(SimulationWorld candidate, bool preserveBoundaryActions = false)
        {
            CancelRuleExport();
            updateWork?.Dispose(); updateWork = null; if (!preserveBoundaryActions) tickBoundaryActions.Clear();
            (map, candidate.map) = (candidate.map, map);
            SwapPresentation(candidate);
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
            (trafficPassages, candidate.trafficPassages) = (candidate.trafficPassages, trafficPassages);
            (activePassage, candidate.activePassage) = (candidate.activePassage, activePassage);
            // Route creation must follow this world's terrain after the ownership transfer.
            vehicles.RoadRouteFactory = route => VehicleRoadRoute.Create(map, route);
            vehicles.RoadRouteWorkFactory = (route, result) => VehicleRoadRoute.CreateWork(map, route, result);
            BindWorldQueries();
            ruleProjectName = candidate.ruleProjectName; ruleProjectWorld = candidate.ruleProjectWorld;
            algorithmGraphs = candidate.algorithmGraphs;
            worldSpecies = candidate.worldSpecies;
            worldSpeciesDefinitions = candidate.worldSpeciesDefinitions;
            ruleEngine = candidate.ruleEngine;
            dynamics = candidate.dynamics;
            behaviorPolicy = candidate.behaviorPolicy;
            ruleValues = candidate.ruleValues; ruleValues.World = this;
            BindBehaviors();
            Expenses = candidate.Expenses;
            roadWeatherSeconds = candidate.roadWeatherSeconds;
            pendingForestRateRefresh = candidate.pendingForestRateRefresh;
            ClearRuleViews();
            BindRoadRules();
            ApplyTuning(candidate.Tuning, preserveTruck: true);
            foreach (var vehicle in vehicles.Vehicles) { vehicle.RoadState = vehicles.RoadState; vehicle.Passage = vehicles.Passage; }
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
                ExecutePendingCommands();
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
            Logistics=new ForestryLogistics(map,forest);
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
            SimulationWork.Complete(DisposeWork());
        }

        private IEnumerable<int> DisposeWork()
        {
            if (disposed) yield break;
            VerifyPresentationAccess();
            CancelRuleExport();
            updateWork?.Dispose(); updateWork = null; tickBoundaryActions.Clear();
            generationWork?.Dispose(); generationWork = null; commands.Clear();
            backgroundJobs.Dispose();
            if (forest != null) foreach (int unit in forest.FinishBackgroundWork()) yield return unit;
            if (ecosystem != null) foreach (int unit in Environment.FinishQueryWork()) yield return unit;
            if (ruleEngine != null) foreach (int unit in ruleEngine.FinishQueryWork()) yield return unit;
            foreach (int unit in DisposePresentationWork()) yield return unit;
            ClearRuleViews(); yield return 1;
            disposed = true;
        }
    }
}
