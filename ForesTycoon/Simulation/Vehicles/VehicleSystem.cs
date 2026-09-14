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
            vehicle.Load(timberCargo.Load(vehicle.CargoCapacity));
            vehicles.Add(vehicle);
            return vehicle;
        }

        public void Update(double deltaSeconds)
        {
            for (int i = 0; i < vehicles.Count; i++)
            {
                Vehicle vehicle = vehicles[i];
                vehicle.Update(deltaSeconds);
                ProcessRouteEndpoints(vehicle);
            }
        }

        private void ProcessRouteEndpoints(Vehicle vehicle)
        {
            int last = vehicle.Route.Length - 1;
            long previousBoundary = (long)Math.Floor(vehicle.PreviousRoutePosition / last);
            long currentBoundary = (long)Math.Floor(vehicle.RoutePosition / last);
            for (long boundary = previousBoundary + 1; boundary <= currentBoundary; boundary++)
            {
                if ((boundary & 1L) != 0L)
                    timberCargo.Deliver(vehicle.Unload());
                else
                    vehicle.Load(timberCargo.Load(vehicle.CargoCapacity - vehicle.CargoAmount));
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
