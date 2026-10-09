using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    // --daylight-preview: one landscape at four seasons × four times of day, saved as separate PNGs for review.
    internal static class DaylightPreview
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings { StartVisible = false, ClientSize = new Vector2i(960, 540),
                API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize(); GL.Enable(EnableCap.DepthTest); GL.Viewport(0, 0, 960, 540);
            string output = Path.GetFullPath("artifacts/daylight"); Directory.CreateDirectory(output);
            try
            {
                using var world = new GameWorld(TerrainSettings.Default);
                world.GetWorldBounds(out var min, out var max);
                var centre = (min + max) * 0.5f;
                var camera = Matrix4.CreateTranslation(-centre) * Matrix4.CreateRotationZ(-MathF.PI / 4)
                    * Matrix4.CreateRotationX(-MathF.PI / 3) * Matrix4.CreateOrthographicOffCenter(-60, 60, -34, 34, -1000, 1000);
                string[] seasons = { "tavasz", "nyar", "osz", "tel" };
                (string Name, float Day)[] hours = { ("hajnal", 0.25f), ("del", 0.5f), ("alkony", 0.75f), ("ejjel", 0.02f) };
                for (int s = 0; s < 4; s++)
                    foreach (var (name, day) in hours)
                    {
                        // Mid-season, the chosen hour of a visible day.
                        double year = s * 0.25 + 0.125;
                        double visibleDay = Math.Floor(year * Daylight.DaysPerYear) + day - Daylight.StartHour;
                        Daylight.Override = Daylight.At(visibleDay / Daylight.DaysPerYear);
                        for (int frame = 0; frame < 3; frame++)
                        {
                            var context = new RenderContext(frame, 1f / 30, (ulong)frame, frame, 0, 0, false, false, 1, -60, -45, -60, -34, 60, 34, 8);
                            RenderDevice.SetCamera(camera);
                            var sky = new Vector3(0.17f, 0.21f, 0.25f) * Daylight.Override.Value.SkyTint;
                            GL.ClearColor(sky.X, sky.Y, sky.Z, 1);
                            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                            world.Draw(context);
                            GL.Finish();
                        }
                        FramebufferCapture.SavePng(Path.Combine(output, $"{s + 1}-{seasons[s]}-{name}.png"), 960, 540);
                    }
                Console.WriteLine($"Daylight preview: {output}");
            }
            finally { Daylight.Override = null; RenderDevice.Dispose(); }
        }
    }
}
