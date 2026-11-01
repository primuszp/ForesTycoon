using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    internal enum ForestMachineKind : byte { Harvester, Forwarder }

    internal enum ForestMachineState : byte { Parked, Driving, Felling, Loading, Unloading }

    /// <summary>
    /// A processor (harvester) or a forwarder of the fleet. It moves tile by tile along a path over the network, its
    /// felling site and stack sites; <see cref="PathPosition"/> runs from 0 (first tile) to the last path index.
    /// </summary>
    internal sealed class ForestMachine
    {
        internal const float ForwarderCapacity = 14f;
        /// <summary>What the processor carries in its grapple and bunk between felling and the stack, m³.</summary>
        internal const float ProcessorCapacity = 3f;
        internal int Id;
        internal ForestMachineKind Kind;
        /// <summary>Processor: the site it fells. Null while at (or on the way to) its depot.</summary>
        internal HarvestSite Site;
        /// <summary>Forwarder: the stack it fetches from.</summary>
        internal TimberStack Source;
        /// <summary>Processor: the stack it carries to (the nearest one).</summary>
        internal TimberStack Target;
        /// <summary>Forwarder: where it takes the wood — any tile (a stack is started there) or a sawmill.</summary>
        internal int Destination = -1;
        internal Depot Home;
        internal bool HomeRequested;
        internal int[] Path;
        internal double PathPosition, PreviousPathPosition;
        internal RigPathFollower SteeringDriver;
        internal VehicleKinematics Kinematics => SteeringDriver?.Rig;
        internal RigState PreviousRigPose;
        internal readonly RigPresentation Presentation = new();
        internal readonly WorkAnimationPresentation WorkPresentation = new();
        internal int[] SteeringPath;
        internal VehicleRoadRoute SteeringRoad;
        internal ForestMachineState State;
        /// <summary>What the machine does on reaching the end of its path.</summary>
        internal ForestMachineState Goal;
        internal float Cargo;
        internal double CargoValue;
        /// <summary>Seconds spent in the present work state; drives crane and saw animation.</summary>
        internal double WorkTime;
        internal float LogTransferVolume;
        internal double LogTransferValue, LogCycleDuration;
        internal double LogGripPhase = ForwarderLoading.Grip, LogReleasePhase = ForwarderLoading.Release;
        internal IBehaviorPolicy Behaviors;
        internal readonly LogisticsRuleEvaluator Rules = new();
        internal bool SourceFed;
        internal LogisticsInputs RuleInputs(double dt = 0, double availableTimber = 0, bool reachable = false) => new(
            StepSeconds: dt, MachineKind: (int)Kind, MachineState: (int)State, MachineCargo: Cargo, MachineCapacity: Capacity,
            MachineWear: Upkeep.Wear, MachinePace: Upkeep.Pace(Tuning), MachineHomeRequested: HomeRequested ? 1 : 0,
            SourceStock: Source?.Volume ?? 0, SourceFed: SourceFed ? 1 : 0, SourceAvailableTimber: availableTimber, SourceReachable: reachable ? 1 : 0,
            SpecFellingRate: Tuning[Tune.FellingRate], SpecUnloadRate: Tuning[Kind == ForestMachineKind.Harvester ? Tune.ProcessorUnloadRate : Tune.ForwarderUnloadRate],
            SpecLoadRate: Tuning[Tune.ForwarderLoadRate], SpecSpeed: Tuning[Kind == ForestMachineKind.Harvester ? Tune.HarvesterSpeed : Tune.ForwarderSpeed],
            SpecLoadedSpeed: Tuning[Kind == ForestMachineKind.Harvester ? Tune.HarvesterSpeed : Tune.ForwarderLoadedSpeed],
            SpecFuelDriving: Tuning[Tune.FuelDriving], SpecFuelLoaded: Tuning[Tune.FuelDrivingLoaded], SpecFuelCrane: Tuning[Tune.FuelCrane], SpecFuelFelling: Tuning[Tune.FuelFelling],
            SpecEmptyMass: Tuning[Kind == ForestMachineKind.Harvester ? Tune.ProcessorMass : Tune.ForwarderMass], SpecTimberDensity: Tuning[Tune.TimberDensity],
            LogVolume: ForwarderLoading.LogVolume, LogLastVolume: Math.Min(ForwarderLoading.LogVolume, Cargo - Math.Max(0, (ForwarderLoading.Count(Cargo) - 1) * ForwarderLoading.LogVolume)),
            TransferUnloading: State == ForestMachineState.Unloading ? 1 : 0,
            MachineControl: (int)Control, MachineWorking: Working ? 1 : 0, MachineBroken: Upkeep.Broken ? 1 : 0, MachineHasVehicle: 1,
            TransferHeld: LogTransferVolume);
        internal double Rule(string hook, double dt = 0, double availableTimber = 0, bool reachable = false) =>
            Rules.Evaluate(Behaviors, hook, Kind == ForestMachineKind.Harvester ? "processor" : "forwarder", (ulong)Id, RuleInputs(dt, availableTimber, reachable));
        internal MachineWorkflowOperation Workflow(MachineWorkflowStage stage, double dt, double availableTimber = 0, bool reachable = false) => (MachineWorkflowOperation)(int)Rules.Evaluate(Behaviors,
            "machine.workflow", Kind == ForestMachineKind.Harvester ? "processor" : "forwarder", (ulong)Id,
            RuleInputs(dt, availableTimber, reachable) with {
                WorkflowStage = (int)stage,
                DecisionContinue = Kind == ForestMachineKind.Harvester ? Rule("machine.continueFelling", dt, availableTimber) : 0,
                DecisionDeliver = Rule("machine.deliver", dt, reachable: reachable),
                DecisionFetch = Kind == ForestMachineKind.Forwarder ? Rule("machine.fetch", dt) : 0,
                DecisionReturn = Kind == ForestMachineKind.Forwarder ? Rule("machine.return", dt) : 0 });
        internal BehaviorAction Control;
        /// <summary>Diesel burnt so far, litres.</summary>
        internal double FuelUsed;
        /// <summary>Processor: price of the wood it is felling (kept when the last standing tree of a tile is gone).</summary>
        internal double UnitPrice;
        internal VehicleUpkeep Upkeep = new(0);
        private GameTuning tuning = GameTuning.Default;
        /// <summary>Owned machines read the published world parameters without a fleet-wide update.</summary>
        internal ForestryLogistics TuningSource { get; set; }
        internal GameTuning Tuning { get => TuningSource?.Tuning ?? tuning; set => tuning = value ?? GameTuning.Default; }
        internal int Tile => Path[Math.Clamp((int)Math.Round(PathPosition), 0, Path.Length - 1)];
        internal bool Arrived => PathPosition >= Path.Length - 1 - 1e-9;
        internal float Capacity => Tuning.F(Kind == ForestMachineKind.Forwarder ? Tune.ForwarderCapacity : Tune.ProcessorCapacity);
        // Existing loads survive capacity reductions; no new timber may be loaded until there is room.
        internal float CargoFill => Math.Clamp(Cargo / Capacity, 0, 1);
        /// <summary>Processor: whether designated timber is still reachable from its stack (set at each decision).</summary>
        internal bool HasWork;
        internal bool Working => Source != null || (Kind == ForestMachineKind.Harvester && Target != null);
    }

    /// <summary>
    /// Cut-to-length harvesting: the processor fells in its site and carries the logs to the nearest stack site the
    /// player marked — the farther, the more driving, diesel and lost output. Forwarders move stacks on (stack → any
    /// tile or a mill); the machines drive on the network, along skid trails and inside the felling.
    /// </summary>
    internal sealed partial class ForestryLogistics
    {
        internal const float FellingRate = 1.2f, ForwarderLoadRate = ForwarderLoading.LogVolume / 8, ForwarderUnloadRate = ForwarderLoading.LogVolume / 8, ProcessorUnloadRate = 2f;
        internal const float HarvesterSpeed = 0.45f, ForwarderSpeed = 0.6f, ForwarderLoadedSpeed = 0.4f;
        /// <summary>Diesel use by activity, litres per second of game time.</summary>
        internal const double FuelFelling = 1.0, FuelDriving = 0.5, FuelDrivingLoaded = 0.8, FuelCrane = 0.35;
        internal readonly List<ForestMachine> Machines = new();
        private GameTuning tuning = GameTuning.Default;
        /// <summary>The world's tunable numbers; the machines read their capacities from it.</summary>
        internal GameTuning Tuning
        {
            get => tuning;
            set => tuning = value ?? GameTuning.Default;
        }
        private int nextMachineId = 1;

        // Where machines may drive: the network, their felling, stack sites, depot and mill yards.
        private bool Passable(HarvestSite site, int tile) =>
            terrain.IsNetworkTile(tile) || ContainsTile(tile) || StackAt(tile) != null
            || Depots.Exists(d => Array.IndexOf(d.Footprint, tile) >= 0) || Mills.Exists(m => Array.IndexOf(m.Footprint, tile) >= 0);

        // Network-to-network travel follows its actual arms. Worksites and yards still allow manoeuvring.
        private int[] ForwarderPath(ForestMachine machine, int from, int target)
        {
            var docks = DestinationDocks(target);
            return FindPath(null, from, t => terrain.IsNetworkTile(t) && docks.Contains(t), networkOnly: true, agent: machine);
        }

        private IEnumerable<int> ForwarderPathWork(ForestMachine machine, int from, int target, WorkResult<int[]> result)
        {
            var docks = DestinationDocks(target);
            foreach (int unit in FindPathWork(null, from, t => terrain.IsNetworkTile(t) && docks.Contains(t), result, networkOnly: true, agent: machine)) yield return unit;
        }

        private bool CanDriveBetween(int from, int to) =>
            !terrain.IsNetworkTile(from) || !terrain.IsNetworkTile(to) || terrain.AreNetworkNeighbours(from, to);

        /// <summary>
        /// Shortest path over passable tiles from <paramref name="from"/> to the first tile that satisfies
        /// <paramref name="goal"/>; <paramref name="extra"/> is one more tile allowed (a new stack site).
        /// </summary>
        private int[] FindPath(HarvestSite site, int from, Func<int, bool> goal, int extra = -1, bool networkOnly = false, ForestMachine agent = null)
        {
            var result = new WorkResult<int[]>();
            SimulationWork.Complete(FindPathWork(site, from, goal, result, extra, networkOnly, agent));
            return result.Value;
        }

        private IEnumerable<int> FindPathWork(HarvestSite site, int from, Func<int, bool> goal, WorkResult<int[]> result,
            int extra = -1, bool networkOnly = false, ForestMachine agent = null)
        {
            result.Value = null;
            if (!terrain.IsValidTileId(from) || !Passable(site, from) && from != extra) yield break;
            if (networkOnly && !terrain.IsNetworkTile(from) && !IsDepotTile(from) && StackAt(from) == null) yield break;
            var previous = new Dictionary<int, int> { [from] = -1 };
            var costs = new Dictionary<int, float> { [from] = 0 }; long sequence = 0;
            var queue = new PriorityQueue<int, (float Cost, long Order)>(); queue.Enqueue(from, (0, sequence++));
            int[] next = new int[4];
            while (queue.Count > 0)
            {
                yield return 1;
                queue.TryDequeue(out int tile, out var priority);
                if (priority.Cost > costs[tile]) continue;
                if (goal(tile))
                {
                    var path = new List<int>();
                    for (int t = tile; t >= 0; t = previous[t]) { path.Add(t); yield return 1; }
                    var ordered = new int[path.Count];
                    for (int i = 0; i < ordered.Length; i++) { yield return 1; ordered[i] = path[path.Count - 1 - i]; }
                    result.Value = ordered; yield break;
                }
                int count = terrain.GetTileNeighbours(tile, next);
                for (int i = 0; i < count; i++)
                    if ((!networkOnly || terrain.IsNetworkTile(next[i]) || IsDepotTile(next[i])) && (next[i] == extra || Passable(site, next[i])) && CanDriveBetween(tile, next[i]) ) {
                        float step = agent == null ? 1 : RouteCost(agent.Kind == ForestMachineKind.Harvester ? "processor" : "forwarder", agent.Id, agent.Cargo, agent.Capacity, tile, next[i], 1);
                        if (float.IsPositiveInfinity(step)) continue;
                        float cost = costs[tile] + step;
                        if (costs.TryGetValue(next[i], out float known) && known <= cost) continue;
                        costs[next[i]] = cost; previous[next[i]] = tile; queue.Enqueue(next[i], (cost, sequence++));
                    }
            }
            yield break;
        }

        /// <summary>Skid trails changed. Paths are searched as machines move, so nothing is cached to rebuild.</summary>
        internal void TrailsChanged() { }

        /// <summary>
        /// The processor's work: <paramref name="stack"/> is its receiving stack. It drives there, then fells the nearest
        /// reachable designated trees (any harvest site) and carries every load back to that stack.
        /// </summary>
        internal bool AssignProcessor(ForestMachine machine, TimberStack stack) { var result = new WorkResult<bool>(); SimulationWork.Complete(AssignProcessorWork(machine, stack, result)); return result.Value; }

        internal IEnumerable<int> AssignProcessorWork(ForestMachine machine, TimberStack stack, WorkResult<bool> result)
        {
            var path = new WorkResult<int[]>();
            foreach (int unit in FindPathWork(null, OrderOrigin(machine), t => t == stack.Tile, path, agent: machine)) yield return unit;
            if (path.Value == null) { Status = "A processzor nem jut el ehhez a saranghoz."; yield break; }
            foreach (int unit in FindPathWork(null, stack.Tile, t => ContainsTile(t) && forest.AvailableTimber(t) > .001f, path, agent: machine)) yield return unit;
            bool hasTimber = path.Value != null;
            string rejection = MachineOrderRejection(machine, stack, hasTimber: hasTimber);
            yield return 1;
            if (rejection != null) { Status = rejection; yield break; }
            Release(machine);
            machine.Target = stack; machine.HasWork = hasTimber;
            Restart(machine);
            Status = $"A processzor a #{stack.Id} saranghoz indult, onnan termel.";
            result.Value = true;
        }

        /// <summary>Path to the nearest designated tile with standing timber, over network, trails, stacks and fellings.</summary>
        private int[] NearestTimber(ForestMachine machine, int from) =>
            FindPath(null, from, t => ContainsTile(t) && forest.AvailableTimber(t) > 0.001f, agent: machine);
        private bool IsDepotTile(int tile) => Depots.Exists(d => Array.IndexOf(d.Footprint, tile) >= 0);

        /// <summary>The forwarder's work: carry <paramref name="source"/> to <paramref name="destination"/> until it is empty.</summary>
        internal bool AssignForwarder(ForestMachine machine, TimberStack source, int destination) { var result = new WorkResult<bool>(); SimulationWork.Complete(AssignForwarderWork(machine, source, destination, result)); return result.Value; }

        internal IEnumerable<int> AssignForwarderWork(ForestMachine machine, TimberStack source, int destination, WorkResult<bool> result)
        {
            if (destination == source.Tile) { Status = "A cél nem lehet maga a forrás sarang."; yield break; }
            bool mill = MillAt(destination) != null;
            if (!mill && StackAt(destination) == null && !CanPlaceStack(destination)) { Status = "Ide nem rakható sarang."; yield break; }
            var path = new WorkResult<int[]>();
            foreach (int unit in ForwarderPathWork(machine, OrderOrigin(machine), source.Tile, path)) yield return unit;
            if (path.Value == null) { Status = "A forwarder nem talál utat a forrás saranghoz."; yield break; }
            foreach (int unit in ForwarderPathWork(machine, path.Value[^1], destination, path)) yield return unit;
            if (path.Value == null) { Status = "A forrás sarangtól nem vezet út a célig."; yield break; }
            string rejection = MachineOrderRejection(machine, source, mill);
            yield return 1;
            if (rejection != null) { Status = rejection; yield break; }
            Release(machine);
            if (!mill && StackAt(destination) == null && !PlaceStack(destination)) yield break;
            machine.Source = source; machine.Destination = destination;
            Restart(machine);
            Status = mill ? "A forwarder a malomba hordja a sarangot." : "A forwarder áthordja a sarangot.";
            result.Value = true;
        }

        private static void Release(ForestMachine machine)
        {
            machine.Cargo += machine.LogTransferVolume; machine.CargoValue += machine.LogTransferValue;
            machine.LogTransferVolume = 0; machine.LogTransferValue = 0; machine.LogCycleDuration = 0; machine.WorkTime = 0;
            machine.Site = null; machine.Source = null; machine.Target = null; machine.Destination = -1; machine.HomeRequested = false;
        }

        private static int OrderOrigin(ForestMachine machine) => machine.Path[Math.Clamp((int)Math.Ceiling(machine.PathPosition), 0, machine.Path.Length - 1)];

        private static void Restart(ForestMachine machine)
        {
            if (machine.State == ForestMachineState.Parked && machine.Home != null && machine.Tile == machine.Home.TileId) {
                machine.SteeringDriver = null; machine.PreviousRigPose = null; machine.Presentation.Reset();
            }
            if (machine.State != ForestMachineState.Driving) machine.State = ForestMachineState.Parked;
            else {
                // Finish only the current segment, then plan the new job from its physical endpoint.
                int last = Math.Clamp((int)Math.Ceiling(machine.PathPosition), 0, machine.Path.Length - 1);
                Array.Resize(ref machine.Path, last + 1);
                machine.Goal = ForestMachineState.Parked;
            }
        }

        /// <summary>Is wood still on its way to this stack (a processor, forwarder or truck will bring more)?</summary>
        private readonly record struct SupplyEdge(ForestMachine Machine = null, FleetTruck Truck = null);
        private IEnumerable<int> BuildSupplyIndexWork(Dictionary<int, List<SupplyEdge>> index)
        {
            void Add(int tile, SupplyEdge edge) {
                if (!index.TryGetValue(tile, out var list)) index.Add(tile, list = new());
                list.Add(edge);
            }
            foreach (var machine in Machines) {
                yield return 1;
                int tile = machine.Kind == ForestMachineKind.Harvester ? machine.Target?.Tile ?? -1 : machine.Source != null ? machine.Destination : -1;
                if (tile >= 0) Add(tile, new(Machine: machine));
            }
            foreach (var truck in Trucks) {
                yield return 1;
                if (truck.Target != null && truck.Source != null) Add(truck.Target.Tile, new(Truck: truck));
            }
        }
        internal bool BeingFed(TimberStack stack)
        {
            var index = new Dictionary<int, List<SupplyEdge>>(); SimulationWork.Complete(BuildSupplyIndexWork(index));
            var result = new WorkResult<bool>(); SimulationWork.Complete(BeingFedWork(stack, result, index)); return result.Value;
        }
        private IEnumerable<int> BeingFedWork(TimberStack stack, WorkResult<bool> result, Dictionary<int, List<SupplyEdge>> index)
        {
            result.Value = false;
            if (stack == null) yield break;
            var pending = new Queue<TimberStack>(); var visited = new HashSet<TimberStack> { stack }; pending.Enqueue(stack);
            while (pending.Count > 0) {
                yield return 1;
                var current = pending.Dequeue();
                if (!index.TryGetValue(current.Tile, out var suppliers)) continue;
                foreach (var edge in suppliers) {
                    yield return 1;
                    TimberStack source;
                    if (edge.Machine is { } machine) {
                        if (machine.Kind == ForestMachineKind.Harvester) {
                            if (machine.Target == current && machine.Rule("supply.active", reachable: machine.HasWork) >= .5) { result.Value = true; yield break; }
                            continue;
                        }
                        if (machine.Destination != current.Tile || machine.Source == null) continue;
                        source = machine.Source;
                        if (machine.Rule("supply.active", reachable: machine.HasWork) >= .5) { result.Value = true; yield break; }
                    } else {
                        var truck = edge.Truck;
                        if (truck.Target != current || truck.Source == null) continue;
                        source = truck.Source;
                        if (rules.Evaluate(Behaviors, "supply.active", "truck", (ulong)truck.Id, TruckInputs(truck)) >= .5) { result.Value = true; yield break; }
                    }
                    if (visited.Add(source)) pending.Enqueue(source);
                }
            }
        }

        /// <summary>Calls a machine back to its depot: it puts down what it carries, then drives home.</summary>
        internal void SendHome(ForestMachine machine)
        {
            if (!machine.Working) return;
            machine.HomeRequested = true;
            Status = "Hazahívva: leteszi, ami nála van, és visszamegy a telephelyre.";
        }

        private IEnumerable<int> GoHomeWork(ForestMachine machine)
        {
            // Releasing a job must never discard a load or a log held by the crane, regardless of the authored decision.
            if (machine.Cargo > .0001f || machine.LogTransferVolume > .0001f) { machine.State = ForestMachineState.Parked; yield break; }
            var site = machine.Site;
            var result = new WorkResult<int[]>();
            if (machine.Home != null) foreach (int unit in FindPathWork(site, machine.Tile, t => t == machine.Home.TileId, result, networkOnly: machine.Kind == ForestMachineKind.Forwarder, agent: machine)) yield return unit;
            int[] path = result.Value;
            if (machine.Home != null && path == null) { machine.State = ForestMachineState.Parked; machine.HomeRequested = true; yield break; }
            Release(machine);
            if (path != null) Drive(machine, path, ForestMachineState.Parked);
            else machine.State = ForestMachineState.Parked;
        }

        private System.Collections.Generic.IEnumerable<int> UpdateMachinesWork(double seconds, Dictionary<int, List<SupplyEdge>> supply)
        {
            // Drawing interpolates across the whole tick (all substeps); a new path resets the start to 0.
            foreach (var machine in Machines) { machine.PreviousPathPosition = machine.PathPosition; machine.PreviousRigPose = machine.Kinematics?.CapturePresentation(); yield return 1; }
            while (seconds > 1e-9)
            {
                double dt = Math.Min(seconds, 1.0 / 30); seconds -= dt;
                for (int i = 0; i < Machines.Count; i++)
                {
                    yield return 1;
                    var machine = Machines[i];
                    if (!PrepareMachineStep(machine, dt)) continue;
                    double pace = machine.Rule("machine.pace", dt);
                    if (machine.State == ForestMachineState.Driving) { foreach (int unit in AdvanceWork(machine, pace)) yield return unit; continue; }
                    if (!machine.Working) continue;
                    if (machine.Kind == ForestMachineKind.Harvester) { foreach (int unit in UpdateProcessorWork(machine, pace)) yield return unit; }
                    else { foreach (int unit in UpdateForwarderWork(machine, pace, supply)) yield return unit; }
                }
            }
        }

        // Control, service and wear always run before the decision to perform work, including while waiting.
        private bool PrepareMachineStep(ForestMachine machine, double dt)
        {
            machine.Behaviors = Behaviors;
            var control = Controllers?.Step(machine.Kind == ForestMachineKind.Harvester ? "processor" : "forwarder",
                (ulong)machine.Id, new(dt, machine.Cargo, (int)machine.State, machine.Working, machine.Upkeep.Broken)) ?? BehaviorAction.Autonomous;
            machine.Control = control;
            if (machine.Rule("control.home", dt) >= .5) SendHome(machine);
            bool wait = machine.Rule("control.allow", dt) < .5;
            int upkeep = (int)machine.Rules.Evaluate(machine.Behaviors, "control.upkeep",
                machine.Kind == ForestMachineKind.Harvester ? "processor" : "forwarder", (ulong)machine.Id,
                machine.RuleInputs(dt) with { DecisionAllow = wait ? 0 : 1 });
            if (upkeep == 1) RunningCosts += machine.Upkeep.Service(dt, Tuning);
            // Wear and breakdowns: rough trails and loads strain the machine; broken, it waits for the mechanic.
            bool rough = terrain.IsSkidTrail(machine.Tile) || (machine.Site != null && Array.IndexOf(machine.Site.Tiles, machine.Tile) >= 0);
            float strain = (float)Tuning.VehicleRules.Evaluate(ForesTycoon.Rules.VehicleResponse.Strain, [(int)machine.Kind + 1, machine.CargoFill, machine.State == ForestMachineState.Felling ? 1 : 0, rough ? 1 : 0]);
            double repair = 0;
            bool running = upkeep != 2 || machine.Upkeep.Operate(dt, strain, out repair, Tuning);
            if (repair > 0) { RunningCosts += repair; Status = $"{MachineName(machine)} megjavítva ({repair:N0} eFt)."; }
            if (!running && machine.Upkeep.RepairLeft >= Tuning[Tune.RepairSeconds] - dt)
                Status = $"{MachineName(machine)} elromlott: a szerelő úton van.";
            return running && !wait;
        }

        private void Drive(ForestMachine machine, int[] path, ForestMachineState goal)
        {
            machine.Path = path; machine.PathPosition = machine.PreviousPathPosition = 0;
            machine.Goal = goal; machine.WorkTime = 0;
            machine.State = path.Length > 1 ? ForestMachineState.Driving : goal;
        }

        private IEnumerable<int> AdvanceWork(ForestMachine machine, double dt)
        {
            float speed = (float)machine.Rule("machine.speed", dt);
            double remaining = speed * dt; bool charged = false;
            do {
                yield return 1;
                int segment = Math.Min((int)Math.Floor(machine.PathPosition), machine.Path.Length - 2);
                if (segment >= 0 && !CanMachineTraverse(machine, machine.Path[segment], machine.Path[segment + 1])) {
                    // Never move across a missing edge or snap a machine out of a broken segment.
                    if (Math.Abs(machine.PathPosition - Math.Round(machine.PathPosition)) < 1e-8) {
                        var replacement = new WorkResult<int[]>();
                        foreach (int unit in MachineContinuationWork(machine, machine.Tile, replacement)) yield return unit;
                        if (replacement.Value != null) { Drive(machine, replacement.Value, machine.Goal); yield break; }
                    }
                    Status = "Az útvonal megszakadt; a gép rakománnyal együtt vár az úthálózat helyreállítására.";
                    yield break;
                }
                if (!charged) { Fuel(machine, machine.Rule("machine.fuel", dt)); charged = true; }
                if (machine.Arrived || remaining <= 0) break;
                double toBoundary = segment + 1 - machine.PathPosition;
                // Projection may approach a waypoint to floating-point precision. Spend real time settling the
                // physical axle there instead of looping forever on an unrepresentably small logical distance.
                double distance = Math.Min(remaining, toBoundary < 1e-6 ? remaining : toBoundary);
                int before = machine.Tile;
                if (machine.SteeringPath != machine.Path || machine.SteeringRoad == null) {
                    var centres = new OpenTK.Mathematics.Vector3[machine.Path.Length];
                    var gradients = new OpenTK.Mathematics.Vector2[machine.Path.Length];
                    for (int i = 0; i < centres.Length; i++) terrain.TryGetTileCenter(machine.Path[i], out centres[i]);
                    machine.SteeringRoad = new(centres, gradients);
                    if (machine.SteeringPath != machine.Path) machine.SteeringDriver?.NewLeg();
                    machine.SteeringPath = machine.Path;
                }
                PlanPoint Guide(double at) {
                    var p = machine.SteeringRoad.Sample(at);
                    return new(p.X / Engine.WorldScale.MetresToWorld, p.Y / Engine.WorldScale.MetresToWorld);
                }
                if (machine.SteeringDriver == null) {
                    var a = Guide(machine.PathPosition); var b = Guide(machine.PathPosition + .001);
                    machine.SteeringDriver = new(new VehicleKinematics(VehicleGeometryProfiles.Machine(machine.Tuning, machine.Kind), a, Math.Atan2(b.Y - a.Y, b.X - a.X)));
                    machine.PreviousRigPose = machine.Kinematics.CapturePresentation();
                }
                double oldPosition = machine.PathPosition;
                machine.PathPosition = machine.SteeringDriver.Advance(Guide, oldPosition, machine.Path.Length - 1,
                    machine.SteeringRoad.TileLength / Engine.WorldScale.MetresToWorld,
                    distance * machine.SteeringRoad.TileLength / Engine.WorldScale.MetresToWorld, speed > 0 ? distance / speed : 0, segment + 1,
                    machine.Kind == ForestMachineKind.Harvester ? machine.Kinematics.Profile.MinimumRadius * 2 +
                        machine.Kinematics.Profile.FrontLength + machine.Kinematics.Profile.RearLength : 0);
                remaining -= distance;
                if (machine.SteeringDriver.BlockedReason != null) { Status = machine.SteeringDriver.BlockedReason; yield break; }
                if (Math.Round(machine.PathPosition) < machine.Path.Length - 1 && Math.Abs(machine.PathPosition - Math.Round(machine.PathPosition)) < 1e-5)
                    machine.PathPosition = Math.Round(machine.PathPosition);
                int after = machine.Tile;
                if (after != before) Passage?.Invoke(new(after,
                    machine.Kind == ForestMachineKind.Harvester ? TrafficVehicleKind.Processor : TrafficVehicleKind.Forwarder,
                    machine.Id, machine.Rule("machine.mass", dt), machine.CargoFill));
            } while (remaining > 1e-9 && !machine.Arrived);
            if (machine.Arrived) { machine.State = machine.Goal; machine.WorkTime = 0; }
        }

        private void Fuel(ForestMachine machine, double litres)
        {
            litres *= machine.Upkeep.Fuel(Tuning) / machine.Upkeep.Pace(Tuning);   // a worn machine burns more for the same work
            machine.FuelUsed += litres; Burn(litres);
        }

        internal static string MachineName(ForestMachine machine) =>
            (machine.Kind == ForestMachineKind.Harvester ? "A processzor #" : "A forwarder #") + machine.Id;

        // Puts part of the load down at the tile it stands on.
        private bool UnloadStep(ForestMachine machine, int destination, double dt)
        {
            machine.WorkTime += dt;
            float amount = Math.Min(machine.Cargo, (float)machine.Rule("unloading.amount", dt));
            double value = machine.Cargo > 0 ? machine.CargoValue * amount / machine.Cargo : 0;
            machine.Cargo -= amount; machine.CargoValue -= value;
            Receive(destination, amount, value);
            Fuel(machine, machine.Rule("machine.fuel", dt));
            if (machine.Cargo > 0.0001f) return false;
            machine.Cargo = 0; machine.CargoValue = 0;
            return true;
        }

        private IEnumerable<int> UpdateProcessorWork(ForestMachine machine, double dt)
        {
            if (machine.State == ForestMachineState.Felling)
            {
                machine.WorkTime += dt;
                int tile = machine.Tile;
                if (forest.TryGetStand(tile, out var stand)) machine.UnitPrice = TimberPrice(stand.Species);
                double price = machine.UnitPrice > 0 ? machine.UnitPrice : TimberPrice(ForestSpecies.None);
                var extracted = new WorkResult<float>();
                foreach (int unit in forest.ExtractTimberWork(tile, Math.Min((float)machine.Rule("machine.felling", dt, forest.AvailableTimber(tile)), Math.Max(0, machine.Capacity - machine.Cargo)), extracted)) yield return unit;
                float cut = extracted.Value;
                machine.Cargo += cut; machine.CargoValue += cut * price;
                Fuel(machine, machine.Rule("machine.fuel", dt));
            }
            else if (machine.State == ForestMachineState.Unloading)
            {
                if (!UnloadStep(machine, machine.Tile, dt)) yield break;
            }
            // The project decides whether to continue locally or request a topology query.
            var operation = machine.Workflow(MachineWorkflowStage.BeforeWorkSearch, dt, forest.AvailableTimber(machine.Tile));
            if (operation == MachineWorkflowOperation.Continue) yield break;
            var timberResult = new WorkResult<int[]>();
            if (operation is MachineWorkflowOperation.GoToWork or MachineWorkflowOperation.SearchForWork) foreach (int unit in FindPathWork(null, machine.Tile, t => ContainsTile(t) && forest.AvailableTimber(t) > .001f, timberResult, agent: machine)) yield return unit;
            int[] timber = timberResult.Value;
            machine.HasWork = timber != null;
            if (operation == MachineWorkflowOperation.SearchForWork) operation = machine.Workflow(MachineWorkflowStage.AfterWorkSearch, dt, reachable: timber != null);
            if (operation == MachineWorkflowOperation.Deliver)
            {
                var stackResult = new WorkResult<int[]>();
                foreach (int unit in FindPathWork(null, machine.Tile, t => t == machine.Target.Tile, stackResult, agent: machine)) yield return unit;
                int[] toStack = stackResult.Value;
                if (toStack != null) { Drive(machine, toStack, ForestMachineState.Unloading); yield break; }
                Status = $"{MachineName(machine)} nem jut vissza a sarangjához.";
                machine.State = ForestMachineState.Parked;
                yield break;
            }
            if (operation == MachineWorkflowOperation.ReturnHome) { foreach (int unit in GoHomeWork(machine)) yield return unit; }
            else if (operation == MachineWorkflowOperation.GoToWork && timber != null) Drive(machine, timber, ForestMachineState.Felling);
            else if (operation != MachineWorkflowOperation.Continue) machine.State = ForestMachineState.Parked;
        }

        private IEnumerable<int> UpdateForwarderWork(ForestMachine machine, double dt, Dictionary<int, List<SupplyEdge>> supply)
        {
            var source = machine.Source;
            if (machine.State is ForestMachineState.Loading or ForestMachineState.Unloading)
            {
                int transferTile = machine.State == ForestMachineState.Loading ? source.Tile : machine.Destination;
                if (!terrain.IsNetworkTile(machine.Tile) || !DestinationDocks(transferTile).Contains(machine.Tile)) {
                    Status = "A forwarder rakodóhelye nem elérhető; a faanyag a gépnél marad."; yield break;
                }
                bool done = ForwarderLoading.Step(machine, dt, (amount, value) => Receive(machine.Destination, amount, value));
                Fuel(machine, machine.Rule("machine.fuel", dt));
                if (!done) yield break;
            }
            var fedResult = new WorkResult<bool>();
            foreach (int unit in BeingFedWork(source, fedResult, supply)) yield return unit;
            machine.SourceFed = fedResult.Value;
            var operation = machine.Workflow(MachineWorkflowStage.AfterWorkSearch, dt);
            if (operation is MachineWorkflowOperation.Deliver or MachineWorkflowOperation.GoToWork)
            {
                bool delivering = operation == MachineWorkflowOperation.Deliver;
                int destination = delivering ? machine.Destination : source.Tile;
                var goal = delivering ? ForestMachineState.Unloading : ForestMachineState.Loading;
                var result = new WorkResult<int[]>();
                foreach (int unit in ForwarderPathWork(machine, machine.Tile, destination, result)) yield return unit;
                if (result.Value != null) Drive(machine, result.Value, goal);
                else machine.State = ForestMachineState.Parked;
                yield break;
            }
            if (operation == MachineWorkflowOperation.ReturnHome) { foreach (int unit in GoHomeWork(machine)) yield return unit; }
            else if (operation != MachineWorkflowOperation.Continue) machine.State = ForestMachineState.Parked;
        }
    }
}
