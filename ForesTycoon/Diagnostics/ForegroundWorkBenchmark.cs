using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon;

internal static class ForegroundWorkBenchmark
{
    internal static void Run(bool rendering = false)
    {
        if (!rendering) { Measure(false); return; }
        using var window = new NativeWindow(new NativeWindowSettings
        {
            StartVisible = false,
            ClientSize = new Vector2i(1280, 720),
            NumberOfSamples = 4,
            API = ContextAPI.OpenGL,
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core
        });
        window.Context.MakeCurrent(); RenderDevice.Initialize(); RenderDevice.InitializeFrameState();
        GL.Viewport(0, 0, 1280, 720);
        try { Measure(true); } finally { RenderDevice.Dispose(); }
    }

    private static void Measure(bool rendering)
    {
        using (var warmup = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false))
        {
            warmup.RequestRegenerate(TerrainSettings.Default.WithNodeSize(17, 19));
            Frames(warmup);
        }
        foreach (int size in rendering ? new[] { 128 } : new[] { 128, 256 })
        {
            var settings = TerrainSettings.Default.WithNodeSize(size + 1, 42);
            using var whole = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
            using var sliced = new GameWorld(TerrainSettings.Default.WithNodeSize(rendering ? 65 : 17, 42), enableRendering: rendering);
            if (rendering)
            {
                sliced.Graphics.AutomaticWeather = false; sliced.Graphics.Preset = WeatherPreset.Sunny;
                for (int frame = 0; frame < 90; frame++) Draw(sliced, (ulong)frame);
            }
            long started = Stopwatch.GetTimestamp(); whole.Regenerate(settings);
            double synchronous = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            sliced.RequestRegenerate(settings);
            Report(size, "generation", synchronous, Frames(sliced)); Equal(whole, sliced);
            void Edit(GameWorld world)
            {
                world.QueueElevationEdit((size / 2) * (size + 1) + size / 2, 1, size / 4, 3);
                world.QueueRoadPath(size + 1, size * size - size - 2, false, RoadPaving.Macadam);
                world.QueueSkidTrailPath(size + 4, size * size - size - 4, false);
                world.QueuePlantForestArea(size + 1, size * 20 + 20, ForestSpecies.Beech);
                world.QueueHarvestForestArea(size + 1, size * 23 + 23);
            }
            Edit(whole); Edit(sliced);
            started = Stopwatch.GetTimestamp(); whole.ExecutePendingCommands();
            synchronous = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Report(size, "brush+road+trail+plant+designate", synchronous, Frames(sliced)); Equal(whole, sliced);
            using var save = new MemoryStream(); sliced.Save(save); save.Position = 0;
            using var restored = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
            restored.Load(save); Equal(sliced, restored);
            for (int tick = 0; tick < 20; tick++) { sliced.Update(1d / 30); restored.Update(1d / 30); }
            Equal(sliced, restored);
            Console.WriteLine($"{size}x{size}: exact checkpoint, save/load and continuation equal");
        }
    }

    private static List<double> Frames(GameWorld world)
    {
        var runner = new SimulationFrameRunner(maximumWorkMilliseconds: 4);
        runner.Clock.IsPaused = true;
        using var profiler = world.HasPresentation ? new RenderPassProfiler() : null;
        var samples = new List<double>(); var totalFrames = new List<double>(); ulong frame = 90;
        int Continue()
        {
            string stage = world.GenerationStage; int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2); long started = Stopwatch.GetTimestamp();
            int done = world.ContinueCommands(16);
            double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (ms > 3) Console.WriteLine($"  slow slice: {stage} -> {world.GenerationStage}: {ms:F3} ms; GC {GC.CollectionCount(0) - g0}/{GC.CollectionCount(1) - g1}/{GC.CollectionCount(2) - g2}");
            return done;
        }
        do
        {
            long started = Stopwatch.GetTimestamp();
            runner.AdvanceWork(0, Continue, dt => world.ContinueUpdate(dt), () => world.ForegroundPending);
            samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (world.HasPresentation)
            {
                profiler.BeginFrame(); Draw(world, frame++);
                double full = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                totalFrames.Add(full);
                var passes = profiler.ReadCompletedFrame();
                if (full > 80) Console.WriteLine($"  slow render frame {frame}, map {world.MapTileColumns}, stage {world.GenerationStage}: {full:F3} ms; " + JsonSerializer.Serialize(passes.OrderByDescending(p => p.CpuMs).Take(3)));
            }
        } while (world.ForegroundPending);
        if (world.GenerationError != null) throw new InvalidOperationException(world.GenerationError);
        if (totalFrames.Count > 0)
        {
            totalFrames.Sort();
            Console.WriteLine($"  GL full-frame p95/p99/max {totalFrames[(int)(totalFrames.Count * .95)]:F3}/{totalFrames[Math.Min(totalFrames.Count - 1, (int)(totalFrames.Count * .99))]:F3}/{totalFrames[^1]:F3} ms ({totalFrames.Count} frames)");
        }
        return samples;
    }

    private static void Report(int size, string operation, double synchronous, List<double> samples)
    {
        samples.Sort();
        double P(double q) => samples[Math.Min(samples.Count - 1, (int)(samples.Count * q))];
        Console.WriteLine($"{size}x{size} {operation}: whole {synchronous:F3} ms; {samples.Count} frames, CPU p95/p99/max {P(.95):F3}/{P(.99):F3}/{samples[^1]:F3} ms; active total {samples.Sum():F1} ms");
    }

    private static void Draw(GameWorld world, ulong frame)
    {
        world.GetWorldBounds(out var min, out var max); var center = (min + max) * .5f;
        var context = new RenderContext(frame / 60d, 1f / 60, frame, 0, world.SimulationTick, 0, false, false, 1,
            -60, -45, center.X - 128, center.Y - 72, center.X + 128, center.Y + 72, 5);
        RenderMetrics.BeginFrame(); RenderDevice.Clear(new Vector4(.55f, .65f, .8f, 1));
        RenderDevice.SetCamera(Matrix4.CreateTranslation(-center) * Matrix4.CreateRotationZ(-MathF.PI / 4)
            * Matrix4.CreateRotationX(-MathF.PI / 3) * Matrix4.CreateOrthographicOffCenter(-128, 128, -72, 72, -1000, 1000));
        world.Draw(context); GL.Finish();
        if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Foreground benchmark OpenGL error.");
    }

    private static void Equal(GameWorld a, GameWorld b)
    {
        if (JsonSerializer.Serialize(a.CaptureCheckpoint()) != JsonSerializer.Serialize(b.CaptureCheckpoint()))
            throw new InvalidOperationException("Foreground slicing changed the checkpoint.");
    }
}
