using System;

namespace ForesTycoon
{
    internal sealed partial class ForestryLogistics
    {
        private float RouteCost(string kind, int id, float cargo, float capacity, int from, int to, float native)
        {
            if (Behaviors == null) return native;
            double surface = terrain.IsRoadTile(to) ? terrain.GetRoadPaving(to) == RoadPaving.Asphalt ? 1 : 2 : terrain.IsSkidTrail(to) ? 3 : 0;
            double damage = terrain.IsRoadTile(to) ? 1 - terrain.GetRoadCondition(to) : terrain.GetSkidTrailWear(to);
            var query = new BehaviorQuery("route.allow", kind, (ulong)id, 1, Time: forest.ForestYear,
                State: damage, Amount: cargo, Capacity: capacity, From: from, To: to, Surface: surface);
            if (Behaviors.Evaluate(query) < .5) return float.PositiveInfinity;
            return (float)Behaviors.Evaluate(query with { Hook = "route.cost", Native = native });
        }
        private int[] TruckPath(FleetTruck truck, int from, int to, out float cost) => terrain.FindNetworkPath(from, to, out cost,
            Behaviors == null ? null : (a, b, native) => RouteCost("truck", truck?.Id ?? -1, truck?.Vehicle?.CargoAmount ?? 0, truck?.Vehicle?.CargoCapacity ?? 25, a, b, native));
        private double TruckRule(Vehicle vehicle, string hook, double native, double dt)
        {
            var truck = TruckOf(vehicle);
            if (truck?.HomeRequested == true && hook == "loading.depart" && vehicle.CargoAmount > 0) return 1;
            if (truck == null || Behaviors == null) return native;
            return Behaviors.Evaluate(new(hook, "truck", (ulong)truck.Id, native, dt, forest.ForestYear,
                hook == "loading.depart" ? vehicle.CargoFill : vehicle.TransportState == VehicleTransportState.Unloading ? 1 : 0,
                vehicle.CargoAmount, vehicle.CargoCapacity, truck.Source?.Volume ?? 0));
        }
        private static int TruckState(FleetTruck truck) => truck.Vehicle?.RouteBlocked == true ? 8 : truck.Phase switch {
            TruckPhase.Parked => 0, TruckPhase.ToWork => 1, TruckPhase.ToHome => 6,
            _ => truck.Vehicle.TransportState switch {
                VehicleTransportState.Loading => 2, VehicleTransportState.Hauling => 3, VehicleTransportState.Unloading => 4,
                VehicleTransportState.Returning => 5, _ => 7
            }
        };
        private static bool TruckWait(FleetTruck truck)
        {
            var action = truck.Control; var vehicle = truck.Vehicle;
            return action == BehaviorAction.Wait
                || action == BehaviorAction.LoadOnly && (vehicle == null || vehicle.Transit || vehicle.TransportState is not (VehicleTransportState.Loading or VehicleTransportState.Waiting))
                || action == BehaviorAction.UnloadOnly && (vehicle == null || vehicle.Transit || vehicle.TransportState != VehicleTransportState.Unloading)
                || action == BehaviorAction.TravelOnly && vehicle != null && !vehicle.Transit && vehicle.TransportState is VehicleTransportState.Loading or VehicleTransportState.Waiting or VehicleTransportState.Unloading;
        }
    }
}
