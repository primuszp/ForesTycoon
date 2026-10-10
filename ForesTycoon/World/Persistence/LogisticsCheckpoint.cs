using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon
{
    internal sealed partial class ForestryLogistics
    {
        internal LogisticsCheckpoint Capture() => new(
            Sites.Select(s => new HarvestCheckpoint((int[])s.Tiles.Clone(), s.InitialVolume)).ToArray(),
            Mills.Select(m => new MillCheckpoint(m.TileId, (int[])m.Footprint.Clone(), new(m.Position), m.Received, m.Stock, m.Processed)).ToArray(), Status,
            Machines.Select(m => new ForestMachineCheckpoint(m.Id, m.Kind, m.Site == null ? -1 : Sites.IndexOf(m.Site), (int[])m.Path.Clone(), m.PathPosition,
                m.PreviousPathPosition, m.State, m.Goal, m.Cargo, m.WorkTime, m.Home == null ? -1 : Depots.IndexOf(m.Home), m.HomeRequested,
                m.Source?.Id ?? -1, m.Target?.Id ?? -1, m.Destination, m.CargoValue, m.FuelUsed, m.UnitPrice, Capture(m.Upkeep), m.HasWork, m.LogTransferVolume, m.LogTransferValue, m.LogCycleDuration, m.LogGripPhase, m.LogReleasePhase)).ToArray(), nextMachineId,
            Depots.Select(d => new DepotCheckpoint(d.TileId, (int[])d.Footprint.Clone(), new(d.Position))).ToArray(),
            Trucks.Select(t => new TruckCheckpoint(t.Id, Depots.IndexOf(t.Home), t.Phase, t.Vehicle?.Id ?? -1, t.Source?.Id ?? -1, t.Destination,
                t.HomeRequested, t.Target?.Id ?? -1, t.CargoValue, t.FuelCharged, Capture(t.Upkeep))).ToArray(), nextTruckId,
            Stacks.Select(s => new StackCheckpoint(s.Id, s.Tile, s.Volume, s.Value)).ToArray(), nextStackId, Income, RunningCosts);

        internal void Restore(LogisticsCheckpoint s)
        {
            CheckpointGuard.Require(s != null && s.Sites != null && s.Mills != null && s.Status != null, "logistics system");
            var used = new HashSet<int>(); var sites = new List<HarvestSite>(); var mills = new List<Sawmill>();
            int nextStack = Math.Max(1, s.NextStackId);
            var stacks = new List<TimberStack>(); var stackIds = new Dictionary<int, TimberStack>(); var stackTiles = new HashSet<int>();
            void AddStack(TimberStack stack, string what)
            {
                CheckpointGuard.Require(terrain.IsValidTileId(stack.Tile) && stackTiles.Add(stack.Tile) && stackIds.TryAdd(stack.Id, stack), what);
                CheckpointGuard.NonNegative(stack.Volume, what); CheckpointGuard.NonNegative(stack.Value, what);
                stacks.Add(stack);
            }
            foreach (var st in s.Stacks ?? Array.Empty<StackCheckpoint>())
            {
                CheckpointGuard.Require(st != null && st.Id > 0 && st.Id < nextStack, "timber stack");
                AddStack(new TimberStack { Id = st.Id, Tile = st.Tile, Volume = st.Volume, Value = st.Value }, "timber stack");
            }
            foreach (var site in s.Sites)
            {
                CheckpointGuard.Require(site != null && site.Tiles != null && site.Tiles.Length > 0, "harvest site");
                foreach (int id in site.Tiles) CheckpointGuard.Require(terrain.IsValidTileId(id) && used.Add(id), "harvest tile");
                CheckpointGuard.NonNegative(site.InitialVolume, "harvest volume");
                sites.Add(new HarvestSite { Tiles = (int[])site.Tiles.Clone(), InitialVolume = site.InitialVolume });
                // Older saves kept felled wood as piles and a landing stock: they become stacks.
                foreach (var pile in site.Piles ?? Array.Empty<PileCheckpoint>())
                    if (pile != null && pile.Volume > 0 && !stackTiles.Contains(pile.Tile))
                        AddStack(new TimberStack { Id = nextStack++, Tile = pile.Tile, Volume = pile.Volume, Value = pile.Volume * TimberPrice(ForestSpecies.Spruce) }, "log pile");
                if (site.LandingStock > 0 && site.Landing >= 0 && !stackTiles.Contains(site.Landing))
                    AddStack(new TimberStack { Id = nextStack++, Tile = site.Landing, Volume = site.LandingStock,
                        Value = site.LandingStock * TimberPrice(ForestSpecies.Spruce) }, "landing stock");
            }
            var footprints = new HashSet<int>(); var destinations = new HashSet<int>();
            foreach (var m in s.Mills)
            {
                CheckpointGuard.Require(m != null && terrain.IsValidTileId(m.TileId) && destinations.Add(m.TileId) &&
                    m.Footprint != null && m.Footprint.Length == 4 && Array.IndexOf(m.Footprint, m.TileId) >= 0, "sawmill footprint");
                foreach (int id in m.Footprint) CheckpointGuard.Require(terrain.IsValidTileId(id) && terrain.IsBuildingTile(id) &&
                    footprints.Add(id) && !used.Contains(id), "sawmill tile");
                m.Position.Validate(); CheckpointGuard.NonNegative(m.Received, "mill received");
                CheckpointGuard.NonNegative(m.Stock, "mill stock"); CheckpointGuard.NonNegative(m.Processed, "mill processed");
                CheckpointGuard.Require(Math.Abs(m.Received - m.Stock - m.Processed) <= Math.Max(1, m.Received) * 1e-5, "mill mass balance");
                mills.Add(new Sawmill { TileId = m.TileId, Footprint = (int[])m.Footprint.Clone(), Position = m.Position.Vector,
                    Received = m.Received, Stock = m.Stock, Processed = m.Processed });
            }
            var depots = new List<Depot>();
            foreach (var d in s.Depots ?? Array.Empty<DepotCheckpoint>())
            {
                CheckpointGuard.Require(d != null && terrain.IsValidTileId(d.TileId) && d.Footprint != null && d.Footprint.Length == 4 &&
                    Array.IndexOf(d.Footprint, d.TileId) >= 0, "depot footprint");
                foreach (int id in d.Footprint) CheckpointGuard.Require(terrain.IsValidTileId(id) && terrain.IsBuildingTile(id) &&
                    footprints.Add(id) && !used.Contains(id), "depot tile");
                d.Position.Validate();
                depots.Add(new Depot { TileId = d.TileId, Footprint = (int[])d.Footprint.Clone(), Position = d.Position.Vector });
            }
            TimberStack Stack(int id) => id < 0 ? null : stackIds.TryGetValue(id, out var st) ? st : throw new System.IO.InvalidDataException("Invalid world checkpoint: stack reference.");
            var trucks = new List<FleetTruck>(); var truckIds = new HashSet<int>(); var pending = new List<(FleetTruck, int)>();
            foreach (var t in s.Trucks ?? Array.Empty<TruckCheckpoint>())
            {
                CheckpointGuard.Require(t != null && t.Id > 0 && t.Id < s.NextTruckId && truckIds.Add(t.Id) && (uint)t.Home < (uint)depots.Count &&
                    Enum.IsDefined(t.Phase) && (t.Phase == TruckPhase.Parked) == (t.Vehicle < 0) &&
                    (t.Phase == TruckPhase.Parked || (t.Source >= 0 && terrain.IsValidTileId(t.Destination))), "fleet truck");
                CheckpointGuard.NonNegative(t.CargoValue, "truck cargo value"); CheckpointGuard.NonNegative(t.FuelCharged, "truck fuel");
                var truck = new FleetTruck { Id = t.Id, Home = depots[t.Home], Phase = t.Phase, Source = Stack(t.Source), Destination = t.Destination,
                    Target = Stack(t.Target), HomeRequested = t.HomeRequested, CargoValue = t.CargoValue, FuelCharged = t.FuelCharged,
                    Upkeep = Restore(t.Upkeep, (uint)t.Id * 2246822519u + 1) };
                trucks.Add(truck);
                if (t.Vehicle >= 0) pending.Add((truck, t.Vehicle));
            }
            CheckpointGuard.Require(s.NextTruckId >= 1 && s.NextMachineId >= 1, "fleet ids");
            var machines = new List<ForestMachine>(); var ids = new HashSet<int>();
            foreach (var m in s.Machines ?? Array.Empty<ForestMachineCheckpoint>())
            {
                CheckpointGuard.Require(m != null && ids.Add(m.Id) && m.Id > 0 && m.Id < s.NextMachineId && Enum.IsDefined(m.Kind) &&
                    Enum.IsDefined(m.State) && Enum.IsDefined(m.Goal) && m.Site >= -1 && m.Site < sites.Count && m.Home >= -1 && m.Home < depots.Count &&
                    m.Path != null && m.Path.Length > 0 && Array.TrueForAll(m.Path, terrain.IsValidTileId), "forest machine");
                CheckpointGuard.Require(double.IsFinite(m.Position) && m.Position >= 0 && m.Position <= m.Path.Length - 1 &&
                    double.IsFinite(m.PreviousPosition) && m.PreviousPosition >= 0 && m.PreviousPosition <= m.Path.Length - 1, "forest machine position");
                CheckpointGuard.NonNegative(m.Cargo, "forest machine cargo"); CheckpointGuard.NonNegative(m.WorkTime, "forest machine work time");
                CheckpointGuard.NonNegative(m.LogTransferVolume, "grapple volume"); CheckpointGuard.NonNegative(m.LogTransferValue, "grapple value"); CheckpointGuard.NonNegative(m.LogCycleDuration, "log cycle duration");
                CheckpointGuard.Require(double.IsFinite(m.LogGripPhase) && double.IsFinite(m.LogReleasePhase) && m.LogGripPhase >= .01 &&
                    m.LogReleasePhase > m.LogGripPhase && m.LogReleasePhase <= .99, "grapple timing");
                CheckpointGuard.Require(m.LogTransferVolume <= ForwarderLoading.LogVolume + .001f && (m.LogTransferVolume == 0 || (m.Kind == ForestMachineKind.Forwarder && m.LogCycleDuration > 0 && m.State is ForestMachineState.Loading or ForestMachineState.Unloading)), "grapple state");
                CheckpointGuard.NonNegative(m.CargoValue, "forest machine cargo value"); CheckpointGuard.NonNegative(m.FuelUsed, "forest machine fuel");
                CheckpointGuard.Require(m.Cargo <= GameTuning.Spec(Tune.ForwarderCapacity).Max + 0.001f && m.Destination >= -1 && m.Destination < terrain.Tiles.Count, "forest machine cargo");
                machines.Add(new ForestMachine { Id = m.Id, Kind = m.Kind, Site = m.Site < 0 ? null : sites[m.Site], Path = (int[])m.Path.Clone(),
                    Home = m.Home < 0 ? null : depots[m.Home], HomeRequested = m.HomeRequested, Source = Stack(m.Source), Target = Stack(m.Target),
                    Destination = m.Destination, PathPosition = m.Position, PreviousPathPosition = m.PreviousPosition, State = m.State, Goal = m.Goal,
                    Cargo = m.Cargo, CargoValue = m.CargoValue, WorkTime = m.WorkTime, FuelUsed = m.FuelUsed, UnitPrice = Math.Max(0, m.UnitPrice),
                    Upkeep = Restore(m.Upkeep, (uint)m.Id * 2654435761u), HasWork = m.HasWork, LogTransferVolume = m.LogTransferVolume, LogTransferValue = m.LogTransferValue, LogCycleDuration = m.LogCycleDuration,
                    LogGripPhase = m.LogGripPhase, LogReleasePhase = m.LogReleasePhase });
            }
            CheckpointGuard.NonNegative(s.Income, "income"); CheckpointGuard.NonNegative(s.RunningCosts, "running costs");
            Sites.Clear(); Sites.AddRange(sites); Mills.Clear(); Mills.AddRange(mills); Status = s.Status;
            Machines.Clear(); Machines.AddRange(machines); nextMachineId = s.NextMachineId;
            Depots.Clear(); Depots.AddRange(depots); Trucks.Clear(); Trucks.AddRange(trucks); nextTruckId = s.NextTruckId;
            Stacks.Clear(); Stacks.AddRange(stacks); nextStackId = nextStack; Income = s.Income; RunningCosts = s.RunningCosts;
            pendingTruckVehicles.Clear(); pendingTruckVehicles.AddRange(pending);
        }

        private static UpkeepCheckpoint Capture(VehicleUpkeep u) => new(u.Wear, u.Broken, u.RepairLeft, u.Seed, u.Breakdowns);

        private static VehicleUpkeep Restore(UpkeepCheckpoint u, uint seed)
        {
            if (u == null) return new VehicleUpkeep(seed);
            CheckpointGuard.Unit(u.Wear, "vehicle wear"); CheckpointGuard.NonNegative(u.RepairLeft, "vehicle repair");
            CheckpointGuard.Require(u.Breakdowns >= 0 && u.RepairLeft <= GameTuning.Spec(Tune.RepairSeconds).Max, "vehicle breakdowns");
            var restored = VehicleUpkeep.FromState(u.Seed);
            restored.Wear = u.Wear; restored.Broken = u.Broken; restored.RepairLeft = u.RepairLeft; restored.Breakdowns = u.Breakdowns;
            return restored;
        }

        // Fleet trucks refer to their road vehicles by id; vehicles are restored after the logistics.
        private readonly List<(FleetTruck Truck, int Vehicle)> pendingTruckVehicles = new();

        internal void BindVehicles(VehicleSystem vehicles)
        {
            Vehicles = vehicles;
            foreach (var (truck, id) in pendingTruckVehicles)
            {
                truck.Vehicle = null;
                foreach (var v in vehicles.Vehicles) if (v.Id == id) truck.Vehicle = v;
                CheckpointGuard.Require(truck.Vehicle != null && truck.Vehicle.Transit == (truck.Phase != TruckPhase.Working), "fleet truck vehicle");
            }
            pendingTruckVehicles.Clear();
        }
    }
}
