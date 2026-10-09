using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    internal static class RoadNetworkSmokeTest
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings
            {
                StartVisible = false, ClientSize = new Vector2i(1100, 800), NumberOfSamples = 4,
                API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core
            });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            try
            {
                using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
                var map = terrain.Map;
                map.BuildRoadTilePath(34, 82, RoadPaving.Asphalt);
                map.BuildRoadTilePath(98, 130, RoadPaving.Macadam);
                map.BuildRoadTilePath(146, 210, RoadPaving.Asphalt);
                map.BuildRoadTilePath(130, 135, RoadPaving.Macadam);
                map.MarkSkidTrailPath(135, 199);
                map.MarkSkidTrailPath(66, 71);
                map.MarkSkidTrailPath(163, 167); // Loose end beside the road: a visible reciprocal exit.
                // Both unused and worn tracks: the unused ones must remain simple ruts without painted markers.
                foreach (int id in map.SkidTrailTiles) if (id > 80) map.DriveSkidTrail(id, 0.6f);
                int[] path = map.FindNetworkPath(34, 199);
                Require(path.Length > 2 && map.FindNetworkPath(199, 34).Length == path.Length, "Mixed network is not bidirectional.");
                var cargo = new TimberCargoSystem(); cargo.AddHarvested(25);
                var traffic = new VehicleSystem(cargo, route => VehicleRoadRoute.Create(map, route)) { UseCargoStops = false };
                traffic.Spawn(path); traffic.Update(6);
                var stands = new ForestStand[256];
                foreach (int id in new[] { 89, 90, 105, 106, 121, 122, 137, 138, 153, 154, 169, 170 })
                    stands[id] = new ForestStand(ForestSpecies.Spruce, 40, 0.45f, 1);
                var graphics = new GraphicsSettings { Fog = false, Wildlife = false, ShowGrid = false, ExperimentalSnow = true };
                using var renderer = new TerrainRenderer(terrain, traffic, new WorldEffectSystem(), new ForestSystem(map, stands), graphics);
                GL.Enable(EnableCap.DepthTest); GL.Viewport(0, 0, 1100, 800);
                Matrix4 view = Matrix4.CreateRotationZ(-MathF.PI / 4) * Matrix4.CreateRotationX(-MathF.PI / 4) *
                    Matrix4.CreateOrthographicOffCenter(-48, 48, -28, 42, -1000, 1000);
                string output = Path.GetFullPath("artifacts/road-network"); Directory.CreateDirectory(output);
                double time = 0;
                byte[] dry = Frame("dry");
                graphics.Preset = WeatherPreset.Rain;
                for (int i = 0; i < 30; i++) { time++; Frame(); }
                byte[] rain = Frame("rain");
                Require(!dry.AsSpan().SequenceEqual(rain), "Rain does not affect roads.");
                graphics.Preset = WeatherPreset.Snow;
                for (int i = 0; i < 35; i++) { time++; Frame(); }
                byte[] snow = Frame("snow");
                Require(!rain.AsSpan().SequenceEqual(snow), "Snow does not affect roads.");
                byte[] paused = Frame();
                for (int i = 0; i < snow.Length; i++)
                    Require(Math.Abs(snow[i] - paused[i]) <= (i % 4 == 3 ? 2 : 1), "Paused weather changed the image beyond framebuffer rounding tolerance.");
                graphics.Preset = WeatherPreset.Sunny;
                for (int i = 0; i < 90; i++) { time++; Frame(); }
                Frame("thaw");
                Console.WriteLine($"Road network smoke passed: reciprocal exits, mixed routes, rain, snow, pause and thaw. Captures: {output}");

                byte[] Frame(string name = null)
                {
                    GL.ClearColor(0.1725f, 0.2078f, 0.251f, 1);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    RenderDevice.SetCamera(view);
                    renderer.Draw(new RenderContext(time, 1, 0, time, (ulong)(time * 30), 1, false, false, 1,
                        -45, -45, -100, -100, 100, 100, 11));
                    GL.Finish(); Require(GL.GetError() == ErrorCode.NoError, "Road material OpenGL error.");
                    byte[] pixels = new byte[1100 * 800 * 4];
                    GL.ReadPixels(0, 0, 1100, 800, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                    if (name != null) FramebufferCapture.SavePng(Path.Combine(output, name + ".png"), 1100, 800);
                    return pixels;
                }
            }
            finally { RenderDevice.Dispose(); }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
