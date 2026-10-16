using System.Text.Json;
using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class ResponseRuleCardTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), forestYearSeconds: 120, enableRendering: false);
    private static string State(GameWorld w) => JsonSerializer.Serialize(w.CaptureCheckpoint());
    [Fact]
    public void EveryResponseHasMeaningAndValidSuggestedOffValue() {
        foreach (var hook in EcologyResponseProgram.Hooks) {
            var info = ResponseRuleDescription.For(hook.Id);
            Assert.NotEmpty(info.Result); Assert.NotEmpty(info.Consumers); Assert.NotEmpty(info.FixedMeaning);
            Assert.InRange(info.SuggestedOffValue, hook.Min, hook.Max);
        }
    }
    [Fact]
    public void ConsumerLinksIgnoreDisconnectedInputsAndDisabledConsumers() {
        var project = GameWorld.DefaultRuleProject(); var graph = project.Behaviors.Graphs.Single(g => g.Hook == "forest.light");
        var info = ResponseRuleDescription.For("forest.shading");
        Assert.Contains(graph.Name, info.GraphConsumers(new[] { graph }));
        var original = graph.Output;
        graph.Nodes.Add(new() { Id = "fixed", Name = "Fixed value", Operation = BehaviorOperation.Constant, Value = 1 }); graph.Output = "fixed";
        Assert.Empty(info.GraphConsumers(new[] { graph }));
        graph.Output = original; graph.Disabled = true; graph.DisabledValue = 1;
        Assert.Empty(info.GraphConsumers(new[] { graph }));
    }
    [Fact]
    public void DisabledGraphReturnsExplicitConstantWithoutReadingOrRunningItsNodes() {
        var hook = EcologyResponseProgram.Hooks.Single(h => h.Id == "water.stress");
        var graph = EcologyResponseProgram.DefaultGraph(hook);
        graph.Nodes = new() {
            new() { Id = "one", Name = "One", Operation = BehaviorOperation.Constant, Value = 1 },
            new() { Id = "zero", Name = "Zero", Operation = BehaviorOperation.Constant, Value = 0 },
            new() { Id = "divide", Name = "Disconnected while off", Operation = BehaviorOperation.Divide, A = "one", B = "zero" }
        }; graph.Output = "divide"; graph.Disabled = true; graph.DisabledValue = .37;
        var source = new BehaviorModel { Graphs = new() { graph } }; var compiled = new CompiledBehaviorModel(source, new[] { hook });
        for (int i = 0; i < 300; i++) {
            Assert.Equal(.37, compiled.Evaluate(hook.Id, hook.Kind, 0, default));
            Assert.Equal(.37, compiled.Evaluator(hook.Id, hook.Kind)!(default, null));
        }
        Assert.Null(compiled.LastError); Assert.Equal("divide", compiled.Document.Graphs[0].Output);
        source = BehaviorModel.FromJson(source.ToJson()); Assert.True(source.Graphs[0].Disabled); Assert.Equal(.37, source.Graphs[0].DisabledValue);
        source.Graphs[0].Disabled = false;
        Assert.Throws<ArithmeticException>(() => new CompiledBehaviorModel(source, new[] { hook }).Evaluator(hook.Id, hook.Kind)!(default, null));
    }
    [Theory]
    [InlineData(null)]
    [InlineData(-.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void OffStateRequiresAnExplicitFiniteResultInsideTheHookRange(double? value) {
        var p = GameWorld.DefaultRuleProject(); var graph = p.Behaviors.Graphs.Single(g => g.Hook == "water.stress");
        graph.Disabled = true; graph.DisabledValue = value;
        Assert.Throws<InvalidDataException>(() => EcologyResponseProgram.FromModel(p.Behaviors));
    }
    [Fact]
    public void DisabledWaterResponseIsActuallyUsedAndSurvivesSlicingSaveAndReplay() {
        using var whole = World(); using var sliced = World(); var p = whole.RuleProjectDocument;
        var graph = p.Behaviors.Graphs.Single(g => g.Hook == "water.stress"); graph.Disabled = true; graph.DisabledValue = .37;
        foreach (var w in new[] { whole, sliced }) w.QueueRuleProject(p);
        whole.ExecutePendingCommands(); do { sliced.ContinueCommands(3); } while (sliced.CommandPending);
        for (int tick = 0; tick < 310; tick++) { whole.Update(1d / 30); while (!sliced.ContinueUpdate(1d / 30, 3)) { } }
        Assert.Equal(State(whole), State(sliced));
        Assert.All(whole.Forest.IndividualTrees.Patches.SelectMany(p => p.Value.Trees.Take(p.Value.Count)), t => Assert.Equal(.37f, t.Resources.Water, 6));
        using var save = new MemoryStream(); whole.Save(save); save.Position = 0; sliced.Load(save); Assert.Equal(State(whole), State(sliced));
        save.Position = 0; var data = WorldSaveSerializer.Read(save);
        using var journal = new MemoryStream(); WorldSaveSerializer.Write(journal, new WorldSaveData { Terrain = data.Terrain, SoilModel = data.SoilModel, Climate = data.Climate,
            Tick = data.Tick, TickRate = data.TickRate, ForestYearSeconds = data.ForestYearSeconds, Commands = data.Commands });
        journal.Position = 0; sliced.Load(journal); Assert.Equal(State(whole), State(sliced));
        graph.Disabled = false; foreach (var w in new[] { whole, sliced }) { w.QueueRuleProject(p); w.ExecutePendingCommands(); w.Update(1d / 30); }
        Assert.Equal(State(whole), State(sliced)); Assert.False(whole.BehaviorDocument.Graphs.Single(g => g.Hook == graph.Hook).Disabled);
    }
    [Fact]
    public void InitialSizeOffConstantOnlyAppliesToNewTrees() {
        var p = GameWorld.DefaultRuleProject(); p.World = GameWorld.DefaultScenario(42, 16);
        var graph = p.Behaviors.Graphs.Single(g => g.Hook == "forest.initialHeight"); graph.Disabled = true; graph.DisabledValue = 3.5;
        using var world = WorldLoader.CreateProject(p);
        var trees = world.Forest.IndividualTrees.Patches.SelectMany(p => p.Value.Trees.Take(p.Value.Count)).ToArray();
        Assert.NotEmpty(trees); Assert.All(trees, t => Assert.Equal(3.5f, t.Dimensions.Height));
    }
}
