using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    // Fixed, fully prepared geometry. CPU submission and completed GPU frames are
    // measured separately; loading, simulation, UI and presentation are excluded.
    internal static class RenderThroughputBenchmark
    {
        internal static void Run(string output)
        {
            output = Path.GetFullPath(output); Directory.CreateDirectory(output);
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new(1280, 720), NumberOfSamples = 4,
                API = ContextAPI.OpenGL, APIVersion = new(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            try
            {
                GL.Viewport(0, 0, 1280, 720); GL.Enable(EnableCap.DepthTest);
                var results = new List<object>();
                results.Add(MeasureVisibility());
                using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), ForestVisualFixture.Height);
                var forest = new ForestSystem(terrain.Map, ForestVisualFixture.CreateStands());
                var settings = new GraphicsSettings { Quality = GraphicsQuality.High, TimeOfDay = false };
                using var scene = new TerrainRenderer(terrain, new VehicleSystem(), new WorldEffectSystem(), forest, settings);
                foreach (float progress in scene.PrepareMapGeometry()) { }
                var model = AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Vehicles", "log-truck.glb"));
                using var renderer = new AnimatedModelRenderer(model);
                var pose = model.CreatePose(); pose.Evaluate(null, 0);
                var camera = Matrix4.CreateRotationZ(-MathF.PI / 4) * Matrix4.CreateRotationX(-MathF.PI / 3)
                    * Matrix4.CreateOrthographic(128, 72, -1000, 1000);
                foreach (string name in new[] { "plain", "sunny", "storm", "model-outlines" })
                {
                    settings.Enhanced = name != "plain";
                    settings.Preset = name == "storm" ? WeatherPreset.Storm : WeatherPreset.Sunny;
                    bool models = name == "model-outlines";
                    for (int warm = 0; warm <= 60; warm++) Draw(warm, models);
                    const int samples = 120;
                    var cpu = new double[samples]; var completed = new double[samples];
                    long allocated = GC.GetAllocatedBytesForCurrentThread(), builds = terrain.TotalForestChunkRebuilds;
                    for (int i = 0; i < samples; i++)
                    {
                        long start = Stopwatch.GetTimestamp();
                        Draw(60, models, finish: false);
                        long submitted = Stopwatch.GetTimestamp(); GL.Finish();
                        cpu[i] = Stopwatch.GetElapsedTime(start, submitted).TotalMilliseconds;
                        completed[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    }
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    if (builds != terrain.TotalForestChunkRebuilds || terrain.HasPendingForestBuild)
                        throw new InvalidOperationException("Render benchmark included unfinished geometry.");
                    RenderDevice.CheckErrors(name);
                    Array.Sort(cpu); Array.Sort(completed);
                    var result = new { name, cpuMedianMs = cpu[samples / 2], cpuP95Ms = cpu[(int)(samples * .95)],
                        completedMedianMs = completed[samples / 2], completedP95Ms = completed[(int)(samples * .95)],
                        allocatedBytesPerFrame = bytes / samples, vertices = RenderMetrics.SubmittedVertices,
                        draws = RenderMetrics.DrawCalls, trees = forest.IndividualTreeCount,
                        particles = scene.WeatherParticleCount, fogParticles = scene.FogParticleCount, cloudSteps = scene.WeatherCloudSteps };
                    results.Add(result); Console.WriteLine(JsonSerializer.Serialize(result));
                    FramebufferCapture.SavePng(Path.Combine(output, name + ".png"), 1280, 720);
                }
                var report = new { gpu = GL.GetString(StringName.Renderer), width = 1280, height = 720, msaa = 4,
                    quality = "High", results };
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

                void Draw(double time, bool models, bool finish = true)
                {
                    RenderDevice.SetCamera(camera);
                    RenderDevice.Clear(new Vector4(.09f, .12f, .15f, 1));
                    RenderMetrics.BeginFrame();
                    var context = new RenderContext(time, 0, 0, time, 0, 0, false, false, 1, -60, -45, -64, -36, 64, 36, 10);
                    scene.Draw(context);
                    if (models)
                    {
                        using var batch = renderer.BeginBatch();
                        for (int i = 0; i < 25; i++)
                            renderer.Draw(pose, Matrix4.CreateTranslation((i % 5 - 2) * 10, (i / 5 - 2) * 10, 12),
                                settings, outlineWorldWidth: .02f, sharedState: batch);
                    }
                    if (finish) GL.Finish();
                }
            }
            finally { RenderDevice.Dispose(); }
            Console.WriteLine("Prepared render benchmark: " + output);
        }

        private static object MeasureVisibility()
        {
            using var terrain = new Terrain(new TerrainMap(TerrainSettings.Default.WithNodeSize(513, 42), (_, _) => 4), deferGeometry: true);
            var context = new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1, -60, -45, -64, -36, 64, 36, 10);
            for (int i = 0; i < 256; i++) terrain.UpdateVisibleTiles(context);
            double stationary = Time(false), moving = Time(true);
            var result = new { name = "visibility-512", cpuMedianMs = stationary, movingCpuMedianMs = moving,
                chunks = terrain.TotalChunkCount, visible = terrain.VisibleChunkCount };
            Console.WriteLine(JsonSerializer.Serialize(result)); return result;

            double Time(bool moving)
            {
                var times = new double[9];
                for (int sample = 0; sample < times.Length; sample++)
                {
                    long start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < 1024; i++)
                        terrain.UpdateVisibleTiles(moving ? new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1,
                            -60, -45 + i * .01f, -64, -36, 64, 36, 10) : context);
                    times[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 1024;
                }
                Array.Sort(times); return times[4];
            }
        }
    }
}
