using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OpenTK.Mathematics;

namespace ForesTycoon;

internal static class VehicleResponseBenchmark
{
    internal static void Run()
    {
        foreach (int count in new[] { 1000, 10000 }) {
            var geometry = new VehicleRoadRoute(new[] { Vector3.Zero, new Vector3(16, 0, 1) }, new Vector2[2]);
            VehicleSystem Create() {
                var system = new VehicleSystem(roadRouteFactory: _ => geometry) { UseCargoStops = false,
                    Spec = TruckSpec.Default with { Rules = new ForesTycoon.Rules.VehicleRuleProgram(WorldBehaviorPolicy.DefaultDocument()) } };
                for (int id = 0; id < count; id++) system.Spawn(new[] { 0, 1 }, .4);
                return system;
            }
            var whole = Create(); var sliced = Create();
            var ticks = new List<double>(); var slices = new List<double>();
            for (int tick = 0; tick < 30; tick++) {
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
            ticks.Sort(); slices.Sort();
            double P(List<double> values, double q) => values[Math.Min(values.Count - 1, (int)(values.Count * q))];
            Console.WriteLine($"{count} physical trucks, 30 ticks, first 3 warmup: whole p95/p99/max {P(ticks,.95):F3}/{P(ticks,.99):F3}/{ticks[^1]:F3} ms; "
                + $"64-unit slice p95/p99/max {P(slices,.95):F3}/{P(slices,.99):F3}/{slices[^1]:F3} ms; exact complete vehicle state equal: {equal}");
            if (!equal) throw new InvalidOperationException("Vehicle slicing changed the result.");
        }
    }
}
