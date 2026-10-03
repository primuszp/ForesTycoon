using System;
using System.IO;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
namespace ForesTycoon
{
    internal static class EnvironmentSmokeTest
    {
        internal static void Run()
        {
            using var window=new NativeWindow(new NativeWindowSettings {StartVisible=false,ClientSize=new Vector2i(1100,800),
                NumberOfSamples=4,API=ContextAPI.OpenGL,APIVersion=new Version(3,3),Profile=ContextProfile.Core});
            window.Context.MakeCurrent();RenderDevice.Initialize();GL.Enable(EnableCap.DepthTest);GL.Viewport(0,0,1100,800);
            string output=Path.GetFullPath("artifacts/environment");Directory.CreateDirectory(output);
            try {
                using var world=new GameWorld(TerrainSettings.Default.WithNodeSize(17,42).WithForestPattern(ForestPattern.LargeMixed));
                var environment=world.Environment;
                world.GetWorldBounds(out var min,out var max);var center=(min+max)*0.5f;
                var camera=Matrix4.CreateTranslation(-center)*Matrix4.CreateRotationZ(-MathF.PI/4)*Matrix4.CreateRotationX(-MathF.PI/4)*
                    Matrix4.CreateOrthographicOffCenter(-90,90,-65,65,-1000,1000);
                RenderDevice.SetCamera(camera);Capture("dry");
                world.QueueWeather(WeatherPreset.Storm,40,90);world.ExecutePendingCommands();
                for(int tick=0;tick<2100;tick++)world.Update(1.0/30);
                byte[] wet=Capture("storm-70s");
                if(environment.TotalRain<=0||environment.MeanWetness<=0)throw new InvalidOperationException("Rain did not wet terrain.");
                using var save=new MemoryStream();world.Save(save);double rain=environment.TotalRain,water=environment.StoredWater;
                var first=environment.Cell(57);
                world.Graphics.Weather=false;Capture("effects-off");
                if(environment.TotalRain!=rain||environment.Cell(57)!=first)throw new InvalidOperationException("Graphics changed simulation.");
                world.Graphics.Weather=true;
                if(!SameImage(wet,Capture("paused")))throw new InvalidOperationException("Paused scene changed.");
                save.Position=0;world.Load(save);environment=world.Environment;
                if(environment.TotalRain!=rain||environment.StoredWater!=water||environment.Cell(57)!=first)
                    throw new InvalidOperationException("Environment save/replay changed state.");
                Capture("replayed");
                world.QueueWeather(WeatherPreset.Sunny,0,300);world.ExecutePendingCommands();
                for(int tick=0;tick<9000;tick++)world.Update(1.0/30);Capture("drying-300s");
                if(Math.Abs(environment.BalanceError)>1e-5)throw new InvalidOperationException("Water balance failed.");
                world.Graphics.Enhanced=false;Capture("original-mode");
                Console.WriteLine($"Environment smoke passed: wet terrain, storm, pause, graphics independence, exact save/replay, drying, original mode; balance error {environment.BalanceError:E3} cell-mm.");
                Console.WriteLine("Captures: "+output);
                byte[] Capture(string name){
                    GL.ClearColor(0.2f,0.24f,0.28f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
                    double time=environment.Time;
                    world.Draw(new RenderContext(time,0,0,time,world.SimulationTick,0,false,false,1,-45,-45,-500,-500,500,500,6));GL.Finish();
                    if(GL.GetError()!=ErrorCode.NoError)throw new InvalidOperationException("Environment renderer GL error.");
                    FramebufferCapture.SavePng(Path.Combine(output,name+".png"),1100,800);
                    var pixels=new byte[1100*800*4];GL.ReadPixels(0,0,1100,800,PixelFormat.Rgba,PixelType.UnsignedByte,pixels);return pixels;
                }
                foreach(int side in new[]{64,128,256}){
                    var habitat=new BenchmarkHabitat(side*side);var simulation=new EnvironmentSystem(habitat,null);
                    simulation.ForceWeather(WeatherPreset.Storm,40,90);simulation.Update(2);
                    long start=Stopwatch.GetTimestamp();for(int i=0;i<40;i++)simulation.Update(0.5);
                    Console.WriteLine($"Environment {side}x{side}: {Stopwatch.GetElapsedTime(start).TotalMilliseconds/40:0.00} ms/water step; {simulation.BalanceError:E2} balance error.");
                }
            } finally { RenderDevice.Dispose(); }
        }
        private static bool SameImage(byte[] first,byte[] second)
        {
            // MSAA resolves can differ by one 8-bit unit after intervening GPU passes.
            for(int i=0;i<first.Length;i++)if(Math.Abs(first[i]-second[i])>1)return false;
            return true;
        }
        private sealed class BenchmarkHabitat(int count):IForestHabitat
        {
            public int TileCount=>count;public int Seed=>42;public bool CanSupportForest(int id)=>true;
            public float GetMoisture(int id)=>0.5f;public float GetNormalizedElevation(int id)=>0.5f;
            public int GetAdjacentTileIds(int id,Span<int> destination)=>0;
        }
    }
}
