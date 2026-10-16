using System.Text.Json;

namespace ForesTycoon.Tests;

public class SeasonalWeatherTests
{

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

}
