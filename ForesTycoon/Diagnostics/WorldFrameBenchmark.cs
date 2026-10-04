using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    // Actual GameWorld updates and completed rendering across a month boundary.
    // Excludes host input/UI, swap, frame pacing and Viewport diorama composition.
    internal static class WorldFrameBenchmark
    {
        private readonly record struct Sample(int Frame, double SimulationSeconds, double UpdateMs,
            double RenderMs, double TotalMs, bool MonthlyBoundary, int ForestRebuilds, WorldUpdateProfile UpdateProfile,
            RenderPassProfiler.Timing[] RenderPasses);

        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new Vector2i(1280, 720), NumberOfSamples = 4,
                API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            GL.Enable(EnableCap.DepthTest); GL.Viewport(0, 0, 1280, 720);
            Console.WriteLine($"World benchmark: {GL.GetString(StringName.Renderer)}, 1280x720 MSAA4; simulation + completed rendering, high quality.");
            try
            {
                foreach (WeatherPreset preset in new[] { WeatherPreset.Sunny, WeatherPreset.Storm }) RunCase(preset);
            }
            finally { RenderDevice.Dispose(); }
        }

        private static void RunCase(WeatherPreset preset)
        {
            const double secondsPerMonth = EnvironmentSystem.SecondsPerForestYear / 12;
            using var world = new GameWorld(TerrainSettings.Default);
            using var renderProfile = new RenderPassProfiler();
            world.ProfileUpdates = true;
            world.QueueWeather(preset, preset == WeatherPreset.Storm ? 32 : 0, 600);
            world.ExecutePendingCommands();
            for (int tick = 0; tick < 90 * 30; tick++) world.Update(1.0 / 30);
            world.GetWorldBounds(out var min, out var max);
            var center = (min + max) * .5f;
            var camera = Matrix4.CreateTranslation(-center) * Matrix4.CreateRotationZ(-MathF.PI / 4)
                * Matrix4.CreateRotationX(-MathF.PI / 3)
                * Matrix4.CreateOrthographicOffCenter(-128, 128, -72, 72, -1000, 1000);
            var samples = new Sample[600];
            for (int frame = -30; frame < samples.Length; frame++)
            {
                int previousMonth = (int)(world.Environment.Time / secondsPerMonth);
                long start = Stopwatch.GetTimestamp();
                world.ExecutePendingCommands(); world.Update(1.0 / 30);
                long updated = Stopwatch.GetTimestamp();
                double seconds = world.SimulationTick / 30.0;
                var context = new RenderContext(seconds, 1f / 30, (ulong)(frame + 30), seconds,
                    world.SimulationTick, 0, false, false, 1, -60, -45, -128, -72, 128, 72, 5);
                RenderDevice.SetCamera(camera);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                renderProfile.BeginFrame();
                RenderMetrics.BeginFrame(); world.Draw(context); GL.Finish();
                long finished = Stopwatch.GetTimestamp();
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("World benchmark GL error.");
                if (frame >= 0) samples[frame] = new(frame, seconds,
                    Stopwatch.GetElapsedTime(start, updated).TotalMilliseconds,
                    Stopwatch.GetElapsedTime(updated, finished).TotalMilliseconds,
                    Stopwatch.GetElapsedTime(start, finished).TotalMilliseconds,
                    (int)(world.Environment.Time / secondsPerMonth) != previousMonth, world.ForestChunkRebuilds, world.LastUpdateProfile,
                    renderProfile.ReadCompletedFrame());
            }
            var sorted = new double[samples.Length];
            Sample worst = default, boundary = default;
            int boundaries = 0;
            foreach (var sample in samples)
            {
                sorted[sample.Frame] = sample.TotalMs;
                if (sample.TotalMs > worst.TotalMs) worst = sample;
                if (sample.MonthlyBoundary) { boundary = sample; boundaries++; }
            }
            if (boundaries != 1) throw new InvalidOperationException($"Expected one measured month boundary, got {boundaries}.");
            Array.Sort(sorted);
            string directory = Path.GetFullPath("artifacts/world-benchmark");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, preset.ToString().ToLowerInvariant() + ".json"),
                JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"World64 {preset}, {world.ForestTreeCount} trees, {world.VehicleCount} vehicles: median {sorted[300]:0.00} ms, p95 {sorted[570]:0.00} ms, max {worst.TotalMs:0.00} ms (update {worst.UpdateMs:0.00}, render {worst.RenderMs:0.00}, t={worst.SimulationSeconds:0.00}); month {boundary.TotalMs:0.00} ms (update {boundary.UpdateMs:0.00}, render {boundary.RenderMs:0.00}).");
            Console.WriteLine("Samples: " + directory);
            var profile = boundary.UpdateProfile;
            Console.WriteLine($"  Month update phases: environment/forest {profile.EnvironmentForestMs:0.00}, logistics {profile.LogisticsMs:0.00}, wildlife {profile.WildlifeMs:0.00}, other {profile.OtherSystemsMs:0.00} ms.");
            foreach (var pass in samples.SelectMany(s => s.RenderPasses).GroupBy(p => p.Name)
                .OrderByDescending(p => p.Average(t => t.GpuMs)).Take(6))
                Console.WriteLine($"  {pass.Key}: CPU mean/max {pass.Average(p => p.CpuMs):0.00}/{pass.Max(p => p.CpuMs):0.00} ms; GPU mean/max {pass.Average(p => p.GpuMs):0.00}/{pass.Max(p => p.GpuMs):0.00} ms.");
        }
    }
}
