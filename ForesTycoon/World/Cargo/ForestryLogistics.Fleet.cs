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
    /// A log truck of the fleet. Parked at its depot it is no vehicle on the map; with an order it drives to the landing
    /// (<see cref="TruckPhase.ToWork"/>), shuttles landing → sawmill (<see cref="TruckPhase.Working"/>) and returns home
    /// when the site is cleared or the player calls it back.
    /// </summary>
    internal sealed class FleetTruck
    {
        internal int Id;
        internal Depot Home;
        internal TruckPhase Phase;
        internal Vehicle Vehicle;
        internal HarvestSite Site;
        internal int Mill = -1;
        internal bool HomeRequested;
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
                    Machines.Add(new ForestMachine { Id = nextMachineId++, Kind = kind, Home = depot, Path = new[] { depot.TileId } });
                Trucks.Add(new FleetTruck { Id = nextTruckId++, Home = depot });
                Status = "Telephely kész: egy processzor, egy forwarder és egy rönkszállító várja a munkát.";
            }
            else Status = "Új telephely kész.";
            return true;
        }

        /// <summary>Timber of a site still to be hauled, including what its forwarders carry right now.</summary>
        internal float Unfinished(HarvestSite site)
        {
            float sum = Volume(site);
            foreach (var m in Machines) if (m.Site == site) sum += m.Cargo;
            return sum;
        }

        private int DepotDock(Depot depot) { var docks = terrain.FindRoadDocks(depot.Footprint); return docks.Count > 0 ? docks[0] : -1; }

        /// <summary>The sawmill nearest (by road) to a site's landing, with the road route from landing to mill.</summary>
        private (Sawmill Mill, int[] Route) NearestMill(HarvestSite site)
        {
            (Sawmill, int[]) best = (null, null);
            if (site.Landing < 0) return best;
            foreach (int start in terrain.FindRoadDocks(new[] { site.Landing }))
                foreach (var mill in Mills)
                    foreach (int end in terrain.FindRoadDocks(mill.Footprint))
                    {
                        int[] route = terrain.FindLogisticsRoadPath(start, end);
                        if (route.Length >= 2 && (best.Item2 == null || route.Length < best.Item2.Length)) best = (mill, route);
                    }
            return best;
        }

        /// <summary>Gives a parked truck its schedule: load at the site's landing, unload at the nearest connected mill.</summary>
        internal bool AssignTruck(FleetTruck truck, HarvestSite site)
        {
            if (truck.Phase != TruckPhase.Parked) { Status = "A rönkszállító épp úton van: előbb hívd haza."; return false; }
            if (site.Landing < 0) { Status = "A vágásnak még nincs rakodója: jelölj ki közelítő nyomot az útig."; return false; }
            var (mill, route) = NearestMill(site);
            if (mill == null) { Status = "A rakodótól nem vezet út fűrészmalomhoz."; return false; }
            int dock = DepotDock(truck.Home);
            int[] approach = dock < 0 ? Array.Empty<int>() : terrain.FindLogisticsRoadPath(dock, route[0]);
            if (dock != route[0] && approach.Length < 2) { Status = "A telephelyről nem vezet út a rakodóhoz."; return false; }
            truck.Site = site; truck.Mill = mill.TileId; truck.HomeRequested = false;
            if (approach.Length >= 2) { truck.Vehicle = Vehicles.SpawnTransit(approach); truck.Phase = TruckPhase.ToWork; }
            else StartShuttle(truck, route);
            Status = "A rönkszállító a rakodóhoz indult.";
            return true;
        }

        private void StartShuttle(FleetTruck truck, int[] route)
        {
            if (truck.Vehicle != null) Vehicles.Remove(truck.Vehicle);
            truck.Vehicle = Vehicles.SpawnLogistics(route, truck.Site.Tiles, truck.Mill);
            truck.Phase = TruckPhase.Working;
        }

        /// <summary>Calls a machine or a truck back to its depot.</summary>
        internal void SendHome(ForestMachine machine)
        {
            if (machine.Site == null) return;
            machine.HomeRequested = true;
            Status = "Hazahívva: befejezi a mostani mozdulatot, és visszamegy a telephelyre.";
        }

        internal void SendHome(FleetTruck truck)
        {
            if (truck.Phase is TruckPhase.Parked or TruckPhase.ToHome) return;
            truck.HomeRequested = true;
            Status = "A rönkszállító leadja a rakományt, és visszamegy a telephelyre.";
        }

        private void UpdateTrucks()
        {
            foreach (var truck in Trucks)
            {
                // The road under it was removed: the truck is taken back to its yard.
                if (truck.Vehicle != null && !Vehicles.Contains(truck.Vehicle)) { Park(truck); continue; }
                switch (truck.Phase)
                {
                    case TruckPhase.ToWork when truck.Vehicle.TransitArrived:
                    {
                        var site = truck.Site;
                        int[] route = Mills.Exists(m => m.TileId == truck.Mill) ? NearestMill(site).Route : null;
                        if (route == null || truck.HomeRequested) { Return(truck, truck.Vehicle.Route[^1]); break; }
                        StartShuttle(truck, route);
                        break;
                    }
                    case TruckPhase.Working:
                    {
                        var v = truck.Vehicle;
                        bool done = truck.HomeRequested || Unfinished(truck.Site) <= 0.001f;
                        // Leave only empty and standing at the landing, so no timber is carried off.
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
            int[] path = dock < 0 ? Array.Empty<int>() : terrain.FindLogisticsRoadPath(from, dock);
            if (truck.Vehicle != null) Vehicles.Remove(truck.Vehicle);
            truck.Vehicle = null;
            if (path.Length < 2) { Park(truck); return; }
            truck.Vehicle = Vehicles.SpawnTransit(path);
            truck.Phase = TruckPhase.ToHome;
        }

        private void Park(FleetTruck truck)
        {
            if (truck.Vehicle != null && Vehicles.Contains(truck.Vehicle)) Vehicles.Remove(truck.Vehicle);
            truck.Vehicle = null; truck.Phase = TruckPhase.Parked; truck.Site = null; truck.Mill = -1; truck.HomeRequested = false;
        }

        /// <summary>The harvest site a tile belongs to, or null.</summary>
        internal HarvestSite SiteAt(int tile)
        {
            foreach (var site in Sites) if (Array.IndexOf(site.Tiles, tile) >= 0 || site.Landing == tile) return site;
            return null;
        }
    }
}
