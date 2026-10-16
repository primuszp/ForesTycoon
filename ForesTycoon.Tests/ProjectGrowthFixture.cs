namespace ForesTycoon.Tests;

/// <summary>Hosts the real project growth graph for small ecology-only habitats; contains no growth formula.</summary>
internal static class ProjectGrowthFixture
{
    internal static ForestSystem Attach(ForestSystem forest)
    {
        string[] fields = { "fitness", "lightResponse", "water", "health", "space", "yearFraction", "diameter", "relativeHeight", "radialRate", "heightRate", "heightGrowth", "diameterGrowth", "crownRatio", "crownSpread" };
        var source = TreeGrowthRule.Default();
        var hooks = source.Effects.Select((e, i) => new BehaviorHook("test.growth." + i, e.Target, "tree", "m/year", 0, 100, "", "", "rate refresh", false, fields)).ToArray();
        var names = fields.Select(f => new FormulaName(f, BehaviorOperation.Input, f)).ToArray();
        var graphs = source.EffectiveEffects().Select((e, i) => RuleFormula.Compile(e.Formula,
            new BehaviorGraph { Id = hooks[i].Id, Hook = hooks[i].Id, Kind = "tree", Name = e.Target }, names)).ToList();
        var program = new CompiledBehaviorModel(new() { Graphs = graphs }, hooks);
        IEnumerable<int> Grow(int tile, double year) {
            forest.IndividualTrees.TryGet(tile, out var p);
            for (int i = 0; i < p.Count; i++) {
                var t = p.Trees[i]; var traits = ForestSpeciesTraits.For(t.Species);
                var values = new double[] { forest.SiteFitness(t.Species, tile), t.Resources.LightResponse(t.Species), t.Resources.Water,
                    t.Health, t.Resources.Space, year - Math.Floor(year), t.Dimensions.Diameter, t.Dimensions.Height / traits.MaxHeight,
                    traits.RadialRate, traits.HeightRate, 0, 0, traits.CrownRatio, traits.CrownSpread };
                float diameter = (float)program.Evaluate(hooks[0].Id, "tree", t.Id, new(), values); values[11] = diameter;
                float height = (float)program.Evaluate(hooks[1].Id, "tree", t.Id, new(), values); values[10] = height;
                float crown = (float)program.Evaluate(hooks[2].Id, "tree", t.Id, new(), values);
                p.Trees[i] = t with { AnnualGrowth = new(diameter, height, crown) }; yield return 1;
            }
        }
        forest.GrowthRules = Grow; SimulationWork.Complete(forest.RefreshEnvironmentRatesWork()); return forest;
    }
}
