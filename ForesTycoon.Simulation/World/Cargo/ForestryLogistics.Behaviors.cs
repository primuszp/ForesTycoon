using System;

namespace ForesTycoon
{
    internal sealed partial class ForestryLogistics
    {
        private float RouteCost(string kind, int id, float cargo, float capacity, int from, int to, float unused)
        {
            double surface = terrain.IsRoadTile(to) ? terrain.GetRoadPaving(to) switch { RoadPaving.Asphalt => 1, RoadPaving.Macadam => 2, _ => 4 } : terrain.IsSkidTrail(to) ? 3 : 0;
            double damage = terrain.IsRoadTile(to) ? 1 - terrain.GetRoadCondition(to) : terrain.GetSkidTrailWear(to);
            var inputs = new LogisticsInputs(MachineCargo: cargo, MachineCapacity: capacity, RouteDamage: damage, RouteSurface: surface, RouteFrom: from, RouteTo: to);
            if (rules.Evaluate(Behaviors, "route.allow", kind, (ulong)id, inputs) < .5) return float.PositiveInfinity;
            float cost = (float)rules.Evaluate(Behaviors, "route.cost", kind, (ulong)id, inputs);
            return float.IsFinite(cost) && cost > 0 ? cost : float.PositiveInfinity;
        }
        private System.Collections.Generic.IEnumerable<int> TruckPathWork(FleetTruck truck, int from, int to, WorkResult<(int[] Path, float Cost)> result)
            => terrain.FindNetworkPathWork(from, to, result,
                (a, b, unused) => RouteCost("truck", truck?.Id ?? -1, truck?.Vehicle?.CargoAmount ?? 0, truck?.Vehicle?.CargoCapacity ?? 25, a, b, unused));
        private double TruckRule(Vehicle vehicle, string hook, double dt, double requested, double loaded)
        {
            var truck = TruckOf(vehicle);
            return rules.Evaluate(Behaviors, hook, "truck", (ulong)(truck?.Id ?? vehicle.Id), new(
                StepSeconds: dt, MachineKind: 2, MachineCargo: vehicle.CargoAmount, MachineCapacity: vehicle.CargoCapacity,
                MachineWear: truck?.Upkeep.Wear ?? 0, MachineHomeRequested: truck?.HomeRequested == true ? 1 : 0,
                SourceStock: truck?.Source?.Volume ?? 0, TransferRequested: requested, TransferLoaded: loaded,
                TransferUnloading: vehicle.TransportState == VehicleTransportState.Unloading ? 1 : 0,
                MachineControl: (int)(truck?.Control ?? BehaviorAction.Autonomous)));
        }
        private static int TruckState(FleetTruck truck) => truck.Vehicle?.RouteBlocked == true ? 8 : truck.Phase switch {
            TruckPhase.Parked => 0, TruckPhase.ToWork => 1, TruckPhase.ToHome => 6,
            _ => truck.Vehicle.TransportState switch {
                VehicleTransportState.Loading => 2, VehicleTransportState.Hauling => 3, VehicleTransportState.Unloading => 4,
                VehicleTransportState.Returning => 5, _ => 7
            }
        };
        private LogisticsInputs TruckInputs(FleetTruck truck, double seconds = 0) => new(
            StepSeconds: seconds, MachineKind: 2, MachineState: (int)truck.Phase,
            MachineCargo: truck.Vehicle?.CargoAmount ?? 0, MachineCapacity: truck.Vehicle?.CargoCapacity ?? 0,
            MachineWear: truck.Upkeep.Wear, MachineHomeRequested: truck.HomeRequested ? 1 : 0,
            SourceStock: truck.Source?.Volume ?? 0, MachineControl: (int)truck.Control,
            MachineWorking: truck.Phase is TruckPhase.ToWork or TruckPhase.Working ? 1 : 0,
            MachineBroken: truck.Upkeep.Broken ? 1 : 0, MachineHasVehicle: truck.Vehicle == null ? 0 : 1,
            MachineTransit: truck.Vehicle?.Transit == true ? 1 : 0, MachineTransportState: (int)(truck.Vehicle?.TransportState ?? VehicleTransportState.Waiting),
            MachineSpeed: truck.Vehicle?.CurrentSpeed ?? 0);
        private bool TruckWait(FleetTruck truck) => rules.Evaluate(Behaviors, "control.allow", "truck", (ulong)truck.Id, TruckInputs(truck)) < .5;
    }
}
