using System;
using System.Diagnostics;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    internal static class TruckRenderSmokeTest
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new Vector2i(960, 640), NumberOfSamples = 4,
                API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent();
            RenderDevice.Initialize();
            try
            {
                CheckTerrainRoad();
                GL.Enable(EnableCap.DepthTest);
                GL.Enable(EnableCap.CullFace);
                GL.Viewport(0, 0, 960, 640);
                string output = Path.GetFullPath("artifacts/truck-preview");
                Directory.CreateDirectory(output);
                // Vehicle scaling follows the terrain's lane width, including isolated road fixtures.
                using var previewTerrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42), (u, v) => 2);
                foreach (var (name, grade, load) in new[] { ("loaded-uphill", 0.25f, 25f), ("loaded-downhill", -0.25f, 25f), ("empty-flat", 0f, 0f) })
                {
                    var road = new VehicleRoadRoute(new[] { new Vector3(-10, 0, -10 * grade), Vector3.Zero, new Vector3(10, 0, 10 * grade) },
                        new[] { new Vector2(grade, 0), new Vector2(grade, 0), new Vector2(grade, 0) });
                    var cargo = new TimberCargoSystem(); cargo.AddHarvested(load);
                    var system = new VehicleSystem(cargo, _ => road);
                    var truck = system.Spawn(new[] { 0, 1, 2 });
                    system.Update(1);
                    road.GetPose(truck.RoutePosition, out var center, out _, out _, out _);
                    RenderDevice.SetCamera(Matrix4.CreateTranslation(-center.X, -center.Y, -center.Z - 0.3f) *
                        Matrix4.CreateRotationZ(name == "loaded-downhill" ? 2.4f : -0.65f) * Matrix4.CreateRotationX(-1.0f) *
                        Matrix4.CreateOrthographic(5.8f, 3.87f, -100, 100));
                    GL.ClearColor(0.39f, 0.46f, 0.29f, 1);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () => {
                        DynamicPrimitiveBatch.Color3(System.Drawing.Color.FromArgb(104, 107, 107));
                        DynamicPrimitiveBatch.Vertex3(-20, -1, -20 * grade);
                        DynamicPrimitiveBatch.Vertex3(20, -1, 20 * grade);
                        DynamicPrimitiveBatch.Vertex3(20, 1, 20 * grade);
                        DynamicPrimitiveBatch.Vertex3(-20, 1, -20 * grade);
                    });
                    RenderMetrics.BeginFrame();
                    VehicleRenderer.Draw(system, previewTerrain, 1);
                    GL.Finish();
                    if (RenderMetrics.SubmittedVertices == 0 || GL.GetError() != ErrorCode.NoError)
                        throw new InvalidOperationException("Truck rendering failed.");
                    FramebufferCapture.SavePng(Path.Combine(output, name + ".png"), 960, 640);
                    Console.WriteLine($"{name}: vertices={RenderMetrics.SubmittedVertices}, draws={RenderMetrics.DrawCalls}");
                    // Isolated model submission cost; excludes terrain, GUI and presentation.
                    long start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < 120; i++) { VehicleRenderer.Draw(system, previewTerrain, 1); GL.Finish(); }
                    Console.WriteLine($"Truck render average: {Stopwatch.GetElapsedTime(start).TotalMilliseconds / 120:F3} ms");
                }
                {
                    // A right-angle bend: the semi-trailer must swing about the kingpin, cutting in toward the inside.
                    var road = new VehicleRoadRoute(new[] { new Vector3(-10, 0, 0), Vector3.Zero, new Vector3(0, 10, 0) },
                        new[] { Vector2.Zero, Vector2.Zero, Vector2.Zero });
                    var cargo = new TimberCargoSystem(); cargo.AddHarvested(25);
                    var system = new VehicleSystem(cargo, _ => road);
                    var truck = system.Spawn(new[] { 0, 1, 2 });
                    for (int i = 0; i < 4000 && truck.RoutePosition < 1.3; i++) system.Update(0.05);
                    road.GetPose(truck.RoutePosition, out var center, out var forward, out _, out _);
                    road.GetPose(truck.RoutePosition, out _, out _, out var left, out var up);
                    float articulation = VehicleRenderer.Articulation(road, truck.RoutePosition, center, forward, left, up,
                        previewTerrain.RoadLaneWidth * DioramaScale.TruckLaneFill / VehicleRenderer.ModelWidth).Yaw;
                    Console.WriteLine($"loaded-bend: position={truck.RoutePosition:F2}, articulation={articulation * 180 / MathF.PI:F1}°");
                    // Turning left (+Y), the trailer lags behind: it points right of the tractor (negative yaw).
                    if (VehicleRenderer.ModelArticulated && !(articulation < -0.05f))
                        throw new InvalidOperationException("Semi-trailer does not articulate in a bend.");
                    RenderDevice.SetCamera(Matrix4.CreateTranslation(-center.X, -center.Y, -center.Z) *
                        Matrix4.CreateOrthographic(5.8f, 3.87f, -100, 100));
                    GL.ClearColor(0.39f, 0.46f, 0.29f, 1);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    VehicleRenderer.Draw(system, previewTerrain, 1);
                    GL.Finish();
                    FramebufferCapture.SavePng(Path.Combine(output, "loaded-bend.png"), 960, 640);
                }
                {
                    // Over a crest and through a dip the trailer must pitch so its wheels stay on the road.
                    VehicleRenderer.TruckScale = previewTerrain.RoadLaneWidth * DioramaScale.TruckLaneFill / VehicleRenderer.ModelWidth;
                    foreach (var (name, z) in new[] { ("crest", -2.5f), ("dip", 2.5f) })
                    {
                        // Level approach, then the crest/dip tile, then level again; the whole trailer stays on built road.
                        var road = new VehicleRoadRoute(new[] { new Vector3(-20, 0, z), new Vector3(-10, 0, z), Vector3.Zero,
                                new Vector3(10, 0, z), new Vector3(20, 0, z) },
                            new[] { Vector2.Zero, new Vector2(-z / 10, 0), Vector2.Zero, new Vector2(z / 10, 0), Vector2.Zero });
                        float worst = 0;
                        for (double p = 1.6; p <= 2.8; p += 0.05) worst = Math.Max(worst, Math.Abs(VehicleRenderer.TrailerAxleGap(road, p)));
                        Console.WriteLine($"loaded-{name}: largest trailer-wheel gap {worst:F3} (lane {previewTerrain.RoadLaneWidth:F2})");
                        if (VehicleRenderer.ModelArticulated && worst > 0.02f * VehicleRenderer.TruckScale * VehicleRenderer.ModelWidth)
                            throw new InvalidOperationException($"Semi-trailer wheels leave the road on a {name}.");
                    }
                }
                Console.WriteLine($"Truck render smoke passed. Captures: {output}");
            }
            finally { RenderDevice.Dispose(); }
        }

        private static void CheckTerrainRoad()
        {
            var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 42),
                (u, v) => 2 + Math.Clamp(u - 3, 0, 3));
            try
            {
                terrain.Map.BuildRoadTilePath(36, 132);
                int[] tiles = terrain.Map.FindDemoRoadRoute();
                if (tiles.Length < 5) throw new InvalidOperationException("Slope road fixture failed to build.");
                var road = VehicleRoadRoute.Create(terrain.Map, tiles);
                bool slope = false;
                for (int i = 0; i < tiles.Length; i++)
                {
                    terrain.Map.TryGetRoadTileCenter(tiles[i], out var center);
                    if ((road.Sample(i) - center).Length > 0.001f)
                        throw new InvalidOperationException("Vehicle path left the road surface.");
                    road.GetPose(i, out _, out var forward, out _, out _);
                    slope |= Math.Abs(forward.Z) > 0.01f;
                }
                if (!slope) throw new InvalidOperationException("Slope fixture was flat.");
                var frozen = road.Sample(2.25);
                terrain.Map.EditElevationAtNode(4 * 17 + 4, -1, 0, 1);
                var rebuilt = VehicleRoadRoute.Create(terrain.Map, tiles);
                if ((rebuilt.Sample(2.25) - frozen).Length > 0.001f)
                    throw new InvalidOperationException("Terrain edit moved the frozen road driving surface.");
                var cargo = new TimberCargoSystem(); cargo.AddHarvested(25);
                var vehicles = new VehicleSystem(cargo, route => VehicleRoadRoute.Create(terrain.Map, route));
                vehicles.Spawn(tiles);
                vehicles.Update(1.0 / 30);
                VehicleRenderer.Draw(vehicles, terrain, 1);
                Console.WriteLine("Real road: grade, centerline, frozen foundation and vehicle draw passed.");
            }
            finally { terrain.Dispose(); }
        }
    }
}
