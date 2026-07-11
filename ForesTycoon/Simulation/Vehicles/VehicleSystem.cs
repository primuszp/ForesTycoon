using System.Collections.Generic;
using System;

namespace ForesTycoon
{
    sealed class VehicleSystem
    {
        private readonly List<Vehicle> vehicles = new List<Vehicle>();
        private int nextId = 1;

        public IReadOnlyList<Vehicle> Vehicles => vehicles;
        public int Count => vehicles.Count;

        public Vehicle Spawn(int[] route, double speedTilesPerSecond = 1.5)
        {
            Vehicle vehicle = new Vehicle(nextId++, route, speedTilesPerSecond);
            vehicles.Add(vehicle);
            return vehicle;
        }

        public void Update(double deltaSeconds)
        {
            for (int i = 0; i < vehicles.Count; i++) vehicles[i].Update(deltaSeconds);
        }

        public void Clear() => vehicles.Clear();

        public int RemoveInvalidRoutes(Func<int, bool> isRoadTile)
        {
            if (isRoadTile == null) throw new ArgumentNullException(nameof(isRoadTile));
            return vehicles.RemoveAll(vehicle =>
            {
                for (int i = 0; i < vehicle.Route.Length; i++)
                    if (!isRoadTile(vehicle.Route[i])) return true;
                return false;
            });
        }
    }
}
