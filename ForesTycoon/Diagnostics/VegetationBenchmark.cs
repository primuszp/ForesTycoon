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
    internal static class VegetationBenchmark
    {
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
                var forest = new ForestSystem(terrain, ForestVisualFixture.CreateStands());
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
