using System;
using System.Collections.Generic;

namespace ForesTycoon.Rules;

/// <summary>Project-owned growth rates. Ecology only integrates the resulting metres per forest year.</summary>
public static class TreeGrowthRule
{
    public static WorldRule Default() => new() {
        Id = "forest.growth", Name = "Fák növekedése", Scope = "tree", Schedule = "growth",
        Description = "A termőhely, fény, víz, tér, egészség és évszak külön kapcsolatokon szabályozza a vastagodást és a magassági növekedést. A korona a hajtás és a vastagodás közül az erősebb hatást követi.",
        Effects = {
            new() { Target = "diameterGrowth", Mode = "set", Influence = new() { PerYear = false, Terms = {
                Growth("Vastagodás az erőforrások és az évszak szerint", "radialRate", 2,
                    new() { Source = "diameter", Shape = "inverse", InputHigh = 1.25, OutputLow = 1, OutputHigh = 0 })
            } } },
            new() { Target = "heightGrowth", Mode = "set", Influence = new() { PerYear = false, Terms = {
                Growth("Magassági növekedés a faj magassági határáig", "heightRate", 1,
                    new() { Source = "relativeHeight", OutputLow = 1, OutputHigh = 0 })
            } } },
            new() { Target = "crownGrowth", Mode = "set", Influence = new() { PerYear = false, Combine = "maximum", Terms = {
                new() { Name = "A hajtás növeli a koronát", AmountField = "heightGrowth", Decrease = false,
                    Factors = { new() { Source = "crownRatio" } } },
                new() { Name = "A vastagodás szélesíti a koronát", AmountField = "diameterGrowth", Decrease = false,
                    Factors = { new() { Source = "crownSpread", InputHigh = 20, OutputHigh = 20 } } }
            } } }
        }
    };
    private static InfluenceTerm Growth(string name, string amount, double scale, InfluenceFactor size)
    {
        var term = new InfluenceTerm { Name = name, AmountField = amount, AmountScale = scale, Decrease = false };
        foreach (string field in new[] { "fitness", "lightResponse", "water", "health" }) term.Factors.Add(new() { Source = field });
        term.Factors.Add(new() { Source = "space", Shape = "root" });
        var season = new InfluenceFactor { Source = "yearFraction", Shape = "points", Breakpoints = new() };
        for (int month = 0; month <= 8; month++) {
            season.Breakpoints.Add(month / 8d);
            season.Breakpoints.Add(Math.Clamp(.65 + .8 * Math.Cos(Math.Tau * (month / 8d - .25)), 0, 1.45));
        }
        term.Factors.Add(season); term.Factors.Add(size); return term;
    }
}
