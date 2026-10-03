using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    internal static class ForestIndividualSmokeTest
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings
            {
                StartVisible = false, ClientSize = new Vector2i(1100, 800),
                API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core
            });
            window.Context.MakeCurrent();
            RenderDevice.Initialize();
            GL.Enable(EnableCap.DepthTest); GL.Viewport(0, 0, 1100, 800);
            try
            {
                using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
                var stands = new ForestStand[256];
                foreach (var (id, species) in new[] { (102, ForestSpecies.Oak), (105, ForestSpecies.Birch), (150, ForestSpecies.Spruce), (153, ForestSpecies.Beech) })
                    stands[id] = new(species, 3, 0.1f, 1);
                var forest = new ForestSystem(new GrowthHabitat(terrain), stands);
                var graphics = new GraphicsSettings { Weather = false, Fog = false, Wildlife = false };
                using var scene = new TerrainRenderer(terrain, new VehicleSystem(), new WorldEffectSystem(), forest, graphics);
                Capture("01-young");
                var mist = new System.Collections.Generic.List<FogSource>();
                var crowns = new System.Collections.Generic.List<Vector3>();
                terrain.CollectForestWeather(mist, forest, crowns, true);
                Require(crowns.Count == forest.IndividualTreeCount, "Weather omitted actual individual crowns.");
                forest.IndividualTrees.TryGet(102, out var weatherPatch);
                terrain.TryGetTileCenter(102, out var root);
                float expectedHeight = root.Z + weatherPatch.Trees[0].At(forest.ForestYear).Height * Terrain.TreeMetresToWorld;
                Require(crowns.Exists(point => Math.Abs(point.Z - expectedHeight) < 0.00001f),
                    "Lightning crown height does not match the drawn individual.");
                Draw(); Require(terrain.ForestChunkRebuilds == 0, "Stable individual frame rebuilt geometry.");
                forest.Update(0.5);
                Draw(); Require(terrain.ForestChunkRebuilds == 0, "Within-month growth rebuilt geometry.");
                forest.IndividualTrees.TryGet(102, out var growthPatch);
                ForestTree originalTree = growthPatch.Trees[0];
                growthPatch.Trees[0] = originalTree.Settle(forest.ForestYear) with
                {
                    AnnualGrowth = new(originalTree.Dimensions.Diameter * 6, originalTree.Dimensions.Height * 6, originalTree.Dimensions.CrownRadius * 6)
                };
                growthPatch.Revision++;
                // An intentionally exaggerated rate verifies real GPU deformation, not just CPU state.
                forest.NotifyIndividualVisualEdit();
                Draw(); byte[] beforePixels = Pixels();
                forest.Update(1);
                Draw(); byte[] afterPixels = Pixels();
                Require(terrain.ForestChunkRebuilds == 0, "GPU growth test rebuilt the mesh.");
                Require(!beforePixels.AsSpan().SequenceEqual(afterPixels), "Textured GPU geometry did not grow.");
                growthPatch.Trees[0] = originalTree.Settle(forest.ForestYear);
                growthPatch.Revision++; forest.NotifyIndividualVisualEdit();
                forest.Update(30 * 25 - 1.5);
                Capture("02-grown");
                forest.IndividualTrees.TryGet(102, out var patch);
                Require(patch.Count > 0 && patch.Trees[0].At(forest.ForestYear).Diameter > 0.1f, "Individual did not grow.");
                forest.ExtractTimber(102, forest.AvailableTimber(102) * 0.15f);
                Capture("03-individual-cut");
                Require(patch.Stumps?.Count > 0, "Cut tree did not leave its own stump.");
                graphics.Enhanced = false;
                Draw(); Require(GL.GetError() == ErrorCode.NoError, "Legacy growth shader GL error.");

                using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42));
                int source = 0;
                while (source < 256 && !world.TryGetForestStand(source, out _)) source++;
                Require(source < 256, "No new-world forest.");
                world.QueueHarvestForest(source); world.ExecutePendingCommands();
                for (int i = 0; i < 3300; i++) world.Update(1.0 / 30);
                world.TryGetForestStand(source, out var before);
                float stock = world.Logistics.Remaining;
                using var save = new MemoryStream();
                world.Save(save); save.Position = 0;
                Require(WorldSaveSerializer.Read(save).Version == WorldSaveData.CurrentVersion, "Save format version mismatch.");
                save.Position = 0; world.Load(save);
                world.TryGetForestStand(source, out var after);
                Require(before == after && stock == world.Logistics.Remaining, "Save replay changed individual growth/stock.");

                world.Regenerate(TerrainSettings.Default.WithNodeSize(17, 42));
                int count = world.ForestTreeCount;
                Require(count > 0, "Regeneration did not create individuals.");
                using var fresh = new MemoryStream(); world.Save(fresh); fresh.Position = 0;
                world.Load(fresh);
                Require(world.ForestTreeCount == count, "Fresh save replay changed individual count.");
                Console.WriteLine("Individual forest smoke passed: continuous cached growth, real cut stems, both shaders, monthly save/replay and regeneration.");

                void Draw()
                {
                    RenderDevice.SetCamera(Matrix4.CreateRotationZ(-MathF.PI / 4) * Matrix4.CreateRotationX(-MathF.PI / 4)
                        * Matrix4.CreateOrthographicOffCenter(-15, 15, -10, 12, -1000, 1000));
                    GL.ClearColor(0.17f, 0.21f, 0.25f, 1);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    scene.Draw(new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1, -45, -45, -1000, -1000, 1000, 1000, 12));
                    Require(GL.GetError() == ErrorCode.NoError, "Individual forest GL error.");
                }
                void Capture(string name)
                {
                    Draw(); GL.Finish();
                    string output = Path.GetFullPath("artifacts/tree-growth"); Directory.CreateDirectory(output);
                    FramebufferCapture.SavePng(Path.Combine(output, name + ".png"), 1100, 800);
                }
                byte[] Pixels()
                {
                    GL.Finish();
                    var result = new byte[1100 * 800 * 4];
                    GL.ReadPixels(0, 0, 1100, 800, PixelFormat.Rgba, PixelType.UnsignedByte, result);
                    return result;
                }
            }
            finally { RenderDevice.Dispose(); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        // The growth fixture isolates healthy conditions from terrain moisture generation.
        private sealed class GrowthHabitat : IForestHabitat
        {
            private readonly IForestHabitat terrain;
            internal GrowthHabitat(Terrain terrain) => this.terrain = terrain;
            public int TileCount => terrain.TileCount;
            public int Seed => terrain.Seed;
            public bool CanSupportForest(int id) => terrain.CanSupportForest(id);
            public float GetMoisture(int id) => 0.65f;
            public float GetNormalizedElevation(int id) => 0.45f;
            public int GetAdjacentTileIds(int id, Span<int> target) => terrain.GetAdjacentTileIds(id, target);
        }
    }
}
