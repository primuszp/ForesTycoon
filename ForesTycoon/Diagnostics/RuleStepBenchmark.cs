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

        internal static void RunHosts()
        {
            foreach (int size in new[] { 64, 128 }) {
                using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(size + 1, 42).WithForestPattern(ForestPattern.LargeMixed),
                    forestYearSeconds: 900, enableRendering: false);
                world.Environment.ForceWeather(WeatherPreset.Rain, 30, 600);
                Console.WriteLine($"Normal tempo: {size}x{size}, {world.ForestTreeCount} trees, 900 s/year; 120 simulated seconds including a month boundary");
                MeasureHost(world, size, WeatherPreset.Rain, 7200);
            }
        }

        private static void RunCase(int size, WeatherPreset preset)
        {
            var settings = TerrainSettings.Default.WithNodeSize(size + 1, 42).WithForestPattern(ForestPattern.LargeMixed);
            using var whole = new GameWorld(settings, forestYearSeconds: 120, enableRendering: false);
            using var sliced = new GameWorld(settings, forestYearSeconds: 120, enableRendering: false);
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

        private static void MeasureHost(GameWorld world, int size, WeatherPreset preset, int frameCount = 900)
        {
            var runner = new ForesTycoon.Engine.SimulationFrameRunner(maximumWorkMilliseconds: 4);
            runner.Clock.Reset(world.SimulationTick);
            var frames = new List<double>(); double peakDebt = 0;
            // Virtual 60 Hz arrival rate; no GPU or sleeps. Both normal and accelerated forest years are measured.
            for (int frame = 0; frame < frameCount; frame++) {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                runner.AdvanceWork(1d / 60, () => world.ContinueCommands(), dt => world.ContinueUpdate(dt), () => world.ForegroundPending);
                frames.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                peakDebt = Math.Max(peakDebt, runner.Clock.PendingSimulationSeconds);
            }
            double debt = runner.Clock.PendingSimulationSeconds;
            int recovery = 0;
            while (runner.Clock.WorkInProgress || runner.Clock.PendingSimulationSeconds >= runner.Clock.StepSeconds) {
                if (++recovery > 100000) throw new InvalidOperationException("Simulation debt did not clear.");
                runner.AdvanceWork(0, () => world.ContinueCommands(), dt => world.ContinueUpdate(dt), () => world.ForegroundPending);
            }
            frames.Sort();
            double P(double q) => frames[Math.Min(frames.Count - 1, (int)(frames.Count * q))];
            Console.WriteLine($"{size}x{size} {preset} host 4ms/60Hz: CPU frame p95/p99/max {P(.95):F3}/{P(.99):F3}/{frames[^1]:F3} ms; "
                + $"debt peak/end {peakDebt:F4}/{debt:F4} simulation-s; {recovery} recovery frames without new time; "
                + $"dropped {runner.Clock.DroppedSimulationSeconds:F4} s; final debt {runner.Clock.PendingSimulationSeconds:F4} s");
            if (runner.Clock.DroppedSimulationSeconds != 0) throw new InvalidOperationException("Host discarded simulation time.");
        }

        /// <summary>A hash of every tile's water stores, the flow ledger and the road conditions, exact to the bit.</summary>
        internal static string Fingerprint(GameWorld world)
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
