using System.Collections.Generic;

namespace ForesTycoon.Rules;

/// <summary>Editable road weathering; all four contributions are ordinary graph data.</summary>
public static class RoadWeatherRule
{
    public const float AsphaltBaseRate = .015f, AsphaltRainRate = .08f, MacadamBaseRate = .10f, MacadamRainRate = 1.6f;
    public static WorldRule Default() => new() {
        Id = "road.weather", Name = "Időjárási útromlás", Scope = "road",
        Description = "Az eső és a térképi nedvesség burkolatonként csökkenti az út állapotát.",
        Effects = { new() { Target = "condition", Mode = "add", Influence = new() { Terms = {
            Term("Aszfalt: nedvesség", 0, "AsphaltWeather", Moisture(1, 2)),
            Term("Aszfalt: eső", 0, "AsphaltRainWeather", new InfluenceFactor { Source = "rainIntensity" }),
            Term("Makadám: nedvesség", 1, "MacadamWeather", Moisture(1, 3.5)),
            Term("Makadám: eső és nedvesség", 1, "MacadamRainWeather", new InfluenceFactor { Source = "rainIntensity" }, Moisture(.4, 1.4))
        } } } }
    };

    private static InfluenceFactor Moisture(double low, double high) => new() { Source = "tile.moisture", OutputLow = low, OutputHigh = high };
    private static InfluenceTerm Term(string name, double surface, string tuning, params InfluenceFactor[] factors) => new() {
        Name = name, AmountField = "tuning." + tuning, Factors = new List<InfluenceFactor>(factors),
        Conditions = { new() { Field = "surface", Comparison = "equal", Value = surface } }
    };
}
