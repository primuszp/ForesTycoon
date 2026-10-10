using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace ForesTycoon
{
    internal static class SeasonalWeatherSmokeTest
    {
        internal static void Run()
        {
            using var window = new NativeWindow(new NativeWindowSettings {
                StartVisible = false, ClientSize = new Vector2i(1100, 800), NumberOfSamples = 4,
                API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
            window.Context.MakeCurrent(); RenderDevice.Initialize(); RenderDevice.InitializeFrameState();
            try
            {
                int seed = FindSeed();
                using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(17, seed), ForestVisualFixture.Height);
                var forest = new ForestSystem(terrain.Map, ForestVisualFixture.CreateStands());
                var environment = new EnvironmentSystem(terrain.Map, forest, 900, ClimateDefinition.Default);
                var settings = new GraphicsSettings { AutomaticWeather = true, Wildlife = false };
                Require(!settings.ExperimentalSnow, "Normal winter must not depend on the experimental preview switch.");
                using var renderer = new TerrainRenderer(terrain, new VehicleSystem(), new WorldEffectSystem(), forest, settings, environment);
                using var postProcess = new DioramaPostProcess();
                string output = Path.GetFullPath("artifacts/seasonal-weather"); Directory.CreateDirectory(output);
                GL.Viewport(0, 0, 1100, 800);
                ulong frame = 0;
                Capture("01-spring");
                Seek(1, WeatherPreset.Storm); Capture("02-summer-storm");
                Seek(2, WeatherPreset.Rain); Capture("03-autumn-rain");
                Seek(3, WeatherPreset.Snow); environment.Update(10); Capture("04-winter-snow");
                Require(environment.MeanSnowCover > 0 && renderer.WeatherParticleCount > 0,
                    "Automatic winter snow or its accumulated cover is missing.");
                using var flakes = new WeatherRenderer();
                var snowVisuals = new WeatherVisualState();
                var snowSurface = new SnowSurface();
                byte[] paused = DrawFlakes();
                Require(paused.AsSpan().SequenceEqual(DrawFlakes()), "Paused snowfall must retain exactly the same flakes.");
                environment.Update(.5);
                Require(!paused.AsSpan().SequenceEqual(DrawFlakes()), "Snowfall did not animate.");
                for (int i = 0; i < 48; i++) {
                    environment.Update(1.0 / 12);
                    Draw();
                    FramebufferCapture.SavePng(Path.Combine(output, $"motion-{i:D3}.png"), 1100, 800);
                }
                Console.WriteLine($"Seasonal weather smoke passed: natural summer storm, autumn rain, winter snow/cover, paused flakes and animation. Seed {seed}. Captures: {output}");

                byte[] DrawFlakes()
                {
                    RenderDevice.Clear(new Vector4(.1f, .15f, .2f, 1));
                    RenderDevice.SetCamera(Matrix4.CreateRotationZ(-MathF.PI / 4) * Matrix4.CreateRotationX(-MathF.PI / 4)
                        * Matrix4.CreateOrthographicOffCenter(-65, 65, -43, 51, -1000, 1000));
                    snowVisuals.Update(environment, settings);
                    flakes.Draw(snowSurface, snowVisuals, new RenderContext(environment.Time, 0, 0, environment.Time,
                        0, 0, false, false, 1, -45, -45, -50, -50, 50, 50, 8), settings);
                    GL.Finish(); Require(GL.GetError() == ErrorCode.NoError, "Snow particle OpenGL error.");
                    var pixels = new byte[1100 * 800 * 4];
                    GL.ReadPixels(0, 0, 1100, 800, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                    Require(flakes.Metrics.Particles > 0, "Snow particle pass submitted no flakes.");
                    return pixels;
                }

                void Seek(int season, WeatherPreset preset)
                {
                    while (environment.Time < 900 && !((int)(environment.Time / 225) == season
                        && environment.Preset == preset && environment.RainRate > 2)) environment.Update(.5);
                    Require(environment.Time < 900, $"Missing seasonal event: {season}, {preset}.");
                }
                void Capture(string name)
                {
                    for (int i = 0; i < 120; i++) Draw();
                    FramebufferCapture.SavePng(Path.Combine(output, name + ".png"), 1100, 800);
                    Console.WriteLine($"{name}: t={environment.Time:F1}, {environment.Preset}, precipitation={environment.RainRate:F2}, snow cover={environment.MeanSnowCover:F3}");
                }
                byte[] Draw()
                {
                    var context = new RenderContext(environment.Time, 0, ++frame, environment.Time,
                        (ulong)(environment.Time * 30), 0, false, false, 1, -45, -45, -100, -100, 100, 100, 8);
                    bool diorama = postProcess.Begin(settings, 1100, 800);
                    RenderDevice.Clear(new Vector4(.55f, .65f, .8f, 1));
                    if (diorama) postProcess.DrawBackdrop(settings, new Vector3(.55f, .65f, .8f));
                    RenderDevice.SetCamera(Matrix4.CreateRotationZ(-MathF.PI / 4) * Matrix4.CreateRotationX(-MathF.PI / 4)
                        * Matrix4.CreateOrthographicOffCenter(-65, 65, -43, 51, -1000, 1000));
                    renderer.Draw(context);
                    if (diorama) postProcess.End(settings, 8, 1, (float)environment.Time);
                    GL.Finish(); Require(GL.GetError() == ErrorCode.NoError, "Seasonal weather OpenGL error.");
                    var pixels = new byte[1100 * 800 * 4];
                    GL.ReadPixels(0, 0, 1100, 800, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                    return pixels;
                }
            }
            finally { RenderDevice.Dispose(); }
        }

        // Deterministically choose a year containing all requested seasonal reference events.
        private static int FindSeed()
        {
            for (int seed = 1; seed <= 64; seed++) {
                var weather = new WeatherSystem(seed, 900);
                bool summer = false, autumn = false, winter = false;
                while (weather.Time < 899.9) {
                    weather.AdvanceInterval(.5);
                    if (weather.RainRate <= 2) continue;
                    summer |= weather.Season == 1 && weather.Preset == WeatherPreset.Storm;
                    autumn |= weather.Season == 2 && weather.Preset == WeatherPreset.Rain;
                    winter |= weather.Season == 3 && weather.Preset == WeatherPreset.Snow;
                }
                if (summer && autumn && winter) return seed;
            }
            throw new InvalidOperationException("No complete seasonal reference year found.");
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private sealed class SnowSurface : IWeatherSurface
        {
            public int Columns => 2;
            public int Rows => 2;
            public ulong Revision => 0;
            public void GetBounds(out Vector3 min, out Vector3 max)
            { min = new Vector3(-50, -50, 0); max = new Vector3(50, 50, 20); }
            public void GetVisibleBounds(out Vector2 min, out Vector2 max)
            { min = new Vector2(-50); max = new Vector2(50); }
            public void FillHeights(float[] heights) => Array.Clear(heights);
        }
    }
}
