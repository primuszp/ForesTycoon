using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    /// <summary>A spring floods part of a real world; the 3D view must draw the moving surface water where it stands.</summary>
    internal static class FloodRenderSmokeTest
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings { StartVisible = false, ClientSize = new Vector2i(1100, 800), API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize(); GL.Enable(EnableCap.DepthTest); GL.Viewport(0, 0, 1100, 800);
            string output = Path.GetFullPath("artifacts/flood-render"); Directory.CreateDirectory(output);
            try
            {
                using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: true);
                world.Graphics.Fog = false; world.Graphics.Weather = false; world.Graphics.Wildlife = false;
                Capture(world, "before", 0);
                Require(world.FloodTilesDrawn == 0, $"A dry world drew {world.FloodTilesDrawn} flooded tiles.");

                int spring = Enumerable.Range(0, world.Map.Tiles.Count).Where(t => !world.IsWaterTile(t))
                    .OrderByDescending(world.TileElevation).Skip(world.Map.Tiles.Count / 4).First();
                world.Environment.Springs = new Dictionary<int, double> { [spring] = 20000 };
                world.Update(12 * world.SecondsPerEnvironmentHour);
                int flooded = Enumerable.Range(0, world.Map.Tiles.Count).Count(t => world.TileSurfaceWater(t) >= Terrain.MinimumFloodDepth && !world.IsWaterTile(t));
                Require(flooded > 0, "The spring flooded nothing.");
                Capture(world, "flooded", 12);
                Require(world.FloodTilesDrawn > 0, "Flooded tiles were not drawn.");
                Console.WriteLine($"Flood render smoke passed: {flooded} flooded tiles in the model, {world.FloodTilesDrawn} drawn.");
                Console.WriteLine("Captures: " + output);

                void Capture(GameWorld w, string name, double time)
                {
                    Matrix4 camera = Matrix4.CreateRotationZ(-MathF.PI / 4) * Matrix4.CreateRotationX(-MathF.PI / 4) * Matrix4.CreateOrthographicOffCenter(-62, 62, -42, 48, -1000, 1000);
                    RenderDevice.SetCamera(camera); GL.ClearColor(0.17f, 0.21f, 0.25f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    w.Draw(new RenderContext(time, 0, 0, time, (ulong)(time * 30), 1, false, false, 1, -45, -45, -1000, -1000, 1000, 1000, 8)); GL.Finish();
                    Require(GL.GetError() == ErrorCode.NoError, "Flood render OpenGL error.");
                    FramebufferCapture.SavePng(Path.Combine(output, name + ".png"), 1100, 800);
                }
            }
            finally { RenderDevice.Dispose(); }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
