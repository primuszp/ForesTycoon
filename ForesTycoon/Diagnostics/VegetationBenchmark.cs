using System;
using System.Collections.Generic;
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
    internal static class VegetationBenchmark
    {
        // Exercises the same seasonal cache-miss path as terrain streaming, without a GPU.
        internal static void RunGeneration()
        {
            var samples = new List<TreeShapeSpec>();
            foreach (var species in Enum.GetValues<ForestSpecies>().Where(s => s != ForestSpecies.None))
            {
                var profile = ForestSpeciesProfile.For(species);
                foreach (var phase in Enum.GetValues<TreeLifePhase>())
                {
                    float start = phase == TreeLifePhase.Seedling ? 0 : TreeLifePhases.NextAge(species, phase - 1);
                    float end = phase == TreeLifePhase.Senescent ? profile.MaximumAgeYears : TreeLifePhases.NextAge(species, phase);
                    var size = ForestTreeGrowth.Initial(species, (start + end) * .5f, 1);
                    foreach (int seed in new[] { 42, 123 })
                        samples.Add(new(species, seed, phase, size, phase == TreeLifePhase.Senescent ? .5f : 1,
                            new TreeSite(.85f), LeafState.Full, seed * .21f));
                }
            }
            var lods = Enum.GetValues<ForestLod>();
            const int warmups = 6, rounds = 15;
            var times = new double[rounds];
            var bytes = new long[rounds];
            long triangles = 0;
            for (int pass = -warmups; pass < rounds; pass++)
            {
                triangles = 0;
                var start = Stopwatch.GetTimestamp();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                foreach (var spec in samples)
                foreach (var lod in lods)
                {
                    var mesh = DendroTreeGenerator.GenerateSeasonal(spec, lod);
                    triangles += (mesh.Trunk.Length + mesh.Branches.Length + mesh.Crown.Length) / 3;
                }
                if (pass < 0) continue;
                times[pass] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                bytes[pass] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            }
            Array.Sort(times); Array.Sort(bytes);
            Console.WriteLine(JsonSerializer.Serialize(new { models = samples.Count * lods.Length,
                rounds, medianMs = times[rounds / 2], medianAllocatedBytes = bytes[rounds / 2], triangles }));
        }

        internal static void Run(string label)
        {
            int triangles = 0, maximum = 0, count = 0;
            // Same inputs and camera before/after; second pass measures reuse.
            double[] generation = new double[2]; long[] allocations = new long[2];
            for (int pass = 0; pass < 2; pass++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
                foreach (var species in new[] { ForestSpecies.Spruce, ForestSpecies.Birch, ForestSpecies.Oak, ForestSpecies.Beech })
                foreach (var stage in Enum.GetValues<TreeLifeStage>())
                for (int seed = 0; seed < 8; seed++)
                {
                    var mesh = DendroTreeGenerator.Generate(species, seed, stage, 0.85f,
                        ForestTreeGrowth.Initial(species, 40, 1), 0, ForestLod.Near);
                    int n = (mesh.Trunk.Length + mesh.Branches.Length + mesh.Crown.Length) / 3;
                    if (pass == 0) { triangles += n; maximum = Math.Max(maximum, n); count++; }
                }
                generation[pass] = watch.Elapsed.TotalMilliseconds;
                allocations[pass] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            }
            using var window = new NativeWindow(new NativeWindowSettings { StartVisible = false,
                ClientSize = new(960, 640), API = ContextAPI.OpenGL, APIVersion = new(3,3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            object report;
            try
            {
                GL.Viewport(0,0,960,640); GL.Enable(EnableCap.DepthTest);
                using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), ForestVisualFixture.Height);
                var forest = new ForestSystem(terrain.Map, ForestVisualFixture.CreateStands());
                var settings = new GraphicsSettings { Weather = false, Fog = false, Wildlife = false, Diorama = false, StudioBackdrop = false };
                var setup = Stopwatch.StartNew();
                using var scene = new TerrainRenderer(terrain, new VehicleSystem(), new WorldEffectSystem(), forest, settings);
                double setupMs = setup.Elapsed.TotalMilliseconds;
                var times = new double[30];
                var context = new RenderContext(0,0,0,0,0,0,false,false,1,-60,-45,-60,-40,60,40,12);
                var camera = Matrix4.CreateRotationZ(-MathF.PI/4) * Matrix4.CreateRotationX(-MathF.PI/3)
                    * Matrix4.CreateOrthographic(80, 54, -1000, 1000);
                for (int frame = -5; frame < times.Length; frame++)
                {
                    RenderDevice.SetCamera(camera); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    RenderMetrics.BeginFrame(); var watch = Stopwatch.StartNew(); scene.Draw(context); GL.Finish();
                    if (frame >= 0) times[frame] = watch.Elapsed.TotalMilliseconds;
                }
                Array.Sort(times);
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Vegetation benchmark GL error.");
                report = new { label, gpu = GL.GetString(StringName.Renderer), samples = count,
                    meanTreeTriangles = triangles / (double)count, maxTreeTriangles = maximum,
                    generationMs = generation, allocatedBytes = allocations, sceneSetupMs = setupMs,
                    sceneTrees = forest.IndividualTreeCount, frameMedianMs = times[15], frameP95Ms = times[28],
                    submittedVertices = RenderMetrics.SubmittedVertices, drawCalls = RenderMetrics.DrawCalls };
            }
            finally { RenderDevice.Dispose(); }
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            string directory = Path.GetFullPath("artifacts/vegetation-benchmark"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, label == "before" ? "before.json" : "after.json"), json);
            Console.WriteLine(json);
        }
    }
}
