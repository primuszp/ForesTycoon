using System.Text.Json;

namespace ForesTycoon.Tests;

public class SeasonalWeatherTests
{
    [Theory]
    [InlineData(17)]
    [InlineData(65)]
    public void WinterDriftsMeltCompletelyBySummer(int nodeSize)
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(nodeSize, 42), (_, _) => 4);
        var environment = new EnvironmentSystem(map, null, 900, ClimateDefinition.Default);
        environment.Update(900);
        Assert.True(environment.SnowWater > 0);
        environment.Update(225);
        Assert.Equal(0, Enumerable.Range(0, environment.CellCount).Max(environment.SnowWaterAt));
        Assert.Equal(0, environment.MeanSnowCover);
        Assert.InRange(Math.Abs(environment.BalanceError), 0, 1e-5);
    }

    [Fact]
    public void WindRedistributesSnowByTileWithoutCreatingWaterAndReloadContinuesExactly()
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
        var environment = new EnvironmentSystem(map, null);
        environment.ForceWeather(WeatherPreset.Snow, 10, 120);
        environment.Update(60);
        var amounts = Enumerable.Range(0, environment.CellCount).Select(environment.SnowWaterAt).ToArray();
        Assert.True(amounts.Max() > amounts.Min() * 1.2, "Wind failed to vary snow depth across tiles.");
        Assert.All(amounts, value => Assert.True(value >= 0));
        Assert.InRange(Math.Abs(environment.BalanceError), 0, 1e-6);
        var restored = new EnvironmentSystem(map, null); restored.Restore(environment.Capture());
        environment.Update(10); restored.Update(10);
        Assert.Equal(JsonSerializer.Serialize(environment.Capture()), JsonSerializer.Serialize(restored.Capture()));
    }

    [Fact]
    public void AutumnIsWetterSummerHasShortStrongStormsAndWinterSnow()
    {
        double summerWet = 0, autumnWet = 0, winterSnow = 0;
        int summerStorms = 0;
        for (int seed = 1; seed <= 64; seed++)
        {
            var weather = new WeatherSystem(seed, 900);
            double lastEvent = -1;
            while (weather.Time < 900 - 1e-8)
            {
                var interval = weather.AdvanceInterval(Math.Min(1, 900 - weather.Time));
                int season = (int)((weather.Time - interval.Seconds / 2) / 225);
                if (interval.Rain > 0)
                {
                    if (season == 1) summerWet += interval.Seconds;
                    if (season == 2) autumnWet += interval.Seconds;
                    if (season == 3 && interval.Snow) winterSnow += interval.Seconds;
                    Assert.Equal(season == 3, interval.Snow);
                }
                if (season == 1 && weather.EventStart != lastEvent)
                {
                    if (weather.Preset is WeatherPreset.Rain or WeatherPreset.Storm)
                        Assert.InRange(weather.EventEnd - weather.EventStart, 0, 20);
                    if (weather.Preset == WeatherPreset.Storm)
                    {
                        Assert.InRange(weather.PeakRain, 32, 52);
                        summerStorms++;
                    }
                }
                lastEvent = weather.EventStart;
            }
        }
        Assert.True(autumnWet > summerWet * 2, $"Autumn {autumnWet}, summer {summerWet}");
        Assert.True(winterSnow > 64 * 225 * .35);
        Assert.True(summerStorms > 10);
    }

    [Fact]
    public void SnowIsStoredConservesWaterRestoresExactlyAndThaws()
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42));
        var environment = new EnvironmentSystem(map, null);
        environment.ForceWeather(WeatherPreset.Snow, 8, 60);
        environment.Update(30);
        Assert.True(environment.SnowWater > 0);
        Assert.True(environment.MeanSnowCover > 0);
        Assert.Equal(0, environment.LiquidRainRate);
        Assert.True(environment.SnowfallRate > 0);
        Assert.InRange(Math.Abs(environment.BalanceError), 0, 1e-6);
        var checkpoint = JsonSerializer.Deserialize<EnvironmentCheckpoint>(JsonSerializer.Serialize(environment.Capture()))!;
        var clone = new EnvironmentSystem(map, null);
        clone.Restore(checkpoint);
        Assert.Equal(JsonSerializer.Serialize(environment.Capture()), JsonSerializer.Serialize(clone.Capture()));
        var visuals = new WeatherVisualState();
        visuals.Update(environment, new GraphicsSettings { AutomaticWeather = true }, 30);
        Assert.True(visuals.Snowfall > 0); Assert.True(visuals.SnowCover > 0); Assert.Equal(0, visuals.Rain);
        double snow = environment.SnowWater;
        environment.ForceWeather(WeatherPreset.Sunny, 0, 60);
        clone.ForceWeather(WeatherPreset.Sunny, 0, 60);
        environment.Update(20); clone.Update(20);
        Assert.True(environment.SnowWater < snow);
        Assert.InRange(Math.Abs(environment.BalanceError), 0, 1e-6);
        Assert.Equal(JsonSerializer.Serialize(environment.Capture()), JsonSerializer.Serialize(clone.Capture()));
    }
}
