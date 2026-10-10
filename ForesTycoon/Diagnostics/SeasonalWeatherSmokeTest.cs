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
                forest.UseEnvironmentTempo(900);
                var environment = new EnvironmentSystem(terrain.Map, forest, 900, ClimateDefinition.Default);
                var settings = new GraphicsSettings { AutomaticWeather = true, Wildlife = false };
                Require(!settings.ExperimentalSnow, "Normal winter must not depend on the experimental preview switch.");
                using var renderer = new TerrainRenderer(terrain, new VehicleSystem(), new WorldEffectSystem(), forest, settings, environment);
                using var postProcess = new DioramaPostProcess();
                string output = Path.GetFullPath("artifacts/seasonal-weather"); Directory.CreateDirectory(output);
                GL.Viewport(0, 0, 1100, 800);
                ulong frame = 0;
                bool closeUp = false;
                Capture("01-spring");
                while (environment.Time < 900 * .30) Advance(.5);
                closeUp = true; Capture("07-close-up-summer"); closeUp = false;
                Seek(1, WeatherPreset.Storm); Capture("02-summer-storm");
                Seek(2, WeatherPreset.Rain); Capture("03-autumn-rain");
                closeUp = true; Capture("08-close-up-autumn"); closeUp = false;
                Seek(3, WeatherPreset.Snow); Advance(10); Capture("04-winter-snow");
                Require(environment.MeanSnowCover > 0 && renderer.WeatherParticleCount > 0,
                    "Automatic winter snow or its accumulated cover is missing.");
                using var flakes = new WeatherRenderer();
                var snowVisuals = new WeatherVisualState();
                var snowSurface = new SnowSurface();
                byte[] paused = DrawFlakes();
                Require(paused.AsSpan().SequenceEqual(DrawFlakes()), "Paused snowfall must retain exactly the same flakes.");
                Advance(.5);
                Require(!paused.AsSpan().SequenceEqual(DrawFlakes()), "Snowfall did not animate.");
                for (int i = 0; i < 48; i++) {
                    Advance(1.0 / 12);
                    Draw();
                    FramebufferCapture.SavePng(Path.Combine(output, $"motion-{i:D3}.png"), 1100, 800);
                }
                environment.ForceWeather(WeatherPreset.Snow, 10, 120);
                Advance(40);
                Capture("05-winter-drifts");
                float minSnow = float.MaxValue, maxSnow = 0;
                for (int id = 0; id < environment.CellCount; id++) {
                    float depth = (float)environment.SnowWaterAt(id);
                    minSnow = Math.Min(minSnow, depth); maxSnow = Math.Max(maxSnow, depth);
                }
                Require(maxSnow - minSnow > .1f, "Winter snow depth is uniform across tiles.");
                closeUp = true;
                Capture("06-close-up-winter");
                Console.WriteLine($"Winter tile snow range: {minSnow:F2}–{maxSnow:F2} mm water equivalent.");
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
                        && environment.Preset == preset && environment.RainRate > 2)) Advance(.5);
                    Require(environment.Time < 900, $"Missing seasonal event: {season}, {preset}.");
                }
                void Capture(string name)
                {
                    for (int i = 0; i < 240; i++) Draw();
                    if (closeUp)
                        for (int i = 0; i < 2000 && terrain.ReadyVisibleForestChunks(ForestLod.Near) != terrain.VisibleChunkCount; i++) Draw();
                    if (closeUp) Require(terrain.ReadyVisibleForestChunks(ForestLod.Near) == terrain.VisibleChunkCount,
                        $"Close-up diorama still displays coarse fallback trees: Near={terrain.ReadyVisibleForestChunks(ForestLod.Near)}/{terrain.VisibleChunkCount}, builds={terrain.TotalForestChunkRebuilds}, pending={terrain.HasPendingForestBuild}.");
                    FramebufferCapture.SavePng(Path.Combine(output, name + ".png"), 1100, 800);
                    Console.WriteLine($"{name}: t={environment.Time:F1}, {environment.Preset}, precipitation={environment.RainRate:F2}, snow cover={environment.MeanSnowCover:F3}");
                }
                byte[] Draw()
                {
                    var context = new RenderContext(environment.Time, 0, ++frame, environment.Time,
                        (ulong)(environment.Time * 30), 0, false, false, 1, -45, -45, -100, -100, 100, 100, closeUp ? 16 : 8);
                    bool diorama = postProcess.Begin(settings, 1100, 800);
                    RenderDevice.Clear(new Vector4(.55f, .65f, .8f, 1));
                    if (diorama) postProcess.DrawBackdrop(settings, new Vector3(.55f, .65f, .8f));
                    RenderDevice.SetCamera(Matrix4.CreateRotationZ(-MathF.PI / 4) * Matrix4.CreateRotationX(-MathF.PI / 4)
                        * (closeUp ? Matrix4.CreateOrthographicOffCenter(-35, 35, -22, 38, -1000, 1000)
                            : Matrix4.CreateOrthographicOffCenter(-65, 65, -43, 51, -1000, 1000)));
                    renderer.Draw(context);
                    if (diorama) postProcess.End(settings, closeUp ? 16 : 8, 1, (float)environment.Time);
                    GL.Finish(); Require(GL.GetError() == ErrorCode.NoError, "Seasonal weather OpenGL error.");
                    var pixels = new byte[1100 * 800 * 4];
                    GL.ReadPixels(0, 0, 1100, 800, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                    return pixels;
                }
                void Advance(double seconds) { environment.Update(seconds); forest.Update(seconds); }
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
