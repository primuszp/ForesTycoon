using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using ForesTycoon.Rules;

namespace ForesTycoon;

internal static class DepotRuleBenchmark
{
    internal static void Run()
    {
        foreach (int size in new[] { 128, 256 }) {
            ForestryLogistics Create() {
                var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(size + 1, 42), (_, _) => 4);
                var model = WorldBehaviorPolicy.DefaultDocument();
                foreach (var graph in model.Graphs.Where(g => g.Kind == "logisticsDepot")) {
                    graph.Disabled = true; graph.DisabledValue = graph.Hook == "depot.allow" ? 1 : 1024;
                }
                var logistics = new ForestryLogistics(map, new ForestSystem(map, new ForestStand[map.Tiles.Count])) {
                    Behaviors = new WorldBehaviorPolicy(model) };
                // Exercise the growing harvest-site scan as well as per-actor creation.
                logistics.Sites.Add(new() { Tiles = Enumerable.Range(size * 4, size * size - size * 4).ToArray() });
                return logistics;
            }
            var whole = Create(); var sliced = Create(); var slices = new List<double>();
            long start = Stopwatch.GetTimestamp(); bool placed = whole.PlaceDepot(size + 1);
            double total = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var result = new WorkResult<bool>();
            using (var work = sliced.PlaceDepotWork(size + 1, result).GetEnumerator()) {
                bool pending = true;
                while (pending) {
                    start = Stopwatch.GetTimestamp();
                    for (int unit = 0; unit < 64; unit++) if (!SimulationWork.MoveNext(work)) { pending = false; break; }
                    slices.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
            }
            bool equal = JsonSerializer.Serialize(whole.Capture()) == JsonSerializer.Serialize(sliced.Capture());
            slices.Sort();
            Console.WriteLine($"{size}x{size}, 3072 actors, {size * size - size * 4} harvest tiles: whole {total:F3} ms; 64-unit slice p95/p99/max {slices[(int)(slices.Count * .95)]:F3}/{slices[(int)(slices.Count * .99)]:F3}/{slices[^1]:F3} ms; exact state equal: {equal}");
            if (!placed || !result.Value || !equal) throw new InvalidOperationException("Depot slicing failed.");
        }
    }
}
