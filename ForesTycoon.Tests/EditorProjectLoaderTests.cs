using ForesTycoon.Editor;

namespace ForesTycoon.Tests;

public sealed class EditorProjectLoaderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OldWildlifeHooksOpenAsEditableEquationsWithoutChangingTheSource(int version)
    {
        var source = RuleProject.CreateDefault(); source.Version = version;
        source.Rules.RemoveAll(r => r.Schedule == "wildlife");
        foreach (string hook in RuleProjectMigrations.RetiredWildlifeQueries)
            source.Behaviors.Graphs.Add(new() { Id = "default.gameplay." + hook, Name = "Saját " + hook,
                Kind = "gameplay", Hook = hook, Output = "custom", Nodes = new() {
                    new() { Id = "custom", Name = "Saját érték", Operation = BehaviorOperation.Constant, Value = .123 } } });
        string original = source.ToJson();

        var loaded = EditorProjectLoader.Prepare(RuleProject.FromJson(original));

        Assert.Equal(4, loaded.Version);
        Assert.DoesNotContain(loaded.Behaviors.Graphs, g => RuleProjectMigrations.RetiredWildlifeQueries.Contains(g.Hook));
        var computations = loaded.Rules.SelectMany(r => r.Effects).Select(e => e.Computation).OfType<BehaviorGraph>().ToArray();
        foreach (string hook in RuleProjectMigrations.RetiredWildlifeQueries) {
            var graph = Assert.Single(computations, g => g.Id == "default.gameplay." + hook);
            Assert.Equal(.123, graph.Nodes.Single(n => n.Id == "custom").Value);
        }
        Assert.Equal(original, source.ToJson());
        Assert.Equal(loaded.ToJson(), EditorProjectLoader.Prepare(loaded).ToJson());
    }

    [Fact]
    public void ModernDraftKeepsDeletedRulesAndIncompleteExpressions()
    {
        var source = RuleProject.CreateDefault();
        source.Rules.Clear(); source.Behaviors.Graphs.Clear();
        source.Behaviors.Graphs.Add(new() { Id = "unfinished", Name = "Félkész", Kind = "gameplay", Hook = "wildlife.walkable",
            Output = "missing", Nodes = new() { new() { Id = "sum", Name = "Összeg", Operation = BehaviorOperation.Add, A = "missing" } } });
        string original = source.ToJson();
        Assert.Equal(original, EditorProjectLoader.Prepare(source).ToJson());
    }

    [Fact]
    public void UnknownHooksStillReportTheOffendingGraph()
    {
        var source = RuleProject.CreateDefault();
        source.Behaviors.Graphs.Add(new() { Id = "unknown", Name = "Ismeretlen", Kind = "gameplay", Hook = "unknown.hook" });
        var error = Assert.Throws<InvalidDataException>(() => EditorProjectLoader.Prepare(source));
        Assert.Contains("unknown.hook", error.Message);
        Assert.Contains("[unknown]", error.Message);
    }
}
