using System.Collections.Generic;

namespace ForesTycoon.Rules;

/// <summary>Editable monthly health, stress, death and deadwood lifecycle.</summary>
public static class TreeVitalityRules
{
    public static IEnumerable<WorldRule> Defaults()
    {
        yield return new() { Id = "forest.health", Name = "Fák egészsége", Scope = "tree", Schedule = "month",
            Description = "Az egészség a termőhely és az elérhető erőforrások adta célhoz közelít. A kapcsolatok, a görbék és a közelítés sebessége szerkeszthető.",
            Effects = { new() { Target = "health", Mode = "approach", ApproachPerYear = .42, Influence = new() { PerYear = false, Terms = {
                new() { Name = "Termőhely adta alapegészség", Amount = .15, Decrease = false, Factors = { Source("fitness") } },
                new() { Name = "Az elérhető erőforrások javítják az egészséget", Amount = .85, Decrease = false,
                    Factors = { Source("fitness"), Source("resourceLimit") } }
            } } } } };
        yield return new() { Id = "forest.stress", Name = "Fák tartós stressze", Scope = "tree", Schedule = "month",
            Description = "Erőforráshiány vagy rossz egészség növeli a stresszéveket; megfelelő körülmények között a fa lassan regenerálódik. Az árnyéktűrés a küszöb görbéjét módosítja.",
            Effects = { new() { Target = "stressYears", Mode = "add", Influence = new() { Terms = {
                new() { Name = "Erőforráshiány növeli a stresszt", Decrease = false, Amount = 1,
                    Conditions = { Limit("less"), Health("atLeast", .18) } },
                new() { Name = "Rossz egészség növeli a stresszt", Decrease = false, Amount = 1,
                    Conditions = { Health("less", .18) } },
                new() { Name = "Jó körülmények között oldódik a stressz", Decrease = true, Amount = .5,
                    Conditions = { Limit("atLeast"), Health("atLeast", .18) } }
            } } } } };
        yield return new() { Id = "forest.mortality", Name = "Fák elhalása", Scope = "tree", Schedule = "month",
            Description = "A tartósan stresszes, gyenge fa elhal; a legnagyobb korát meghaladó fa is elhal. Az elhalt fa holtfaként marad a világban, nem lesz belőle kitermelt faanyag vagy tönk.",
            Effects = { new() { Target = "die", Mode = "action", Influence = new() { PerYear = false, Terms = {
                new() { Name = "Tartós stressz és gyenge egészség elhalást okoz", Amount = 1, Decrease = false,
                    Conditions = { new() { Field = "stressYears", Comparison = "atLeast", Value = 4 },
                        new() { Field = "health", Comparison = "less", ValueCurve = new() { Source = "shadeTolerance", OutputLow = .65, OutputHigh = .4 } } } },
                new() { Name = "A faj legnagyobb korának túllépése elhalást okoz", Amount = 1, Decrease = false,
                    Conditions = { new() { Field = "age", Comparison = "greater", ValueField = "maximumAge" } } }
            } } } } };
        yield return new() { Id = "forest.deadwood", Name = "Elhalt fák eltűnése", Scope = "deadTree", Schedule = "month",
            Description = "A holtfa a választott idő után eltűnik. A határérték, a további feltételek és a ható tényezők a gráfon változtathatók.",
            Effects = { new() { Target = "remove", Mode = "action", Influence = new() { PerYear = false, Terms = {
                new() { Name = "Elég ideje elhalt fa eltűnik", Amount = 1, Decrease = false,
                    Conditions = { new() { Field = "deadYears", Comparison = "atLeast", Value = 8 } } }
            } } } } };
    }
    private static InfluenceFactor Source(string field) => new() { Source = field };
    private static InfluenceCondition Health(string comparison, double value) => new() { Field = "health", Comparison = comparison, Value = value };
    private static InfluenceCondition Limit(string comparison) => new() { Field = "resourceLimit", Comparison = comparison,
        ValueCurve = new() { Source = "shadeTolerance", OutputLow = .5, OutputHigh = .25 } };
}
