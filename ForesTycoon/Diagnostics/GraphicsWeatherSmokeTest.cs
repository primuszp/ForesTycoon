using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    internal static class GraphicsWeatherSmokeTest
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings
            {
                StartVisible = false, ClientSize = new Vector2i(1100, 800), NumberOfSamples = 4,
                API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core
            });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            Terrain terrain = null;
            try
            {
                terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, 20260913), ForestVisualFixture.Height);
                terrain.Map.BuildRoadTilePath(101, 165);
                var forest = new ForestSystem(terrain.Map, ForestVisualFixture.CreateStands());
                var vehicles = new VehicleSystem();
                int[] route = terrain.Map.FindDemoRoadRoute();
                Require(route.Length >= 2, "Graphics fixture has no road.");
                vehicles.Spawn(route);
                Require(new GraphicsSettings().ShowGrid, "Grid is not enabled by default.");
                // Toggle comparisons intentionally start with the grid disabled.
                var settings = new GraphicsSettings { Enhanced = false, Fog = false, Wildlife = false, ShowGrid = false };
                using var renderer = new TerrainRenderer(terrain, vehicles, new WorldEffectSystem(), forest, settings);
                string output = Path.GetFullPath("artifacts/graphics-weather"); Directory.CreateDirectory(output);
                GL.Enable(EnableCap.DepthTest); GL.Viewport(0, 0, 1100, 800);
                Matrix4 matrix = Matrix4.CreateRotationZ(-MathF.PI / 4) * Matrix4.CreateRotationX(-MathF.PI / 4) *
                    Matrix4.CreateOrthographicOffCenter(-65, 65, -43, 51, -1000, 1000);
                double time = 0;
                byte[] original = Frame("original");
                settings.Enhanced = true;
                byte[] sunny = Frame("sunny-textured");
                settings.ShowGrid = true;
                byte[] texturedGrid = Frame("grid-textured-all-surfaces");
                Require(!sunny.AsSpan().SequenceEqual(texturedGrid), "Textured grid is missing.");
                Require(terrain.CachedGridHasAllTileBoundaries(), "Textured grid omits tile boundaries.");
                settings.Textures = false;
                byte[] plainGrid = Frame("grid-plain-all-surfaces");
                settings.ShowGrid = false;
                Require(!plainGrid.AsSpan().SequenceEqual(Frame("grid-plain-disabled")), "Plain grid is missing.");
                settings.Textures = true;
                Frame("lod-far-medium-blend",3.5f);
                byte[] lodBlend=Frame("lod-medium-near-blend",9);
                CheckRestored(lodBlend,Frame("lod-medium-near-paused",9));
                Frame(null, 1); // Leave the near hysteresis band before comparing the initial medium-LOD baseline.
                Require(!original.AsSpan().SequenceEqual(sunny), "Enhanced graphics did not change the image.");
                settings.Shadows = false;
                Require(!sunny.AsSpan().SequenceEqual(Frame("sunny-no-shadows")), "Shadow toggle did not change the image.");
                settings.Shadows = true;
                settings.Lighting = false;
                Require(!sunny.AsSpan().SequenceEqual(Frame("sunny-no-lighting")), "Lighting toggle did not change the image.");
                settings.Lighting = true;
                settings.SunAzimuth = 225;
                Require(!sunny.AsSpan().SequenceEqual(Frame("sunny-other-direction")), "Sun direction did not change the image.");
                settings.SunAzimuth = 135;
                settings.Textures = false;
                byte[] untextured = Frame("sunny-no-textures");
                Require(!sunny.AsSpan().SequenceEqual(untextured), "Texture toggle did not change the image.");
                settings.Textures = true;
                settings.Preset = WeatherPreset.Rain;
                for(int i = 0; i < 25; i++) { time += 1; Frame(); }
                byte[] rainy = Frame("rain");
                Require(!sunny.AsSpan().SequenceEqual(rainy), "Rain did not change the image.");
                settings.Fog = true;
                settings.Preset = WeatherPreset.Storm;
                for(int i = 0; i < 12; i++) { time += 1; Frame(); }
                byte[] storm = Frame("storm");
                Require(!rainy.AsSpan().SequenceEqual(storm), "Storm did not change the image.");
                settings.Fog = false;
                byte[] noFog=Frame("storm-no-fog");
                int visibleFogPixels=0;
                for(int p=0;p<storm.Length;p+=4)
                    if(Math.Abs(storm[p]-noFog[p])+Math.Abs(storm[p+1]-noFog[p+1])+Math.Abs(storm[p+2]-noFog[p+2])>24) visibleFogPixels++;
                Require(visibleFogPixels>5000, "Fog is too faint or billboard orientation is incorrect.");
                settings.Fog = true;
                settings.Clouds = false;
                Require(!storm.AsSpan().SequenceEqual(Frame("storm-no-clouds")), "Cloud toggle did not change the image.");
                settings.Clouds = true;
                CheckRestored(storm, Frame("storm-paused"));
                time = WeatherVisualState.LightningOnset(3) + 0.005;
                byte[] lightning = Frame("storm-lightning");
                CheckRestored(lightning, Frame("storm-lightning-paused"));
                settings.Lightning = false;
                Require(!lightning.AsSpan().SequenceEqual(Frame("storm-no-lightning")), "Lightning toggle did not change the image.");
                settings.Lightning = true;
                settings.Preset = WeatherPreset.Sunny;
                for(int i=0;i<8;i++){time+=1;Frame();}
                Frame("post-rain-fog");
                settings.Fog = false;
                settings.Preset = WeatherPreset.Sunny;
                for(int i = 0; i < 25; i++) { time += 1; Frame(); }
                Frame("drying");
                settings.LightningRequest++;
                Require(!sunny.AsSpan().SequenceEqual(Frame("manual-lightning-sunny")), "Manual lightning did not appear.");
                settings.Lightning = false;
                settings.Fog = true;
                byte[] fogFrame=Frame("fog-sunny");
                Require(!sunny.AsSpan().SequenceEqual(fogFrame), "Fog did not appear in sunny weather.");
                CheckRestored(fogFrame,Frame("fog-paused"));
                GL.Viewport(0,0,900,650); Frame();
                GL.Viewport(0,0,1100,800);
                CheckRestored(fogFrame,Frame("fog-resize-restored"));
                settings.Quality = GraphicsQuality.Low;
                Frame("quality-low");
                settings.Quality = GraphicsQuality.Medium;
                Frame("quality-medium");
                settings.Quality = GraphicsQuality.High;
                CheckRestored(fogFrame, Frame("quality-high-restored"));
                settings.Fog = false;
                settings.Weather = false;
                CheckRestored(sunny, Frame("weather-disabled"));
                settings.Enhanced = false;
                byte[] restored = Frame("original-restored");
                // MSAA/driver rounding can differ by one 8-bit channel value after shader switches.
                CheckRestored(original, restored);
                Require(terrain.StaticTerrainRebuilds == 0 && terrain.ForestChunkRebuilds == 0, "Graphics toggles rebuilt static geometry.");
                Console.WriteLine("Graphics/weather smoke passed: texture toggle, sunlight/shadows, rain/storm/clouds, paused frame, drying, cache reuse, original mode restoration (1/255 channel tolerance).");
                Console.WriteLine($"Captures: {output}");
                CaptureLoading();
                CaptureWaterGrid();
                CaptureCurve();
                CaptureTruck(1,"imported-log-truck-loaded");
                CaptureTruck(0,"imported-log-truck-empty");
                CaptureLargeForest(ForestPattern.LargeMixed,"large-mixed-forest");
                CaptureLargeForest(ForestPattern.LargeSpruce,"large-spruce-forest");
                CaptureLargeForest(ForestPattern.LargeBroadleaf,"large-broadleaf-forest");

                void CaptureWaterGrid()
                {
                    var map=new Terrain(TerrainSettings.Default.WithNodeSize(17,42),
                        (u,v)=>(u-8)*(u-8)+(v-8)*(v-8)<20?-1:2);
                    try {
                    var options=new GraphicsSettings { ShowGrid=true, Weather=false, Fog=false, Wildlife=false };
                    using var scene=new TerrainRenderer(map,new VehicleSystem(),new WorldEffectSystem(),
                        new ForestSystem(map.Map,new ForestStand[256]),options);
                    foreach(bool textured in new[]{false,true}) {
                        options.Textures=textured;
                        GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
                        RenderDevice.SetCamera(matrix);
                        scene.Draw(new RenderContext(0,0,0,0,0,1,false,false,1,-45,-45,-1000,-1000,1000,1000,8));
                        GL.Finish();Require(GL.GetError()==ErrorCode.NoError,"Water grid render error.");
                        Require(map.CachedGridHasAllTileBoundaries(),"Shore grid is incomplete.");
                        FramebufferCapture.SavePng(Path.Combine(output,textured?"grid-water-textured.png":"grid-water-plain.png"),1100,800);
                    }
                    } finally { map.Dispose(); }
                }

                void CaptureLoading()
                {
                    var map = new Terrain(TerrainSettings.Default.WithNodeSize(17,42), (_,_)=>4);
                    try
                    {
                        map.Map.BuildRoadTilePath(53,133);
                        var cargo = new TimberCargoSystem();
                        var traffic = new VehicleSystem(cargo,route => VehicleRoadRoute.Create(map.Map, route));
                        var truck = traffic.Spawn(new[] {53,69,85,101,117,133});
                        using var scene = new TerrainRenderer(map,traffic,new WorldEffectSystem(),
                            new ForestSystem(map.Map,new ForestStand[256]),new GraphicsSettings {Fog=false,Weather=false});
                        truck.RoadRoute.GetPose(0,out var position,out _,out _,out _);
                        Matrix4 view = Matrix4.CreateTranslation(-position)*Matrix4.CreateRotationZ(-MathF.PI/4)*
                            Matrix4.CreateRotationX(-MathF.PI/4)*Matrix4.CreateOrthographicOffCenter(-13,13,-9,11,-1000,1000);
                        Capture("truck-waiting");
                        cargo.AddHarvested(25); traffic.Update(1.0/30); traffic.Update(1.5);
                        Require(truck.TransportState == VehicleTransportState.Loading, "Loading state not visible.");
                        Capture("truck-loading-half");
                        traffic.Update(1.51);
                        Capture("truck-loading-full");
                        Require(truck.VisualCargoFill > 0.99f, "Full load not visible.");
                        void Capture(string name)
                        {
                            GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
                            RenderDevice.SetCamera(view);
                            scene.Draw(new RenderContext(0,0,0,0,0,1,false,false,1,-45,-45,-100,-100,100,100,40));
                            GL.Finish(); Require(GL.GetError()==ErrorCode.NoError,"Loading render error.");
                            FramebufferCapture.SavePng(Path.Combine(output,name+".png"),1100,800);
                        }
                    }
                    finally { map.Dispose(); }
                }

                void CaptureCurve()
                {
                    var map=new Terrain(TerrainSettings.Default.WithNodeSize(17,42),(_,_)=>4);
                    try{
                        map.Map.BuildRoadTilePath(53,133);map.Map.BuildRoadTilePath(133,138);
                        var cargo=new TimberCargoSystem();cargo.AddHarvested(25);
                        var traffic=new VehicleSystem(cargo,route => VehicleRoadRoute.Create(map.Map, route)) { UseCargoStops = false };
                        var truck=traffic.Spawn(new[]{53,69,85,101,117,133,134,135,136,137,138});
                        var graphics=new GraphicsSettings{Fog=false,Weather=false};
                        using var scene=new TerrainRenderer(map,traffic,new WorldEffectSystem(),new ForestSystem(map.Map,new ForestStand[256]),graphics);
                        int frame=0;
                        foreach(double point in new[]{4.3,5.0,5.7}){
                            while(truck.RoutePosition<point && frame++<2000)traffic.Update(1.0/60);
                            truck.RoadRoute.GetPose(truck.RoutePosition,out var p,out _,out _,out _);
                            GL.ClearColor(0.1725f,0.2078f,0.251f,1);
                            GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
                            Matrix4 view=Matrix4.CreateTranslation(-p)*Matrix4.CreateRotationZ(-MathF.PI/4)*
                                Matrix4.CreateRotationX(-MathF.PI/4)*Matrix4.CreateOrthographicOffCenter(-13,13,-9,11,-1000,1000);
                            RenderDevice.SetCamera(view);
                            scene.Draw(new RenderContext(frame/60.0,1f/60,0,frame/60.0,0,1,false,false,1,-45,-45,-100,-100,100,100,40));
                            GL.Finish();Require(GL.GetError()==ErrorCode.NoError,"Steered truck graphics error.");
                            FramebufferCapture.SavePng(Path.Combine(output,"truck-curve-"+point.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+".png"),1100,800);
                        }
                    }finally{map.Dispose();}
                }

                void CaptureTruck(float fill,string name)
                {
                    var graphics=new GraphicsSettings{Weather=false,Shadows=false};
                    using var visual=new SurfaceVisualRenderer(graphics,new WeatherVisualState());
                    visual.BeginFrame(); visual.Kind=SurfaceKind.Vehicle;
                    var previous=RenderDevice.Visuals;
                    try{
                        RenderDevice.Visuals=visual;
                        GL.ClearColor(0.1725f,0.2078f,0.251f,1);
                        GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
                        Matrix4 view=Matrix4.CreateRotationZ(-MathF.PI/4)*Matrix4.CreateRotationX(-MathF.PI/5)*
                            Matrix4.CreateOrthographicOffCenter(-2.7f,2.7f,-1.2f,2.7f,-100,100);
                        RenderDevice.SetCamera(view);
                        VehicleRenderer.DrawPreview(fill);
                        GL.Finish();Require(GL.GetError()==ErrorCode.NoError,"Imported truck graphics error.");
                        FramebufferCapture.SavePng(Path.Combine(output,name+".png"),1100,800);
                    }finally{RenderDevice.Visuals=previous;}
                }

                void CaptureLargeForest(ForestPattern pattern,string name)
                {
                    var landscape=new Terrain(TerrainSettings.Default.WithNodeSize(33,8127).WithForestPattern(pattern),
                        (_,_)=>4);
                    try
                    {
                    var landscapeForest=new ForestSystem(landscape.Map);
                    var graphics=new GraphicsSettings { Fog=true, FogDensity=0.8f };
                    using var landscapeRenderer=new TerrainRenderer(landscape,new VehicleSystem(),new WorldEffectSystem(),landscapeForest,graphics);
                    GL.ClearColor(0.1725f,0.2078f,0.251f,1);
                    GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
                    Matrix4 wide=Matrix4.CreateRotationZ(-MathF.PI/4)*Matrix4.CreateRotationX(-MathF.PI/4)*
                        Matrix4.CreateOrthographicOffCenter(-130,130,-86,102,-1000,1000);
                    RenderDevice.SetCamera(wide);
                    landscapeRenderer.Draw(new RenderContext(30,1,0,30,900,0,false,false,1,-45,-45,-130,-86,130,102,4));
                    GL.Finish();Require(GL.GetError()==ErrorCode.NoError,"Large forest graphics error.");
                    FramebufferCapture.SavePng(Path.Combine(output,name+".png"),1100,800);
                    Console.WriteLine($"{name}: {landscapeForest.Count} stands.");
                    }
                    finally { landscape.Dispose(); }
                }

                void CheckRestored(byte[] expected, byte[] actual)
                {
                    for(int i = 0; i < expected.Length; i++)
                        Require(Math.Abs(expected[i] - actual[i]) <= (i%4==3?2:1), "Display toggle failed to restore its baseline image.");
                }

                byte[] Frame(string name = null,float pixelsPerWorldUnit=8)
                {
                    GL.ClearColor(0.1725f, 0.2078f, 0.251f, 1);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    RenderDevice.SetCamera(matrix); RenderMetrics.BeginFrame();
                    renderer.Draw(new RenderContext(time, 1, 0, time, (ulong)(time * 30), 0, false, false, 1,
                          -45, -45, -65, -43, 65, 51, pixelsPerWorldUnit));
                    GL.Finish(); Require(GL.GetError() == ErrorCode.NoError, "Graphics/weather OpenGL error.");
                    if(name == null) return Array.Empty<byte>();
                    FramebufferCapture.SavePng(Path.Combine(output, name + ".png"), 1100, 800);
                    byte[] pixels = new byte[1100 * 800 * 4];
                    GL.ReadPixels(0, 0, 1100, 800, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                    return pixels;
                }
            }
            finally { terrain?.Dispose(); RenderDevice.Dispose(); }
        }

        private static void Require(bool condition, string message)
        { if(!condition) throw new InvalidOperationException(message); }
    }
}
