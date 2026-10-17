using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace ForesTycoon;

internal static class OrderRuleBenchmark
{
    internal static void Run()
    {
        foreach (int size in new[] { 128, 256 }) {
            int source = (size - 3) * size + 3, target = 3 * size + 3;
            (ForestryLogistics Logistics, VehicleSystem Vehicles) Create() {
                var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(size + 1, 42), (_, _) => 4);
                map.BuildRoadTilePath(size + 2, (size - 2) * size + 2, RoadPaving.Macadam);
                var logistics = new ForestryLogistics(map, new ForestSystem(map, new ForestStand[map.Tiles.Count])) {
                    Behaviors = new WorldBehaviorPolicy(WorldBehaviorPolicy.DefaultDocument()) };
                var vehicles = new VehicleSystem { RoadRouteWorkFactory = (path, result) => VehicleRoadRoute.CreateWork(map, path, result) };
                logistics.Vehicles = vehicles;
                if (!logistics.PlaceDepot(size + 3) || !logistics.PlaceStack(source) || !logistics.PlaceStack(target))
                    throw new InvalidOperationException(logistics.Status);
                logistics.StackAt(source).Add(20, 200);
                return (logistics, vehicles);
            }
            var whole = Create(); var sliced = Create(); var slices = new List<double>();
            long start = Stopwatch.GetTimestamp();
            bool assigned = whole.Logistics.AssignTruck(whole.Logistics.Trucks.Single(), whole.Logistics.StackAt(source), target);
            double total = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var result = new WorkResult<bool>();
            using (var work = sliced.Logistics.AssignTruckWork(sliced.Logistics.Trucks.Single(), sliced.Logistics.StackAt(source), target, result).GetEnumerator()) {
                bool pending = true;
                while (pending) {
                    start = Stopwatch.GetTimestamp();
                    for (int unit = 0; unit < 64; unit++) if (!SimulationWork.MoveNext(work)) { pending = false; break; }
                    slices.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
            }
            bool equal = JsonSerializer.Serialize(whole.Logistics.Capture()) == JsonSerializer.Serialize(sliced.Logistics.Capture())
                && JsonSerializer.Serialize(whole.Vehicles.Capture()) == JsonSerializer.Serialize(sliced.Vehicles.Capture());
            slices.Sort();
            Console.WriteLine($"{size}x{size}, long truck order including approach, route graph and vehicle geometry: whole {total:F3} ms; {slices.Count} slices of 64 units, p95/p99/max {slices[(int)(slices.Count * .95)]:F3}/{slices[(int)(slices.Count * .99)]:F3}/{slices[^1]:F3} ms; exact state equal: {equal}");
            if (!assigned || !result.Value || !equal) throw new InvalidOperationException("Order slicing failed.");
        }
    }
}
