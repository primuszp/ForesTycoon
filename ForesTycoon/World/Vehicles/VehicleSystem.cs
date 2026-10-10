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
        public bool UseRoadPhysics { get; set; } = true;
        /// <summary>Vehicle seconds per world second (the world's vehicle clock runs faster than its calendar).</summary>
        internal double TimeScale { get; set; } = 1;
        public bool UseCargoStops { get; set; } = true;
        internal Func<Vehicle,float,float> SourceLoader;
        internal Action<Vehicle,float> DestinationReceiver;
        internal Func<Vehicle,bool> RouteValidator;
        /// <summary>Surface and condition (1 new … 0 ruined) of a road tile.</summary>
        internal Func<int,(RoadSurface Surface,float Condition)> RoadState;
        /// <summary>Wears a road tile; the amount is for one pass of a loaded truck on macadam.</summary>
        internal Action<int,float> RoadWear;
        /// <summary>Truck physics and road load every vehicle uses (the world's tuning).</summary>
        internal TruckSpec Spec = TruckSpec.Default;
        internal Func<float,float> RoadLoad;
        internal Vehicle SpawnLogistics(int[] route,int[] sources,int mill)
        {
            var vehicle=new Vehicle(nextId++,route,1.5,roadRoute:RoadRouteFactory?.Invoke(route),roadPhysics:UseRoadPhysics);
            vehicle.SourceTiles=(int[])sources.Clone();vehicle.SawmillTileId=mill;vehicle.CargoStopsEnabled=true;
            vehicle.TransportState=VehicleTransportState.Loading;vehicle.Hold();vehicles.Add(vehicle);return vehicle;
        }

        /// <summary>A fleet truck driving one way along <paramref name="route"/>; <see cref="Vehicle.TransitArrived"/> marks the end.</summary>
        internal Vehicle SpawnTransit(int[] route)
        {
            var vehicle=new Vehicle(nextId++,route,1.5,roadRoute:RoadRouteFactory?.Invoke(route),roadPhysics:UseRoadPhysics){Transit=true};
            vehicles.Add(vehicle);return vehicle;
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

        public void Update(double deltaSeconds)
        {
            if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            deltaSeconds *= TimeScale;
            for (int i = 0; i < vehicles.Count; i++)
            {
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
                vehicle.RoadState = RoadState; vehicle.RoadWear = RoadWear; vehicle.Spec = Spec; vehicle.RoadLoad = RoadLoad;
                if(vehicle.RouteBlocked||vehicle.Broken){vehicle.Hold();return;}
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
                switch(vehicle.TransportState){
                    case VehicleTransportState.Waiting:
                    case VehicleTransportState.Loading:
                        vehicle.Hold();
                        float amount=SourceLoader?.Invoke(vehicle,Math.Min(vehicle.CargoCapacity-vehicle.CargoAmount,(float)dt*vehicle.CargoCapacity/3))??0;
                        vehicle.Load(amount);
                        if(vehicle.CargoAmount>=vehicle.CargoCapacity-0.001f||(amount<0.000001f&&vehicle.CargoAmount>0))vehicle.TransportState=VehicleTransportState.Hauling;
                        else vehicle.TransportState=amount>0?VehicleTransportState.Loading:VehicleTransportState.Waiting;
                        break;
                    case VehicleTransportState.Unloading:
                        vehicle.Hold();float delivered=vehicle.TakeCargo((float)dt*vehicle.CargoCapacity/3);
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
