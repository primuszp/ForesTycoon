using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace ForesTycoon
{
    // Whole logical ticks versus bounded work slices, including a forest-month boundary on dense 64/128 maps.
    // No window/GPU: the measured scope is simulation CPU work, not total displayed frame time.
    internal static class RuleStepBenchmark
    {
        internal static void Run(params int[] sizes)
        {
            foreach (int size in sizes.Length == 0 ? new[] { 64, 128 } : sizes)
                foreach (var weather in new[] { WeatherPreset.Rain, WeatherPreset.Snow }) RunCase(size, weather);
        }

        internal static void RunHosts(params int[] sizes)
        {
            foreach (int size in sizes.Length == 0 ? new[] { 64, 128, 256 } : sizes) {
                using var world = new SimulationWorld(TerrainSettings.Default.WithNodeSize(size + 1, 42).WithForestPattern(ForestPattern.LargeMixed),
                    forestYearSeconds: 900);
                world.Environment.ForceWeather(WeatherPreset.Rain, 30, 600);
                bool restored = System.Environment.GetEnvironmentVariable("FORESTYCOON_RESTORED_HOST") == "1";
                if (restored) {
                    using var saved = new System.IO.MemoryStream(); world.Save(saved); saved.Position = 0; world.Load(saved);
                }
                if (System.Environment.GetEnvironmentVariable("FORESTYCOON_SERIAL_FLOWS") == "1") world.RuleBook.PlanLocalFlows = false;
                bool profile = System.Environment.GetEnvironmentVariable("FORESTYCOON_PROFILE_HOST") == "1";
                int seconds = profile ? 30 : 240;
                if (int.TryParse(System.Environment.GetEnvironmentVariable("FORESTYCOON_HOST_SECONDS"), out int requested) && requested is >= 1 and <= 900) seconds = requested;
                if (profile) { world.ProfileUpdates = true; world.EcologyWorkProfile = new(StringComparer.Ordinal); world.RuleBook.Profile = new(StringComparer.Ordinal); }
                Console.WriteLine($"Normal tempo: {size}x{size}, {world.ForestTreeCount} trees, 900 s/year; {seconds} simulated seconds; restored {restored}");
                MeasureHost(world, size, WeatherPreset.Rain, seconds * 60);
            }
        }

        internal static void RunMonthProfile()
        {
            foreach (int size in new[] { 64, 128 }) {
                using var world = new SimulationWorld(TerrainSettings.Default.WithNodeSize(size + 1, 42).WithForestPattern(ForestPattern.LargeMixed),
                    forestYearSeconds: 120);
                world.Environment.ForceWeather(WeatherPreset.Rain, 30, 600);
                world.Forest.ProfileMonthlyWork = true;
                world.RuleBook.Profile = new(StringComparer.Ordinal);
                var ticks = new List<double>();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                for (int tick = 0; tick < 310; tick++) {
                    long started = System.Diagnostics.Stopwatch.GetTimestamp();
                    world.Update(1d / 30);
                    ticks.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                var p = world.Forest.LastMonthProfile;
                string state = System.Text.Json.JsonSerializer.Serialize(world.CaptureCheckpoint());
                string hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(state)));
                Console.WriteLine($"{size}x{size}, {world.ForestTreeCount} trees: month close {p.CloseStandsMs:F3}; snapshot/seeds {p.SnapshotAndSeedsMs:F3}; "
                    + $"growth/mortality {p.GrowthAndMortalityMs:F3}; regeneration/publish {p.RegenerationAndPublishMs:F3} ms; "
                    + $"max tick {ticks.Max():F3} ms; {world.Forest.LastMonthlyPreparedTrees} prepared trees; allocated {allocated} B; state {hash}");
                foreach (var rule in world.RuleBook.Profile.OrderByDescending(p => p.Value).Take(12))
                    Console.WriteLine($"  rule {rule.Key}: {rule.Value:F3} ms (all 310 ticks)");
            }
        }

        private static void RunCase(int size, WeatherPreset preset)
        {
            var settings = TerrainSettings.Default.WithNodeSize(size + 1, 42).WithForestPattern(ForestPattern.LargeMixed);
            using var whole = new SimulationWorld(settings, forestYearSeconds: 120);
            using var sliced = new SimulationWorld(settings, forestYearSeconds: 120);
            foreach (var world in new[] { whole, sliced }) world.Environment.ForceWeather(preset, 30, 600);
            var full = new List<double>(); var slices = new List<double>();
            const int warmup = 60, measured = 330;
            int sliceCount = 0;
            for (int tick = 0; tick < warmup + measured; tick++) {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                whole.Update(1d / 30);
                if (tick >= warmup) full.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                bool done;
                do {
                    start = System.Diagnostics.Stopwatch.GetTimestamp();
                    done = sliced.ContinueUpdate(1d / 30, 64);
                    if (tick >= warmup) slices.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    sliceCount++;
                } while (!done);
            }
            full.Sort(); slices.Sort();
            double P(List<double> v, double q) => v[Math.Min(v.Count - 1, (int)(v.Count * q))];
            string first = System.Text.Json.JsonSerializer.Serialize(whole.CaptureCheckpoint());
            string second = System.Text.Json.JsonSerializer.Serialize(sliced.CaptureCheckpoint());
            bool equal = first == second && Fingerprint(whole) == Fingerprint(sliced);
            Console.WriteLine($"{size}x{size} {preset}: {sliced.ForestTreeCount} trees; whole tick p95/p99/max {P(full,.95):0.000}/{P(full,.99):0.000}/{full[^1]:0.000} ms; "
                + $"64-unit slice p95/p99/max {P(slices,.95):0.000}/{P(slices,.99):0.000}/{slices[^1]:0.000} ms; {sliceCount} slices; exact state equal: {equal}");
            if (!equal) throw new InvalidOperationException("Work slicing changed the simulation result.");
            MeasureHost(sliced, size, preset);
        }

        private static void MeasureHost(SimulationWorld world, int size, WeatherPreset preset, int frameCount = 900)
        {
            var runner = ForesTycoon.Engine.SimulationFrameRunner.Interactive();
            runner.Clock.Reset(world.SimulationTick);
            var frames = new List<double>(); double peakDebt = 0, warmPeakDebt = 0;
            bool paced = System.Environment.GetEnvironmentVariable("FORESTYCOON_PACED_HOST") == "1";
            var pacing = System.Diagnostics.Stopwatch.StartNew();
            double maximumBudget = 0;
            double environment = 0, logistics = 0, wildlife = 0, other = 0;
            bool Advance(double dt) {
                bool done = world.ContinueUpdate(dt);
                if (done && world.ProfileUpdates) {
                    var p = world.LastUpdateProfile; environment += p.EnvironmentForestMs; logistics += p.LogisticsMs;
                    wildlife += p.WildlifeMs; other += p.OtherSystemsMs;
                }
                return done;
            }
            // Virtual 60 Hz arrival rate; no GPU or sleeps. Both normal and accelerated forest years are measured.
            // Process-wide counters include worker jobs; world construction is outside this interval.
            long allocationStart = GC.GetTotalAllocatedBytes(precise: true);
            int[] collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            for (int frame = 0; frame < frameCount; frame++) {
                // Background query jobs also run between render frames. A tight virtual host is a conservative
                // stress test; paced mode gives workers the same wall time as a real 60 Hz interactive host.
                if (paced) while (pacing.Elapsed.TotalSeconds < frame / 60d) System.Threading.Thread.Sleep(1);
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                runner.AdvanceWork(1d / 60, () => world.ContinueCommands(), Advance, () => world.ForegroundPending);
                frames.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                maximumBudget = Math.Max(maximumBudget, runner.FrameWorkMilliseconds);
                peakDebt = Math.Max(peakDebt, runner.Clock.PendingSimulationSeconds);
                if (frame >= 1800) warmPeakDebt = Math.Max(warmPeakDebt, runner.Clock.PendingSimulationSeconds);
                if (frameCount >= 7200 && (frame + 1) % 1800 == 0)
                    Console.WriteLine($"  frame {frame + 1}: logical tick {world.SimulationTick}; debt {runner.Clock.PendingSimulationSeconds:F4} s");
            }
            double debt = runner.Clock.PendingSimulationSeconds;
            int recovery = 0;
            while (runner.Clock.WorkInProgress || runner.Clock.PendingSimulationSeconds >= runner.Clock.StepSeconds) {
                if (++recovery > 100000) throw new InvalidOperationException("Simulation debt did not clear.");
                runner.AdvanceWork(0, () => world.ContinueCommands(), Advance, () => world.ForegroundPending);
            }
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocationStart;
            int[] collected = Enumerable.Range(0, 3).Select(generation => GC.CollectionCount(generation) - collections[generation]).ToArray();
            frames.Sort();
            double P(double q) => frames[Math.Min(frames.Count - 1, (int)(frames.Count * q))];
            Console.WriteLine($"{size}x{size} {preset} host 4–12ms/60Hz, paced {paced}, budget peak {maximumBudget:F3} ms: CPU frame p95/p99/max {P(.95):F3}/{P(.99):F3}/{frames[^1]:F3} ms; "
                + $"debt peak/warm-peak/end {peakDebt:F4}/{warmPeakDebt:F4}/{debt:F4} simulation-s; {recovery} recovery frames without new time; "
                + $"dropped {runner.Clock.DroppedSimulationSeconds:F4} s; final debt {runner.Clock.PendingSimulationSeconds:F4} s");
            Console.WriteLine($"  host interval including debt recovery: process allocated {allocated} B; GC 0/1/2 {collected[0]}/{collected[1]}/{collected[2]}");
            if (world.ProfileUpdates) {
                Console.WriteLine($"  profile environment/forest {environment:F3}, logistics/rules {logistics:F3}, wildlife {wildlife:F3}, other {other:F3} ms");
                foreach (var rule in world.RuleBook.Profile.OrderByDescending(p => p.Value)) Console.WriteLine($"  rule {rule.Key}: {rule.Value:F3} ms");
                foreach (var phase in world.EcologyWorkProfile) Console.WriteLine($"  ecology {phase.Key}: {phase.Value:F3} ms (elapsed, includes worker waits)");
            }
            string checkpoint = System.Text.Json.JsonSerializer.Serialize(world.CaptureCheckpoint());
            Console.WriteLine($"  final tick {world.SimulationTick}; checkpoint SHA256 {Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(checkpoint)))}");
            if (runner.Clock.DroppedSimulationSeconds != 0) throw new InvalidOperationException("Host discarded simulation time.");
        }

        /// <summary>A hash of every tile's water stores, the flow ledger and the road conditions, exact to the bit.</summary>
        internal static string Fingerprint(SimulationWorld world)
        {
            var bytes = new List<byte>();
            var env = world.Environment;
            for (int id = 0; id < env.CellCount; id++)
                foreach (var store in Enum.GetValues<Ecology.EnvironmentSystem.WaterStore>())
                    bytes.AddRange(BitConverter.GetBytes(env.Water(id, store)));
            foreach (var (name, total) in world.RuleBook.FlowTotals.OrderBy(p => p.Key, StringComparer.Ordinal)) {
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(name)); bytes.AddRange(BitConverter.GetBytes(total));
            }
            bytes.AddRange(BitConverter.GetBytes(env.RuleExchanged));
            for (int id = 0; id < world.Map.Tiles.Count; id++) bytes.AddRange(BitConverter.GetBytes(world.Map.GetRoadCondition(id)));
            return Convert.ToHexString(SHA256.HashData(bytes.ToArray()))[..16];
        }
    }
}
