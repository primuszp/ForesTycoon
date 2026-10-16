namespace ForesTycoon.Rules;

/// <summary>Editable regeneration and overgrowth. Terrain code only provides state and machine-pass events.</summary>
public static class TrailAgeRule
{
    public const double DefaultOvergrowYears = 3;
    public static WorldRule Default() => new() {
        Id = "trail.age", Name = "Nyom regenerációja és benövése", Scope = "trail",
        Description = "A nyomvályúk erdőévenként 0,4-del simulnak. A használaton kívüli, sima nyom a benövési idő után eltűnik.",
        Effects = {
            new() { Target = "wear", Mode = "add", Influence = new() { Terms = {
                new() { Name = "Vályú regenerációja", Decrease = true, Amount = .4 }
            } } },
            new() { Target = "idleYears", Mode = "add", Influence = new() { Terms = {
                new() { Name = "Használat nélküli idő", Decrease = false, Amount = 1 }
            } } },
            new() { Target = "remove", Mode = "action", Influence = new() { PerYear = false, Terms = {
                new() { Name = "Benövés és eltűnés", Decrease = false, Amount = 1, Conditions = {
                    new() { Field = "idleYears", Comparison = "atLeast", ValueField = "tuning.SkidTrailOvergrowYears" },
                    new() { Field = "wear", Comparison = "atMost", Value = .001 }
                } }
            } } }
        }
    };

}
