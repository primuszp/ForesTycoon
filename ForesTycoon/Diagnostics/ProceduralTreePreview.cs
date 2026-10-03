using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    // Six equal-age, equal-size trees isolate morphology from physical growth.
    internal static class ProceduralTreePreview
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new Vector2i(1200, 900), NumberOfSamples = 4,
                API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize(); GL.Viewport(0, 0, 1200, 900);
            GL.Enable(EnableCap.DepthTest);
            try
            {
                string output = Path.GetFullPath("artifacts/procedural-tree-variation");
                Directory.CreateDirectory(output);
                foreach (ForestSpecies species in new[] { ForestSpecies.Spruce, ForestSpecies.Oak, ForestSpecies.Birch, ForestSpecies.Beech })
                {
                    using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
                    int[] ids = { 101, 104, 107, 165, 168, 171 };
                    var stands = new ForestStand[256];
                    foreach (int id in ids) stands[id] = new(species, 40, 0.7f, 1);
                    var forest = new ForestSystem(terrain, stands);
                    for (int i = 0; i < ids.Length; i++)
                    {
                        if (!forest.IndividualTrees.TryGet(ids[i], out var patch))
                            throw new InvalidOperationException("Preview tree patch missing.");
                        while (patch.Count > 1) forest.IndividualTrees.RemoveLiving(patch, patch.Count - 1);
                        patch.Trees[0] = patch.Trees[0] with { U = 0.5f, V = 0.5f, Seed = (uint)(42 + i * 7919),
                            Dimensions = ForestTreeGrowth.Initial(species, 40, 1), AnnualGrowth = default };
                        patch.Revision++;
                    }
                    forest.NotifyIndividualVisualEdit();
                    using var renderer = new TerrainRenderer(terrain, new VehicleSystem(), new WorldEffectSystem(), forest,
                        new GraphicsSettings { ForestModels = ForestModelStyle.Procedural, Weather = false, Fog = false,
                            Wildlife = false, Shadows = false, Diorama = false, StudioBackdrop = false });
                    RenderDevice.SetCamera(Matrix4.CreateRotationX(-MathF.PI / 3)
                        * Matrix4.CreateOrthographicOffCenter(-24, 24, -10, 26, -1000, 1000));
                    GL.ClearColor(0.17f, 0.21f, 0.25f, 1); GL.DepthMask(true);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    renderer.Draw(new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1, -60, 0,
                        -1000, -1000, 1000, 1000, 25));
                    if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Procedural variation preview GL error.");
                    FramebufferCapture.SavePng(Path.Combine(output, species + ".png"), 1200, 900);
                }
                Console.WriteLine("Procedural variation views: " + output);
            }
            finally { RenderDevice.Dispose(); }
        }
    }
}
