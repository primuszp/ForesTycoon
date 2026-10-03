using System;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
namespace ForesTycoon
{
    internal static class EngineStressBenchmark
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings { StartVisible=false,
                ClientSize=new Vector2i(1280,720), NumberOfSamples=4, API=ContextAPI.OpenGL,
                APIVersion=new Version(3,3), Profile=ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize(); GL.Enable(EnableCap.DepthTest);
            Console.WriteLine($"GPU: {GL.GetString(StringName.Renderer)}; 1280x720 MSAA4; completed frame incl. simulation");
            try
            {
                foreach (var quality in Enum.GetValues<GraphicsQuality>())
                foreach (int count in new[] { 1, 25, 100 })
                {
                    var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(33,42).WithForestPattern(ForestPattern.LargeMixed), (_,_)=>4);
                    try
                    {
                    terrain.BuildRoadTilePath(264,744); terrain.BuildRoadTilePath(744,758);
                    int[] route = terrain.FindDemoRoadRoute();
                    var cargo = new TimberCargoSystem(); cargo.AddHarvested(count * 25);
                    var traffic = new VehicleSystem(cargo,terrain.CreateVehicleRoadRoute) { UseCargoStops = false };
                    for(int i=0;i<count;i++) traffic.Spawn(route).Update(i*0.13);
                    var forest = new ForestSystem(terrain);
                    var settings = new GraphicsSettings { Quality=quality, Preset=WeatherPreset.Storm };
                    long startup = Stopwatch.GetTimestamp();
                    using var renderer = new TerrainRenderer(terrain, traffic, new WorldEffectSystem(), forest,settings);
                    double startupMs = Stopwatch.GetElapsedTime(startup).TotalMilliseconds;
                    var camera = Matrix4.CreateRotationZ(-MathF.PI/4)*Matrix4.CreateRotationX(-MathF.PI/3)*
                        Matrix4.CreateOrthographicOffCenter(-128,128,-72,72,-1000,1000);
                    var times = new double[60]; long allocated=0;
                    GL.Viewport(0,0,1280,720);
                    // Warm actual rain/cloud/wetness state; paused measurements are reproducible.
                    for(int frame=-40;frame<times.Length;frame++)
                    {
                        double time = frame < 0 ? frame+40 : 40;
                        var context = new RenderContext(time,0, (ulong)(frame+40),time,0,1,false,false,1,-60,-45,-128,-72,128,72,5);
                        long allocationStart=GC.GetAllocatedBytesForCurrentThread(), start=Stopwatch.GetTimestamp();
                        if(frame>=0) traffic.Update(1.0/30);
                        RenderDevice.SetCamera(camera); GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
                        RenderMetrics.BeginFrame(); renderer.Draw(context); GL.Finish();
                        if(frame>=0) { times[frame]=Stopwatch.GetElapsedTime(start).TotalMilliseconds; allocated+=GC.GetAllocatedBytesForCurrentThread()-allocationStart; }
                    }
                    Array.Sort(times);
                    Console.WriteLine($"mixed32 storm {quality} trucks={count}: median={times[30]:F2}ms p95={times[57]:F2}ms draws={RenderMetrics.DrawCalls} alloc/frame={allocated/60}B renderer-start={startupMs:F0}ms");
                    var error=GL.GetError(); if(error!=ErrorCode.NoError) throw new InvalidOperationException(error.ToString());
                    }
                    finally { terrain.Dispose(); }
                }
            }
            finally { RenderDevice.Dispose(); }
        }
    }
}
