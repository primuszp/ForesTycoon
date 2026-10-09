using System;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    // Player edits must never stall a frame: times a road build (command + following frames) on the default map.
    internal static class RoadBuildBenchmark
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new Vector2i(1280, 720),
                API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            GL.Enable(EnableCap.DepthTest); GL.Viewport(0, 0, 1280, 720);
            try
            {
                using var world = new GameWorld(TerrainSettings.Default);
                world.GetWorldBounds(out var min, out var max);
                var center = (min + max) * .5f;
                var camera = Matrix4.CreateTranslation(-center) * Matrix4.CreateRotationZ(-MathF.PI / 4)
                    * Matrix4.CreateRotationX(-MathF.PI / 3) * Matrix4.CreateOrthographicOffCenter(-128, 128, -72, 72, -1000, 1000);
                ulong frame = 0;
                using var passes = new RenderPassProfiler();
                string lastPasses = "";
                double Frame()
                {
                    long start = Stopwatch.GetTimestamp();
                    world.ExecutePendingCommands(); world.Update(1.0 / 30);
                    double update = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    passes.BeginFrame();
                    double seconds = world.SimulationTick / 30.0;
                    var context = new RenderContext(seconds, 1f / 30, frame++, seconds, world.SimulationTick, 0, false, false,
                        1, -60, -45, -128, -72, 128, 72, 5);
                    RenderDevice.SetCamera(camera);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    world.Draw(context); GL.Finish();
                    var slow = System.Linq.Enumerable.Where(passes.ReadCompletedFrame(), p => p.CpuMs > 3);
                    lastPasses = $"update {update:F1}; " + string.Join(", ", System.Linq.Enumerable.Select(slow, p => $"{p.Name} {p.CpuMs:F1}"));
                    return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                }
                for (int i = 0; i < 60; i++) Frame();
                double baseline = 0; for (int i = 0; i < 30; i++) baseline = Math.Max(baseline, Frame());
                Console.WriteLine($"Road build benchmark: idle worst frame {baseline:F1} ms");
                int side = world.TilesPerSide;
                var random = new Random(7);
                double worst = 0;
                for (int k = 0; k < 16; k++)
                {
                    int u = random.Next(side), v = random.Next(side);
                    int a = u * side + v, b = Math.Min(side - 1, u + 8 + random.Next(8)) * side + v;
                    world.QueueRoadPath(a, b, false);
                    long start = Stopwatch.GetTimestamp();
                    world.ExecutePendingCommands();
                    double command = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    double f1 = Frame(); string firstPasses = lastPasses; double f2 = Frame(), f3 = Frame();
                    worst = Math.Max(worst, command + f1);
                    Console.WriteLine($"road {a}->{b}: command {command:F1} ms, frames {f1:F1} / {f2:F1} / {f3:F1} ms  {world.LastRoadBuildProfile} first frame: {firstPasses}");
                }
                Console.WriteLine($"Road build benchmark: worst edit frame {worst:F1} ms");
            }
            finally { RenderDevice.Dispose(); }
        }
    }
}
