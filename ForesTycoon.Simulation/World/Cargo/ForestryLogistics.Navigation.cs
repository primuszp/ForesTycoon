using System;
using System.Collections.Generic;

namespace ForesTycoon;

partial class ForestryLogistics
{
    private static bool AtDock(Vehicle vehicle, List<int> docks)
    {
        vehicle.GetSegment(vehicle.RoutePosition, out int from, out int to, out float amount);
        return amount < 1e-6f && docks.Contains(from) || amount > 1 - 1e-6f && docks.Contains(to);
    }

    private bool CanMachineTraverse(ForestMachine machine, int from, int to)
    {
        if (!terrain.IsValidTileId(from) || !terrain.IsValidTileId(to)) return false;
        Span<int> neighbours = stackalloc int[4];
        int count = terrain.GetTileNeighbours(from, neighbours); bool adjacent = false;
        for (int i = 0; i < count; i++) adjacent |= neighbours[i] == to;
        if (!adjacent || !CanDriveBetween(from, to)) return false;
        if (machine.Kind == ForestMachineKind.Forwarder)
            return (terrain.IsNetworkTile(from) || IsDepotTile(from) || StackAt(from) != null)
                && (terrain.IsNetworkTile(to) || IsDepotTile(to));
        return Passable(machine.Site, from) && Passable(machine.Site, to);
    }

    private IEnumerable<int> MachineContinuationWork(ForestMachine machine, int from, WorkResult<int[]> result)
    {
        result.Value = null;
        if (machine.Kind == ForestMachineKind.Forwarder && machine.Goal is ForestMachineState.Loading or ForestMachineState.Unloading) {
            int target = machine.Goal == ForestMachineState.Loading ? machine.Source?.Tile ?? -1 : machine.Destination;
            foreach (int unit in ForwarderPathWork(machine, from, target, result)) yield return unit;
        } else {
            int target = machine.Goal == ForestMachineState.Unloading ? machine.Target?.Tile ?? -1 : machine.Path[^1];
            foreach (int unit in FindPathWork(machine.Site, from, t => t == target, result,
                networkOnly: machine.Kind == ForestMachineKind.Forwarder, agent: machine)) yield return unit;
        }
    }

    private IEnumerable<int> ForwarderOrderPathWork(ForestMachine machine, int source, int destination, WorkResult<int[]> result)
    {
        var approach = new WorkResult<int[]>();
        foreach (int unit in ForwarderPathWork(machine, OrderOrigin(machine), source, approach)) yield return unit;
        if (approach.Value == null) { result.Value = null; yield break; }
        foreach (int unit in ForwarderPathWork(machine, approach.Value[^1], destination, result)) yield return unit;
    }

    private List<int> DestinationDocks(int destination)
    {
        if (!terrain.IsValidTileId(destination)) return new List<int>();
        var depot = Depots.Find(d => Array.IndexOf(d.Footprint, destination) >= 0);
        return terrain.FindNetworkDocks(MillAt(destination)?.Footprint ?? depot?.Footprint ?? new[] { destination });
    }

    private IEnumerable<int> BestTruckPathWork(FleetTruck truck, int from, IEnumerable<int> ends, WorkResult<(int[] Path, float Cost)> result)
    {
        result.Value = (Array.Empty<int>(), float.PositiveInfinity);
        foreach (int end in ends) {
            var candidate = new WorkResult<(int[] Path, float Cost)>();
            foreach (int unit in TruckPathWork(truck, from, end, candidate)) yield return unit;
            if (candidate.Value.Path.Length > 0 && candidate.Value.Cost < result.Value.Cost) result.Value = candidate.Value;
            yield return 1;
        }
    }

    private readonly record struct TruckJourney(int[] Route, int[] Approach, float Cost);

    private IEnumerable<int> TruckJourneyWork(FleetTruck truck, int source, int destination, WorkResult<TruckJourney> result, int fixedSource = -1)
    {
        result.Value = new(null, null, float.PositiveInfinity);
        var starts = DestinationDocks(source); var ends = DestinationDocks(destination);
        var homes = truck?.Home != null && truck.Phase == TruckPhase.Parked && fixedSource < 0
            ? terrain.FindNetworkDocks(truck.Home.Footprint) : null;
        foreach (int start in starts) {
            if (fixedSource >= 0 && start != fixedSource) continue;
            int[] approach = new[] { start }; float approachCost = 0;
            if (homes != null) {
                approach = null; approachCost = float.PositiveInfinity;
                foreach (int home in homes) {
                    var candidate = new WorkResult<(int[] Path, float Cost)>();
                    foreach (int unit in TruckPathWork(truck, home, start, candidate)) yield return unit;
                    if (candidate.Value.Path.Length > 0 && candidate.Value.Cost < approachCost) {
                        approach = candidate.Value.Path; approachCost = candidate.Value.Cost;
                    }
                }
                if (approach == null) continue;
            }
            var route = new WorkResult<(int[] Path, float Cost)>();
            foreach (int unit in BestTruckPathWork(truck, start, ends, route)) yield return unit;
            if (route.Value.Path.Length >= 1 && approachCost + route.Value.Cost < result.Value.Cost)
                result.Value = new(route.Value.Path, approach, approachCost + route.Value.Cost);
        }
    }

    private readonly Dictionary<int, (ulong Version, int[] Route, bool Connected)> truckNavigation = new();

    private IEnumerable<int> RefreshTruckNavigationWork(FleetTruck truck)
    {
        var vehicle = truck.Vehicle;
        if (!truckNavigation.TryGetValue(truck.Id, out var cached) || cached.Version != terrain.NavigationVersion || cached.Route != vehicle.Route) {
            bool connected = true;
            for (int i = 0; i < vehicle.Route.Length; i++) {
                if (!terrain.IsNetworkTile(vehicle.Route[i]) || i > 0 && !terrain.AreNetworkNeighbours(vehicle.Route[i - 1], vehicle.Route[i])) connected = false;
                yield return 1;
            }
            cached = (terrain.NavigationVersion, vehicle.Route, connected); truckNavigation[truck.Id] = cached;
        }
        bool sourceEndpoint = vehicle.Transit || terrain.FindNetworkDocks(new[] { truck.Source.Tile }).Contains(vehicle.Route[0]);
        bool destinationEndpoint = vehicle.Transit || DestinationDocks(truck.Destination).Contains(vehicle.Route[^1]);
        // A loaded truck can finish delivery even when its source is no longer connected behind it.
        bool endpoints = vehicle.Transit || vehicle.TransportState switch {
            VehicleTransportState.Hauling or VehicleTransportState.Unloading => destinationEndpoint,
            VehicleTransportState.Returning => sourceEndpoint,
            _ => sourceEndpoint && destinationEndpoint
        };
        if (cached.Connected && endpoints) { vehicle.RouteBlocked = false; yield break; }
        vehicle.RouteBlocked = true; vehicle.Hold();
        foreach (int unit in RerouteTruckWork(truck)) yield return unit;
    }

    private IEnumerable<int> RerouteTruckWork(FleetTruck truck)
    {
        var vehicle = truck.Vehicle;
        vehicle.GetSegment(vehicle.RoutePosition, out int from, out int to, out float fraction);
        if (fraction >= 1 - 1e-6f) { from = to; fraction = 0; }
        bool between = fraction > 1e-6f;
        if (!terrain.IsNetworkTile(from) || between && !terrain.AreNetworkNeighbours(from, to)) yield break;
        bool outbound = vehicle.TransportState is VehicleTransportState.Hauling or VehicleTransportState.Unloading
            || !vehicle.Transit && vehicle.TransportState is VehicleTransportState.Loading or VehicleTransportState.Waiting
                && AtDock(vehicle, terrain.FindNetworkDocks(new[] { truck.Source.Tile }));
        var targets = vehicle.Transit ? truck.Phase == TruckPhase.ToHome ? terrain.FindNetworkDocks(truck.Home.Footprint)
            : terrain.FindNetworkDocks(new[] { truck.Source.Tile })
            : outbound ? DestinationDocks(truck.Destination) : terrain.FindNetworkDocks(new[] { truck.Source.Tile });
        var ahead = new WorkResult<(int[] Path, float Cost)>();
        foreach (int unit in BestTruckPathWork(truck, between ? to : from, targets, ahead)) yield return unit;
        if (ahead.Value.Path.Length == 0) yield break;
        var active = new List<int>(); if (between) active.Add(from);
        foreach (int tile in ahead.Value.Path) { active.Add(tile); yield return 1; }
        if (active.Count == 1) {
            // Unloading is legal here even if a road behind the truck has disappeared.
            if (!vehicle.Transit && vehicle.TransportState == VehicleTransportState.Unloading || vehicle.Transit && vehicle.TransitArrived)
                vehicle.RouteBlocked = false;
            yield break;
        }
        var route = new List<int>(); double position = fraction;
        if (vehicle.Transit) {
            foreach (int tile in active) { route.Add(tile); yield return 1; }
        } else if (outbound) {
            int[] prefix = null; float best = float.PositiveInfinity;
            foreach (int source in terrain.FindNetworkDocks(new[] { truck.Source.Tile })) {
                var candidate = new WorkResult<(int[] Path, float Cost)>();
                foreach (int unit in TruckPathWork(truck, source, from, candidate)) yield return unit;
                if (candidate.Value.Path.Length > 0 && candidate.Value.Cost < best
                    && !(between && candidate.Value.Path.Length > 1 && candidate.Value.Path[^2] == to)) {
                    prefix = candidate.Value.Path; best = candidate.Value.Cost;
                }
            }
            if (prefix != null) foreach (int tile in prefix) { route.Add(tile); yield return 1; }
            else route.Add(from);
            position += route.Count - 1;
            for (int i = 1; i < active.Count; i++) { route.Add(active[i]); yield return 1; }
        } else {
            for (int i = active.Count - 1; i >= 0; i--) { route.Add(active[i]); yield return 1; }
            var behind = new WorkResult<(int[] Path, float Cost)>();
            foreach (int unit in BestTruckPathWork(truck, from, DestinationDocks(truck.Destination), behind)) yield return unit;
            double directed = route.Count - 1 - fraction;
            for (int i = 1; i < behind.Value.Path.Length && !(between && behind.Value.Path[1] == to); i++) { route.Add(behind.Value.Path[i]); yield return 1; }
            position = 2 * (route.Count - 1) - directed;
        }
        var path = route.ToArray(); var geometry = new WorkResult<VehicleRoadRoute>();
        if (Vehicles.RoadRouteWorkFactory != null) {
            foreach (int unit in Vehicles.RoadRouteWorkFactory(path, geometry)) yield return unit;
        } else geometry.Value = Vehicles.RoadRouteFactory?.Invoke(path);
        vehicle.ReplaceRoute(path, geometry.Value, position);
    }
}
