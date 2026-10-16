using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class RuleInfluenceTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static int Road(GameWorld w)
    {
        for (int id = 0; id < 255 && w.RoadCount == 0; id++) w.Map.BuildRoadTilePath(id, id + 1, RoadPaving.Asphalt);
        return w.Map.RoadTiles.First();
    }
    private static WorldRule EmptyRoad(RuleProject p)
    {
        var r = p.Rules.Single(r => r.Id == "road.weather");
        r.Effects = new() { new() { Target = "condition", Mode = "add", Influence = new() } };
        p.Algorithms = null;
        return r;
    }

    [Fact]
    public void StumpGraphAccumulatesChosenSourcePersistsAndRemovesOnlyAtChosenThreshold()
    {
        using var w = World(); Assert.True(w.DiagnosticFellForestBlock(out _));
        var scope = RuleEngine.Scope("stump"); int count = scope.Elements(w).Length;
        var project = w.RuleProjectDocument; project.Algorithms = null;
        var rule = project.Rules.Single(r => r.Id == "forest.stumps");
        Assert.All(rule.Effects, e => Assert.NotNull(e.Influence));
        var decay = rule.Effects[0]; decay.Mode = "add"; decay.Influence.PerYear = true;
        decay.Influence.Terms = new() { new() { Name = "Moisture decay", Decrease = false, Amount = .2,
            Factors = new() { new() { Source = "tile.moisture", OutputLow = .5, OutputHigh = .5 } } } };
        rule.Effects[1].Influence.Terms[0].Factors[0].InputHigh = .15;
        w.QueueRuleProject(project); w.ExecutePendingCommands();
        w.RuleBook.Run(w, 1, 0);
        Assert.Equal(count, scope.Elements(w).Length);
        var field = scope.Fields.Single(f => f.Name == "decayProgress");
        Assert.All(scope.Elements(w), id => Assert.Equal(.1, field.Read(w, id), 7));
        var preview = w.RuleBook.PreviewInfluences(w, rule.Id, scope.Elements(w)[0], 1);
        Assert.Equal(1, preview.Last().After);
        Assert.Equal(count, scope.Elements(w).Length); // preview never removes an object
        using var stream = new MemoryStream(); w.Save(stream); stream.Position = 0;
        using var restored = World(); restored.Load(stream);
        Assert.All(scope.Elements(restored), id => Assert.Equal(.1, field.Read(restored, id), 7));
        restored.RuleBook.Run(restored, 1, 0);
        Assert.Null(restored.RuleBook.LastError); Assert.Empty(scope.Elements(restored));
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("step")]
    [InlineData("smooth")]
    [InlineData("saturate")]
    [InlineData("decay")]
    [InlineData("bell")]
    [InlineData("points")]
    public void SharedCurveSurvivesProjectSaveAndMatchesRuntime(string family)
    {
        using var w = World(); int id = Road(w);
        var p = w.RuleProjectDocument; var r = EmptyRoad(p);
        var curve = new RuleCurve { Input = "condition", Points = new() { (.1, .2), (.9, .8) } };
        curve.SetFamily(family);
        if (family == "points") curve.Points[1] = (.5, .123456789);
        var factor = new InfluenceFactor(); factor.SetCurve(curve);
        r.Effects[0].Influence.Terms.Add(new() { Amount = .1, Factors = new() { factor } });
        w.QueueRuleProject(RuleProject.FromJson(p.ToJson())); w.ExecutePendingCommands();
        foreach (double input in new[] { 0.0, .1, .3, .5, .9, 1.0 }) {
            w.Map.SetRoadCondition(id, (float)input);
            double actualInput = w.Map.GetRoadCondition(id);
            var row = Assert.Single(w.RuleBook.PreviewInfluences(w, r.Id, id, .25));
            Assert.Equal(-.025 * curve.Evaluate(actualInput), Assert.Single(row.Contributions).Change, 8);
            w.RuleBook.Run(w, .25, 0);
            Assert.Null(w.RuleBook.LastError);
            Assert.Equal(row.After, w.Map.GetRoadCondition(id), 6);
        }
    }

    [Fact]
    public void CurvesKeepSmallNumbersAndRejectOverlappingPoints()
    {
        var curve = new RuleCurve { Input = "condition", Points = new() { (0, .000000123456789), (1, .8) } };
        Assert.True(RuleCurve.TryParse(curve.ToFormula(), out var parsed));
        Assert.Equal(curve.Points, parsed.Points);
        curve.Points[1] = (0, 1);
        Assert.Throws<InvalidDataException>(() => curve.Validate());
    }

    [Fact]
    public void AuthorCanRemoveRainChooseAnotherSourceReverseDirectionAndDeleteConnection()
    {
        using var w = World(); int id = Road(w);
        w.Map.SetRoadCondition(id, .5f);
        var p = w.RuleProjectDocument; var r = EmptyRoad(p);
        // No fixed road equation: the designer chooses condition as its own source and adds recovery.
        var t = new InfluenceTerm { Name = "Recovery", Decrease = false, Amount = .4,
            Factors = new() { new() { Source = "condition", OutputLow = 1, OutputHigh = 0 } } };
        r.Effects[0].Influence.Terms.Add(t);
        w.QueueRuleProject(RuleProject.FromJson(p.ToJson())); w.ExecutePendingCommands();
        var sample = Assert.Single(w.RuleBook.PreviewInfluences(w, r.Id, id, .5));
        Assert.Equal(.1, Assert.Single(sample.Contributions).Change, 7);
        Assert.Equal(.5f, w.Map.GetRoadCondition(id));
        w.RuleBook.Run(w, .5, 0);
        Assert.Equal(.6, w.Map.GetRoadCondition(id), 6);
        t.Decrease = true;
        w.QueueRuleProject(p); w.ExecutePendingCommands(); w.RuleBook.Run(w, .5, 0);
        Assert.Equal(.52, w.Map.GetRoadCondition(id), 6);
        r.Effects[0].Influence.Terms.Clear();
        w.QueueRuleProject(p); w.ExecutePendingCommands();
        float before = w.Map.GetRoadCondition(id); w.RuleBook.Run(w, 1, 0);
        Assert.Equal(before, w.Map.GetRoadCondition(id));
    }

    [Fact]
    public void ConditionsMultipliersAndThresholdCurvesAreDataAndSurviveSave()
    {
        using var w = World(); int id = Road(w);
        var p = w.RuleProjectDocument; var r = EmptyRoad(p);
        r.Effects[0].Influence.Terms.Add(new() { Name = "Dry damage", Amount = .2, Conditions = { new() { Field = "surface", Comparison = "less", Value = .5 } },
            Factors = new() { new() { Source = "condition", Shape = "step", InputHigh = .9, OutputLow = 0, OutputHigh = 2 },
                new() { Source = "tile.moisture", OutputLow = .5, OutputHigh = .5 } } });
        r.Effects[0].Influence.Terms.Add(new() { Name = "Excluded", Amount = 10, Conditions = { new() { Field = "surface", Comparison = "greater", Value = .5 } } });
        w.QueueRuleProject(p); w.ExecutePendingCommands(); w.RuleBook.Run(w, .25, 0);
        Assert.Equal(.95, w.Map.GetRoadCondition(id), 6);
        using var stream = new MemoryStream(); w.Save(stream); stream.Position = 0;
        using var restored = World(); restored.Load(stream);
        w.RuleBook.Run(w, .25, 0); restored.RuleBook.Run(restored, .25, 0);
        Assert.Equal(w.Map.GetRoadCondition(id), restored.Map.GetRoadCondition(id));
        Assert.Equal(JsonSerializer.Serialize(w.RuleBook.Rules), JsonSerializer.Serialize(restored.RuleBook.Rules));
    }

    [Fact]
    public void SameEditorModelSupportsDifferentTargetsAndReportsInvalidBindings()
    {
        using var w = World(); var p = w.RuleProjectDocument; p.Algorithms = null;
        var r = new WorldRule { Id = "new-rule", Name = "Water stress", Scope = "tree",
            Effects = new() { new() { Target = "stressYears", Mode = "add", Influence = new() { Terms = new() {
                new() { Name = "Stress", Amount = .5, Decrease = false, Factors = new() { new() { Source = "tile.waterSupply", OutputLow = 1, OutputHigh = 1 } } }
            } } } } };
        p.Rules.Add(r); w.QueueRuleProject(p); w.ExecutePendingCommands();
        var scope = RuleEngine.Scope("tree"); int id = scope.Elements(w)[0];
        var field = scope.Fields.Single(f => f.Name == "stressYears"); double before = field.Read(w, id);
        w.RuleBook.Run(w, .2, 0);
        Assert.Equal(before + .1, field.Read(w, id), 6);
        r.Effects[0].Target = "light"; // Read-only (computed) targets must never be written.
        Assert.Throws<InvalidDataException>(() => w.QueueRuleProject(p));
        r.Effects[0].Target = "health";
        r.Effects[0].Influence.Terms[0].Factors[0].Source = "missing.field";
        Assert.Throws<InvalidDataException>(() => w.QueueRuleProject(p));
        r.Effects[0].Influence.Terms[0].Factors[0].Source = "health + 1";
        Assert.Throws<InvalidDataException>(() => w.QueueRuleProject(p));
    }

}
