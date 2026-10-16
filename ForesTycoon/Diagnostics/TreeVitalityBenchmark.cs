using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ForesTycoon;

internal static class TreeVitalityBenchmark
{
    internal static void Run()
    {
        foreach (int side in new[] { 64, 256 }) foreach (bool massDeath in new[] { false, true }) {
            using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(side + 1, 42), enableRendering: false);
            // Warm the exact graph path before measuring the large stand.
            SimulationWork.Complete(world.Forest.RefreshEnvironmentRatesWork());
            SimulationWork.Complete(world.TreeMonthWork(1d / 12, 0));
            int initial = world.Forest.IndividualTreeCount;
            if (massDeath) foreach (var entry in world.Forest.IndividualTrees.Patches)
                for (int i = 0; i < entry.Value.Count; i++)
                    entry.Value.Trees[i] = entry.Value.Trees[i] with { Health = .1f, StressYears = 5 };
            using var work = world.Forest.UpdateWork(EcologyTime.DefaultGameSecondsPerYear / 12).GetEnumerator();
            // One actual forest month includes resource prep, all monthly graphs, death, and resource/statistics refresh.
            var frames = new List<double>(); int calls = 0, units = 0; bool more = true;
            while (more) {
                long started = Stopwatch.GetTimestamp();
                do {
                    for (int i = 0; i < 16 && more; i++) { more = work.MoveNext(); units++; }
                    calls++;
                } while (more && Stopwatch.GetElapsedTime(started).TotalMilliseconds < 4);
                frames.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            if (world.RuleBook.StepPending || world.RuleBook.LastError != null || massDeath && world.Forest.IndividualTreeCount >= initial)
                throw new InvalidOperationException("Tree lifecycle benchmark did not complete death rules.");
            frames.Sort();
            double P(double p) => frames[Math.Clamp((int)Math.Ceiling(frames.Count * p) - 1, 0, frames.Count - 1)];
            Console.WriteLine($"{side}x{side} {(massDeath ? "mass death" : "growth")}: {initial} living trees initially, {world.Forest.IndividualTreeCount} after month; {frames.Count} work frames, {calls} calls, {units} units; p95={P(.95):F3} ms p99={P(.99):F3} ms max={frames[^1]:F3} ms total={frames.Sum():F3} ms");
        }
        Console.WriteLine("Headless CPU work; warm graph path; 4 ms host budget and 16 units per call, including GC. Setup and graphics excluded. Work slices are deterministic, elapsed-time limits are soft.");
    }
}
