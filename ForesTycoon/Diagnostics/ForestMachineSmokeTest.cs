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
                    terrain.Map.MarkSkidTrailPath(117, 85);   // a side branch: a T junction on the trail
                    var logistics = new ForestryLogistics(terrain.Map, forest) { MachinesEnabled = true };
                    var cargo = new TimberCargoSystem();
                    var vehicles = new VehicleSystem(cargo, route => VehicleRoadRoute.Create(terrain.Map, route))
                    { SourceLoader = logistics.Load, DestinationReceiver = logistics.Deliver, RouteValidator = logistics.RouteConnected };
                    logistics.Vehicles = vehicles;
                    Require(logistics.PlaceMill(195), logistics.Status);
                    Require(logistics.PlaceDepot(67), logistics.Status);
                    logistics.Designate(stand);
                    var site = logistics.Sites[0];
                    // A stack beside the trail at the felling, another beside the road.
                    Require(logistics.PlaceStack(120), logistics.Status);
                    Require(logistics.PlaceStack(99), logistics.Status);
                    using var scene = new TerrainRenderer(terrain, vehicles, new WorldEffectSystem(), forest,
                        new GraphicsSettings { Fog = false, Weather = false, Wildlife = false }, logistics: logistics);
                    double time = 0;
                    void Step(double seconds) { for (int i = 0; i < seconds * 30; i++) { logistics.Update(1.0 / 30); vehicles.Update(1.0 / 30); time += 1.0 / 30; } }
                    Capture(scene, terrain, "depot", time);
                    // The order tool's ground marks: target stacks glowing, the cursor tile, a forwarder and a truck route.
                    var orders = terrain.Orders;
                    orders.Candidates.Add(120); orders.Candidates.Add(99); orders.Hover = 99; orders.HoverValid = true;
                    orders.Routes.Add((logistics.PreviewMachinePath(120, 99), System.Drawing.Color.FromArgb(120, 200, 96), true));
                    orders.Routes.Add((logistics.PreviewTruckRoute(99, 195), System.Drawing.Color.FromArgb(110, 176, 236), false));
                    Require(orders.Routes.TrueForAll(r => r.Tiles != null), "Order route preview failed.");
                    Capture(scene, terrain, "orders", time);
                    orders.Clear();
                    foreach (var machine in logistics.Machines)
                        Require(machine.Kind == ForestMachineKind.Harvester ? logistics.AssignProcessor(machine, logistics.StackAt(120))
                            : logistics.AssignForwarder(machine, logistics.StackAt(120), 99), logistics.Status);
                    Require(logistics.AssignTruck(logistics.Trucks[0], logistics.StackAt(99), 195), logistics.Status);
                    Step(6); Capture(scene, terrain, "driving-in", time);
                    Step(30); Capture(scene, terrain, "felling", time);
                    Step(60); Capture(scene, terrain, "forwarding", time);
                    // Discrete eight-second log cycles require a quantity-dependent completion budget.
                    double budget = logistics.Remaining / ForwarderLoading.LogVolume * 24 + 2600;
                    for (double waited = 0; waited < budget; waited += 10)
                    {
                        if (logistics.Machines.TrueForAll(m => !m.Working && m.Tile == 67) && logistics.Trucks[0].Phase == TruckPhase.Parked) break;
                        Step(10);
                    }
                    Capture(scene, terrain, "finished", time);
                    Require(logistics.Machines.TrueForAll(m => !m.Working && m.Tile == 67), "Machines did not return to the depot.");
                    Require(logistics.Trucks[0].Phase == TruckPhase.Parked, "The truck did not return to the depot.");
                    Require(logistics.Remaining < 0.01f, "Timber was left behind when the fleet went home.");
                    Require(logistics.Mills[0].Received > 1, "No timber reached the mill.");
                    Require(terrain.Map.GetSkidTrailWear(117) > 0.3f, "The trail shows no ruts.");
                    Console.WriteLine($"Forest machines: {logistics.Mills[0].Received:F1} m³ delivered for {logistics.Income:N0} eFt, " +
                        $"running costs {logistics.RunningCosts:N0} eFt, trail wear {terrain.Map.GetSkidTrailWear(117):P0}.");
                }
                finally { terrain.Dispose(); }
                Console.WriteLine($"Forest machine smoke passed. Captures: {output}");
            }
            finally { RenderDevice.Dispose(); }

            void Capture(TerrainRenderer scene, Terrain terrain, string name, double seconds)
            {
                terrain.Map.TryGetTileCenter(100, out Vector3 centre);
                RenderDevice.SetCamera(Matrix4.CreateTranslation(-centre) * Matrix4.CreateRotationZ(-MathF.PI / 4)
                    * Matrix4.CreateRotationX(-0.95f) * Matrix4.CreateOrthographicOffCenter(-24, 24, -17, 17, -400, 400));
                GL.ClearColor(0.16f, 0.2f, 0.26f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                scene.Draw(new RenderContext(seconds, 1f / 30, (ulong)(seconds * 30), seconds, (ulong)(seconds * 30), 0, false, false,
                    1, -45, -45, -1000, -1000, 1000, 1000, 20));
                GL.Finish();
                Require(GL.GetError() == ErrorCode.NoError, "OpenGL error.");
                FramebufferCapture.SavePng(Path.Combine(output, name + ".png"), 1100, 800);
            }
        }

        // Three physical logs, photographed at grasp/carry/release on both ends of the route.
        internal static void LoadingPreview(string output)
        {
            Directory.CreateDirectory(output);
            using var window = new NativeWindow(new NativeWindowSettings { StartVisible = false, ClientSize = new Vector2i(1100, 800),
                API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize(); GL.Enable(EnableCap.DepthTest); GL.Viewport(0, 0, 1100, 800);
            try
            {
                var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
                try
                {
                    var forest = new ForestSystem(terrain.Map, new ForestStand[256]);
                    terrain.Map.BuildRoadTilePath(34, 210, RoadPaving.Macadam);
                    var logistics = new ForestryLogistics(terrain.Map, forest) { MachinesEnabled = true };
                    Require(logistics.PlaceDepot(67), logistics.Status);
                    Require(logistics.PlaceStack(99), logistics.Status); Require(logistics.PlaceStack(147), logistics.Status);
                    var source = logistics.StackAt(99); source.Volume = 3 * ForwarderLoading.LogVolume; source.Value = 300;
                    var machine = logistics.Machines.Find(m => m.Kind == ForestMachineKind.Forwarder);
                    Require(logistics.AssignForwarder(machine, source, 147), logistics.Status);
                    using var scene = new TerrainRenderer(terrain, new VehicleSystem(new TimberCargoSystem()), new WorldEffectSystem(), forest,
                        new GraphicsSettings { Fog = false, Weather = false, Wildlife = false }, logistics: logistics);
                    var captured = new System.Collections.Generic.HashSet<string>();
                    double seconds = 0;
                    for (int frame = 0; frame < 18000; frame++)
                    {
                        logistics.Update(1.0 / 30); seconds += 1.0 / 30;
                        if (machine.State is not (ForestMachineState.Loading or ForestMachineState.Unloading)) continue;
                        double phase = ForwarderLoading.Phase(machine);
                        string moment = phase >= .77 ? "release" : phase >= .55 ? "carry" : phase >= .32 ? "grasp" : null;
                        string name = machine.State + "-" + moment;
                        if (moment == null || !captured.Add(name)) continue;
                        Require(terrain.Map.IsNetworkTile(machine.Tile), "Forwarder left the road to load.");
                        terrain.Map.TryGetTileCenter(machine.Tile, out Vector3 center);
                        RenderDevice.SetCamera(Matrix4.CreateTranslation(-center) * Matrix4.CreateRotationZ(-MathF.PI / 4)
                            * Matrix4.CreateRotationX(-.95f) * Matrix4.CreateOrthographicOffCenter(-6, 6, -4.5f, 4.5f, -400, 400));
                        GL.ClearColor(.16f,.2f,.26f,1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                        scene.Draw(new RenderContext(seconds, 1f / 30, (ulong)frame, seconds, (ulong)frame, 0, false, false,
                            1, -45, -45, -1000, -1000, 1000, 1000, 20));
                        GL.Finish(); Require(GL.GetError() == ErrorCode.NoError, "OpenGL error during log loading.");
                        FramebufferCapture.SavePng(Path.Combine(output, name + ".png"), 1100, 800);
                        if (captured.Count == 6) break;
                    }
                    Require(captured.Count == 6, "Missing loading or unloading preview phases.");
                    Console.WriteLine("Three-log roadside loading preview passed: " + Path.GetFullPath(output));
                }
                finally { terrain.Dispose(); }
            }
            finally { RenderDevice.Dispose(); }
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
