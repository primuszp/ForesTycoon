using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    /// <summary>Repeatable close views of the actual spruce renderer at different ages.</summary>
    internal static class SprucePreview
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new Vector2i(1200, 1000), NumberOfSamples = 4,
                API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core
            });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            GL.Enable(EnableCap.DepthTest); GL.Viewport(0, 0, 1200, 1000);
            try
            {
                using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
                var stands = new ForestStand[256];
                int[] ids = { 7 * 16 + 7, 9 * 16 + 7, 7 * 16 + 9, 9 * 16 + 9 };
                int[] ages = { 12, 30, 60, 95 };
                for (int i = 0; i < ids.Length; i++) stands[ids[i]] = new(ForestSpecies.Spruce, ages[i], 0.8f, 1);
                var forest = new ForestSystem(terrain.Map, stands);
                foreach (var entry in forest.IndividualTrees.Patches)
                {
                    var patch = entry.Value;
                    while (patch.Count > 1) forest.IndividualTrees.RemoveLiving(patch, patch.Count - 1);
                    patch.Trees[0] = patch.Trees[0] with { U = 0.5f, V = 0.5f, AnnualGrowth = default };
                    patch.Revision++;
                }
                forest.NotifyIndividualVisualEdit();
                using var renderer = new TerrainRenderer(terrain, new VehicleSystem(), new WorldEffectSystem(), forest,
                    new GraphicsSettings { Fog = false, Weather = false, Wildlife = false });
                string output = Path.GetFullPath("artifacts/spruce-redesign"); Directory.CreateDirectory(output);
                foreach (float yaw in new[] { -45f, 0f, 45f })
                {
                    const float tilt = -35, zoom = 24;
                    RenderDevice.SetCamera(Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(yaw))
                        * Matrix4.CreateRotationX(MathHelper.DegreesToRadians(tilt))
                        * Matrix4.CreateOrthographicOffCenter(-25, 25, -16, 25.6667f, -1000, 1000));
                    GL.ClearColor(0.17f, 0.21f, 0.25f, 1);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    renderer.Draw(new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1, tilt, yaw, -25, -16, 25, 25.6667f, zoom));
                    if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Spruce preview GL error.");
                    FramebufferCapture.SavePng(Path.Combine(output, $"spruce-yaw{yaw}.png"), 1200, 1000);
                }
                terrain.Map.TryGetTileCenter(ids[3], out var focus);
                RenderDevice.SetCamera(Matrix4.CreateTranslation(-focus)
                    * Matrix4.CreateRotationZ(-MathF.PI / 4) * Matrix4.CreateRotationX(-MathF.PI / 3)
                    * Matrix4.CreateOrthographicOffCenter(-6.6f, 6.6f, -1, 10, -1000, 1000));
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                renderer.Draw(new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1, -60, -45,
                    focus.X - 10, focus.Y - 10, focus.X + 10, focus.Y + 10, 1200f / 13.2f));
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Spruce detail preview GL error.");
                FramebufferCapture.SavePng(Path.Combine(output, "spruce-detail.png"), 1200, 1000);
                Console.WriteLine("Spruce review views: " + output);
            }
            finally { RenderDevice.Dispose(); }
        }
    }
}
