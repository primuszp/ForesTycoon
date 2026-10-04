using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    internal static class PlantationSmokeTest
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new(1100, 800), API = ContextAPI.OpenGL,
                APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            GL.Enable(EnableCap.DepthTest); GL.Viewport(0, 0, 1100, 800);
            try
            {
                using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
                var forest = new ForestSystem(new Habitat(terrain), secondsPerYear: 12); forest.Clear();
                int area = forest.AllocatePlantationId();
                for (int x = 6; x <= 9; x++) for (int y = 6; y <= 9; y++)
                    Require(forest.PlantInArea(x * 16 + y, ForestSpecies.Birch, area) == ForestryActionResult.Planted, "Planting failed.");
                forest.FinishPlantingArea(area); terrain.ClearForestryPreview();
                var graphics = new GraphicsSettings { Weather = false, Fog = false, Wildlife = false, Shadows = false };
                using var scene = new TerrainRenderer(terrain, new VehicleSystem(), new WorldEffectSystem(), forest, graphics);
                Capture("01-seedling-rows");
                Draw(); Require(terrain.PlantationMeshRebuilds == 0, "Persistent markers rebuilt on a stable frame.");
                byte[] marked = Pixels(); graphics.ShowPlantations = false; Draw();
                Require(!marked.AsSpan().SequenceEqual(Pixels()), "Persistent markers were invisible after releasing selection.");
                graphics.ShowPlantations = true;
                forest.Update(12 * 15); Capture("02-young-plantation"); Report();
                Require(terrain.PlantationMeshRebuilds == 0, "Growth rebuilt static plantation markers.");
                forest.Update(12 * 35); Capture("03-competing-plantation"); Report();
                forest.Update(12 * 30); Capture("04-old-plantation"); Report();
                forest.Harvest(119, out _); Capture("05-harvested-tile-marked");
                Require(forest.TryGetPlantationStatus(119, out var status) && status.Living == 0, "Harvest erased the designation.");
                Draw(); Require(terrain.PlantationMeshRebuilds == 0, "Harvest rebuilt unchanged markers.");
                terrain.EditElevationAtNode(7 * 17 + 7, 1, 0, 1); Draw();
                Require(terrain.PlantationMeshRebuilds > 0, "Terrain edit left stale marker elevations.");
                Draw(); Require(terrain.PlantationMeshRebuilds == 0, "Edited marker cache did not settle.");
                CheckReplay();
                Console.WriteLine("Plantation GL smoke passed: rows, persistent boundaries, cache, terrain edit and command replay.");

                void Draw()
                {
                    RenderDevice.SetCamera(Matrix4.CreateRotationZ(-MathF.PI / 4) * Matrix4.CreateRotationX(-MathF.PI / 4)
                        * Matrix4.CreateOrthographicOffCenter(-20, 20, -13, 20, -1000, 1000));
                    GL.ClearColor(.17f, .21f, .25f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    scene.Draw(new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1, -45, -45, -1000, -1000, 1000, 1000, 12));
                    Require(GL.GetError() == ErrorCode.NoError, "Plantation OpenGL error.");
                }
                void Capture(string name)
                {
                    Draw(); GL.Finish(); string output = Path.GetFullPath("artifacts/plantations"); Directory.CreateDirectory(output);
                    FramebufferCapture.SavePng(Path.Combine(output, name + ".png"), 1100, 800);
                }
                byte[] Pixels()
                {
                    GL.Finish(); var result = new byte[1100 * 800 * 4];
                    GL.ReadPixels(0, 0, 1100, 800, PixelFormat.Rgba, PixelType.UnsignedByte, result); return result;
                }
                void Report()
                {
                    int living = 0;
                    for (int x = 6; x <= 9; x++) for (int y = 6; y <= 9; y++)
                        if (forest.TryGetPlantationStatus(x * 16 + y, out var current)) living += current.Living;
                    forest.TryGetPlantationStatus(119, out var plot);
                    Console.WriteLine($"Year {forest.ForestYear:F0}: {living}/576 living in plantation, tile 119 light={plot.Resources.Light:F2}, water={plot.Resources.Water:F2}, space={plot.Resources.Space:F2}, deadwood={plot.Dead}.");
                    if (forest.ForestYear >= 80) Require(living < 576, "Healthy dense plantation did not self-thin before old age.");
                }
            }
            finally { RenderDevice.Dispose(); }
        }

        private static void CheckReplay()
        {
            using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42));
            int tile;
            for (tile = 0; tile < 256; tile++)
            {
                if (world.TryGetForestStand(tile, out _)) continue;
                world.QueuePlantForestArea(tile, tile, ForestSpecies.Birch); world.ExecutePendingCommands();
                if (world.TryGetPlantationStatus(tile, out _)) break;
            }
            Require(tile < 256, "World has no plantable clearing.");
            for (int i = 0; i < 3600; i++) world.Update(1.0 / 30);
            Require(world.TryGetPlantationStatus(tile, out var expected), "World did not record plantation.");
            var statistics = world.ForestStatistics;
            using var save = new MemoryStream(); world.Save(save); save.Position = 0; world.Load(save);
            Require(world.TryGetPlantationStatus(tile, out var actual) && expected == actual, "Replay changed plantation resources or designation.");
            Require(statistics == world.ForestStatistics, "Replay changed forest dynamics.");
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private sealed class Habitat(Terrain terrain) : IForestHabitat
        {
            private readonly IForestHabitat source = terrain;
            public int TileCount => source.TileCount;
            public int Seed => source.Seed;
            public bool CanSupportForest(int id) => source.CanSupportForest(id);
            public float GetMoisture(int id) => .64f;
            public float GetNormalizedElevation(int id) => .45f;
            public ForestTileGeometry GetForestTileGeometry(int id) => source.GetForestTileGeometry(id);
            public int GetAdjacentTileIds(int id, Span<int> target) => source.GetAdjacentTileIds(id, target);
        }
    }
}
