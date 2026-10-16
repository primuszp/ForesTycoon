using System.Collections.Generic;
using System;

namespace ForesTycoon
{
    sealed partial class VehicleSystem : IWorldSystem
    {
        private readonly List<Vehicle> vehicles = new List<Vehicle>();
        private readonly TimberCargoSystem timberCargo;
        private int nextId = 1;
        internal Func<int[], VehicleRoadRoute> RoadRouteFactory { get; set; }
        internal Func<int[], WorkResult<VehicleRoadRoute>, IEnumerable<int>> RoadRouteWorkFactory { get; set; }
        public bool UseRoadPhysics { get; set; } = true;
        /// <summary>Vehicle seconds per world second (the world's vehicle clock runs faster than its calendar).</summary>
        internal double TimeScale { get; set; } = 1;
        public bool UseCargoStops { get; set; } = true;
        internal Func<Vehicle,float,float> SourceLoader;
        internal Action<Vehicle,float> DestinationReceiver;
        internal Func<Vehicle,bool> RouteValidator;
        internal Func<Vehicle, bool> BehaviorPause, BehaviorDepart;
        internal Func<Vehicle, float> SourceStock;
        internal Func<Vehicle, string, double, double, double> BehaviorRule;
        /// <summary>Surface and condition (1 new … 0 ruined) of a road tile.</summary>
        internal Func<int,(RoadSurface Surface,float Condition)> RoadState;
        /// <summary>Reports raw passage data; only the world rule book changes roads and tracks.</summary>
        internal Action<TrafficPassage> Passage;
        /// <summary>Truck physics and road load every vehicle uses (the world's tuning).</summary>
        internal TruckSpec Spec = TruckSpec.Default;
        internal Vehicle SpawnLogistics(int[] route, int[] sources, int mill)
        {
            var result = new WorkResult<Vehicle>();
            SimulationWork.Complete(SpawnFleetWork(route, sources, mill, false, result));
            return result.Value;
        }

        internal Vehicle SpawnTransit(int[] route)
        {
            var result = new WorkResult<Vehicle>();
            SimulationWork.Complete(SpawnFleetWork(route, null, -1, true, result));
            return result.Value;
        }

        internal IEnumerable<int> SpawnFleetWork(int[] route, int[] sources, int mill, bool transit, WorkResult<Vehicle> result)
        {
            var geometry = new WorkResult<VehicleRoadRoute>();
            if (RoadRouteWorkFactory != null) {
                foreach (int unit in RoadRouteWorkFactory(route, geometry)) yield return unit;
            } else geometry.Value = RoadRouteFactory?.Invoke(route);
            var vehicle = new Vehicle(nextId++, route, 1.5, roadRoute: geometry.Value, roadPhysics: UseRoadPhysics) { Transit = transit };
            if (!transit) {
                vehicle.SourceTiles = (int[])sources.Clone(); vehicle.SawmillTileId = mill; vehicle.CargoStopsEnabled = true;
                vehicle.TransportState = VehicleTransportState.Loading; vehicle.Hold();
            }
            vehicles.Add(vehicle); result.Value = vehicle;
        }
        internal void Remove(Vehicle vehicle)=>vehicles.Remove(vehicle);
        internal bool Contains(Vehicle vehicle)=>vehicles.Contains(vehicle);

        public VehicleSystem(TimberCargoSystem timberCargo = null, Func<int[], VehicleRoadRoute> roadRouteFactory = null)
        {
            this.timberCargo = timberCargo ?? new TimberCargoSystem();
            RoadRouteFactory = roadRouteFactory;
        }

        public IReadOnlyList<Vehicle> Vehicles => vehicles;
        public int Count => vehicles.Count;

        public Vehicle Spawn(int[] route, double speedTilesPerSecond = 1.5)
        {
            Vehicle vehicle = new Vehicle(nextId++, route, speedTilesPerSecond,
                roadRoute: RoadRouteFactory?.Invoke(route), roadPhysics: UseRoadPhysics);
            vehicle.CargoStopsEnabled = UseCargoStops && vehicle.RoadRoute != null && UseRoadPhysics;
            float initialCargo = timberCargo.Load(vehicle.CargoCapacity);
            if (vehicle.CargoStopsEnabled) vehicle.BeginLoading(initialCargo);
            else vehicle.Load(initialCargo);
            vehicles.Add(vehicle);
            return vehicle;
        }

        public void Update(double deltaSeconds) => SimulationWork.Complete(UpdateWork(deltaSeconds));
        public IEnumerable<int> UpdateWork(double deltaSeconds)
        {
            if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            deltaSeconds *= TimeScale;
            for (int i = 0; i < vehicles.Count; i++)
            {
                yield return 1;
                Vehicle vehicle = vehicles[i];
                // A tick runs in several substeps; drawing interpolates across the whole tick, not just the last substep.
                double before = vehicle.RoutePosition;
                UpdateVehicle(vehicle, deltaSeconds);
                vehicle.PreviousRoutePosition = Math.Min(before, vehicle.RoutePosition);
            }
        }

        private void UpdateVehicle(Vehicle vehicle, double deltaSeconds)
        {
            {
                vehicle.RoadState = RoadState; vehicle.Passage = Passage; vehicle.Spec = Spec;
                if(vehicle.RouteBlocked||vehicle.Broken||BehaviorPause?.Invoke(vehicle)==true){vehicle.Hold();return;}
                if(vehicle.Transit){
                    if(!vehicle.TransitArrived){vehicle.Update(deltaSeconds);if(vehicle.RoutePosition>=vehicle.Route.Length-1-1e-6){vehicle.TransitArrived=true;vehicle.Hold();}}
                    else vehicle.Hold();
                    return;
                }
                if(vehicle.LocalCargo){UpdateLogistics(vehicle,deltaSeconds);return;}
                if (!vehicle.CargoStopsEnabled)
                {
                    vehicle.Update(deltaSeconds); ProcessRouteEndpoints(vehicle);
                    return;
                }
                double remaining = deltaSeconds;
                while (remaining > 0)
                {
                    double dt = Math.Min(remaining, 1.0 / 30); remaining -= dt;
                    switch (vehicle.TransportState)
                    {
                        case VehicleTransportState.Waiting:
                            vehicle.Hold();
                            if (timberCargo.Available > 0) vehicle.BeginLoading(timberCargo.Load(vehicle.CargoCapacity));
                            break;
                        case VehicleTransportState.Loading:
                            if (vehicle.AdvanceTransfer(dt)) vehicle.TransportState = VehicleTransportState.Hauling;
                            break;
                        case VehicleTransportState.Unloading:
                            if (vehicle.AdvanceTransfer(dt))
                            {
                                timberCargo.Deliver(vehicle.Unload());
                                vehicle.TransportState = VehicleTransportState.Returning;
                            }
                            break;
                        default:
                            vehicle.Update(dt); ProcessRouteEndpoints(vehicle);
                            break;
                    }
                }
            }
        }

        private void UpdateLogistics(Vehicle vehicle,double seconds)
        {
            while(seconds>1e-9){double dt=Math.Min(seconds,1.0/30);seconds-=dt;
                if (BehaviorPause?.Invoke(vehicle) == true) { vehicle.Hold(); return; }
                switch(vehicle.TransportState){
                    case VehicleTransportState.Waiting:
                    case VehicleTransportState.Loading:
                        vehicle.Hold();
                        if (BehaviorDepart?.Invoke(vehicle) == true && vehicle.CargoAmount > 0) { vehicle.TransportState = VehicleTransportState.Hauling; break; }
                        float request = Math.Min(vehicle.CargoCapacity-vehicle.CargoAmount, (float)(BehaviorRule?.Invoke(vehicle, "loading.amount", dt*vehicle.CargoCapacity/3, dt) ?? dt*vehicle.CargoCapacity/3));
                        float amount=SourceLoader?.Invoke(vehicle,request)??0;
                        vehicle.Load(amount);
                        bool full = vehicle.CargoAmount >= vehicle.CargoCapacity-0.001f;
                        bool nativeDepart = full || amount < .000001f && vehicle.CargoAmount > 0 && (request > 0 || (SourceStock?.Invoke(vehicle) ?? 0) <= 0);
                        bool depart = (BehaviorRule?.Invoke(vehicle, "loading.depart", nativeDepart ? 1 : 0, dt) ?? (nativeDepart ? 1 : 0)) >= .5;
                        if(vehicle.CargoAmount > 0 && (full || depart))vehicle.TransportState=VehicleTransportState.Hauling;
                        else vehicle.TransportState=amount>0?VehicleTransportState.Loading:VehicleTransportState.Waiting;
                        break;
                    case VehicleTransportState.Unloading:
                        vehicle.Hold();float delivered=vehicle.TakeCargo((float)(BehaviorRule?.Invoke(vehicle, "unloading.amount", dt*vehicle.CargoCapacity/3, dt) ?? dt*vehicle.CargoCapacity/3));
                        DestinationReceiver?.Invoke(vehicle,delivered);timberCargo.Deliver(delivered);
                        if(vehicle.CargoAmount<=0)vehicle.TransportState=VehicleTransportState.Returning;
                        break;
                    default:
                        vehicle.Update(dt);
                        int last=vehicle.Route.Length-1;
                        long before=(long)Math.Floor(vehicle.PreviousRoutePosition/last),after=(long)Math.Floor(vehicle.RoutePosition/last);
                        if(after>before){vehicle.Hold();vehicle.TransportState=(after&1)==1?VehicleTransportState.Unloading:VehicleTransportState.Loading;}
                        break;
                }
            }
        }

        private void ProcessRouteEndpoints(Vehicle vehicle)
        {
            int last = vehicle.Route.Length - 1;
            long previousBoundary = (long)Math.Floor(vehicle.PreviousRoutePosition / last);
            long currentBoundary = (long)Math.Floor(vehicle.RoutePosition / last);
            for (long boundary = previousBoundary + 1; boundary <= currentBoundary; boundary++)
            {
                if (vehicle.CargoStopsEnabled)
                {
                    if ((boundary & 1L) != 0L) vehicle.BeginUnloading();
                    else vehicle.BeginLoading(timberCargo.Load(vehicle.CargoCapacity - vehicle.CargoAmount));
                }
                else if ((boundary & 1L) != 0L) timberCargo.Deliver(vehicle.Unload());
                else vehicle.Load(timberCargo.Load(vehicle.CargoCapacity - vehicle.CargoAmount));
            }
        }

        public void Clear()
        {
            vehicles.Clear();
            nextId = 1;
        }

        internal IEnumerable<int> RemoveInvalidRoutesWork(Func<int, bool> isNetworkTile, Func<int, int, bool> connected)
        {
            // Stable compaction preserves cargo accounting and fleet iteration order.
            int keep = 0, count = vehicles.Count;
            for (int i = 0; i < count; i++) {
                var vehicle = vehicles[i];
                bool valid = true;
                for (int r = 0; r < vehicle.Route.Length; r++) {
                    yield return 1;
                    if (!isNetworkTile(vehicle.Route[r]) || r > 0 && !connected(vehicle.Route[r - 1], vehicle.Route[r])) { valid = false; break; }
                }
                if (vehicle.LocalCargo || vehicle.Transit) {
                    vehicle.RouteBlocked = !valid;
                    if (!valid) vehicle.Hold();
                } else if (!valid) {
                    timberCargo.AddHarvested(vehicle.Unload());
                    continue;
                }
                vehicles[keep++] = vehicle;
            }
            if (keep < count) vehicles.RemoveRange(keep, count - keep);
        }

        public int RemoveInvalidRoutes(Func<int, bool> isRoadTile)
        {
            if (isRoadTile == null) throw new ArgumentNullException(nameof(isRoadTile));
            RefreshLogisticsRoutes(isRoadTile);
            return vehicles.RemoveAll(vehicle =>
            {
                if(vehicle.LocalCargo||vehicle.Transit)return false;
                for (int i = 0; i < vehicle.Route.Length; i++)
                    if (!isRoadTile(vehicle.Route[i]))
                    {
                        timberCargo.AddHarvested(vehicle.Unload());
                        return true;
                    }
                if (RouteValidator?.Invoke(vehicle) == false)
                {
                    timberCargo.AddHarvested(vehicle.Unload());
                    return true;
                }
                return false;
            });
        }
        internal void RefreshLogisticsRoutes(Func<int,bool> isRoadTile)
        {
            foreach(var vehicle in vehicles)if(vehicle.LocalCargo||vehicle.Transit){
                vehicle.RouteBlocked=false;
                foreach(int id in vehicle.Route)if(!isRoadTile(id)){vehicle.RouteBlocked=true;vehicle.Hold();break;}
                if(!vehicle.RouteBlocked&&RouteValidator?.Invoke(vehicle)==false){vehicle.RouteBlocked=true;vehicle.Hold();}
            }
        }
    }
}
