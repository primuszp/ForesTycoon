namespace ForesTycoon.Rules;

/// <summary>The shipped stump rule, also used by the editor's reset and trial. Its behavior is ordinary editable data.</summary>
public static class StumpDecayRule
{
    public static WorldRule Default() => new() {
        Id = "forest.stumps", Name = "Tönkök lebomlása", Scope = "stump", BuiltIn = true,
        Description = "A nagyobb nedvesség gyorsítja, a nagyobb tönkátmérő lassítja a felhalmozódó lebomlást. Teljes lebomláskor a tönk eltűnik.",
        Effects = {
            new() { Target = "decayProgress", Mode = "add", Influence = new() { PerYear = true, Terms = {
                new() { Name = "Éves lebomlás", Decrease = false, Amount = 1d / 6,
                    Factors = {
                        new() { Source = "tile.moisture", InputLow = 0, InputHigh = 1, OutputLow = .5, OutputHigh = 1.5 },
                        new() { Source = "diameter", InputLow = .15, InputHigh = .60, OutputLow = 1.5, OutputHigh = .5 }
                    } }
            } } },
            new() { Target = "remove", Mode = "action", Influence = new() { PerYear = false, Terms = {
                new() { Name = "Eltűnés 100% lebomláskor", Decrease = false, Amount = 1,
                    Factors = { new() { Source = "decayProgress", Shape = "step", InputHigh = 1 } } }
            } } }
        }
    };
}
