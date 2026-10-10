using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    // One case per process: the OS working-set high-water mark must not include another map.
    internal static class LargeWorldBenchmark
    {
        private readonly record struct Sample(int Frame, bool MovingCamera, bool MonthlyBoundary,
            double UpdateMs, double RenderMs, double TotalMs, long AllocatedBytes, int VisibleChunks,
            int ResidentForestLods, long CpuPayloadBytes, long GpuPayloadBytes,
            WorldUpdateProfile UpdateProfile, RenderPassProfiler.Timing[] RenderPasses);

        internal static void Run(string[] args, int argument)
        {
            if (args.Length != argument + 4 || !int.TryParse(args[argument + 1], out int tiles)
                || tiles is not (64 or 128) || !Enum.TryParse(args[argument + 2], false, out WeatherPreset preset)
                || preset is not (WeatherPreset.Sunny or WeatherPreset.Storm))
                throw new ArgumentException("Usage: --large-world-benchmark <64|128> <Sunny|Storm> <output.json>");
            string output = Path.GetFullPath(args[argument + 3]);
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new Vector2i(1280, 720), NumberOfSamples = 4,
                API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize(); RenderDevice.InitializeFrameState();
            GL.Viewport(0, 0, 1280, 720);
            try { Measure(tiles, preset, output); }
            finally { RenderDevice.Dispose(); }
        }

        private static void Measure(int tiles, WeatherPreset preset, string output)
        {
            const int warmupFrames = 180, measuredFrames = 600;
            const double tickSeconds = 1.0 / 30;
            long started = Stopwatch.GetTimestamp();
            using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(tiles + 1, 42));
            double constructorMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            world.Graphics.Preset = preset; world.Graphics.AutomaticWeather = false;
            world.Graphics.Quality = GraphicsQuality.High; world.ProfileUpdates = true;
            world.QueueWeather(preset, preset == WeatherPreset.Storm ? 32 : 0, 600);
            world.ExecutePendingCommands();
            using var postProcess = new DioramaPostProcess();
            using var profiler = new RenderPassProfiler();
            world.GetWorldBounds(out var min, out var max);
            Vector3 center = (min + max) * .5f;
            ulong renderFrame = 0;
            double Draw(int frame, bool moving)
            {
                Vector3 target = center;
                if (moving) {
                    float angle = (frame - measuredFrames / 2) * MathF.Tau / (measuredFrames / 2);
                    target.X += (max.X - min.X) * .32f * MathF.Cos(angle);
                    target.Y += (max.Y - min.Y) * .32f * MathF.Sin(angle);
                }
                double seconds = ++renderFrame * tickSeconds;
                var context = new RenderContext(seconds, (float)tickSeconds, renderFrame,
                    world.SimulationTick * tickSeconds, world.SimulationTick, 0, false, false, 1,
                    -60, -45, target.X - 128, target.Y - 72, target.X + 128, target.Y + 72, 5);
                long drawStarted = Stopwatch.GetTimestamp();
                profiler.BeginFrame();
                RenderMetrics.BeginFrame();
                bool diorama = postProcess.Begin(world.Graphics, 1280, 720);
                RenderDevice.Clear(new Vector4(.55f, .65f, .8f, 1));
                if (diorama) postProcess.DrawBackdrop(world.Graphics, new Vector3(.55f, .65f, .8f));
                RenderDevice.SetCamera(Matrix4.CreateTranslation(-target) * Matrix4.CreateRotationZ(-MathF.PI / 4)
                    * Matrix4.CreateRotationX(-MathF.PI / 3)
                    * Matrix4.CreateOrthographicOffCenter(-128, 128, -72, 72, -1000, 1000));
                world.Draw(context);
                if (diorama) postProcess.End(world.Graphics, 5, 1, (float)seconds);
                GL.Finish();
                double elapsed = Stopwatch.GetElapsedTime(drawStarted).TotalMilliseconds;
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Large world benchmark GL error.");
                return elapsed;
            }
            double firstDrawMs = Draw(-warmupFrames, false);
            for (int frame = -warmupFrames + 1; frame < 0; frame++) Draw(frame, false);
            using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0;
            double loadMs;
            MemoryPeak loadPeak;
            using (var memory = new MemoryPeak()) {
                started = Stopwatch.GetTimestamp(); world.Load(saved);
                loadMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                memory.Stop(); loadPeak = memory;
            }
            double loadedFirstDrawMs = Draw(-warmupFrames, false);
            // Default year = 900 seconds, month = 75 seconds. Measure the first boundary at t=75.
            for (int tick = 0; tick < 65 * 30; tick++) world.Update(tickSeconds);
            for (int frame = -warmupFrames + 1; frame < 0; frame++) Draw(frame, false);
            var samples = new Sample[measuredFrames];
            for (int frame = 0; frame < samples.Length; frame++) {
                int month = (int)(world.Environment.Time / (EcologyTime.DefaultGameSecondsPerYear / 12));
                long allocated = GC.GetTotalAllocatedBytes(false); started = Stopwatch.GetTimestamp();
                world.ExecutePendingCommands(); world.Update(tickSeconds);
                double updateMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                bool moving = frame >= measuredFrames / 2;
                double renderMs = Draw(frame, moving);
                double totalMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                samples[frame] = new(frame, moving,
                    month != (int)(world.Environment.Time / (EcologyTime.DefaultGameSecondsPerYear / 12)),
                    updateMs, renderMs, totalMs, GC.GetTotalAllocatedBytes(false) - allocated,
                    world.VisibleChunkCount, world.ForestResidentLods,
                    world.ForestCpuPayloadBytes + world.StaticCpuPayloadBytes + world.WeatherCpuPayloadBytes,
                    world.ForestGpuPayloadBytes + world.StaticGpuPayloadBytes + world.WeatherGpuPayloadBytes,
                    world.LastUpdateProfile, profiler.ReadCompletedFrame());
            }
            var boundary = samples.Where(sample => sample.MonthlyBoundary).ToArray();
            if (boundary.Length != 1) throw new InvalidOperationException($"Expected one month boundary, found {boundary.Length}.");
            using var process = Process.GetCurrentProcess(); process.Refresh();
            var result = new {
                Protocol = "large-world-v1", Tiles = tiles, Weather = preset.ToString(), Seed = 42,
                WarmupFrames = warmupFrames, MeasuredFrames = measuredFrames, Width = 1280, Height = 720,
                Quality = "High", Msaa = 4, TickRate = 30, ForestYearSeconds = EcologyTime.DefaultGameSecondsPerYear,
                Includes = "simulation, completed GPU render, diorama postprocess; no UI/input/swap/pacing; default world vehicle population",
                Hardware = new { Vendor = GL.GetString(StringName.Vendor), Renderer = GL.GetString(StringName.Renderer),
                    Driver = GL.GetString(StringName.Version), OS = RuntimeInformation.OSDescription,
                    Runtime = RuntimeInformation.FrameworkDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    LogicalProcessors = Environment.ProcessorCount, Cpu = Environment.GetEnvironmentVariable("FORESTYCOON_BENCHMARK_CPU") ?? "unreported" },
                ConstructorMs = constructorMs, FirstDrawMs = firstDrawMs, TransactionLoadMs = loadMs,
                LoadedFirstDrawMs = loadedFirstDrawMs, SavedBytes = saved.Length,
                LoadMemory = new { SamplingIntervalMs = 2, loadPeak.BaselineWorkingSetBytes,
                    loadPeak.PeakWorkingSetBytes, loadPeak.PeakPrivateBytes, loadPeak.PeakGcCommittedBytes },
                ProcessPeakWorkingSetBytes = process.PeakWorkingSet64,
                // Payload counters exclude the driver, simulation state, runtime, and saved checkpoint.
                StationaryP95Ms = Percentile95(samples.Where(sample => !sample.MovingCamera).Select(sample => sample.TotalMs)),
                MovingP95Ms = Percentile95(samples.Where(sample => sample.MovingCamera).Select(sample => sample.TotalMs)),
                MaxFrameMs = samples.Max(sample => sample.TotalMs), MonthBoundaryMs = boundary[0].TotalMs,
                Samples = samples
            };
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"World{tiles} {preset}: stationary P95 {result.StationaryP95Ms:F2} ms, moving P95 {result.MovingP95Ms:F2} ms, month {result.MonthBoundaryMs:F2} ms; load {loadMs:F1} ms; sampled load working set {loadPeak.PeakWorkingSetBytes / 1048576.0:F1} MiB, process high water {process.PeakWorkingSet64 / 1048576.0:F1} MiB. {output}");
        }

        private static double Percentile95(System.Collections.Generic.IEnumerable<double> values)
        {
            double[] sorted = values.Order().ToArray();
            return sorted[(int)Math.Ceiling(sorted.Length * .95) - 1];
        }

        // A sampled load peak may miss spikes shorter than 2 ms; the OS process high water also includes rendering.
        private sealed class MemoryPeak : IDisposable
        {
            private readonly Thread sampler;
            private volatile bool stopped;
            internal long BaselineWorkingSetBytes { get; }
            internal long PeakWorkingSetBytes { get; private set; }
            internal long PeakPrivateBytes { get; private set; }
            internal long PeakGcCommittedBytes { get; private set; }
            internal MemoryPeak()
            {
                using var process = Process.GetCurrentProcess(); process.Refresh();
                BaselineWorkingSetBytes = PeakWorkingSetBytes = process.WorkingSet64;
                sampler = new Thread(() => {
                    using var observed = Process.GetCurrentProcess();
                    do {
                        observed.Refresh(); PeakWorkingSetBytes = Math.Max(PeakWorkingSetBytes, observed.WorkingSet64);
                        PeakPrivateBytes = Math.Max(PeakPrivateBytes, observed.PrivateMemorySize64);
                        PeakGcCommittedBytes = Math.Max(PeakGcCommittedBytes, GC.GetGCMemoryInfo().TotalCommittedBytes);
                        Thread.Sleep(2);
                    } while (!stopped);
                }) { IsBackground = true, Name = "benchmark-load-memory" };
                sampler.Start();
            }
            internal void Stop() { stopped = true; sampler.Join(); }
            public void Dispose() => Stop();
        }
    }
}
