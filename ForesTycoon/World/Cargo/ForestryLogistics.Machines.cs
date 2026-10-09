using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    internal enum ForestMachineKind : byte { Harvester, Forwarder }

    internal enum ForestMachineState : byte { Parked, Driving, Felling, Loading, Unloading }

    /// <summary>
    /// A harvester or a forwarder working one harvest site. It moves tile by tile along a path over the site's skid
    /// trails and felling tiles; <see cref="PathPosition"/> runs from 0 (first tile) to the last path index.
    /// </summary>
    internal sealed class ForestMachine
    {
        internal const float ForwarderCapacity = 14f;
        internal int Id;
        internal ForestMachineKind Kind;
        /// <summary>The site it works; null while at (or on the way to) its depot.</summary>
        internal HarvestSite Site;
        /// <summary>The depot it belongs to; null for machines of old saves, which appear at a site and leave when done.</summary>
        internal Depot Home;
        /// <summary>The player called it home: it finishes the present move and drives back to the depot.</summary>
        internal bool HomeRequested;
        internal int[] Path;
        internal double PathPosition, PreviousPathPosition;
        internal ForestMachineState State;
        /// <summary>What the machine does on reaching the end of its path.</summary>
        internal ForestMachineState Goal;
        internal float Cargo;
        /// <summary>Seconds spent in the present work state; drives crane and saw animation.</summary>
        internal double WorkTime;
        internal int Tile => Path[Math.Clamp((int)Math.Round(PathPosition), 0, Path.Length - 1)];
        internal bool Arrived => PathPosition >= Path.Length - 1 - 1e-9;
        internal float CargoFill => Kind == ForestMachineKind.Forwarder ? Cargo / ForwarderCapacity : 0;
    }

    /// <summary>
    /// Cut-to-length harvesting: the harvester fells and processes the trees of the site and leaves the logs in piles
    /// beside its track; the forwarder carries the piles along the skid trail to the landing at the road, where the
    /// trucks load. Machines reach the felling only over marked skid trails (or straight from an adjacent road).
    /// </summary>
    internal sealed partial class ForestryLogistics
    {
        internal const float FellingRate = 1.2f, ForwarderLoadRate = 2.5f, ForwarderUnloadRate = 3.5f;
        internal const float HarvesterSpeed = 0.45f, ForwarderSpeed = 0.6f, ForwarderLoadedSpeed = 0.4f;
        internal readonly List<ForestMachine> Machines = new();
        private int nextMachineId = 1;
        /// <summary>When set, timber reaches the trucks only through the harvester → forwarder → landing chain.</summary>
        internal bool MachinesEnabled;
        /// <summary>Old behaviour: machines appear at every connected site by themselves (no depot, no orders).</summary>
        internal bool AutoMachines;

        private HarvestSite SiteOf(Vehicle vehicle)
        {
            foreach (var site in Sites)
                if (vehicle.SourceTiles != null && site.Tiles.Length > 0 && vehicle.SourceTiles.Length > 0 && site.Tiles[0] == vehicle.SourceTiles[0])
                    return site;
            return null;
        }

        // Forest floor a machine may cross: its site, the skid trails.
        private bool OffRoad(HarvestSite site, int tile) => (site != null && Array.IndexOf(site.Tiles, tile) >= 0) || terrain.IsSkidTrail(tile);

        // Machines drive on their own wheels: over roads, out of their depot yard, along trails into their site.
        private bool Passable(HarvestSite site, int tile) =>
            OffRoad(site, tile) || terrain.IsRoadTile(tile) || Depots.Exists(d => Array.IndexOf(d.Footprint, tile) >= 0);

        private bool TouchesRoad(int tile)
        {
            Span<int> next = stackalloc int[4];
            int count = terrain.GetTileNeighbours(tile, next);
            for (int i = 0; i < count; i++) if (terrain.IsRoadTile(next[i])) return true;
            return false;
        }

        /// <summary>
        /// The landing: the nearest tile, reachable from the felling over trails and site tiles, that touches a road.
        /// The forwarder unloads there and the trucks load beside it. -1 when the site is not connected.
        /// </summary>
        internal int FindLanding(HarvestSite site)
        {
            var queue = new Queue<int>(); var seen = new HashSet<int>();
            var starts = (int[])site.Tiles.Clone(); Array.Sort(starts);
            foreach (int id in starts) { queue.Enqueue(id); seen.Add(id); }
            Span<int> next = stackalloc int[4];
            while (queue.Count > 0)
            {
                int tile = queue.Dequeue();
                if (TouchesRoad(tile)) return tile;
                int count = terrain.GetTileNeighbours(tile, next);
                for (int i = 0; i < count; i++)
                    if (OffRoad(site, next[i]) && seen.Add(next[i])) queue.Enqueue(next[i]);
            }
            return -1;
        }

        /// <summary>Shortest path over the site's passable tiles from <paramref name="from"/> to the first tile that satisfies <paramref name="goal"/>.</summary>
        private int[] FindPath(HarvestSite site, int from, Func<int, bool> goal)
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
                    if (Passable(site, next[i]) && !previous.ContainsKey(next[i])) { previous[next[i]] = tile; queue.Enqueue(next[i]); }
            }
            return null;
        }

        /// <summary>Skid trails were marked, removed or grew over: landings are found again and machines join connected sites.</summary>
        internal void TrailsChanged()
        {
            if (!MachinesEnabled) return;
            foreach (var site in Sites) { site.Landing = FindLanding(site); site.StackTiles.Clear(); }
            SpawnMachines();
        }

        /// <summary>Sends a machine to work a site (an order from the player). False with a reason in <see cref="Status"/>.</summary>
        internal bool AssignMachine(ForestMachine machine, HarvestSite site)
        {
            string name = machine.Kind == ForestMachineKind.Harvester ? "A processzor" : "A forwarder";
            if (site.Landing < 0) { Status = $"{name} nem jut be: jelölj ki közelítő nyomot az úttól a vágásig."; return false; }
            if (Volume(site) <= 0.001f) { Status = "Ez a vágás már kiürült."; return false; }
            if (FindPath(site, machine.Tile, t => t == site.Landing) == null) { Status = $"{name} nem talál utat a telephelyről a vágáshoz."; return false; }
            machine.Site = site; machine.HomeRequested = false;
            if (machine.State != ForestMachineState.Driving) machine.State = ForestMachineState.Parked;
            else machine.Goal = ForestMachineState.Parked;   // finish the present move, then take up the new work
            Status = $"{name} a vágásba indult.";
            return true;
        }

        private bool GoHome(ForestMachine machine)
        {
            if (machine.Home == null) return false;
            machine.HomeRequested = false;
            var site = machine.Site;
            machine.Site = null;
            int[] path = FindPath(site, machine.Tile, t => t == machine.Home.TileId);
            if (path != null) Drive(machine, path, ForestMachineState.Parked);
            else machine.State = ForestMachineState.Parked;
            return true;
        }

        private void SpawnMachines()
        {
            if (!AutoMachines) return;
            foreach (var site in Sites)
            {
                if (site.Landing < 0 || Volume(site) <= 0.001f || Machines.Exists(m => m.Site == site)) continue;
                foreach (var kind in new[] { ForestMachineKind.Harvester, ForestMachineKind.Forwarder })
                    Machines.Add(new ForestMachine { Id = nextMachineId++, Kind = kind, Site = site, Path = new[] { site.Landing } });
            }
        }

        private float StandingTimber(HarvestSite site)
        {
            float sum = 0;
            foreach (int id in site.Tiles) sum += forest.AvailableTimber(id);
            return sum;
        }

        private static float Piled(HarvestSite site)
        {
            float sum = 0;
            foreach (float v in site.Piles.Values) sum += v;
            return sum;
        }

        /// <summary>Where the logs felled on <paramref name="tile"/> are stacked: the nearest skid-trail tile (within three
        /// tiles), else the felling tile itself.</summary>
        internal int StackTileFor(HarvestSite site, int tile)
        {
            if (site.StackTiles.TryGetValue(tile, out int stack)) return stack;
            int[] path = terrain.IsSkidTrail(tile) ? null : FindPath(site, tile, t => terrain.IsSkidTrail(t));
            stack = path != null && path.Length <= 4 ? path[^1] : tile;
            site.StackTiles[tile] = stack;
            return stack;
        }

        private void UpdateMachines(double seconds)
        {
            while (seconds > 1e-9)
            {
                double dt = Math.Min(seconds, 1.0 / 30); seconds -= dt;
                for (int i = 0; i < Machines.Count; i++)
                {
                    var machine = Machines[i];
                    machine.PreviousPathPosition = machine.PathPosition;
                    if (machine.Kind == ForestMachineKind.Harvester) UpdateHarvester(machine, dt);
                    else UpdateForwarder(machine, dt);
                }
                // Old saves: a finished site sends its depot-less machines away once they are parked back at the landing.
                Machines.RemoveAll(m => m.Home == null && m.Site != null && m.State == ForestMachineState.Parked &&
                    Volume(m.Site) <= 0.001f && m.Tile == m.Site.Landing);
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
            float speed = machine.Kind == ForestMachineKind.Harvester ? HarvesterSpeed
                : machine.Cargo > 0.5f ? ForwarderLoadedSpeed : ForwarderSpeed;
            int before = machine.Tile;
            machine.PathPosition = Math.Min(machine.Path.Length - 1, machine.PathPosition + speed * dt);
            int after = machine.Tile;
            // Each tile entered is one pass: a loaded forwarder cuts the deepest ruts.
            if (after != before)
                terrain.DriveSkidTrail(after, machine.Kind == ForestMachineKind.Harvester ? 0.05f : 0.04f + 0.06f * machine.CargoFill);
            if (machine.Arrived) { machine.State = machine.Goal; machine.WorkTime = 0; }
        }

        private void UpdateHarvester(ForestMachine machine, double dt)
        {
            var site = machine.Site;
            if (site == null) { if (machine.State == ForestMachineState.Driving) Advance(machine, dt); return; }
            switch (machine.State)
            {
                case ForestMachineState.Driving:
                    Advance(machine, dt);
                    break;
                case ForestMachineState.Felling:
                {
                    machine.WorkTime += dt;
                    int tile = machine.Tile;
                    float cut = forest.ExtractTimber(tile, (float)(FellingRate * dt));
                    if (cut > 0)
                    {
                        // The processor stacks the logs in a stack (sarang) beside the nearest skid trail for the forwarder.
                        int stack = StackTileFor(site, tile);
                        site.Piles[stack] = (site.Piles.TryGetValue(stack, out float pile) ? pile : 0) + cut;
                    }
                    if (forest.AvailableTimber(tile) > 0.001f && !machine.HomeRequested) break;
                    goto default;
                }
                default:
                {
                    // Done here (or called home): back to the depot.
                    if ((machine.HomeRequested || StandingTimber(site) <= 0.001f) && GoHome(machine)) break;
                    // Next standing timber, nearest first; with none left the harvester returns to the landing.
                    int[] path = StandingTimber(site) > 0.001f
                        ? FindPath(site, machine.Tile, t => Array.IndexOf(site.Tiles, t) >= 0 && forest.AvailableTimber(t) > 0.001f)
                        : null;
                    if (path != null) Drive(machine, path, ForestMachineState.Felling);
                    else if (site.Landing >= 0 && machine.Tile != site.Landing && FindPath(site, machine.Tile, t => t == site.Landing) is { } home)
                        Drive(machine, home, ForestMachineState.Parked);
                    else machine.State = ForestMachineState.Parked;
                    break;
                }
            }
        }

        private void UpdateForwarder(ForestMachine machine, double dt)
        {
            var site = machine.Site;
            if (site == null) { if (machine.State == ForestMachineState.Driving) Advance(machine, dt); return; }
            switch (machine.State)
            {
                case ForestMachineState.Driving:
                    Advance(machine, dt);
                    break;
                case ForestMachineState.Loading:
                {
                    machine.WorkTime += dt;
                    int tile = machine.Tile;
                    float pile = site.Piles.TryGetValue(tile, out float v) ? v : 0;
                    float take = Math.Min(Math.Min((float)(ForwarderLoadRate * dt), pile), ForestMachine.ForwarderCapacity - machine.Cargo);
                    machine.Cargo += take;
                    if (pile - take <= 0.0001f) site.Piles.Remove(tile); else site.Piles[tile] = pile - take;
                    if (machine.Cargo >= ForestMachine.ForwarderCapacity - 0.001f || machine.HomeRequested) { ReturnToLanding(machine); break; }
                    if (site.Piles.ContainsKey(tile)) break;
                    int[] next = FindPath(site, tile, t => site.Piles.ContainsKey(t));
                    if (next != null) Drive(machine, next, ForestMachineState.Loading);
                    else if (machine.Cargo > 0) ReturnToLanding(machine);
                    else machine.State = ForestMachineState.Parked;
                    break;
                }
                case ForestMachineState.Unloading:
                {
                    machine.WorkTime += dt;
                    float amount = Math.Min((float)(ForwarderUnloadRate * dt), machine.Cargo);
                    machine.Cargo -= amount; site.LandingStock += amount;
                    if (machine.Cargo <= 0.0001f) { site.LandingStock += machine.Cargo; machine.Cargo = 0; machine.State = ForestMachineState.Parked; }
                    break;
                }
                default:
                {
                    // Wait for a worthwhile load; take the rest once the harvester has finished.
                    float piled = Piled(site);
                    bool worth = piled >= ForestMachine.ForwarderCapacity * 0.5f || (piled > 0.01f && StandingTimber(site) <= 0.001f);
                    if (machine.Cargo > 0.01f) { ReturnToLanding(machine); break; }
                    // Every stack skidded and nothing left to fell (or called home): back to the depot.
                    if ((machine.HomeRequested || (piled <= 0.01f && StandingTimber(site) <= 0.001f)) && GoHome(machine)) break;
                    if (!worth) { if (machine.Tile != site.Landing && site.Landing >= 0) ReturnToLanding(machine); break; }
                    int[] path = FindPath(site, machine.Tile, t => site.Piles.ContainsKey(t));
                    if (path != null) Drive(machine, path, ForestMachineState.Loading);
                    break;
                }
            }
        }

        private void ReturnToLanding(ForestMachine machine)
        {
            var site = machine.Site;
            int[] path = site.Landing >= 0 ? FindPath(site, machine.Tile, t => t == site.Landing) : null;
            if (path != null) Drive(machine, path, machine.Cargo > 0 ? ForestMachineState.Unloading : ForestMachineState.Parked);
            else machine.State = ForestMachineState.Parked;
        }
    }
}
