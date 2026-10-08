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
                terrain.SynchronousForestBuilds = true;
                var stands = new ForestStand[256];
                foreach (var (id, species) in new[] { (102, ForestSpecies.Oak), (105, ForestSpecies.Birch), (150, ForestSpecies.Spruce), (153, ForestSpecies.Beech) })
                    stands[id] = new(species, 3, 0.1f, 1);
                var forest = new ForestSystem(new GrowthHabitat(terrain), stands);
                // Isolate continuous growth from spring bud-burst transitions, which
                // legitimately replace crown geometry between monthly ticks.
                forest.Update(ForestSystem.DefaultSecondsPerYear * 0.25);
                var graphics = new GraphicsSettings { Weather = false, Fog = false, Wildlife = false };
                using var scene = new TerrainRenderer(terrain, new VehicleSystem(), new WorldEffectSystem(), forest, graphics);
                Capture("01-young");
                var mist = new System.Collections.Generic.List<FogSource>();
                var crowns = new System.Collections.Generic.List<Vector3>();
                terrain.CollectForestWeather(mist, forest, crowns, true);
                Require(crowns.Count == forest.IndividualTreeCount, "Weather omitted actual individual crowns.");
                forest.IndividualTrees.TryGet(102, out var weatherPatch);
                terrain.Map.TryGetTileCenter(102, out var root);
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
                // Cross an age boundary between monthly ticks without changing dimensions.
                forest.IndividualTrees.TryGet(105, out var phasePatch);
                var savedPhaseTree = phasePatch.Trees[0];
                phasePatch.Trees[0] = savedPhaseTree.Settle(forest.ForestYear) with {
                    BirthYear = forest.ForestYear - 1.99, AnnualGrowth = default };
                phasePatch.Revision++; forest.NotifyIndividualVisualEdit();
                Draw(); byte[] seedlingPixels = Pixels();
                var fixedSize = phasePatch.Trees[0].At(forest.ForestYear);
                long phaseBuilds = terrain.TotalForestChunkRebuilds;
                forest.Update(0.6);
                Draw();
                Require(terrain.TotalForestChunkRebuilds > phaseBuilds, "Life-stage boundary did not replace cached geometry.");
                Require(fixedSize == phasePatch.Trees[0].At(forest.ForestYear), "Stage fixture changed physical size.");
                Require(!seedlingPixels.AsSpan().SequenceEqual(Pixels()), "Life-stage model changed only its scale.");
                Draw(); Require(terrain.ForestChunkRebuilds == 0, "Life-stage cache kept rebuilding.");
                phasePatch.Trees[0] = savedPhaseTree;
                phasePatch.Revision++; forest.NotifyIndividualVisualEdit();
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
                double editTime = world.Environment.Time;
                ulong editTick = world.SimulationTick;
                world.QueueElevationEdit(5 * 17 + 5, 1, 0, 1);
                world.ExecutePendingCommands();
                Require(world.Environment.Time == editTime && world.SimulationTick == editTick,
                    "Terrain command advanced simulation time.");
                var editedStands = new ForestStand[256];
                for (int id = 0; id < editedStands.Length; id++) world.TryGetForestStand(id, out editedStands[id]);
                float editedStock = world.Logistics.Remaining;
                using var save = new MemoryStream();
                world.Save(save); save.Position = 0;
                Require(WorldSaveSerializer.Read(save).Version == WorldSaveData.CurrentVersion, "Save format version mismatch.");
                save.Position = 0; world.Load(save);
                for (int id = 0; id < editedStands.Length; id++)
                {
                    world.TryGetForestStand(id, out var replayed);
                    Require(editedStands[id] == replayed, "Terrain edit replay changed forest state.");
                }
                Require(editedStock == world.Logistics.Remaining, "Save replay changed timber stock.");
                // Old journals retain historical edit semantics when upgraded, while newly queued
                // commands immediately use the corrected local rule in the loaded world.
                save.Position = 0;
                var currentSave = WorldSaveSerializer.Read(save);
                using var legacySave = new MemoryStream();
                WorldSaveSerializer.Write(legacySave, new WorldSaveData { Version = 5,
                    TickRate = currentSave.TickRate, ForestYearSeconds = currentSave.ForestYearSeconds,
                    Tick = currentSave.Tick, Terrain = currentSave.Terrain, Commands = currentSave.Commands });
                legacySave.Position = 0; world.Load(legacySave);
                for (int id = 0; id < editedStands.Length; id++) world.TryGetForestStand(id, out editedStands[id]);
                using var upgraded = new MemoryStream();
                world.Save(upgraded); upgraded.Position = 0;
                var upgradedData = WorldSaveSerializer.Read(upgraded);
                Require(upgradedData.Version == WorldSaveData.CurrentVersion && upgradedData.Commands.Exists(c => c.Kind == WorldCommandKind.EditElevation && c.Flag),
                    "Legacy journal lost its historical terrain rule during upgrade.");
                upgraded.Position = 0; world.Load(upgraded);
                for (int id = 0; id < editedStands.Length; id++)
                {
                    world.TryGetForestStand(id, out var replayed);
                    Require(editedStands[id] == replayed, "Upgraded legacy replay changed forest state.");
                }
                world.QueueElevationEdit(6 * 17 + 6, 1, 0, 1); world.ExecutePendingCommands();
                using var continued = new MemoryStream(); world.Save(continued); continued.Position = 0;
                var continuedData = WorldSaveSerializer.Read(continued);
                Require(!continuedData.Commands[^1].Flag, "New edit in a loaded legacy world retained the old rule.");

                world.Regenerate(TerrainSettings.Default.WithNodeSize(17, 42));
                int count = world.ForestTreeCount;
                Require(count > 0, "Regeneration did not create individuals.");
                using var fresh = new MemoryStream(); world.Save(fresh); fresh.Position = 0;
                world.Load(fresh);
                Require(world.ForestTreeCount == count, "Fresh save replay changed individual count.");
                // A malformed journal must preserve both the running state and its save data.
                world.QueueWeather(WeatherPreset.Storm, 32, 60); world.ExecutePendingCommands();
                world.Update(1.0 / 30);
                using var intact = new MemoryStream(); world.Save(intact);
                var brokenSave = new WorldSaveData {
                    SoilModel = SoilModelData.From(SoilLandscapeDefinition.Default),
                    Terrain = TerrainSettingsData.From(TerrainSettings.Default.WithNodeSize(17, 42)),
                    Tick = 1, Commands = new() { new SpawnVehicleCommand().ToRecord(2) }
                };
                using var broken = new MemoryStream(); WorldSaveSerializer.Write(broken, brokenSave); broken.Position = 0;
                bool rejected = false;
                try { world.Load(broken); } catch (InvalidDataException) { rejected = true; }
                Require(rejected, "Invalid journal was accepted.");
                using var retained = new MemoryStream(); world.Save(retained);
                Require(intact.ToArray().AsSpan().SequenceEqual(retained.ToArray()), "Failed load changed the live world.");
                // The transferred vehicle route factory must still work after a successful load and regeneration.
                world.Regenerate(TerrainSettings.Default.WithNodeSize(17, 42));
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
            internal GrowthHabitat(Terrain terrain) => this.terrain = terrain.Map;
            public int TileCount => terrain.TileCount;
            public int Seed => terrain.Seed;
            public bool CanSupportForest(int id) => terrain.CanSupportForest(id);
            public float GetMoisture(int id) => 0.65f;
            public float GetNormalizedElevation(int id) => 0.45f;
            public ForestTileGeometry GetForestTileGeometry(int id) => terrain.GetForestTileGeometry(id);
            public int GetAdjacentTileIds(int id, Span<int> target) => terrain.GetAdjacentTileIds(id, target);
        }
    }
}
