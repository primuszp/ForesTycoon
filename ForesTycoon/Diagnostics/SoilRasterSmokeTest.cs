using System;
using System.IO;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Vector2 = System.Numerics.Vector2;

namespace ForesTycoon
{
    internal static class SoilRasterSmokeTest
    {
        internal static void Run()
        {
            const int width = 800, height = 800;
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new(width, height), API = ContextAPI.OpenGL,
                APIVersion = new(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            try
            {
                using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(65, 42));
                using var ui = new ImGuiController();
                string output = Path.GetFullPath("artifacts/soil-raster"); Directory.CreateDirectory(output);
                var indices = world.Soils.ProfileIndices.ToArray();
                double time = world.Environment.Time, stored = world.Environment.StoredWater;
                var statistics = world.ForestStatistics;
                int layer = 0, selected = 32 * 64 + 32;
                byte[] Capture(string name)
                {
                    // Warm the ImGui layout and then render the same reader twice.
                    for (int frame = 0; frame < 2; frame++)
                    {
                        ui.Update(width, height, width, height, Vector2.One, 1f / 60);
                        ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(400, 760));
                        ImGui.Begin("Talaj és víz térképe", ImGuiWindowFlags.NoSavedSettings);
                        EcologyRasterView.Draw(world.Soils, world.Environment, ref layer, ref selected);
                        ImGui.End();
                        GL.Viewport(0, 0, width, height); GL.ClearColor(.08f, .1f, .12f, 1);
                        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
                    }
                    if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Soil UI produced a GL error.");
                    FramebufferCapture.SavePng(Path.Combine(output, name + ".png"), width, height);
                    var pixels = new byte[width * height * 4];
                    GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                    return pixels;
                }
                byte[] first = Capture("soil");
                byte[] paused = Capture("soil-paused");
                if (!first.AsSpan().SequenceEqual(paused)) throw new InvalidOperationException("Paused soil view changed.");
                layer = 1; byte[] water = Capture("root-water");
                if (first.AsSpan().SequenceEqual(water)) throw new InvalidOperationException("Raster layer switch had no effect.");
                if (time != world.Environment.Time || stored != world.Environment.StoredWater || statistics != world.ForestStatistics ||
                    !indices.AsSpan().SequenceEqual(world.Soils.ProfileIndices)) throw new InvalidOperationException("Raster reader changed simulation.");
                using var save = new MemoryStream(); world.Save(save); save.Position = 0; world.Load(save);
                if (!indices.AsSpan().SequenceEqual(world.Soils.ProfileIndices) || stored != world.Environment.StoredWater)
                    throw new InvalidOperationException("Soil save/replay changed the raster or water.");
                Console.WriteLine($"Soil raster UI smoke passed: categorical/water layers, paused image, read-only state and pinned save/replay. Captures: {output}");
            }
            finally { RenderDevice.Dispose(); }
        }
    }
}
