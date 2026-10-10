using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    // Invoked in the graphics smoke test's actual current GL context.
    internal static class WeatherEffectBudgetSmoke
    {
        internal static void Run()
        {
            var settings = new Settings(); var weather = new WeatherVisualState();
            for (int second = 0; second < 10; second++) weather.Update(second, settings);
            var surface = new Surface();
            using var rain = new WeatherRenderer(); using var clouds = new CloudRenderer();
            var context = new RenderContext(9, 0, 0, 9, 0, 0, false, false, 1, -60, -45, -32, -32, 32, 32, 5);
            foreach (int budget in new[] { 0, 1, 25, 49, 1500, 3000, 6000 }) {
                settings.RainBudget = budget; rain.Draw(surface, weather, context, settings);
                Require(rain.Metrics.Particles <= budget && rain.Metrics.Particles >= 0, "Rain exceeded its particle budget.");
                Require(rain.Metrics.CpuMilliseconds >= 0, "Rain CPU timing was not published.");
            }
            Require(surface.Fills == 1, "Stable height surface was uploaded repeatedly.");
            surface.Columns = 3; surface.Rows = 2; rain.Draw(surface, weather, context, settings);
            Require(surface.Fills == 2 && rain.Metrics.CpuPayloadBytes == 24 && rain.Metrics.GpuPayloadBytes == 24, "Height texture did not resize at the same revision.");
            var replacement = new Surface { Columns = 3, Rows = 2 }; rain.Draw(replacement, weather, context, settings);
            Require(replacement.Fills == 1, "Same-revision replacement surface reused old height data.");
            replacement.BadHeight = true; replacement.Revision++;
            bool rejected = false;
            try { rain.Draw(replacement, weather, context, settings); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Non-finite height reached the GPU.");
            replacement.BadHeight = false; rain.Draw(replacement, weather, context, settings);
            Require(rain.Metrics.Particles > 0, "Height upload could not retry after failure.");
            replacement.Columns = int.MaxValue; replacement.Rows = int.MaxValue; rejected = false;
            try { rain.Draw(replacement, weather, context, settings); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Oversized height field was not rejected before allocation.");
            replacement.Columns = replacement.Rows = 2;
            foreach (int steps in new[] { 0, 6, 8, 12, 32 }) {
                settings.CloudSteps = steps; clouds.Draw(replacement, weather, settings);
                Require(clouds.Metrics.CloudSteps == steps, "Cloud work did not match its bounded step count.");
            }
            settings.CloudSteps = 33; rejected = false;
            try { clouds.Draw(replacement, weather, settings); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Unbounded cloud work was accepted.");
            rain.BeginFrame(); clouds.BeginFrame();
            Require(rain.Metrics.Particles == 0 && clouds.Metrics.CloudSteps == 0, "Skipped effects retained previous-frame counters.");
            rain.Dispose(); rain.Dispose(); clouds.Dispose(); clouds.Dispose();
            Require(rain.Metrics.CpuPayloadBytes == 0 && rain.Metrics.GpuPayloadBytes == 0, "Disposed rain retained its height data.");
            Require(GL.GetError() == ErrorCode.NoError, "Effect budget smoke produced a GL error.");
            CheckFogDepth();
            CheckFogCollection();
            Console.WriteLine("Effect budgets: tiny/zero rain, bounded clouds, same-revision resize/replacement, height failure/retry, memory limit and frame metrics passed.");
        }
        private static void CheckFogCollection()
        {
            using var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(129, 42), (_, _) => 4);
            var forest = new ForestSystem(terrain.Map); forest.Clear();
            var context = new RenderContext(0, 0, 0, 0, 0, 0, false, false, 1, -60, -45, -10000, -10000, 10000, 10000, 5);
            terrain.UpdateVisibleTiles(context);
            var sources = new System.Collections.Generic.List<FogSource>();
            var crowns = new System.Collections.Generic.List<Vector3>();
            terrain.CollectForestWeather(sources, forest, crowns, false);
            Require(terrain.CachedFogSourceCount == Terrain.MaxCachedFogSources, "Large empty map did not exercise the bounded fog cache.");
            int plantedTile = -1;
            for (int tileId = 0; tileId < 16384; tileId++) {
                if (forest.Plant(tileId, ForestSpecies.Oak) != ForestryActionResult.Planted) continue;
                plantedTile = tileId; break;
            }
            Require(plantedTile >= 0, "Fog fixture contained no suitable planting tile.");
            terrain.CollectForestWeather(sources, forest, crowns, true, sourceLimit: 768, crownLimit: 3);
            Require(crowns.Count > 0 && crowns.Count <= 3 && sources.Count <= 768, "Fog/lightning collection exceeded its target budget.");
            terrain.Dispose(); Require(terrain.CachedFogSourceCount == 0, "Disposed terrain retained fog cache entries.");
            Console.WriteLine("Fog habitat cache: 16384 tiles, bounded entries/crown targets and disposal passed.");
        }
        private static void CheckFogDepth()
        {
            int[] previous = new int[4]; GL.GetInteger(GetPName.Viewport, previous);
            using var fog = new OpenGl.OpenGlForestWeatherRenderer();
            try {
                GL.Viewport(0, 0, 32, 32);
                Require(fog.CaptureDepth(4096) && fog.Metrics.GpuPayloadBytes == 4096, "Fog depth did not fit its exact budget.");
                var handles = fog.DepthHandles;
                Require(!fog.CaptureDepth(4095) && fog.Metrics.GpuPayloadBytes == 0, "Fog budget fallback retained its depth payload.");
                Require(!GL.IsTexture(handles.Texture) && !GL.IsFramebuffer(handles.Framebuffer), "Fog budget fallback did not delete native resources.");
                Require(fog.CaptureDepth(4096), "Fog depth could not recover after budget fallback.");
                handles = fog.DepthHandles; fog.Dispose(); fog.Dispose();
                Require(!GL.IsTexture(handles.Texture) && !GL.IsFramebuffer(handles.Framebuffer), "Disposed fog retained native resources.");
                Require(GL.GetError() == ErrorCode.NoError, "Fog depth lifecycle produced a GL error.");
            } finally { GL.Viewport(previous[0], previous[1], previous[2], previous[3]); }
            Console.WriteLine("Fog depth: exact budget, native deletion under pressure, recovery and disposal passed.");
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private sealed class Surface : IWeatherSurface
        {
            public int Columns { get; set; } = 2;
            public int Rows { get; set; } = 2;
            public ulong Revision { get; set; } = 1;
            internal int Fills;
            internal bool BadHeight;
            public void FillHeights(float[] heights) { Require(heights.Length == Columns * Rows, "Height array has wrong dimensions."); Fills++; Array.Fill(heights, BadHeight ? float.NaN : 0); }
            public void GetBounds(out Vector3 min, out Vector3 max) { min = new(-32, -32, 0); max = new(32, 32, 10); }
            public void GetVisibleBounds(out Vector2 min, out Vector2 max) { min = new(-32); max = new(32); }
        }
        private sealed class Settings : IWeatherSettings
        {
            public bool Weather => true;
            public bool Lightning => false;
            public int LightningRequest => 0;
            public bool AutomaticWeather => false;
            public WeatherPreset Preset => WeatherPreset.Storm;
            public bool ExperimentalSnow => false;
            public int RainBudget { get; set; } = 1500;
            public int CloudSteps { get; set; } = 6;
        }
    }
}
