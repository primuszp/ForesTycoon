using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class BehaviorCurveTests
{
    public static IEnumerable<object[]> Families => RuleCurve.Families.Select(f => new object[] { f.Id });
    private static BehaviorGraph Graph(RuleCurve curve) => new() {
        Id = "curve", Hook = "test.curve", Kind = "test", Output = "curve", Nodes = new() {
            new() { Id = "input", Name = "Bemenet", Operation = BehaviorOperation.Input, Key = "value" },
            new() { Id = "curve", Name = "Válaszgörbe", Operation = BehaviorOperation.Curve, Key = curve.ToFormula(), A = "input" }
        }
    };
    private static readonly BehaviorHook Hook = new("test.curve", "Görbe", "test", "", -1e9, 1e9, "", "", "Próba", false, new[] { "value" });

    [Theory]
    [MemberData(nameof(Families))]
    public void EveryFamilyRunsTheSharedLoweredCurveAfterRoundTrip(string family)
    {
        var curve = new RuleCurve { Input = BehaviorCurves.Input }; curve.SetFamily(family);
        var graph = Graph(curve);
        var document = new BehaviorModel { Graphs = { graph } };
        string before = document.ToJson();
        var program = new CompiledBehaviorModel(BehaviorModel.FromJson(before), new[] { Hook });
        var evaluate = program.Evaluator(Hook.Id, Hook.Kind)!;
        foreach (double value in new[] { -1d, 0, .1, .4, .5, .9, 1, 2 })
            Assert.Equal(curve.Evaluate(value), evaluate(default, new[] { value }), 8);
        Assert.Equal(before, document.ToJson()); // Compilation never replaces the authored curve with expanded nodes.
    }

    [Fact]
    public void ConnectedCurvesUseTheirActualInputAndDecompileToTheSameProgram()
    {
        var curve = new RuleCurve { Input = BehaviorCurves.Input };
        var graph = Graph(curve);
        graph.Nodes.Add(new() { Id = "second", Name = "Második görbe", Operation = BehaviorOperation.Curve,
            Key = new RuleCurve { Input = BehaviorCurves.Input, Points = new() { (0, 1), (1, 0) } }.ToFormula(), A = "curve" });
        graph.Output = "second";
        var names = new[] { new FormulaName("value", BehaviorOperation.Input, "value") };
        var expanded = RuleFormula.Compile(RuleFormula.Decompile(graph, names), new() { Id = "flat", Hook = Hook.Id, Kind = Hook.Kind }, names);
        var a = new CompiledBehaviorModel(new() { Graphs = { graph } }, new[] { Hook }).Evaluator(Hook.Id, Hook.Kind)!;
        var b = new CompiledBehaviorModel(new() { Graphs = { expanded } }, new[] { Hook }).Evaluator(Hook.Id, Hook.Kind)!;
        Assert.Equal(.75, a(default, new[] { .25 }), 10);
        Assert.Equal(a(default, new[] { .25 }), b(default, new[] { .25 }));
    }

    [Theory]
    [InlineData("curve.linear(wrong; 0; 0; 1; 1)")]
    [InlineData("curve.linear(curveInput; 0; 0; 1; 1) * 2")]
    [InlineData("curve.linear(curveInput; 1; 0; 0; 1)")]
    public void InvalidCurvesCannotInstallAnotherHiddenFormula(string invalid)
    {
        var graph = Graph(new RuleCurve { Input = BehaviorCurves.Input }); graph.Nodes[1].Key = invalid;
        Assert.Throws<InvalidDataException>(() => new CompiledBehaviorModel(new() { Graphs = { graph } }, new[] { Hook }));
    }

    [Fact]
    public void ProjectCurveChangesTheLiveResponseAndSurvivesSlicedSaveAndContinuation()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        var project = world.RuleProjectDocument;
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == "water.stress");
        var curve = new RuleCurve { Input = BehaviorCurves.Input, Points = new() { (0, .2), (1, .8) } };
        var authored = Graph(curve); authored.Nodes[0].Key = "tile.waterSupply";
        graph.Nodes = authored.Nodes; graph.Output = authored.Output;
        world.QueueRuleProject(project);
        do { world.ContinueCommands(1); } while (world.CommandPending);
        Assert.Equal(.5, EcologyResponseProgram.FromModel(world.BehaviorDocument).WaterFactor(.5, 1, 4), 8);
        Assert.False(world.ContinueUpdate(1d / 30, 1));
        using var save = new MemoryStream(); world.AtTickBoundary(() => world.Save(save));
        while (!world.ContinueUpdate(1d / 30, 3)) { }
        save.Position = 0;
        using var restored = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false); restored.Load(save);
        for (int tick = 0; tick < 20; tick++) {
            world.Update(1d / 30);
            while (!restored.ContinueUpdate(1d / 30, 7)) { }
        }
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(restored.CaptureCheckpoint()));
        Assert.Equal(BehaviorOperation.Curve, restored.BehaviorDocument.Graphs.Single(g => g.Hook == graph.Hook).Nodes[1].Operation);
    }
}
