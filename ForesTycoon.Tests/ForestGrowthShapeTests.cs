using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class TreeGrowthGraphTests
{
    private static GameWorld World(int side = 16) => new(TerrainSettings.Default.WithNodeSize(side + 1, 42), forestYearSeconds: 120, enableRendering: false);
    private static string State(GameWorld w) => JsonSerializer.Serialize(w.CaptureCheckpoint());
    private static (int Tile, ForestTreeStore.Patch Patch) Plant(GameWorld w, ForestSpecies species = ForestSpecies.Oak)
    {
        w.Forest.Clear();
        int tile = Enumerable.Range(0, w.Map.Tiles.Count).First(t => w.Forest.Plant(t, species) == ForestryActionResult.Planted);
        w.Forest.IndividualTrees.TryGet(tile, out var p); return (tile, p);
    }
    private static void Grow(GameWorld w, int tile, double year = 0) => SimulationWork.Complete(w.TreeGrowthWork(tile, year));

    [Fact]
    public void GrowthIsAProjectGraphWithThreeWritableRatesAndNoNativeGrowthHook()
    {
        using var w = World(); var r = w.RuleBook.Rules.Single(r => r.Id == "forest.growth");
        Assert.Equal("growth", r.Schedule); Assert.True(r.Enabled);
        Assert.Equal(new[] { "diameterGrowth", "heightGrowth", "crownGrowth" }, r.Effects.Select(e => e.Target));
        Assert.All(r.Effects, e => { Assert.NotNull(e.Influence); Assert.False(e.Influence.PerYear); Assert.Empty(e.Formula); });
        Assert.DoesNotContain(WorldBehaviorPolicy.Hooks, h => h.Id == "forest.growth");
        var (tile, p) = Plant(w); Grow(w, tile);
        Assert.True(p.Trees[0].AnnualGrowth.Diameter > 0); Assert.True(p.Trees[0].AnnualGrowth.Height > 0);
        Assert.Equal(Math.Max(p.Trees[0].AnnualGrowth.Height * ForestSpeciesTraits.For(ForestSpecies.Oak).CrownRatio,
            p.Trees[0].AnnualGrowth.Diameter * ForestSpeciesTraits.For(ForestSpecies.Oak).CrownSpread), p.Trees[0].AnnualGrowth.CrownRadius, 6);
    }

    [Theory]
    [InlineData("water")]
    [InlineData("light")]
    [InlineData("space")]
    [InlineData("health")]
    public void EachVisibleResourceConnectionControlsActualGrowth(string source)
    {
        using var w = World(); var (tile, p) = Plant(w);
        var tree = p.Trees[0] with { Health = 1, Resources = new(1, 1, 1), AnnualGrowth = default };
        p.Trees[0] = tree; Grow(w, tile); var healthy = p.Trees[0].AnnualGrowth;
        p.Trees[0] = source switch {
            "health" => tree with { Health = 0 },
            "water" => tree with { Resources = new(1,0,1) },
            "space" => tree with { Resources = new(1,1,0) },
            _ => tree with { Resources = new(0,1,1) }
        };
        Grow(w, tile); Assert.Equal(default, p.Trees[0].AnnualGrowth);
        Assert.True(healthy.Diameter > 0 && healthy.Height > 0);
    }

    [Fact]
    public void HeightEnvelopeStopsHeightButAMatureTreeCanKeepThickening()
    {
        using var w = World(); var (tile, p) = Plant(w); var traits = ForestSpeciesTraits.For(ForestSpecies.Oak);
        p.Trees[0] = p.Trees[0] with { Dimensions = new(.8f, traits.MaxHeight, 4), Health = 1, Resources = new(1,1,1), AnnualGrowth = default };
        Grow(w, tile); Assert.Equal(0, p.Trees[0].AnnualGrowth.Height); Assert.True(p.Trees[0].AnnualGrowth.Diameter > 0);
    }

    [Fact]
    public void SeasonCurveIsEditableAndUsesTheSameCurveImplementation()
    {
        using var w = World(); var (tile, p) = Plant(w);
        var project = w.RuleProjectDocument; var rule = project.Rules.Single(r => r.Id == "forest.growth");
        var season = rule.Effects[0].Influence.Terms[0].Factors.Single(f => f.Source == "yearFraction");
        season.Shape = "linear"; season.Breakpoints = null; season.OutputLow = season.OutputHigh = 0;
        w.QueueRuleProject(project); w.ExecutePendingCommands(); Grow(w, tile);
        Assert.Equal(0, p.Trees[0].AnnualGrowth.Diameter); Assert.True(p.Trees[0].AnnualGrowth.Height > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisablingOrDeletingGrowthStopsExistingAndNewTreesWithoutFallback(bool delete)
    {
        using var w = World(); var (tile, p) = Plant(w); Grow(w, tile); Assert.True(p.Trees[0].AnnualGrowth.Diameter > 0);
        var project = w.RuleProjectDocument; var rule = project.Rules.Single(r => r.Id == "forest.growth");
        if (delete) { project.Rules.Remove(rule); project.RemovedStarters.Add(rule.Id); } else rule.Enabled = false;
        w.QueueRuleProject(project); w.ExecutePendingCommands(); w.Update(1d / 30);
        Assert.All(p.Trees.Take(p.Count), t => Assert.Equal(default, t.AnnualGrowth));
        w.Forest.Update(20); Assert.All(p.Trees.Take(p.Count), t => Assert.Equal(default, t.AnnualGrowth));
        var (nextTile, next) = Plant(w); Assert.All(next.Trees.Take(next.Count), t => Assert.Equal(default, t.AnnualGrowth));
    }

    [Fact]
    public void EditedRatesIntegrateContinuouslyAndPendingRefreshSurvivesSaveAndReplay()
    {
        using var w = World();
        var project = w.RuleProjectDocument; var r = project.Rules.Single(r => r.Id == "forest.growth");
        foreach (var e in r.Effects) { e.Influence.Terms.Clear(); e.Influence.Terms.Add(new() { Amount = .12, Decrease = false }); }
        w.QueueRuleProject(project); w.ExecutePendingCommands();
        using var saved = new MemoryStream(); w.Save(saved); saved.Position = 0;
        using var restored = World(); restored.Load(saved); Assert.Equal(State(w), State(restored));
        for (int i = 0; i < 305; i++) {
            w.Update(1d / 30); int slices = 0; while (!restored.ContinueUpdate(1d / 30, 3)) Assert.True(++slices < 300000);
        }
        Assert.Equal(State(w), State(restored));
        Assert.All(w.Forest.IndividualTrees.Patches.SelectMany(e => e.Value.Trees.Take(e.Value.Count)), t => Assert.Equal(.12f, t.AnnualGrowth.Diameter, 6));
        saved.Position = 0; var data = WorldSaveSerializer.Read(saved);
        using var journal = new MemoryStream(); WorldSaveSerializer.Write(journal, new WorldSaveData { Terrain = data.Terrain, SoilModel = data.SoilModel,
            Climate = data.Climate, Tick = data.Tick, TickRate = data.TickRate, ForestYearSeconds = data.ForestYearSeconds, Commands = data.Commands });
        journal.Position = 0; using var replay = World(); replay.Load(journal);
        saved.Position = 0; restored.Load(saved); Assert.Equal(State(restored), State(replay));
    }

    [Fact]
    public void GrowthScheduleCannotPerformTopologyActionsOrWriteOtherState()
    {
        var rule = TreeGrowthRule.Default(); rule.Effects[0].Target = "health"; Assert.Throws<InvalidDataException>(rule.Validate);
        rule = TreeGrowthRule.Default(); rule.Effects[0].Target = "die"; rule.Effects[0].Mode = "action"; Assert.Throws<InvalidDataException>(rule.Validate);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("inverse")]
    public void NewCurveFamiliesMatchTheCompiledRuntime(string family)
    {
        using var w = World(); var (tile, p) = Plant(w); var project = w.RuleProjectDocument;
        var r = project.Rules.Single(r => r.Id == "forest.growth");
        var factor = new InfluenceFactor { Source = "health", Shape = family, InputHigh = 1, OutputLow = family == "inverse" ? 1 : 0, OutputHigh = family == "inverse" ? 0 : 1 };
        r.Effects = new() { new() { Target = "diameterGrowth", Mode = "set", Influence = new() { PerYear = false, Terms = { new() { Amount = 1, Decrease = false, Factors = { factor } } } } } };
        w.QueueRuleProject(project); w.ExecutePendingCommands();
        foreach (float value in new[] { 0f, .1f, .5f, 1f }) {
            p.Trees[0] = p.Trees[0] with { Health = value }; Grow(w, tile);
            Assert.Equal(factor.ToCurve().Evaluate(value), p.Trees[0].AnnualGrowth.Diameter, 6);
        }
    }

    [Fact]
    public void AStoredCalculationGraphExecutesWithoutFormulaAndRetainsFullPrecision()
    {
        using var w = World(); var project = w.RuleProjectDocument;
        var graph = new BehaviorGraph { Id = "calculation", Hook = "unused", Kind = "unused", Output = "value", Nodes = new() {
            new() { Id = "value", Name = "Pontos érték", Operation = BehaviorOperation.Constant, Value = .12345678901234567 }
        } };
        project.Rules.Add(new() { Id = "calculation", Scope = "world", Effects = { new() { Target = "saját.result", Mode = "set", Computation = graph } },
            ConditionGraph = new() { Id = "gate", Output = "yes", Nodes = new() { new() { Id = "yes", Name = "Igaz", Operation = BehaviorOperation.Constant, Value = 1 } } } });
        project.Fields.Add(new() { Name = "saját.result", Scope = "world" });
        w.QueueRuleProject(project); w.ExecutePendingCommands(); for (int i = 0; i < 20; i++) w.Update(1d / 30);
        Assert.Equal(graph.Nodes[0].Value, w.RuleBook.ReadCustom(w, "saját.result", 0));
        Assert.Null(w.RuleBook.LastError);
        var again = RuleProject.FromJson(w.RuleProjectDocument.ToJson()); Assert.Equal(graph.Nodes[0].Value, again.Rules.Single(r => r.Id == "calculation").Effects[0].Computation.Nodes[0].Value);
    }
    [Fact]
    public void TreeCustomStateKeepsItsIdentityAcrossGrowthRefreshDeathAndSnapshot()
    {
        using var w = World(); var (tile, patch) = Plant(w);
        var project = w.RuleProjectDocument;
        project.Fields.Add(new() { Name = "saját.memory", Scope = "tree", Initial = 3 });
        project.Rules.Add(new() { Id = "remember-id", Scope = "tree", Effects = { new() { Target = "saját.memory", Mode = "set", Formula = "treeId" } } });
        w.QueueRuleProject(project); w.ExecutePendingCommands(); for (int tick = 0; tick < 16; tick++) w.Update(1d / 30);
        for (int i = 0; i < patch.Count; i++) patch.Trees[i] = patch.Trees[i] with { Health = .8f, StressYears = 0 };
        patch.Trees[0] = patch.Trees[0] with { Health = .1f, StressYears = 5 };
        SimulationWork.Complete(w.TreeMonthWork(1d / 12, w.Forest.ForestYear));
        Assert.Equal(ForestTreeStore.PlantedTreesPerTile - 1, patch.Count);
        SimulationWork.Complete(w.Forest.RefreshEnvironmentRatesWork());
        var scope = RuleEngine.Scope("tree"); var identity = scope.Fields.Single(f => f.Name == "treeId");
        foreach (int id in scope.Elements(w)) Assert.Equal(identity.Read(w, id), w.RuleBook.ReadCustom(w, "saját.memory", id));
        using var saved = new MemoryStream(); w.Save(saved); saved.Position = 0; using var restored = World(); restored.Load(saved);
        Assert.Equal(State(w), State(restored));
        foreach (int id in scope.Elements(restored)) Assert.Equal(identity.Read(restored, id), restored.RuleBook.ReadCustom(restored, "saját.memory", id));
    }

    [Fact]
    public void PlantingCommandResumesItsOwnGrowthGraphAndDefersSaveToItsBoundary()
    {
        using var w = World(); var (tile, _) = Plant(w); w.Forest.Clear();
        w.QueuePlantForest(tile, ForestSpecies.Oak);
        int slices = 0;
        while (!w.RuleBook.StepPending) { w.ContinueCommands(1); Assert.True(++slices < 100000); }
        using var saved = new MemoryStream(); bool captured = false;
        w.AtTickBoundary(() => { w.Save(saved); captured = true; }); Assert.False(captured);
        while (w.CommandPending) { w.ContinueCommands(3); Assert.True(++slices < 100000); }
        Assert.True(captured); Assert.False(w.RuleBook.StepPending);
        Assert.Equal(ForestTreeStore.PlantedTreesPerTile, w.Forest.IndividualTreeCount);
        Assert.True(w.Forest.IndividualTrees.TryGet(tile, out var patch)); Assert.True(patch.Trees[0].AnnualGrowth.Diameter > 0);
        saved.Position = 0; using var restored = World(); restored.Load(saved); Assert.Equal(State(w), State(restored));
    }

}
