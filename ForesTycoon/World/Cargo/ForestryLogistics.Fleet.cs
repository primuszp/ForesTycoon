using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>The forestry depot: the machine yard where the fleet is kept and from where every vehicle sets out.</summary>
    internal sealed class Depot
    {
        internal int TileId;
        internal int[] Footprint;
        internal Vector3 Position;
    }

    internal enum TruckPhase : byte { Parked, ToWork, Working, ToHome }

    /// <summary>
    /// A log truck of the fleet. Parked at its depot it is no vehicle on the map; with an order it drives to its source
    /// stack (<see cref="TruckPhase.ToWork"/>), shuttles source → destination (<see cref="TruckPhase.Working"/>) and
    /// returns home when the source is cleared or the player calls it back.
    /// </summary>
    internal sealed class FleetTruck
    {
        internal int Id;
        internal Depot Home;
        internal TruckPhase Phase;
        internal Vehicle Vehicle;
        internal TimberStack Source;
        /// <summary>Destination tile: a sawmill tile, or a stack site.</summary>
        internal int Destination = -1;
        internal TimberStack Target;
        /// <summary>Value of the wood on the truck, thousand forints.</summary>
        internal double CargoValue;
        internal bool HomeRequested;
        /// <summary>Fuel already charged to the running costs, litres (the vehicle counts what it burns).</summary>
        internal double FuelCharged;
        internal VehicleUpkeep Upkeep = new(0);
        internal BehaviorAction Control;
    }

    internal sealed partial class ForestryLogistics
    {
        internal readonly List<Depot> Depots = new();
        internal readonly List<FleetTruck> Trucks = new();
        private int nextTruckId = 1;
        /// <summary>The road vehicles the trucks of the fleet run as; set by the world.</summary>
        private VehicleSystem vehicles;
        internal VehicleSystem Vehicles
        {
            get => vehicles;
            set {
                vehicles = value;
                if (value == null) return;
                value.BehaviorPause = v => { var truck = TruckOf(v); return truck != null && TruckWait(truck); };
                value.BehaviorRule = TruckRule;
                value.BehaviorDepart = null; // Departure is entirely evaluated by loading.depart, including controller requests.
                value.SourceStock = v => TruckOf(v)?.Source?.Volume ?? 0;
            }
        }

        /// <summary>Synchronous adapter for fixtures; interactive commands and scene generation consume the iterator.</summary>
        internal bool PlaceDepot(int id)
        {
            var result = new WorkResult<bool>(); SimulationWork.Complete(PlaceDepotWork(id, result)); return result.Value;
        }

        internal IEnumerable<int> PlaceDepotWork(int id, WorkResult<bool> result)
        {
            if (!terrain.TryGetSawmillFootprint(id, out var footprint, out var position)) {
                Status = "A telephelyhez 2×2 sík, száraz, szabad épülethely kell."; yield break;
            }
            bool trees = false, harvest = false;
            foreach (int tile in footprint) { trees |= forest.TryGetStand(tile, out _); yield return 1; }
            foreach (var site in Sites) {
                foreach (int tile in site.Tiles) {
                    harvest |= Array.IndexOf(footprint, tile) >= 0; yield return 1;
                    if (harvest) break;
                }
                if (harvest) break;
            }
            var inputs = new LogisticsInputs(SiteForest: trees ? 1 : 0, SiteHarvest: harvest ? 1 : 0,
                SiteRoadAccess: terrain.FindRoadDocks(footprint).Count > 0 ? 1 : 0, DepotCount: Depots.Count);
            double Read(string hook) => rules.Evaluate(Behaviors, hook, "depot", (ulong)id, inputs);
            if (Read("depot.allow") < .5) { Status = "A telephely építését a szabály nem engedélyezi (erdő, kitermelés vagy hiányzó útkapcsolat)."; yield break; }
            int Count(string hook) { double value = Read(hook); return double.IsFinite(value) ? (int)Math.Clamp(Math.Floor(value), 0, 1024) : 0; }
            int processors = Count("depot.processors"), forwarders = Count("depot.forwarders"), trucks = Count("depot.trucks");
            yield return 1;
            if (trees) foreach (int unit in forest.ClearTerrainTilesWork(footprint)) yield return unit;
            var depot = new Depot { TileId = id, Footprint = footprint, Position = position };
            Depots.Add(depot); terrain.SetBuildingFootprint(footprint); yield return 1;
            foreach (var kind in new[] { ForestMachineKind.Harvester, ForestMachineKind.Forwarder }) {
                int count = kind == ForestMachineKind.Harvester ? processors : forwarders;
                for (int i = 0; i < count; i++) {
                    int machineId = nextMachineId++;
                    Machines.Add(new ForestMachine { Id = machineId, Kind = kind, Home = depot, Path = new[] { depot.TileId },
                        Upkeep = new VehicleUpkeep((uint)machineId * 2654435761u), TuningSource = this });
                    yield return 1;
                }
            }
            for (int i = 0; i < trucks; i++) {
                int truckId = nextTruckId++;
                Trucks.Add(new FleetTruck { Id = truckId, Home = depot, Upkeep = new VehicleUpkeep((uint)truckId * 2246822519u + 1) });
                yield return 1;
            }
            Status = $"Telephely kész: {processors} processzor, {forwarders} forwarder, {trucks} rönkszállító.";
            result.Value = true;
        }

        private int DepotDock(Depot depot) { var docks = terrain.FindRoadDocks(depot.Footprint); return docks.Count > 0 ? docks[0] : -1; }

        /// <summary>The cheapest network route between any dock of the source and any dock of the destination.</summary>
        private int[] TruckRoute(FleetTruck truck, int sourceTile, int destination)
        {
            var result = new WorkResult<int[]>();
            SimulationWork.Complete(TruckRouteWork(truck, sourceTile, destination, result));
            return result.Value;
        }

        private IEnumerable<int> TruckRouteWork(FleetTruck truck, int sourceTile, int destination, WorkResult<int[]> result)
        {
            var mill = MillAt(destination);
            var ends = terrain.FindNetworkDocks(mill != null ? mill.Footprint : new[] { destination });
            float bestCost = float.PositiveInfinity;
            foreach (int start in terrain.FindNetworkDocks(new[] { sourceTile }))
                foreach (int end in ends) {
                    var path = new WorkResult<(int[] Path, float Cost)>();
                    foreach (int unit in TruckPathWork(truck, start, end, path)) yield return unit;
                    if (path.Value.Path.Length >= 2 && path.Value.Cost < bestCost) {
                        result.Value = path.Value.Path; bestCost = path.Value.Cost;
                    }
                }
        }

        /// <summary>Gives a parked truck its order: load at <paramref name="source"/>, unload at <paramref name="destination"/> (a mill or a stack site).</summary>
        internal bool AssignTruck(FleetTruck truck, TimberStack source, int destination) { var result = new WorkResult<bool>(); SimulationWork.Complete(AssignTruckWork(truck, source, destination, result)); return result.Value; }

        internal IEnumerable<int> AssignTruckWork(FleetTruck truck, TimberStack source, int destination, WorkResult<bool> result)
        {
            if (destination == source.Tile) { Status = "A cél nem lehet maga a forrás sarang."; yield break; }
            if (truck.Phase != TruckPhase.Parked) { Status = "A rönkszállító épp úton van: előbb hívd haza."; yield break; }
            bool mill = MillAt(destination) != null;
            if (!mill && StackAt(destination) == null && !CanPlaceStack(destination)) { Status = "Ide nem rakható le a fa."; yield break; }
            var routeResult = new WorkResult<int[]>();
            foreach (int unit in TruckRouteWork(truck, source.Tile, destination, routeResult)) yield return unit;
            int[] route = routeResult.Value;
            if (route == null) { Status = "A sarangtól nem vezet út a célig: a teherautó csak úton és nyomon jár."; yield break; }
            int dock = DepotDock(truck.Home);
            var approachResult = new WorkResult<(int[] Path, float Cost)> { Value = (Array.Empty<int>(), 0) };
            if (dock >= 0) foreach (int unit in TruckPathWork(truck, dock, route[0], approachResult)) yield return unit;
            int[] approach = approachResult.Value.Path;
            if (dock != route[0] && approach.Length < 2) { Status = "A telephelyről nem vezet út a sarangig."; yield break; }
            string rejection = TruckOrderRejection(truck, source, mill);
            yield return 1;
            if (rejection != null) { Status = rejection; yield break; }
            truck.Source = source; truck.Destination = destination; truck.Target = mill ? null : StackAt(destination); truck.HomeRequested = false;
            if (truck.Target == null && !mill) { truck.Target = new TimberStack { Id = nextStackId++, Tile = destination }; Stacks.Add(truck.Target); }
            if (approach.Length >= 2) {
                var vehicle = new WorkResult<Vehicle>();
                foreach (int unit in Vehicles.SpawnFleetWork(approach, null, -1, true, vehicle)) yield return unit;
                truck.Vehicle = vehicle.Value; truck.Phase = TruckPhase.ToWork;
            } else foreach (int unit in StartShuttleWork(truck, route)) yield return unit;
            Status = mill ? "A rönkszállító a sarangtól a malomba fuvaroz." : "A rönkszállító áthordja a sarangot.";
            result.Value = true;
        }
        private IEnumerable<int> StartShuttleWork(FleetTruck truck, int[] route)
        {
            ChargeTruckFuel(truck);
            if (truck.Vehicle != null) Vehicles.Remove(truck.Vehicle);
            var result = new WorkResult<Vehicle>();
            foreach (int unit in Vehicles.SpawnFleetWork(route, new[] { truck.Source.Tile }, truck.Destination, false, result)) yield return unit;
            truck.Vehicle = result.Value;
            truck.FuelCharged = 0;
            truck.Phase = TruckPhase.Working;
        }

        internal void SendHome(FleetTruck truck)
        {
            if (truck.Phase is TruckPhase.Parked or TruckPhase.ToHome) return;
            truck.HomeRequested = true;
            Status = "A rönkszállító leadja a rakományt, és visszamegy a telephelyre.";
        }

        private FleetTruck TruckOf(Vehicle vehicle) => Trucks.Find(t => t.Vehicle == vehicle);

        private void ChargeTruckFuel(FleetTruck truck)
        {
            if (truck.Vehicle == null) return;
            double used = truck.Vehicle.FuelUsed;
            double burnt = (used - truck.FuelCharged) * truck.Upkeep.Fuel(Tuning);
            if (burnt > 0) Burn(burnt);
            truck.FuelCharged = used;
        }

        private System.Collections.Generic.IEnumerable<int> UpdateTrucksWork(double seconds, Dictionary<int, List<SupplyEdge>> supply)
        {
            foreach (var truck in Trucks)
            {
                yield return 1;
                truck.Control = Controllers?.Step("truck", (ulong)truck.Id, new(seconds, truck.Vehicle?.CargoAmount ?? 0,
                    TruckState(truck), truck.Phase is TruckPhase.ToWork or TruckPhase.Working, truck.Upkeep.Broken)) ?? BehaviorAction.Autonomous;
                if (rules.Evaluate(Behaviors, "control.home", "truck", (ulong)truck.Id, TruckInputs(truck, seconds)) >= .5) SendHome(truck);
                bool wait = TruckWait(truck);
                int upkeep = (int)rules.Evaluate(Behaviors, "control.upkeep", "truck", (ulong)truck.Id,
                    TruckInputs(truck, seconds) with { DecisionAllow = wait ? 0 : 1 });
                if (upkeep == 1) RunningCosts += truck.Upkeep.Service(seconds, Tuning);
                if (truck.Vehicle != null)
                {
                    ChargeTruckFuel(truck);
                    // Wear while on the move; a broken truck stands where it is until the mechanic is done.
                    var road = truck.Vehicle;
                    road.GetSegment(road.RoutePosition, out int from, out _, out _);
                    float strain = (float)Tuning.VehicleRules.Evaluate(ForesTycoon.Rules.VehicleResponse.Strain, [0, road.CargoFill, 0, terrain.IsSkidTrail(from) ? 1 : 0]);
                    if (upkeep == 2)
                    {
                        bool running = truck.Upkeep.Operate(seconds, strain, out double repair, Tuning);
                        if (repair > 0) { RunningCosts += repair; Status = $"A rönkszállító #{truck.Id} megjavítva ({repair:N0} eFt)."; }
                        if (!running && !road.Broken) Status = $"A rönkszállító #{truck.Id} elromlott: a szerelő úton van.";
                        road.Broken = !running;
                    }
                }
                // The road under it was removed: the truck is taken back to its yard.
                if (truck.Vehicle != null && !Vehicles.Contains(truck.Vehicle)) { Park(truck); continue; }
                if (wait) continue;
                var v = truck.Vehicle;
                if (v == null) continue;
                var fed = new WorkResult<bool>();
                if (truck.Phase == TruckPhase.Working) foreach (int unit in BeingFedWork(truck.Source, fed, supply)) yield return unit;
                var input = new LogisticsInputs(StepSeconds: seconds, MachineKind: 2, MachineState: (int)truck.Phase,
                    MachineCargo: v.CargoAmount, MachineCapacity: v.CargoCapacity, MachineWear: truck.Upkeep.Wear,
                    MachineHomeRequested: truck.HomeRequested ? 1 : 0, SourceStock: truck.Source?.Volume ?? 0, SourceFed: fed.Value ? 1 : 0,
                    WorkflowArrived: v.TransitArrived ? 1 : 0,
                    WorkflowAtSource: v.TransportState is VehicleTransportState.Loading or VehicleTransportState.Waiting ? 1 : 0);
                input = input with { DecisionReturn = rules.Evaluate(Behaviors, "truck.return", "truck", (ulong)truck.Id, input) };
                int operation = (int)rules.Evaluate(Behaviors, "truck.workflow", "truck", (ulong)truck.Id, input);
                switch (operation)
                {
                    case 1 when truck.Source != null && v.Transit && v.TransitArrived && v.CargoAmount <= .0001f:
                    {
                        var result = new WorkResult<int[]>();
                        foreach (int unit in TruckRouteWork(truck, truck.Source.Tile, truck.Destination, result)) yield return unit;
                        int[] route = result.Value;
                        if (route == null) { foreach (int unit in ReturnWork(truck, truck.Vehicle.Route[^1])) yield return unit; break; }
                        if (route[0] != v.Route[^1]) break; // A new route must begin where the physical truck actually stands.
                        foreach (int unit in StartShuttleWork(truck, route)) yield return unit;
                        break;
                    }
                    case 2:
                        foreach (int unit in ReturnWork(truck, truck.Phase == TruckPhase.Working ? v.Route[0] : v.Route[^1])) yield return unit;
                        break;
                    case 3 when v.TransitArrived && v.CargoAmount <= .0001f && v.Route[^1] == DepotDock(truck.Home):
                        Park(truck); // Releasing a physical vehicle cannot discard timber or teleport it.
                        break;
                }
            }
        }

        private IEnumerable<int> ReturnWork(FleetTruck truck, int from)
        {
            if (truck.Vehicle is { } loaded && (loaded.CargoAmount > .0001f || loaded.Transit && !loaded.TransitArrived ||
                truck.Phase == TruckPhase.Working && loaded.TransportState is not (VehicleTransportState.Loading or VehicleTransportState.Waiting))) yield break;
            ChargeTruckFuel(truck);
            int dock = DepotDock(truck.Home);
            var result = new WorkResult<(int[] Path, float Cost)> { Value = (Array.Empty<int>(), float.PositiveInfinity) };
            if (dock >= 0) foreach (int unit in TruckPathWork(truck, from, dock, result)) yield return unit;
            int[] path = result.Value.Path;
            // No route means wait at the current location, not teleport to the depot.
            if (dock != from && path.Length < 2) { Status = "A teherautó nem talál engedélyezett hazautat."; yield break; }
            if (truck.Vehicle != null) Vehicles.Remove(truck.Vehicle);
            truck.Vehicle = null; truck.FuelCharged = 0;
            if (path.Length < 2) { Park(truck); yield break; }
            var spawned = new WorkResult<Vehicle>();
            foreach (int unit in Vehicles.SpawnFleetWork(path, null, -1, true, spawned)) yield return unit;
            truck.Vehicle = spawned.Value;
            truck.Phase = TruckPhase.ToHome;
        }

        private void Park(FleetTruck truck)
        {
            ChargeTruckFuel(truck);
            if (truck.Vehicle != null && Vehicles.Contains(truck.Vehicle)) Vehicles.Remove(truck.Vehicle);
            truck.Vehicle = null; truck.FuelCharged = 0; truck.Phase = TruckPhase.Parked;
            truck.Source = null; truck.Target = null; truck.Destination = -1; truck.HomeRequested = false;
        }

        /// <summary>The harvest site a tile belongs to, or null.</summary>
        internal HarvestSite SiteAt(int tile)
        {
            foreach (var site in Sites) if (Array.IndexOf(site.Tiles, tile) >= 0) return site;
            return null;
        }
    }
}
