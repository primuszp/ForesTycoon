using System;
using System.Diagnostics;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    internal static class ForestTempoBenchmark
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings { StartVisible = false,
                ClientSize = new Vector2i(1280,720), NumberOfSamples = 4, API = ContextAPI.OpenGL,
                APIVersion = new Version(3,3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize();
            GL.Enable(EnableCap.DepthTest); GL.Viewport(0,0,1280,720);
            try
            {
                Measure(1200); Measure(EnvironmentSystem.DefaultGameSecondsPerYear);
            }
            finally { RenderDevice.Dispose(); }
        }
        private static void Measure(double yearSeconds)
        {
            using var world = new GameWorld(TerrainSettings.Default, yearSeconds);
            var runner = new SimulationFrameRunner(30,2048,8);
            runner.Clock.Speed = 256;
            world.GetWorldBounds(out var min,out var max);
            var camera = Matrix4.CreateTranslation(-(min+max)*.5f) * Matrix4.CreateRotationZ(-MathF.PI/4)
                * Matrix4.CreateRotationX(-MathF.PI/3) * Matrix4.CreateOrthographic(256,144,-1000,1000);
            for(int frame=0;frame<30;frame++) { world.Update(1.0/30); Draw(frame); }
            runner.Clock.Reset(world.SimulationTick);
            double initialYear = world.Environment.Time/world.Environment.ForestYearSeconds;
            long started = Stopwatch.GetTimestamp(), previous = started;
            double maxFrameMs = 0;
            for(int frame=0;frame<180;frame++)
            {
                long current = Stopwatch.GetTimestamp();
                double dt = frame==0 ? 1.0/60 : Stopwatch.GetElapsedTime(previous,current).TotalSeconds;
                previous = current;
                runner.Advance(dt,world.ExecutePendingCommands,world.Update);
                Draw(frame+30);
                maxFrameMs = Math.Max(maxFrameMs,Stopwatch.GetElapsedTime(current).TotalMilliseconds);
            }
            double elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds;
            var environment = world.Environment;
            double years = environment.Time/environment.ForestYearSeconds - initialYear;
            Console.WriteLine($"256x, year={yearSeconds:0}s, 180 update/render frames: {years:0.00} forest years / {elapsed:0.00} real seconds ({years/elapsed:0.00} years/s), max frame {maxFrameMs:0.00} ms; {world.ForestTreeCount} trees.");
            if(world.SimulationTick!=runner.Clock.Tick)throw new InvalidOperationException("Fast-forward world/clock ticks diverged.");
            double water=environment.StoredWater, rain=environment.TotalRain;
            var statistics=world.ForestStatistics;
            using var saved = new MemoryStream(); world.Save(saved); saved.Position=0;
            if(yearSeconds==1200)
            {
                // A genuine v4 replay has no tempo field and must retain the old calendar.
                var data=WorldSaveSerializer.Read(saved); saved.SetLength(0); saved.Position=0;
                WorldSaveSerializer.Write(saved,new WorldSaveData { Version=4,Tick=data.Tick,
                    TickRate=data.TickRate,Terrain=data.Terrain,Commands=data.Commands });
            }
            saved.Position=0;world.Load(saved);
            if(world.Environment.ForestYearSeconds!=yearSeconds || world.Environment.StoredWater!=water
                || world.Environment.TotalRain!=rain || world.ForestStatistics!=statistics)
                throw new InvalidOperationException("Fast-forward calendar/save replay changed ecology.");
            Console.WriteLine($"  Calendar, water, rain and forest statistics replay exactly; water balance error {world.Environment.BalanceError:E2}.");
            void Draw(int frame)
            {
                RenderDevice.SetCamera(camera);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
                double seconds=world.SimulationTick/30.0;
                world.Draw(new RenderContext(seconds,0,(ulong)frame,seconds,world.SimulationTick,0,
                    false,false,1,-60,-45,-128,-72,128,72,5));GL.Finish();
                if(GL.GetError()!=ErrorCode.NoError)throw new InvalidOperationException("Forest tempo GL error.");
            }
        }
    }
}
