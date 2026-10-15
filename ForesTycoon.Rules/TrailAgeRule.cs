using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ForesTycoon.Rules;

/// <summary>Editable regeneration and overgrowth. Terrain code only provides state and machine-pass events.</summary>
public static class TrailAgeRule
{
    public static WorldRule Default() => new() {
        Id = "trail.age", BuiltIn = true, Name = "Nyom regenerációja és benövése", Scope = "trail",
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

    public static void Upgrade(WorldRule rule)
    {
        if (rule.Scope != "trail" || rule.Effects == null || rule.Effects.Any(e => e == null)) return;
        rule.RenameFields(new Dictionary<string, string> { ["nyom.kopás"] = "wear", ["nyom.tétlenség"] = "idleYears", ["nyom.megszüntetése"] = "remove" });
        string Compact(string text) => Regex.Replace(text ?? "", @"\s+", "");
        if (rule.Condition != "" || rule.Effects.Count != 3 || rule.Effects.Any(e => e.Influence != null)) return;
        var effects = rule.Effects;
        if (effects[0].Target == "wear" && effects[0].Mode == "set" && Compact(effects[0].Formula) == "max(0;wear-0,4*lépés)"
            && effects[1].Target == "idleYears" && effects[1].Mode == "add" && Compact(effects[1].Formula) == "lépés"
            && effects[2].Target == "remove" && effects[2].Mode == "action"
            && Compact(effects[2].Formula) == "ha(idleYears<hangolás.SkidTrailOvergrowYears;0;ha(wear>0,001;0;1))")
            rule.Effects = Default().Effects;
    }
}
