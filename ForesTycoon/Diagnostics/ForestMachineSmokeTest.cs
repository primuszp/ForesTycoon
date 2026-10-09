using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    // Harvester → forwarder → landing → truck on a small map, rendered at a few moments.
    internal static class ForestMachineSmokeTest
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings { StartVisible = false, ClientSize = new Vector2i(1100, 800),
                API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize(); GL.Enable(EnableCap.DepthTest); GL.Viewport(0, 0, 1100, 800);
            string output = Path.GetFullPath("artifacts/forest-machines"); Directory.CreateDirectory(output);
            try
            {
                var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
                try
                {
                    int[] stand = { 136, 137, 138, 152, 153, 154 };
                    var stands = new ForestStand[256];
                    foreach (int id in stand) stands[id] = new ForestStand(ForestSpecies.Spruce, 60, 0.75f, 1);
                    var forest = new ForestSystem(terrain.Map, stands);
                    terrain.Map.BuildRoadTilePath(34, 210, RoadPaving.Macadam);
                    // A trail with a bend: up from the road, then across to the stand.
                    terrain.Map.MarkSkidTrailPath(115, 119); terrain.Map.MarkSkidTrailPath(119, 135);
                    var logistics = new ForestryLogistics(terrain.Map, forest) { MachinesEnabled = true };
                    var cargo = new TimberCargoSystem();
                    var vehicles = new VehicleSystem(cargo, route => VehicleRoadRoute.Create(terrain.Map, route))
                    { SourceLoader = logistics.Load, DestinationReceiver = logistics.Deliver, RouteValidator = logistics.RouteConnected };
                    Require(logistics.PlaceMill(195), logistics.Status);
                    logistics.Designate(stand);
                    Require(logistics.Sites[0].Landing == 115, $"Landing {logistics.Sites[0].Landing}, expected 115.");
                    using var scene = new TerrainRenderer(terrain, vehicles, new WorldEffectSystem(), forest,
                        new GraphicsSettings { Fog = false, Weather = false, Wildlife = false }, logistics: logistics);
                    double time = 0;
                    void Step(double seconds) { for (int i = 0; i < seconds * 30; i++) { logistics.Update(1.0 / 30); vehicles.Update(1.0 / 30); time += 1.0 / 30; } }
                    Step(6); Capture(scene, terrain, "driving-in", time);
                    Step(20); Capture(scene, terrain, "felling", time);
                    logistics.Dispatch(vehicles);
                    Step(60); Capture(scene, terrain, "forwarding", time);
                    Step(400);
                    Capture(scene, terrain, "finished", time);
                    Require(logistics.Mills[0].Received > 1, "No timber reached the mill.");
                    Require(terrain.Map.GetSkidTrailWear(117) > 0.3f, "The trail shows no ruts.");
                    Console.WriteLine($"Forest machines: {logistics.Mills[0].Received:F1} m³ delivered, trail wear {terrain.Map.GetSkidTrailWear(117):P0}.");
                }
                finally { terrain.Dispose(); }
                Console.WriteLine($"Forest machine smoke passed. Captures: {output}");
            }
            finally { RenderDevice.Dispose(); }

            void Capture(TerrainRenderer scene, Terrain terrain, string name, double seconds)
            {
                terrain.Map.TryGetTileCenter(120, out Vector3 centre);
                RenderDevice.SetCamera(Matrix4.CreateTranslation(-centre) * Matrix4.CreateRotationZ(-MathF.PI / 4)
                    * Matrix4.CreateRotationX(-0.95f) * Matrix4.CreateOrthographicOffCenter(-15, 15, -11, 11, -400, 400));
                GL.ClearColor(0.16f, 0.2f, 0.26f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                scene.Draw(new RenderContext(seconds, 1f / 30, (ulong)(seconds * 30), seconds, (ulong)(seconds * 30), 0, false, false,
                    1, -45, -45, -1000, -1000, 1000, 1000, 20));
                GL.Finish();
                Require(GL.GetError() == ErrorCode.NoError, "OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, name + ".png"), 1100, 800);
            }
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
