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
    }

    internal sealed partial class ForestryLogistics
    {
        internal readonly List<Depot> Depots = new();
        internal readonly List<FleetTruck> Trucks = new();
        private int nextTruckId = 1;
        /// <summary>The road vehicles the trucks of the fleet run as; set by the world.</summary>
        internal VehicleSystem Vehicles;

        /// <summary>
        /// Places a depot on a 2×2 flat, dry, empty spot beside a road. The first depot receives the starting fleet: a
        /// processor, a forwarder and a log truck.
        /// </summary>
        internal bool PlaceDepot(int id)
        {
            if (!terrain.TryGetSawmillFootprint(id, out var footprint, out var position)) { Status = "A telephelyhez 2×2 sík, száraz, üres csempe kell."; return false; }
            foreach (int tile in footprint)
                if (forest.TryGetStand(tile, out _) || ContainsTile(tile) || terrain.IsBuildingTile(tile)) { Status = "A telephely helyét erdő, vágás vagy épület foglalja."; return false; }
            if (terrain.FindRoadDocks(footprint).Count == 0) { Status = "A telephelyet út mellé kell építeni."; return false; }
            var depot = new Depot { TileId = id, Footprint = footprint, Position = position };
            Depots.Add(depot);
            terrain.SetBuildingFootprint(footprint);
            if (Depots.Count == 1)
            {
                foreach (var kind in new[] { ForestMachineKind.Harvester, ForestMachineKind.Forwarder })
                {
                    int machineId = nextMachineId++;
                    Machines.Add(new ForestMachine { Id = machineId, Kind = kind, Home = depot, Path = new[] { depot.TileId },
                        Upkeep = new VehicleUpkeep((uint)machineId * 2654435761u) });
                }
                int truckId = nextTruckId++;
                Trucks.Add(new FleetTruck { Id = truckId, Home = depot, Upkeep = new VehicleUpkeep((uint)truckId * 2246822519u + 1) });
                Status = "Telephely kész: egy processzor, egy forwarder és egy rönkszállító várja a munkát.";
            }
            else Status = "Új telephely kész.";
            return true;
        }

        private int DepotDock(Depot depot) { var docks = terrain.FindRoadDocks(depot.Footprint); return docks.Count > 0 ? docks[0] : -1; }

        /// <summary>The cheapest network route between any dock of the source and any dock of the destination.</summary>
        private int[] TruckRoute(int sourceTile, int destination)
        {
            var mill = MillAt(destination);
            var ends = terrain.FindNetworkDocks(mill != null ? mill.Footprint : new[] { destination });
            int[] best = null;
            foreach (int start in terrain.FindNetworkDocks(new[] { sourceTile }))
                foreach (int end in ends)
                {
                    int[] route = terrain.FindNetworkPath(start, end);
                    if (route.Length >= 2 && (best == null || route.Length < best.Length)) best = route;
                }
            return best;
        }

        /// <summary>Gives a parked truck its order: load at <paramref name="source"/>, unload at <paramref name="destination"/> (a mill or a stack site).</summary>
        internal bool AssignTruck(FleetTruck truck, TimberStack source, int destination)
        {
            if (truck.Phase != TruckPhase.Parked) { Status = "A rönkszállító épp úton van: előbb hívd haza."; return false; }
            bool mill = MillAt(destination) != null;
            if (!mill && StackAt(destination) == null && !CanPlaceStack(destination)) { Status = "Ide nem rakható le a fa."; return false; }
            int[] route = TruckRoute(source.Tile, destination);
            if (route == null) { Status = "A sarangtól nem vezet út a célig: a teherautó csak úton és nyomon jár."; return false; }
            int dock = DepotDock(truck.Home);
            int[] approach = dock < 0 ? Array.Empty<int>() : terrain.FindNetworkPath(dock, route[0]);
            if (dock != route[0] && approach.Length < 2) { Status = "A telephelyről nem vezet út a sarangig."; return false; }
            truck.Source = source; truck.Destination = destination; truck.Target = mill ? null : StackAt(destination); truck.HomeRequested = false;
            if (truck.Target == null && !mill) { truck.Target = new TimberStack { Id = nextStackId++, Tile = destination }; Stacks.Add(truck.Target); }
            if (approach.Length >= 2) { truck.Vehicle = Vehicles.SpawnTransit(approach); truck.Phase = TruckPhase.ToWork; }
            else StartShuttle(truck, route);
            Status = mill ? "A rönkszállító a sarangtól a malomba fuvaroz." : "A rönkszállító áthordja a sarangot.";
            return true;
        }

        private void StartShuttle(FleetTruck truck, int[] route)
        {
            if (truck.Vehicle != null) Vehicles.Remove(truck.Vehicle);
            truck.Vehicle = Vehicles.SpawnLogistics(route, new[] { truck.Source.Tile }, truck.Destination);
            truck.Phase = TruckPhase.Working;
        }

        internal void SendHome(FleetTruck truck)
        {
            if (truck.Phase is TruckPhase.Parked or TruckPhase.ToHome) return;
            truck.HomeRequested = true;
            Status = "A rönkszállító leadja a rakományt, és visszamegy a telephelyre.";
        }

        private FleetTruck TruckOf(Vehicle vehicle) => Trucks.Find(t => t.Vehicle == vehicle);

        private void UpdateTrucks(double seconds)
        {
            foreach (var truck in Trucks)
            {
                if (truck.Vehicle != null)
                {
                    double burnt = (truck.Vehicle.FuelUsed - truck.FuelCharged) * truck.Upkeep.FuelFactor;
                    if (burnt > 0) { Burn(burnt); truck.FuelCharged = truck.Vehicle.FuelUsed; }
                    // Wear while on the move; a broken truck stands where it is until the mechanic is done.
                    var road = truck.Vehicle;
                    road.GetSegment(road.RoutePosition, out int from, out _, out _);
                    float strain = (terrain.IsSkidTrail(from) ? 2f : 1f) * (1 + 0.5f * road.CargoFill);
                    bool moving = road.CurrentSpeed > 0.01 || road.TransportState is VehicleTransportState.Loading or VehicleTransportState.Unloading;
                    if (moving || truck.Upkeep.Broken)
                    {
                        bool running = truck.Upkeep.Operate(seconds, strain, out double repair);
                        if (repair > 0) { RunningCosts += repair; Status = $"A rönkszállító #{truck.Id} megjavítva ({repair:N0} eFt)."; }
                        if (!running && !road.Broken) Status = $"A rönkszállító #{truck.Id} elromlott: a szerelő úton van.";
                        road.Broken = !running;
                    }
                }
                else if (truck.Phase == TruckPhase.Parked) RunningCosts += truck.Upkeep.Service(seconds);
                // The road under it was removed: the truck is taken back to its yard.
                if (truck.Vehicle != null && !Vehicles.Contains(truck.Vehicle)) { Park(truck); continue; }
                switch (truck.Phase)
                {
                    case TruckPhase.ToWork when truck.Vehicle.TransitArrived:
                    {
                        int[] route = truck.HomeRequested ? null : TruckRoute(truck.Source.Tile, truck.Destination);
                        if (route == null) { Return(truck, truck.Vehicle.Route[^1]); break; }
                        StartShuttle(truck, route);
                        break;
                    }
                    case TruckPhase.Working:
                    {
                        var v = truck.Vehicle;
                        bool done = truck.HomeRequested || (truck.Source.Volume <= 0.01f && !BeingFed(truck.Source));
                        // Leave only empty and standing at the source, so no timber is carried off.
                        if (done && v.CargoAmount <= 0.0001f && v.TransportState is VehicleTransportState.Loading or VehicleTransportState.Waiting)
                            Return(truck, v.Route[0]);
                        break;
                    }
                    case TruckPhase.ToHome when truck.Vehicle.TransitArrived:
                        Park(truck);
                        break;
                }
            }
        }

        private void Return(FleetTruck truck, int from)
        {
            int dock = DepotDock(truck.Home);
            int[] path = dock < 0 ? Array.Empty<int>() : terrain.FindNetworkPath(from, dock);
            if (truck.Vehicle != null) Vehicles.Remove(truck.Vehicle);
            truck.Vehicle = null; truck.FuelCharged = 0;
            if (path.Length < 2) { Park(truck); return; }
            truck.Vehicle = Vehicles.SpawnTransit(path);
            truck.Phase = TruckPhase.ToHome;
        }

        private void Park(FleetTruck truck)
        {
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
