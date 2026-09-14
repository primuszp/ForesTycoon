using System;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    internal static class ForestBenchmark
    {
        internal static void Run(bool cameraMotion = false)
        {
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new Vector2i(1280, 720), NumberOfSamples = 4,
                API = ContextAPI.OpenGL, APIVersion = new Version(3,3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent();
            RenderDevice.Initialize();
            GL.Enable(EnableCap.DepthTest);
            Console.WriteLine($"GPU: {GL.GetString(StringName.Renderer)}");
            foreach (bool fixture in new[] { true, false })
            {
                var terrain = fixture ? new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), ForestVisualFixture.Height)
                    : new Terrain(TerrainSettings.Default);
                var forest = fixture ? new ForestSystem(terrain, ForestVisualFixture.CreateStands()) : new ForestSystem(terrain);
                using var renderer = new TerrainRenderer(terrain, new VehicleSystem(), new WorldEffectSystem(), forest);
                try
                {
                    foreach (float zoom in new[] { 10f, 5f })
                    {
                        var times = new double[90];
                        var context = new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1, -60, -45,
                            -640 / zoom, -360 / zoom, 640 / zoom, 360 / zoom, zoom);
                        var view = Matrix4.CreateRotationZ(-MathF.PI / 4) * Matrix4.CreateRotationX(-MathF.PI / 3);
                        var projection = Matrix4.CreateOrthographicOffCenter(-640 / zoom, 640 / zoom, -360 / zoom, 360 / zoom, -1000, 1000);
                        GL.Viewport(0, 0, 1280, 720);
                        for (int frame = -10; frame < times.Length; frame++)
                        {
                            if (cameraMotion)
                            {
                                float movingZoom = 7 + 5 * MathF.Sin(frame * MathF.PI / 30);
                                float yaw = -45 + frame * 4;
                                context = new RenderContext(0, 0, (ulong)(frame + 10), 0, 0, 0, false, false, 1, -60, yaw,
                                    -640 / movingZoom, -360 / movingZoom, 640 / movingZoom, 360 / movingZoom, movingZoom);
                                view = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(yaw)) * Matrix4.CreateRotationX(-MathF.PI / 3);
                                projection = Matrix4.CreateOrthographicOffCenter(-640 / movingZoom, 640 / movingZoom,
                                    -360 / movingZoom, 360 / movingZoom, -1000, 1000);
                            }
                            long start = Stopwatch.GetTimestamp();
                            if (cameraMotion) forest.Update(1.0 / 30.0);
                            RenderDevice.SetCamera(view * projection);
                            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                            RenderMetrics.BeginFrame();
                            renderer.Draw(context);
                            GL.Finish(); // Benchmark only: includes completion, not just CPU submission.
                            if (frame >= 0) times[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        }
                        if (!cameraMotion && !fixture && zoom == 10)
                        {
                            long startPass = 0;
                            RenderPipeline.PassProbe = (name, begin) => {
                                GL.Finish();
                                if (begin) startPass = Stopwatch.GetTimestamp();
                                else Console.WriteLine($"  {name}: {Stopwatch.GetElapsedTime(startPass).TotalMilliseconds:F2}ms");
                            };
                            RenderMetrics.BeginFrame();
                            try { renderer.Draw(context); } finally { RenderPipeline.PassProbe = null; }
                        }
                        Array.Sort(times);
                        Console.WriteLine($"{(fixture ? "fixture16" : "world64")} zoom={zoom}: median={times[45]:F2}ms p95={times[85]:F2}ms max={times[89]:F2}ms vertices={RenderMetrics.SubmittedVertices} draws={RenderMetrics.DrawCalls}");
                    }
                }
                finally { terrain.Dispose(); }
            }
            RenderDevice.Dispose();
        }
    }
}
