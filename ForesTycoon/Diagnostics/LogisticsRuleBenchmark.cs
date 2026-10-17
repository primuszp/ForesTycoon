using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ForesTycoon;

internal static class LogisticsRuleBenchmark
{
    internal static void Run()
    {
        foreach (int count in new[] { 1000, 10000 }) {
            ForestryLogistics Create() {
                var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(129, 42), (_, _) => 4);
                var logistics = new ForestryLogistics(map, new ForestSystem(map, new ForestStand[map.Tiles.Count])) {
                    Behaviors = new WorldBehaviorPolicy(WorldBehaviorPolicy.DefaultDocument()),
                    Tuning = GameTuning.FromOverrides(new Dictionary<string, double> {
                        [nameof(Tune.WearPerSecond)] = 0, [nameof(Tune.BreakdownHazard)] = 0 }) };
                for (int i = 0; i < count; i++) {
                    var source = new TimberStack { Id = i + 1, Tile = i, Volume = 100, Value = 1000 };
                    logistics.Stacks.Add(source);
                    logistics.Machines.Add(new() { Id = i + 1, Kind = ForestMachineKind.Forwarder, TuningSource = logistics,
                        Source = source, Path = new[] { i }, State = ForestMachineState.Loading, Upkeep = new((uint)i + 1) });
                }
                return logistics;
            }
            var whole = Create(); var sliced = Create();
            var ticks = new List<double>(); var slices = new List<double>();
            for (int tick = 0; tick < 220; tick++) {
                long started = System.Diagnostics.Stopwatch.GetTimestamp(); whole.Update(1d / 30);
                if (tick >= 3) ticks.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                using var work = sliced.UpdateWork(1d / 30).GetEnumerator(); bool pending = true;
                while (pending) {
                    started = System.Diagnostics.Stopwatch.GetTimestamp();
                    for (int unit = 0; unit < 64; unit++) if (!SimulationWork.MoveNext(work)) { pending = false; break; }
                    if (tick >= 3) slices.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
            }
            bool equal = JsonSerializer.Serialize(whole.Capture()) == JsonSerializer.Serialize(sliced.Capture());
            bool transferred = whole.Machines.All(m => m.Cargo > 0 && m.Source.Volume < 100 && m.LogTransferVolume == 0);
            ticks.Sort(); slices.Sort();
            double P(List<double> v, double q) => v[Math.Min(v.Count - 1, (int)(v.Count * q))];
            Console.WriteLine($"128x128, {count} loading machines, 220 ticks (3 warmup), measured grasp and release: whole p95/p99/max {P(ticks,.95):F3}/{P(ticks,.99):F3}/{ticks[^1]:F3} ms; "
                + $"64-unit slice p95/p99/max {P(slices,.95):F3}/{P(slices,.99):F3}/{slices[^1]:F3} ms; exact complete logistics state equal: {equal}; completed grasp/release: {transferred}");
            if (!transferred) throw new InvalidOperationException("Benchmark did not exercise the transfer.");
            if (!equal) throw new InvalidOperationException("Logistics slicing changed the result.");
        }
    }
}
