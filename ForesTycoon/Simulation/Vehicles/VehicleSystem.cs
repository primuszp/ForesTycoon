using System.Collections.Generic;
using System;

namespace ForesTycoon
{
    sealed class VehicleSystem : IWorldSystem
    {
        private readonly List<Vehicle> vehicles = new List<Vehicle>();
        private readonly TimberCargoSystem timberCargo;
        private int nextId = 1;
        private readonly Func<int[], VehicleRoadRoute> roadRouteFactory;
        public bool UseRoadPhysics { get; set; } = true;
        public bool UseCargoStops { get; set; } = true;

        public VehicleSystem(TimberCargoSystem timberCargo = null, Func<int[], VehicleRoadRoute> roadRouteFactory = null)
        {
            this.timberCargo = timberCargo ?? new TimberCargoSystem();
            this.roadRouteFactory = roadRouteFactory;
        }

        public IReadOnlyList<Vehicle> Vehicles => vehicles;
        public int Count => vehicles.Count;

        public Vehicle Spawn(int[] route, double speedTilesPerSecond = 1.5)
        {
            Vehicle vehicle = new Vehicle(nextId++, route, speedTilesPerSecond,
                roadRoute: roadRouteFactory?.Invoke(route), roadPhysics: UseRoadPhysics);
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
            for (int i = 0; i < vehicles.Count; i++)
            {
                Vehicle vehicle = vehicles[i];
                if (!vehicle.CargoStopsEnabled)
                {
                    vehicle.Update(deltaSeconds); ProcessRouteEndpoints(vehicle);
                    continue;
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
            return vehicles.RemoveAll(vehicle =>
            {
                for (int i = 0; i < vehicle.Route.Length; i++)
                    if (!isRoadTile(vehicle.Route[i]))
                    {
                        timberCargo.AddHarvested(vehicle.Unload());
                        return true;
                    }
                return false;
            });
        }
    }
}
