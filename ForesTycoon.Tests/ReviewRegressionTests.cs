namespace ForesTycoon.Tests;

public class ReviewRegressionTests
{
    [Fact]
    public void FlowPreviewMatchesCapacityLimitedExecution()
    {
        using var w = new SimulationWorld(TerrainSettings.Default.WithNodeSize(9, 42));
        double a = 10, b = 9;
        var scope = new RuleScope("probe", "Probe", "row", _ => new[] { 0 }, _ => 1, new[] { new RuleScopeField("a", "", (_, _) => a, (_, _, v) => a = v) { Min = 0, Max = 100 }, new RuleScopeField("b", "", (_, _) => b, (_, _, v) => b = v) { Min = 0, Max = 10 }, new RuleScopeField("unrelated", "", (_, _) => throw new InvalidOperationException("Unused fields must not be read.")) }, Array.Empty<RuleScopeAction>());
        var rules = new List<WorldRule> { new() { Id = "probe", Name = "Probe", Scope = "probe", Effects = { new() { Mode = "flow", Target = "a", To = "b", Formula = "5" } } } };
        var e = new RuleEngine(rules, new(), Array.Empty<string>(), _ => 0, scopes: new[] { scope });
        Assert.Equal(9, Assert.Single(e.PreviewInfluences(w, "probe", 0, 0)).After);
        e.Run(w, 0, 0); Assert.Equal(9, a); Assert.Equal(10, b);
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(999)]
    public void InvalidSavedElementIsRejected(int element)
    {
        using var w = new SimulationWorld(TerrainSettings.Default.WithNodeSize(9, 42)); double amount = 0;
        var scope = new RuleScope("probe", "Probe", "row", _ => new[] { 0, 1 }, _ => 2, new[] { new RuleScopeField("amount", "", (_, _) => amount, (_, _, v) => amount = v) }, Array.Empty<RuleScopeAction>());
        var rules = new List<WorldRule> { new() { Id = "probe", Name = "Probe", Scope = "probe", Effects = { new() { Mode = "set", Target = "amount", Formula = "17" } } } };
        var e = new RuleEngine(rules, new(), Array.Empty<string>(), _ => 0, scopes: new[] { scope });
        Assert.Throws<InvalidDataException>(() => e.RestoreStep(w, new RuleStepCheckpoint(0, 0, 0, element, new())));
    }
    [Fact]
    public async Task AnUneditedOlderBundleIsRejectedAfterASameFileSourceFix()
    {
        string root = Path.Combine(ForesTycoon.RuleCompiler.AlgorithmBuild.FindWorkspace()!, "artifacts", "review-validation", "stale-probe", Guid.NewGuid().ToString("N"));
        foreach (string project in ForesTycoon.RuleCompiler.AlgorithmBundle.Projects) Directory.CreateDirectory(Path.Combine(root, project));
        File.WriteAllText(Path.Combine(root, "ForesTycoon", "ForesTycoon.csproj"), "<Project />");
        Directory.CreateDirectory(Path.Combine(root, "ForesTycoon.Ecology", "Soil")); Directory.CreateDirectory(Path.Combine(root, "ForesTycoon.Ecology", "Climate"));
        File.WriteAllText(Path.Combine(root, "ForesTycoon.Ecology", "Soil", "profiles.json"), "{\n  \"soil\": [1, 2]\n}");
        File.WriteAllText(Path.Combine(root, "ForesTycoon.Ecology", "Climate", "default.json"), "{}");
        string source = Path.Combine(root, "ForesTycoon.Simulation", "Probe.cs"); File.WriteAllText(source, "class Probe { int Value => 1; }");
        var bundle = ForesTycoon.RuleCompiler.AlgorithmBundle.ImportWorkspace(root);
        var reference = ForesTycoon.RuleCompiler.AlgorithmBuild.Store(bundle, root, Path.Combine(root, "project.json"));
        Assert.NotNull(ForesTycoon.RuleCompiler.AlgorithmBuild.StaleReason(reference));
        var edited = bundle.Clone();
        edited.Files.Single(f => f.Path.EndsWith("Probe.cs")).Nodes.Single(n => n.Token && n.Text == "1").Text = "3";
        var editedReference = ForesTycoon.RuleCompiler.AlgorithmBuild.Store(edited, root, Path.Combine(root, "edited.json"));
        Assert.Equal(reference.BaseRevision, editedReference.BaseRevision);
        Assert.Null(ForesTycoon.RuleCompiler.AlgorithmBuild.StaleReason(editedReference));
        File.WriteAllText(source, "class Probe { int Value => 2; }");
        Assert.NotNull(ForesTycoon.RuleCompiler.AlgorithmBuild.StaleReason(reference));
        Assert.NotNull(ForesTycoon.RuleCompiler.AlgorithmBuild.StaleReason(editedReference));
        await Assert.ThrowsAsync<InvalidDataException>(() => ForesTycoon.RuleCompiler.AlgorithmBuild.CompileAsync(editedReference));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ValidSavedCursorBoundariesResume(int element)
    {
        using var w = new SimulationWorld(TerrainSettings.Default.WithNodeSize(9, 42)); double amount = 0;
        var scope = new RuleScope("probe", "Probe", "row", _ => new[] { 0, 1 }, _ => 2, new[] { new RuleScopeField("amount", "", (_, _) => amount, (_, _, v) => amount = v) }, Array.Empty<RuleScopeAction>());
        var rules = new List<WorldRule> { new() { Id = "probe", Name = "Probe", Scope = "probe", Effects = { new() { Mode = "set", Target = "amount", Formula = "17" } } } };
        var e = new RuleEngine(rules, new(), Array.Empty<string>(), _ => 0, scopes: new[] { scope });
        e.RestoreStep(w, new RuleStepCheckpoint(0, 0, 0, element, new()));
        while (e.StepPending) e.Continue(w, 7);
        Assert.Equal(element == 2 ? 0 : 17, amount);
    }
    [Fact]
    public void SequentialFlowPreviewUpdatesTheReceiversDetachedStock()
    {
        using var w = new SimulationWorld(TerrainSettings.Default.WithNodeSize(9, 42)); double a = 10, b = 9;
        var scope = new RuleScope("probe", "Probe", "row", _ => new[] { 0 }, _ => 1, new[] { new RuleScopeField("a", "", (_, _) => a, (_, _, v) => a = v) { Min = 0, Max = 100 }, new RuleScopeField("b", "", (_, _) => b, (_, _, v) => b = v) { Min = 0, Max = 10 } }, Array.Empty<RuleScopeAction>());
        var rules = new List<WorldRule> { new() { Id = "probe", Name = "Probe", Scope = "probe", Effects = { new() { Mode = "flow", Target = "a", To = "b", Formula = "5" }, new() { Mode = "flow", Target = "b", To = "", Formula = "2" } } } };
        var e = new RuleEngine(rules, new(), Array.Empty<string>(), _ => 0, scopes: new[] { scope });
        var rows = e.PreviewInfluences(w, "probe", 0, 0); Assert.Equal(9, rows[0].After); Assert.Equal(8, rows[1].After);
        Assert.Equal(10, a); Assert.Equal(9, b); Assert.Empty(e.FlowTotals);
        e.Run(w, 0, 0); Assert.Equal(9, a); Assert.Equal(8, b);
    }
    [Fact]
    public void DownhillPreviewHonorsReceiverCapacityWithoutWritingToNeighbours()
    {
        using var w = new SimulationWorld(TerrainSettings.Default.WithNodeSize(9, 42)); double source = 10; var received = new double[] { 9, 0 };
        var neighbours = new RuleScope("neighbor", "Neighbor", "row", _ => new[] { 0, 1 }, _ => 2, new[] { new RuleScopeField("b", "", (_, id) => received[id], (_, id, v) => received[id] = v) { Min = 0, Max = 10 }, new RuleScopeField("level", "", (_, id) => id == 0 ? 0 : 10) }, Array.Empty<RuleScopeAction>());
        var scope = new RuleScope("probe", "Probe", "row", _ => new[] { 0 }, _ => 1, new[] { new RuleScopeField("a", "", (_, _) => source, (_, _, v) => source = v) { Min = 0, Max = 100 }, new RuleScopeField("level", "", (_, _) => 5) }, Array.Empty<RuleScopeAction>()) { Collections = new[] { new RuleScopeCollection("neighbors", "neighbor", (_, _) => new[] { 0, 1 }) } };
        var rules = new List<WorldRule> { new() { Id = "probe", Name = "Probe", Scope = "probe", Effects = { new() { Mode = "flow", Target = "a", To = "neighbors.b", Formula = "5", Spread = "downhill", Level = "level" } } } };
        var e = new RuleEngine(rules, new(), Array.Empty<string>(), _ => 0, scopes: new[] { scope, neighbours });
        Assert.Equal(9, Assert.Single(e.PreviewInfluences(w, "probe", 0, 0)).After);
        Assert.Equal(10, source); Assert.Equal(new double[] { 9, 0 }, received);
        e.Run(w, 0, 0); Assert.Equal(9, source); Assert.Equal(new double[] { 10, 0 }, received);
    }
}
