using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

/// <summary>Visual rules read aloud in Hungarian for designers, derived from the saved influence data.</summary>
public sealed class RuleSentenceTests
{
    private static readonly RuleSentence Road = new("út", field => field switch {
        "condition" => new("Állapot", "0–1"),
        "tile.moisture" => new("Nedvesség", "0–1"),
        "rainIntensity" => new("Eső erőssége", "0–1"),
        "surface" => new("Burkolat") { Options = new[] { (0.0, "aszfalt"), (1.0, "makadám") } },
        "tuning.AsphaltWeather" => new("Aszfalt romlása szárazon", "/év", Current: .05),
        _ => null });

    [Fact]
    public void AConnectionReadsAsOneSentenceWithStrengthMannerAndCondition()
    {
        var effect = new RuleEffect { Target = "condition", Mode = "add", Influence = new() { Terms = {
            new() { Name = "Aszfalt: nedvesség", Amount = .05, Factors = { new() { Source = "tile.moisture" } },
                Conditions = { new() { Field = "surface", Comparison = "less", Value = .5 } } } } } };
        Assert.Equal("Minden út esetén az állapot évente 0,05 értékkel csökken, a nedvesség arányában, ha a burkolat aszfalt.",
            Road.Term(effect, effect.Influence.Terms[0]));
    }

    [Fact]
    public void ParametersScaledFactorsAndTheWholeRuleAreNamed()
    {
        var effect = new RuleEffect { Target = "condition", Mode = "add", Influence = new() { Terms = {
            new() { Name = "a", AmountField = "tuning.AsphaltWeather", Factors = { new() { Source = "tile.moisture", OutputLow = 1, OutputHigh = 2 } } },
            new() { Name = "b", Decrease = false, Amount = .1, Enabled = false, Factors = { new() { Source = "rainIntensity" } } } } } };
        var rule = new WorldRule { Scope = "road", Effects = { effect } };
        var lines = Road.Rule(rule);
        Assert.Contains("„Aszfalt romlása szárazon” (most 0,05 /év)", lines[0]);
        Assert.Contains("minél nagyobb a nedvesség, annál erősebben (0: 1×, 1: 2×)", lines[0]);
        Assert.StartsWith("(Kikapcsolva) Minden út esetén az állapot évente 0,1 értékkel nő", lines[1]);
        Assert.Equal(2, lines.Count); // only one enabled connection: no sum line
    }

    [Fact]
    public void ActionsAndThresholdsFromParametersRead()
    {
        var trail = new RuleSentence("nyom", f => f switch {
            "idleYears" => new("Állásidő", "erdőév"), "wear" => new("Vályúmélység", "0–1"), "remove" => new("Eltüntetés"),
            "tuning.SkidTrailOvergrowYears" => new("Nyom benövési ideje", "erdőév", Current: 5), _ => null });
        var effect = new RuleEffect { Target = "remove", Mode = "action", Influence = new() { PerYear = false, Terms = {
            new() { Decrease = false, Amount = 1, Conditions = {
                new() { Field = "idleYears", Comparison = "atLeast", ValueField = "tuning.SkidTrailOvergrowYears" },
                new() { Field = "wear", Comparison = "atMost", Value = .001 } } } } } };
        Assert.Equal("Minden nyom esetén megtörténik az eltüntetés, ha az állásidő legalább „Nyom benövési ideje” (most 5 erdőév) és a vályúmélység legfeljebb 0,001.",
            trail.Term(effect, effect.Influence.Terms[0]));
    }

    [Theory]
    [InlineData("equal", 1, "a burkolat makadám")]
    [InlineData("notEqual", 1, "a burkolat aszfalt")]
    [InlineData("equal", 0, "a burkolat aszfalt")]
    public void OptionConditionsUseTheOptionNames(string comparison, double value, string expected) =>
        Assert.Equal(expected, Road.Condition(new() { Field = "surface", Comparison = comparison, Value = value }));
}
