using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ForesTycoon;

internal static class TrafficRuleBenchmark
{
    internal static void Run()
    {
        foreach (int side in new[] { 64, 256 }) {
            using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(side + 1, 42), enableRendering: false);
            int tile = Enumerable.Range(0, world.Map.Tiles.Count).First(world.Map.CanCarrySkidTrail);
            world.QueueSkidTrailPath(tile, tile, false); world.ExecutePendingCommands();
            world.QueuePassage(new(tile, TrafficVehicleKind.Forwarder, 0, 20000, .5)); world.ExecutePendingCommands();
            world.Map.SetSkidTrailState(tile, 0, 0);
            const int events = 3000;
            for (int i = 0; i < events; i++) world.QueuePassage(new(tile, TrafficVehicleKind.Forwarder, i, 20000, .5));
            var frames = new List<double>(); int units = 0;
            while (world.ForegroundPending) {
                long start = Stopwatch.GetTimestamp();
                do { world.ContinueCommands(16); units++; }
                while (world.ForegroundPending && Stopwatch.GetElapsedTime(start).TotalMilliseconds < 4);
                frames.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            if (world.PendingTrafficPassages != 0 || world.CommandPending || world.Map.GetSkidTrailWear(tile) < .99f)
                throw new InvalidOperationException("Traffic benchmark did not finish correctly.");
            frames.Sort();
            double Percentile(double p) => frames[Math.Clamp((int)Math.Ceiling(frames.Count * p) - 1, 0, frames.Count - 1)];
            Console.WriteLine($"{side}x{side}: {events} passages; {frames.Count} work frames; {units} bounded calls; CPU p95={Percentile(.95):F3} ms p99={Percentile(.99):F3} ms max={frames[^1]:F3} ms total={frames.Sum():F3} ms");
        }
        Console.WriteLine("Headless CPU work only after one warm-up passage; 4 ms host budget, 16 deterministic work units per call. Setup, enqueue and graphics excluded; no hard real-time guarantee.");
    }
}
