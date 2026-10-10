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
        internal ForestMachineState State;
        /// <summary>What the machine does on reaching the end of its path.</summary>
        internal ForestMachineState Goal;
        internal float Cargo;
        internal double CargoValue;
        /// <summary>Seconds spent in the present work state; drives crane and saw animation.</summary>
        internal double WorkTime;
        /// <summary>Diesel burnt so far, litres.</summary>
        internal double FuelUsed;
        /// <summary>Processor: price of the wood it is felling (kept when the last standing tree of a tile is gone).</summary>
        internal double UnitPrice;
        internal VehicleUpkeep Upkeep = new(0);
        /// <summary>The world's tuning (capacities); set by the logistics.</summary>
        internal GameTuning Tuning = GameTuning.Default;
        internal int Tile => Path[Math.Clamp((int)Math.Round(PathPosition), 0, Path.Length - 1)];
        internal bool Arrived => PathPosition >= Path.Length - 1 - 1e-9;
        internal float Capacity => Tuning.F(Kind == ForestMachineKind.Forwarder ? Tune.ForwarderCapacity : Tune.ProcessorCapacity);
        internal float CargoFill => Cargo / Capacity;
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
        internal const float FellingRate = 1.2f, ForwarderLoadRate = 2.5f, ForwarderUnloadRate = 3.5f, ProcessorUnloadRate = 2f;
        internal const float HarvesterSpeed = 0.45f, ForwarderSpeed = 0.6f, ForwarderLoadedSpeed = 0.4f;
        /// <summary>Diesel use by activity, litres per second of game time.</summary>
        internal const double FuelFelling = 1.0, FuelDriving = 0.5, FuelDrivingLoaded = 0.8, FuelCrane = 0.35;
        internal readonly List<ForestMachine> Machines = new();
        private GameTuning tuning = GameTuning.Default;
        /// <summary>The world's tunable numbers; the machines read their capacities from it.</summary>
        internal GameTuning Tuning
        {
            get => tuning;
            set { tuning = value ?? GameTuning.Default; foreach (var machine in Machines) machine.Tuning = tuning; }
        }
        private int nextMachineId = 1;
        /// <summary>When set, timber reaches the mills only through the machines and stacks (no direct loading from the forest).</summary>
        internal bool MachinesEnabled;

        // Where machines may drive: the network, their felling, stack sites, depot and mill yards.
        private bool Passable(HarvestSite site, int tile) =>
            terrain.IsNetworkTile(tile) || ContainsTile(tile) || StackAt(tile) != null
            || Depots.Exists(d => Array.IndexOf(d.Footprint, tile) >= 0) || Mills.Exists(m => Array.IndexOf(m.Footprint, tile) >= 0);

        // Network-to-network travel follows its actual arms. Worksites and yards still allow manoeuvring.
        private bool CanDriveBetween(int from, int to) =>
            !terrain.IsNetworkTile(from) || !terrain.IsNetworkTile(to) || terrain.AreNetworkNeighbours(from, to);

        /// <summary>
        /// Shortest path over passable tiles from <paramref name="from"/> to the first tile that satisfies
        /// <paramref name="goal"/>; <paramref name="extra"/> is one more tile allowed (a new stack site).
        /// </summary>
        private int[] FindPath(HarvestSite site, int from, Func<int, bool> goal, int extra = -1)
        {
            var previous = new Dictionary<int, int> { [from] = -1 };
            var queue = new Queue<int>(); queue.Enqueue(from);
            Span<int> next = stackalloc int[4];
            while (queue.Count > 0)
            {
                int tile = queue.Dequeue();
                if (goal(tile))
                {
                    var path = new List<int>();
                    for (int t = tile; t >= 0; t = previous[t]) path.Add(t);
                    path.Reverse();
                    return path.ToArray();
                }
                int count = terrain.GetTileNeighbours(tile, next);
                for (int i = 0; i < count; i++)
                    if ((next[i] == extra || Passable(site, next[i])) && CanDriveBetween(tile, next[i]) && !previous.ContainsKey(next[i])) { previous[next[i]] = tile; queue.Enqueue(next[i]); }
            }
            return null;
        }

        /// <summary>Skid trails changed. Paths are searched as machines move, so nothing is cached to rebuild.</summary>
        internal void TrailsChanged() { }

        private float StandingTimber(HarvestSite site)
        {
            float sum = 0;
            foreach (int id in site.Tiles) sum += forest.AvailableTimber(id);
            return sum;
        }

        /// <summary>
        /// The processor's work: <paramref name="stack"/> is its receiving stack. It drives there, then fells the nearest
        /// reachable designated trees (any harvest site) and carries every load back to that stack.
        /// </summary>
        internal bool AssignProcessor(ForestMachine machine, TimberStack stack)
        {
            if (FindPath(null, machine.Tile, t => t == stack.Tile) == null) { Status = "A processzor nem jut el ehhez a saranghoz."; return false; }
            if (NearestTimber(stack.Tile) == null)
            { Status = "A sarangtól nem érhető el kijelölt fa: jelölj ki kitermelést, és kösd nyommal a saranghoz."; return false; }
            Release(machine);
            machine.Target = stack; machine.HasWork = true;
            Restart(machine);
            Status = $"A processzor a #{stack.Id} saranghoz indult, onnan termel.";
            return true;
        }

        /// <summary>Path to the nearest designated tile with standing timber, over network, trails, stacks and fellings.</summary>
        private int[] NearestTimber(int from) =>
            FindPath(null, from, t => ContainsTile(t) && forest.AvailableTimber(t) > 0.001f);

        /// <summary>The forwarder's work: carry <paramref name="source"/> to <paramref name="destination"/> until it is empty.</summary>
        internal bool AssignForwarder(ForestMachine machine, TimberStack source, int destination)
        {
            if (destination == source.Tile) { Status = "A cél nem lehet maga a forrás sarang."; return false; }
            bool mill = MillAt(destination) != null;
            if (!mill && StackAt(destination) == null && !CanPlaceStack(destination)) { Status = "Ide nem rakható sarang."; return false; }
            if (FindPath(null, machine.Tile, t => t == source.Tile) == null) { Status = "A forwarder nem talál utat a forrás saranghoz."; return false; }
            if (FindPath(null, source.Tile, t => t == destination, destination) == null) { Status = "A forrás sarangtól nem vezet út a célig."; return false; }
            Release(machine);
            machine.Source = source; machine.Destination = destination;
            Restart(machine);
            Status = mill ? "A forwarder a malomba hordja a sarangot." : "A forwarder áthordja a sarangot.";
            return true;
        }

        private static void Release(ForestMachine machine)
        {
            machine.Site = null; machine.Source = null; machine.Target = null; machine.Destination = -1; machine.HomeRequested = false;
        }

        private static void Restart(ForestMachine machine)
        {
            if (machine.State != ForestMachineState.Driving) machine.State = ForestMachineState.Parked;
            else machine.Goal = ForestMachineState.Parked;   // finish the present move, then take up the new work
        }

        /// <summary>The stack site nearest (by path) to a tile of the felling, or null.</summary>
        private TimberStack NearestStack(HarvestSite site, int from)
        {
            int[] path = FindPath(site, from, t => StackAt(t) != null);
            return path == null ? null : StackAt(path[^1]);
        }

        /// <summary>Is wood still on its way to this stack (a processor, forwarder or truck will bring more)?</summary>
        internal bool BeingFed(TimberStack stack, int depth = 0)
        {
            if (depth > 4) return false;
            foreach (var m in Machines)
            {
                if (m.Kind == ForestMachineKind.Harvester && m.Target == stack && (m.Cargo > 0 || m.HasWork)) return true;
                if (m.Kind == ForestMachineKind.Forwarder && m.Destination == stack.Tile && m.Source != null &&
                    (m.Cargo > 0 || m.Source.Volume > 0.01f || BeingFed(m.Source, depth + 1))) return true;
            }
            foreach (var t in Trucks)
                if (t.Target == stack && t.Source != null &&
                    ((t.Vehicle?.CargoAmount ?? 0) > 0 || t.Source.Volume > 0.01f || BeingFed(t.Source, depth + 1))) return true;
            return false;
        }

        /// <summary>Calls a machine back to its depot: it puts down what it carries, then drives home.</summary>
        internal void SendHome(ForestMachine machine)
        {
            if (!machine.Working) return;
            machine.HomeRequested = true;
            Status = "Hazahívva: leteszi, ami nála van, és visszamegy a telephelyre.";
        }

        private void GoHome(ForestMachine machine)
        {
            var site = machine.Site;
            Release(machine);
            int[] path = machine.Home == null ? null : FindPath(site, machine.Tile, t => t == machine.Home.TileId);
            if (path != null) Drive(machine, path, ForestMachineState.Parked);
            else machine.State = ForestMachineState.Parked;
        }

        private void UpdateMachines(double seconds)
        {
            // Drawing interpolates across the whole tick (all substeps); a new path resets the start to 0.
            foreach (var machine in Machines) machine.PreviousPathPosition = machine.PathPosition;
            while (seconds > 1e-9)
            {
                double dt = Math.Min(seconds, 1.0 / 30); seconds -= dt;
                for (int i = 0; i < Machines.Count; i++)
                {
                    var machine = Machines[i];
                    bool atHome = !machine.Working && machine.State == ForestMachineState.Parked;
                    if (atHome) { RunningCosts += machine.Upkeep.Service(dt, Tuning); continue; }
                    // Wear and breakdowns: rough trails and loads strain the machine; broken, it waits for the mechanic.
                    bool rough = terrain.IsSkidTrail(machine.Tile) || (machine.Site != null && Array.IndexOf(machine.Site.Tiles, machine.Tile) >= 0);
                    float strain = (rough ? 1.5f : 1f) * (1 + 0.5f * machine.CargoFill) * (machine.State == ForestMachineState.Felling ? 1.3f : 1f);
                    bool running = machine.Upkeep.Operate(dt, strain, out double repair, Tuning);
                    if (repair > 0) { RunningCosts += repair; Status = $"{MachineName(machine)} megjavítva ({repair:N0} eFt)."; }
                    if (!running)
                    {
                        if (machine.Upkeep.RepairLeft >= Tuning[Tune.RepairSeconds] - dt) Status = $"{MachineName(machine)} elromlott: a szerelő úton van.";
                        continue;
                    }
                    double pace = machine.Upkeep.Pace(Tuning) * dt;
                    if (machine.State == ForestMachineState.Driving) { Advance(machine, pace); continue; }
                    if (!machine.Working) continue;
                    dt = pace;
                    if (machine.Kind == ForestMachineKind.Harvester) UpdateProcessor(machine, dt);
                    else UpdateForwarder(machine, dt);
                }
            }
        }

        private void Drive(ForestMachine machine, int[] path, ForestMachineState goal)
        {
            machine.Path = path; machine.PathPosition = machine.PreviousPathPosition = 0;
            machine.Goal = goal; machine.WorkTime = 0;
            machine.State = path.Length > 1 ? ForestMachineState.Driving : goal;
        }

        private void Advance(ForestMachine machine, double dt)
        {
            int segment = Math.Min((int)Math.Floor(machine.PathPosition), machine.Path.Length - 2);
            if (segment >= 0 && ((!Passable(machine.Site, machine.Path[segment + 1]) && machine.Path[segment + 1] != machine.Destination) || !CanDriveBetween(machine.Path[segment], machine.Path[segment + 1])))
            {
                Status = "Az útvonal megszakadt. Állítsd helyre az úthálózatot a gép továbbhaladásához.";
                return;
            }
            float speed = Tuning.F(machine.Kind == ForestMachineKind.Harvester ? Tune.HarvesterSpeed
                : machine.Cargo > 0.5f ? Tune.ForwarderLoadedSpeed : Tune.ForwarderSpeed);
            Fuel(machine, Tuning[machine.Cargo > 0.5f ? Tune.FuelDrivingLoaded : Tune.FuelDriving] * dt);
            int before = machine.Tile;
            machine.PathPosition = Math.Min(machine.Path.Length - 1, machine.PathPosition + speed * dt);
            int after = machine.Tile;
            // Each tile entered is one pass: a loaded machine cuts the deepest ruts.
            if (after != before) terrain.DriveSkidTrail(after, 0.04f + 0.06f * machine.CargoFill);
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
        private bool UnloadStep(ForestMachine machine, float rate, int destination, double dt)
        {
            machine.WorkTime += dt;
            float amount = Math.Min((float)(rate * dt), machine.Cargo);
            double value = machine.Cargo > 0 ? machine.CargoValue * amount / machine.Cargo : 0;
            machine.Cargo -= amount; machine.CargoValue -= value;
            Receive(destination, amount, value);
            Fuel(machine, Tuning[Tune.FuelCrane] * dt);
            if (machine.Cargo > 0.0001f) return false;
            machine.Cargo = 0; machine.CargoValue = 0;
            return true;
        }

        private void UpdateProcessor(ForestMachine machine, double dt)
        {
            if (machine.State == ForestMachineState.Felling)
            {
                machine.WorkTime += dt;
                int tile = machine.Tile;
                if (forest.TryGetStand(tile, out var stand)) machine.UnitPrice = TimberPrice(stand.Species);
                double price = machine.UnitPrice > 0 ? machine.UnitPrice : TimberPrice(ForestSpecies.None);
                float cut = forest.ExtractTimber(tile, Math.Min((float)(Tuning[Tune.FellingRate] * dt), machine.Capacity - machine.Cargo));
                machine.Cargo += cut; machine.CargoValue += cut * price;
                Fuel(machine, Tuning[Tune.FuelFelling] * dt);
                if (machine.Cargo < machine.Capacity - 0.001f && forest.AvailableTimber(tile) > 0.001f && !machine.HomeRequested) return;
            }
            else if (machine.State == ForestMachineState.Unloading)
            {
                if (!UnloadStep(machine, Tuning.F(Tune.ProcessorUnloadRate), machine.Tile, dt)) return;
            }
            // Decide: a full grapple (or nothing more to fell, or called home) goes to the stack; empty, it fells on or goes home.
            int[] timber = machine.HomeRequested ? null : NearestTimber(machine.Tile);
            machine.HasWork = timber != null;
            if (machine.Cargo > 0.001f && (timber == null || machine.Cargo >= machine.Capacity - 0.001f))
            {
                int[] toStack = FindPath(null, machine.Tile, t => t == machine.Target.Tile);
                if (toStack != null) { Drive(machine, toStack, ForestMachineState.Unloading); return; }
                Status = $"{MachineName(machine)} nem jut vissza a sarangjához.";
                machine.State = ForestMachineState.Parked;
                return;
            }
            if (timber == null) { GoHome(machine); return; }
            Drive(machine, timber, ForestMachineState.Felling);
        }

        private void UpdateForwarder(ForestMachine machine, double dt)
        {
            var source = machine.Source;
            if (machine.State == ForestMachineState.Loading)
            {
                machine.WorkTime += dt;
                var (taken, value) = source.Take(Math.Min((float)(Tuning[Tune.ForwarderLoadRate] * dt), machine.Capacity - machine.Cargo));
                machine.Cargo += taken; machine.CargoValue += value;
                Fuel(machine, Tuning[Tune.FuelCrane] * dt);
                if (machine.Cargo < machine.Capacity - 0.001f && source.Volume > 0.0001f && !machine.HomeRequested) return;
            }
            else if (machine.State == ForestMachineState.Unloading)
            {
                if (!UnloadStep(machine, Tuning.F(Tune.ForwarderUnloadRate), machine.Destination, dt)) return;
            }
            // Decide: loaded → to the destination; empty → fetch a worthwhile load, the rest once nothing more comes, or go home.
            if (machine.Cargo > 0.01f)
            {
                int[] path = FindPath(null, machine.Tile, t => t == machine.Destination, machine.Destination);
                if (path != null) Drive(machine, path, ForestMachineState.Unloading); else machine.State = ForestMachineState.Parked;
                return;
            }
            if (machine.HomeRequested) { GoHome(machine); return; }
            bool fed = BeingFed(source);
            if (source.Volume >= machine.Capacity * 0.5f || (source.Volume > 0.01f && !fed))
            {
                int[] back = FindPath(null, machine.Tile, t => t == source.Tile);
                if (back != null) Drive(machine, back, ForestMachineState.Loading); else machine.State = ForestMachineState.Parked;
                return;
            }
            if (!fed) GoHome(machine);
            else machine.State = ForestMachineState.Parked;
        }
    }
}
