using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    // Equal physical dimensions isolate the seed, age and light effects on morphology.
    internal static class ProceduralTreePreview
    {
        internal static void Run(bool lifeStages = false, bool compareLight = false)
        {
            int imageHeight = compareLight ? 480 : 900;
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new Vector2i(1200, imageHeight), NumberOfSamples = 4,
                API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize(); GL.Viewport(0, 0, 1200, imageHeight);
            GL.Enable(EnableCap.DepthTest);
            try
            {
                string output = Path.GetFullPath(compareLight ? "artifacts/dendro-trees" : lifeStages ? "artifacts/tree-life-stages" : "artifacts/procedural-tree-variation");
                Directory.CreateDirectory(output);
                foreach (ForestSpecies species in new[] { ForestSpecies.Spruce, ForestSpecies.Oak, ForestSpecies.Birch, ForestSpecies.Beech })
                foreach (TreeLifeStage stage in lifeStages ? Enum.GetValues<TreeLifeStage>() : new[] { TreeLifeStage.Mature })
                {
                    using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
                    int[] ids = lifeStages ? new[] { 88, 136, 184 } : new[] { 101, 104, 107, 165, 168, 171 };
                    float age = stage == TreeLifeStage.Seedling ? 0.7f : stage == TreeLifeStage.Young ? 8
                        : stage == TreeLifeStage.Old ? ForestSpeciesProfile.For(species).MaximumAgeYears * 0.75f
                        : ForestSpeciesProfile.For(species).MatureAgeYears;
                    var stands = new ForestStand[256];
                    foreach (int id in ids) stands[id] = new(species, age, 0.7f, 1);
                    var forest = new ForestSystem(terrain.Map, stands);
                    for (int i = 0; i < ids.Length; i++)
                    {
                        if (!forest.IndividualTrees.TryGet(ids[i], out var patch))
                            throw new InvalidOperationException("Preview tree patch missing.");
                        while (patch.Count > 1) forest.IndividualTrees.RemoveLiving(patch, patch.Count - 1);
                        patch.Trees[0] = patch.Trees[0] with { U = 0.5f, V = 0.5f, Seed = (uint)(compareLight ? 42 : 42 + i), BirthYear = -age,
                            Resources = new(compareLight ? i == 0 ? 0.12f : i == 1 ? 0.48f : 1f : 1, 1, 1),
                            Dimensions = ForestTreeGrowth.Initial(species, 40, 1), AnnualGrowth = default };
                        patch.Revision++;
                    }
                    forest.NotifyIndividualVisualEdit();
                    using var renderer = new TerrainRenderer(terrain, new VehicleSystem(), new WorldEffectSystem(), forest,
                        new GraphicsSettings { ForestModels = ForestModelStyle.Procedural, Weather = false, Fog = false,
                            Wildlife = false, Shadows = false, Diorama = false, StudioBackdrop = false, ShowGrid = !compareLight });
                    RenderDevice.SetCamera(Matrix4.CreateRotationX(-MathF.PI / 3)
                        * Matrix4.CreateOrthographicOffCenter(-24, 24, compareLight ? 1 : -10, compareLight ? 20.2f : 26, -1000, 1000));
                    GL.ClearColor(0.17f, 0.21f, 0.25f, 1); GL.DepthMask(true);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    renderer.Draw(new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1, -60, 0,
                        -1000, -1000, 1000, 1000, 25));
                    if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Procedural variation preview GL error.");
                    FramebufferCapture.SavePng(Path.Combine(output, species + (lifeStages ? "-" + stage : "") + ".png"), 1200, imageHeight);
                }
                Console.WriteLine("Procedural variation views: " + output);
            }
            finally { RenderDevice.Dispose(); }
        }
    }
}
