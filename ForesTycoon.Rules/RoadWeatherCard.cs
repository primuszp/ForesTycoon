using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ForesTycoon.Rules;

/// <summary>Formula-free authoring data. Compiles to ordinary rule effects; the card is the only saved source.</summary>
public sealed class RoadWeatherCard
{
    /// <summary>Lossless migration of the old fixed card to editable sources, curves and conditions.</summary>
    public List<RuleEffect> ToInfluences()
    {
        Validate();
        InfluenceFactor Moisture(double low, double high) => new() { Source = "tile.moisture", OutputLow = low, OutputHigh = high };
        InfluenceTerm Term(string name, bool macadam, double? amount, string tuning, params InfluenceFactor[] factors) => new() {
            Name = name, Amount = amount ?? 0, AmountField = amount.HasValue ? "" : "tuning." + tuning,
            WhenField = "isMacadam", Comparison = macadam ? "greater" : "less", Threshold = .5, Factors = new(factors) };
        return new() { new() { Target = "condition", Mode = "add", Influence = new() { Terms = new() {
            Term("Aszfalt: nedvesség", false, AsphaltBase, "AsphaltWeather", Moisture(1, 1 + AsphaltMoistureFactor)),
            Term("Aszfalt: eső", false, AsphaltRain, "AsphaltRainWeather", new InfluenceFactor { Source = "rainIntensity" }),
            Term("Makadám: nedvesség", true, MacadamBase, "MacadamWeather", Moisture(1, 1 + MacadamMoistureFactor)),
            Term("Makadám: eső és nedvesség", true, MacadamRain, "MacadamRainWeather", new() { Source = "rainIntensity" }, Moisture(MacadamRainBaseline, MacadamRainBaseline + MacadamRainMoistureFactor))
        } } } };
    }
    // Null retains the existing project's tuning (including dynamic tuning bindings).
    public double? AsphaltBase { get; set; }
    public double? AsphaltRain { get; set; }
    public double? MacadamBase { get; set; }
    public double? MacadamRain { get; set; }
    public double AsphaltMoistureFactor { get; set; } = 1;
    public double MacadamMoistureFactor { get; set; } = 2.5;
    public double MacadamRainBaseline { get; set; } = 0.4;
    public double MacadamRainMoistureFactor { get; set; } = 1;
    /// <summary>Explicit compatibility with previously authored road.weather hook graphs.</summary>
    public bool UseLegacyOverride { get; set; } = true;

    public void Validate()
    {
        foreach (var value in new[] { AsphaltBase, AsphaltRain, MacadamBase, MacadamRain,
            AsphaltMoistureFactor, MacadamMoistureFactor, MacadamRainBaseline, MacadamRainMoistureFactor })
            if (value.HasValue && (!double.IsFinite(value.Value) || value < 0 || value > 100))
                throw new InvalidDataException("Útromlás: minden együttható véges, 0 és 100 közötti szám legyen.");
    }

    public List<RuleEffect> CompileEffects()
    {
        Validate();
        static string N(double x) => x.ToString("R", CultureInfo.InvariantCulture);
        static string Rate(double? x, string tuning) => x.HasValue ? N(x.Value) : "tuning." + tuning;
        string asphalt = $"{Rate(AsphaltBase, "AsphaltWeather")} * (1 + {N(AsphaltMoistureFactor)} * tile.moisture) + {Rate(AsphaltRain, "AsphaltRainWeather")} * rainIntensity";
        string macadam = $"{Rate(MacadamBase, "MacadamWeather")} * (1 + {N(MacadamMoistureFactor)} * tile.moisture) + {Rate(MacadamRain, "MacadamRainWeather")} * rainIntensity * ({N(MacadamRainBaseline)} + {N(MacadamRainMoistureFactor)} * tile.moisture)";
        return new() { new() { Target = "condition", Mode = "add",
            Formula = $"-max(0; ha(isMacadam; {macadam}; {asphalt})) * deltaYears" } };
    }
}
