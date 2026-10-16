using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class TreeVitalityRuleTests
{
    private static GameWorld World(int side = 16) => new(TerrainSettings.Default.WithNodeSize(side + 1, 42), forestYearSeconds: 120, enableRendering: false);
    private static string State(GameWorld w) => JsonSerializer.Serialize(w.CaptureCheckpoint());
    private static ForestTreeStore.Patch Plantation(GameWorld w)
    {
        w.Forest.Clear();
        int tile = Enumerable.Range(0, w.Map.Tiles.Count).First(t => w.Forest.Plant(t, ForestSpecies.Birch) == ForestryActionResult.Planted);
        w.Forest.IndividualTrees.TryGet(tile, out var p); return p;
    }
    private static void Month(GameWorld w, double year = 0) => SimulationWork.Complete(w.TreeMonthWork(1d / 12, year));
    private static void Only(GameWorld w, params string[] rules)
    {
        var p = w.RuleProjectDocument;
        foreach (var r in p.Rules) r.Enabled = rules.Contains(r.Id);
        w.QueueRuleProject(p); w.ExecutePendingCommands();
    }

    [Fact]
    public void EveryVitalityDecisionIsAnEnabledMonthlyVisualGraphWithNoNativeHook()
    {
        using var w = World();
        foreach (var r in TreeVitalityRules.Defaults()) {
            var saved = w.RuleBook.Rules.Single(x => x.Id == r.Id);
            Assert.True(saved.Enabled); Assert.Equal("month", saved.Schedule);
            Assert.All(saved.Effects, e => { Assert.NotNull(e.Influence); Assert.Empty(e.Formula); });
        }
        Assert.DoesNotContain(WorldBehaviorPolicy.Hooks, h => h.Id == "forest.health");
        Assert.Contains(RuleEngine.Scope("tree").Actions, a => a.Name == "die");
        Assert.Contains(RuleEngine.Scope("deadTree").Actions, a => a.Name == "remove");
    }

    [Fact]
    public void HealthMovesTowardsTheGraphTargetAtTheEditedSpeedWithoutOvershoot()
    {
        using var w = World(); var p = Plantation(w); Only(w, "forest.health");
        var project = w.RuleProjectDocument; var e = project.Rules.Single(r => r.Id == "forest.health").Effects[0];
        e.ApproachPerYear = .24; e.Influence.Terms.Clear(); e.Influence.Terms.Add(new() { Amount = .7, Decrease = false });
        w.QueueRuleProject(project); w.ExecutePendingCommands();
        p.Trees[0] = p.Trees[0] with { Health = .3f }; p.Trees[1] = p.Trees[1] with { Health = .71f };
        Month(w); Assert.Equal(.32f, p.Trees[0].Health, 6); Assert.Equal(.7f, p.Trees[1].Health, 6);
    }

    [Theory]
    [InlineData(.1, 1, 1.08333333)]
    [InlineData(.1, 0, 1.08333333)]
    [InlineData(.8, 0, 1.08333333)]
    [InlineData(.8, 1, .95833333)]
    public void StressUsesExclusiveCausesAndRecoversInGoodConditions(double health, double resource, double expected)
    {
        using var w = World(); var p = Plantation(w); Only(w, "forest.stress");
        p.Trees[0] = p.Trees[0] with { Health = (float)health, StressYears = 1, Resources = new((float)resource, (float)resource, (float)resource) };
        Month(w); Assert.Equal(expected, p.Trees[0].StressYears, 6);
    }

    [Fact]
    public void DeathRemovesAllMatchingTreesWithoutSkippingAndPreservesIdentityWithoutTimber()
    {
        using var w = World(); var p = Plantation(w); Only(w, "forest.mortality", "forest.deadwood");
        var ids = p.Trees.Take(p.Count).Select(t => t.Id).Order().ToArray();
        for (int i = 0; i < p.Count; i++) p.Trees[i] = p.Trees[i] with { Health = .1f, StressYears = 4, AnnualGrowth = default };
        Month(w, 1); Assert.Equal(0, p.Count); Assert.Equal(ids, p.DeadTrees!.Select(d => d.Tree.Id).Order());
        Assert.Equal(0, p.Depot); Assert.Null(p.Stumps);
        Assert.All(p.DeadTrees, d => { Assert.Equal(0, d.Tree.Health); Assert.Equal(default, d.Tree.AnnualGrowth); });
        Month(w, 8.99); Assert.Equal(ids.Length, p.DeadTrees.Count);
        Month(w, 9); Assert.Empty(p.DeadTrees);
    }

    [Fact]
    public void AgeAndShadeDependentThresholdsAreReadFromDiscoveredProperties()
    {
        using var w = World(); var p = Plantation(w); Only(w, "forest.mortality");
        p.Trees[0] = p.Trees[0] with { BirthYear = -1000, Health = 1, StressYears = 0 };
        p.Trees[1] = p.Trees[1] with { BirthYear = 0, Health = .9f, StressYears = 4 };
        ulong old = p.Trees[0].Id, healthy = p.Trees[1].Id; Month(w);
        Assert.Contains(p.DeadTrees!, d => d.Tree.Id == old);
        Assert.Contains(p.Trees.Take(p.Count), t => t.Id == healthy);
    }

    [Fact]
    public void DisabledRulesHaveNoNativeFallbackAtRealForestMonthBoundaries()
    {
        using var w = World(); var p = Plantation(w); Only(w);
        for (int i = 0; i < p.Count; i++) p.Trees[i] = p.Trees[i] with { Health = .1f, StressYears = 5, BirthYear = -1000 };
        w.Forest.Update(120); Assert.Equal(ForestTreeStore.PlantedTreesPerTile, p.Count);
        Assert.All(p.Trees.Take(p.Count), t => { Assert.Equal(.1f, t.Health); Assert.Equal(5, t.StressYears); });
    }

    [Fact]
    public void RealMonthlyHostRunsHealthStressAndMortalityGraphs()
    {
        using var w = World(); var p = Plantation(w);
        for (int i = 0; i < p.Count; i++) p.Trees[i] = p.Trees[i] with { Health = .1f, StressYears = 5 };
        w.Forest.Update(10); Assert.Equal(0, p.Count); Assert.NotEmpty(p.DeadTrees!);
        Assert.Null(w.RuleBook.LastError);
    }

    [Fact]
    public void PartialMonthDefersSaveAndSnapshotAndJournalReplayRemainExact()
    {
        using var w = World();
        var project = w.RuleProjectDocument;
        var e = project.Rules.Single(r => r.Id == "forest.mortality").Effects[0];
        e.Influence.Terms.Clear(); e.Influence.Terms.Add(new() { Amount = 1, Decrease = false });
        w.QueueRuleProject(project); w.ExecutePendingCommands();
        for (int i = 0; i < 299; i++) w.Update(1d / 30);
        Assert.False(w.ContinueUpdate(1d / 30, 1));
        using var saved = new MemoryStream(); bool captured = false;
        w.AtTickBoundary(() => { w.Save(saved); captured = true; });
        Assert.False(captured); Assert.Throws<InvalidOperationException>(() => w.CaptureCheckpoint());
        int slices = 0; while (!w.ContinueUpdate(1d / 30, 7)) Assert.True(++slices < 200000);
        Assert.True(captured); Assert.True(slices > 100); Assert.Equal(0, w.Forest.IndividualTreeCount);
        saved.Position = 0; using var restored = World(); restored.Load(saved); Assert.Equal(State(w), State(restored));
        saved.Position = 0; var data = WorldSaveSerializer.Read(saved);
        using var journal = new MemoryStream(); WorldSaveSerializer.Write(journal, new WorldSaveData {
            Terrain = data.Terrain, SoilModel = data.SoilModel, Climate = data.Climate, Tick = data.Tick,
            TickRate = data.TickRate, ForestYearSeconds = data.ForestYearSeconds, Commands = data.Commands });
        journal.Position = 0; using var replay = World(); replay.Load(journal); Assert.Equal(State(w), State(replay));
        for (int i = 0; i < 35; i++) {
            w.Update(1d / 30); while (!restored.ContinueUpdate(1d / 30, 3)) { }
        }
        Assert.Equal(State(w), State(restored));
    }
    [Fact]
    public void MonthlyGraphsKeepTheExactResultWhenSeveralMonthsArriveTogether()
    {
        using var a = World(); using var b = World();
        a.Forest.Update(120); for (int i = 0; i < 12; i++) b.Forest.Update(10);
        Assert.Equal(State(a), State(b));
        Assert.Equal(a.Forest.ForestYear, a.RuleEvaluationYear);
    }

    [Fact]
    public void LoadingSnapshotCancelsThePartialMonthlyGraphAndItsTimeOverride()
    {
        using var w = World(); using var saved = new MemoryStream(); w.Save(saved); string initial = State(w);
        for (int i = 0; i < 299; i++) w.Update(1d / 30);
        int slices = 0;
        while (!w.RuleBook.StepPending) { Assert.False(w.ContinueUpdate(1d / 30, 1)); Assert.True(++slices < 200000); }
        saved.Position = 0; w.Load(saved);
        Assert.False(w.RuleBook.StepPending); Assert.False(w.UpdatePending);
        Assert.Equal(initial, State(w)); Assert.Equal(w.Forest.ForestYear, w.RuleEvaluationYear);
        for (int i = 0; i < 301; i++) w.Update(1d / 30);
        Assert.Null(w.RuleBook.LastError);
    }

    [Fact]
    public void ConditionThresholdUsesTheEditedCurveAndApproachPreviewMatchesRuntime()
    {
        using var w = World(); var p = Plantation(w); Only(w, "forest.health", "forest.stress");
        var project = w.RuleProjectDocument;
        var stress = project.Rules.Single(r => r.Id == "forest.stress");
        stress.Effects[0].Influence.Terms.RemoveRange(1, 2);
        stress.Effects[0].Influence.Terms[0].Conditions[0].ValueCurve.OutputLow = .9;
        stress.Effects[0].Influence.Terms[0].Conditions[0].ValueCurve.OutputHigh = .9;
        w.QueueRuleProject(project); w.ExecutePendingCommands();
        for (int i = 0; i < p.Count; i++) p.Trees[i] = p.Trees[i] with { Health = .8f, StressYears = 0, Resources = new(.7f,.7f,.7f) };
        var scope = RuleEngine.Scope("tree"); int id = scope.Elements(w)[0];
        var before = w.RuleBook.PreviewInfluences(w, "forest.health", id, 1d / 12).Single();
        Month(w); Assert.Equal(before.After, p.Trees[p.Count - 1].Health, 6);
        Assert.All(p.Trees.Take(p.Count), t => Assert.Equal(1d / 12, t.StressYears, 6));
    }

}
