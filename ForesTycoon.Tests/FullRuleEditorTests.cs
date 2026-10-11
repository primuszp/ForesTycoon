using ForesTycoon.Editor;
using ForesTycoon.RuleCompiler;

namespace ForesTycoon.Tests;

public sealed class FullRuleEditorTests
{
    [Fact]
    public void SemanticPortsKeepExactGraphConnectionsAndEditingChangesTheRenderedAlgorithm()
    {
        const string source = "class Rule { double Step(double value) { if (value > 2) { value *= 3; } else { value = 1; } return value; } }";
        var file = AlgorithmBundle.Import("ForesTycoon/World/Rule.cs", source); var index = file.Index();
        var branch = file.Nodes.Single(n => n.Kind == "IfStatement"); var ports = AlgorithmSemantics.Ports(branch, index);
        Assert.Equal(new[] { "Feltétel", "Ha teljesül", "Különben" }, ports.Select(p => p.Name));
        Assert.All(ports, p => Assert.Equal(p.Target, index[p.Owner].Children[p.Position]));
        Assert.Equal(source, AlgorithmBundle.Render(file));
        var multiply = file.Nodes.Single(n => n.Kind == "MultiplyAssignmentExpression");
        var value = AlgorithmSemantics.Values(multiply.Id, index).Single(n => n.Text == "3");
        AlgorithmSemantics.SetValue(value, "7.5"); Assert.Contains("value *= 7.5", AlgorithmBundle.Render(file));
        Assert.False(AlgorithmSemantics.CanConnect(ports[0].Target, branch.Id, index));
        Assert.True(AlgorithmSemantics.CanConnect(branch.Id, ports[0].Target, index));
        Assert.Throws<InvalidDataException>(() => AlgorithmSemantics.SetValue(value, "1 + 2"));
        Assert.Equal("7.5", value.Text);
        var comparison = index[ports[0].Target]; AlgorithmSemantics.SetOperator(comparison, index, "<");
        Assert.Equal("LessThanExpression", comparison.Kind); Assert.Contains("value < 2", AlgorithmBundle.Render(file));
        var type = file.Nodes.First(n => n.Kind == "PredefinedType"); AlgorithmSemantics.SetType(type, index, "float");
        Assert.Contains("float Step", AlgorithmBundle.Render(file));
    }
    [Fact]
    public void OwnProcedureIsAnEditableConnectedGraphAndRetainsTheExistingMethod()
    {
        var file = AlgorithmBundle.Import("ForesTycoon/World/Rule.cs", "class Rule { double Existing() { return 2; } }");
        string owner = file.Nodes.Single(n => n.Kind == "ClassDeclaration").Id;
        string added = AlgorithmSemantics.InsertProcedure(file, owner, "GrowthRule", "double");
        var index = file.Index(); Assert.Contains(added, index[owner].Children);
        Assert.Contains("double Existing()", AlgorithmBundle.Render(file)); Assert.Contains("double GrowthRule(double value)", AlgorithmBundle.Render(file));
        Assert.Contains(file.Entries, e => e.Id == added && e.Name == "GrowthRule");
        Assert.Throws<ArgumentException>(() => AlgorithmSemantics.InsertProcedure(file, owner, "bad name", "double"));
    }
    [Fact]
    public void TemplatesReplaceOnlyTheChosenBindingAndNeverShareActiveMutableNodes()
    {
        var model = new BehaviorModel { Graphs = new() {
            new() { Id = "type", Hook = "forest.growth", Kind = "tree" }, new() { Id = "local", Hook = "forest.growth", Kind = "tree", ObjectId = 42 } } };
        var template = AuthoringOperations.Template(model.Graphs[0]); template.Graph!.Nodes[0].Name = "Copied";
        Assert.NotEqual("Copied", model.Graphs[0].Nodes[0].Name);
        AuthoringOperations.Instantiate(model, template, 42);
        Assert.Equal(2, model.Graphs.Count); Assert.Contains(model.Graphs, g => g.Id == "type");
        var instance = model.Graphs.Single(g => g.ObjectId == 42); Assert.NotEqual(template.Graph.Id, instance.Id);
        instance.Nodes[0].Name = "User edited"; Assert.Equal("Copied", template.Graph.Nodes[0].Name);
        Assert.Equal(2, new CompiledBehaviorModel(model, WorldBehaviorPolicy.Hooks).Evaluate("forest.growth", "tree", 42, new(2, 0, 0, 0, 0)));
    }
    [Fact]
    public void ExternalAgentEditsAreDetectedAndArchivedVersionsRemainImmutable()
    {
        string folder = Path.Combine(Path.GetTempPath(), "full-editor-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try {
            string file = Path.Combine(folder, "project.json"); var guard = new ProjectFileGuard(); guard.Observe(file);
            Assert.False(guard.Changed(file)); File.WriteAllText(file, "initial"); Assert.True(guard.Changed(file));
            guard.Observe(file); ProjectHistory.Archive(file); File.WriteAllText(file, "agent edit");
            Assert.Throws<IOException>(() => guard.RequireUnchanged(file));
            Assert.Equal("initial", File.ReadAllText(Directory.GetFiles(file + ".history").Single()));
            guard.Observe(file); guard.RequireUnchanged(file);
        } finally { Directory.Delete(folder, true); }
    }
    [Fact]
    public void SharedAuthoringMetadataRoundTripsWithoutChangingRuntimeIdentity()
    {
        var project = GameWorld.DefaultRuleProject(); string runtime = project.ContentRevision();
        project.Authoring.Entries["forest.growth"] = new() { Topic = 1, Notes = "AI és felhasználó közös jegyzete" };
        project.Authoring.Templates.Add(AuthoringOperations.Template(project.Behaviors.Graphs[0]));
        var copy = RuleProject.FromJson(project.ToJson()); Assert.Equal(1, copy.Authoring.Entries["forest.growth"].Topic);
        Assert.Equal(runtime, copy.ContentRevision());
        copy.Road.Nodes.Single(n => n.Id == "scale").Value = 2; Assert.NotEqual(runtime, copy.ContentRevision());
    }
    [Fact]
    public void StockDependencyListingFindsFlowsAndActualRuleConsumers()
    {
        var project = GameWorld.DefaultRuleProject(); project.Dynamics.Stocks.Add(new() { Id = "water" });
        project.Dynamics.Flows.Add(new() { Id = "rain", Name = "Rain", To = "water" });
        var graph = project.Behaviors.Graphs.First(); graph.Nodes.Add(new() { Id = "feedback", Name = "Feedback", Operation = BehaviorOperation.NamedValue, Key = "stock.water" });
        var uses = AuthoringOperations.StockConsumers(project, "water"); Assert.Contains("Rain", uses); Assert.Contains(graph.Name, uses);
    }
    [Fact]
    public void RemovingAnOverrideStillPublishesTheExactRuntimeIdentity()
    {
        var project = GameWorld.DefaultRuleProject(); project.Behaviors.Graphs.RemoveAt(0);
        project.Tuning[GameTuning.Specs[0].Id] = GameTuning.Specs[0].Default;
        var normalized = GameWorld.NormalizeRuleProject(project);
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false);
        world.QueueRuleProject(normalized); world.ExecutePendingCommands();
        Assert.Equal(normalized.ContentRevision(), world.ExportRuleWorld("test").ProjectRevision);
        Assert.Equal(WorldBehaviorPolicy.Hooks.Length, normalized.Behaviors.Graphs.Count);
        Assert.Equal(WorldBehaviorPolicy.Hooks.Length - 1, project.Behaviors.Graphs.Count);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditorLoadsForeignRuntimeCheckpointsWithoutChangingTheSaveAndRejectsPendingMigration(bool pending)
    {
        string folder = Path.Combine(Path.GetTempPath(), "editor-world-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try {
            using var source = new GameWorld(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false); source.Update(.1);
            if (pending) source.QueueElevationEdit(0, 1, 0, 1);
            using var memory = new MemoryStream(); source.Save(memory);
            var json = System.Text.Json.Nodes.JsonNode.Parse(memory.ToArray())!;
            json["checkpoint"]!["algorithms"] = System.Text.Json.JsonSerializer.SerializeToNode(new AlgorithmGraphReference { Revision = new string('a', 64), BundlePath = Path.Combine(folder, "graphs.json"), WorkspaceRoot = folder });
            string file = Path.Combine(folder, "world.json"), saved = json.ToJsonString(); File.WriteAllText(file, saved);
            using var target = new GameWorld(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false);
            if (pending) { Assert.Throws<InvalidOperationException>(() => EditorWorldLoader.Load(target, file)); Assert.Equal(0UL, target.SimulationTick); }
            else { Assert.Contains("editor", EditorWorldLoader.Load(target, file)); Assert.Equal(source.SimulationTick, target.SimulationTick); Assert.Null(target.RuleProjectDocument.Algorithms); }
            Assert.Equal(saved, File.ReadAllText(file));
        } finally { Directory.Delete(folder, true); }
    }
}
