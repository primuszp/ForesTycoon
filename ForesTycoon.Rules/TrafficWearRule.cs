namespace ForesTycoon.Rules;

/// <summary>One editable rule for every vehicle passage, on roads and forest tracks.</summary>
public static class TrafficWearRule
{
    public static WorldRule Default() => new() {
        Id = "traffic.wear", Name = "Forgalmi út- és nyomkopás", Scope = "passage", Schedule = "passage",
        Description = "Minden áthaladáskor: a jármű tömege és a burkolat határozza meg az útkopást; a nyomon a terhelés és a meglévő vályú hat. A használt nyom pihenőideje nullázódik.",
        Effects = {
            new() { Target = "road.condition", Mode = "add", Influence = new() { PerYear = false, Terms = {
                Road("Teherautó az aszfalton", 0, .0003), Road("Teherautó a makadámon", 1, .0015) } } },
            new() { Target = "trail.wear", Mode = "add", Influence = new() { PerYear = false, Terms = {
                new() { Name = "Teherautó terhelése mélyíti a nyomot", Decrease = false, Amount = .03,
                    Conditions = { On("isTrail"), Kind(0) }, Factors = { Mass(), Remaining() } },
                new() { Name = "Erdészeti gép mélyíti a nyomot", Decrease = false, Amount = .04,
                    Conditions = { On("isTrail"), new() { Field = "vehicle.kind", Comparison = "notEqual", Value = 0 } }, Factors = { Remaining() } },
                new() { Name = "A rakomány tovább mélyíti a nyomot", Decrease = false, Amount = .06,
                    Conditions = { On("isTrail"), new() { Field = "vehicle.kind", Comparison = "notEqual", Value = 0 } },
                    Factors = { new() { Source = "vehicle.fill" }, Remaining() } }
            } } },
            new() { Target = "trail.idleYears", Mode = "set", Influence = new() { PerYear = false, Terms = { new() { Name = "Áthaladáskor újraindul a pihenőidő", Decrease = false, Amount = 0 } } } }
        }
    };
    private static InfluenceCondition On(string field) => new() { Field = field, Comparison = "equal", Value = 1 };
    private static InfluenceCondition Kind(int kind) => new() { Field = "vehicle.kind", Comparison = "equal", Value = kind };
    // A displayed curve: one reference truck is 36 tonnes; the full supported range is visible and editable.
    private static InfluenceFactor Mass() => new() { Source = "vehicle.mass", InputHigh = 1000000, OutputHigh = 1000000.0 / 36000 };
    private static InfluenceFactor Remaining() => new() { Source = "trail.wear", OutputLow = 1, OutputHigh = 0 };
    private static InfluenceTerm Road(string name, int paving, double amount) => new() {
        Name = name, Amount = amount, Conditions = { On("isRoad"), Kind(0), new() { Field = "road.surface", Comparison = "equal", Value = paving } }, Factors = { Mass() }
    };
}
