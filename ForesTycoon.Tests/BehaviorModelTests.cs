using ForesTycoon;
using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

public class BehaviorModelTests
{
    private static BehaviorGraph Constant(string hook, string kind, double value, ulong? id = null) => new() {
        Id = Guid.NewGuid().ToString("N"), Hook = hook, Kind = kind, ObjectId = id, Output = "value",
        Nodes = new() { new() { Id = "value", Name = "Value", Operation = BehaviorOperation.Constant, Value = value } }
    };
    private static CompiledBehaviorModel Compile(params BehaviorGraph[] graphs) => new(new() { Graphs = graphs.ToList() }, WorldBehaviorPolicy.Hooks);

    [Fact]
    public void ObjectBindingOverridesTypeAndUnboundTypesKeepNativeBehavior()
    {
        var type = Constant("machine.pace", "forwarder", 2);
        var one = Constant("machine.pace", "forwarder", 3, 7);
        var runtime = Compile(type, one);
        Assert.Equal(3, runtime.Evaluate("machine.pace", "forwarder", 7, new(1, 0, 0, 0, 0)));
        Assert.Equal(2, runtime.Evaluate("machine.pace", "forwarder", 8, new(1, 0, 0, 0, 0)));
        Assert.Equal(1, runtime.Evaluate("machine.pace", "processor", 7, new(1, 0, 0, 0, 0)));
        one.Nodes[0].Value = 9;
        Assert.Equal(3, runtime.Evaluate("machine.pace", "forwarder", 7, new(1, 0, 0, 0, 0)));
    }
    [Fact]
    public void ConditionalGraphUsesLiveInputsAndOutputBounds()
    {
        var graph = Constant("forest.health", "tree", 2);
        graph.Nodes.AddRange(new BehaviorNode[] {
            new() { Id = "health", Name = "Health", Operation = BehaviorOperation.State },
            new() { Id = "threshold", Name = "Threshold", Operation = BehaviorOperation.Constant, Value = .5 },
            new() { Id = "condition", Name = "Condition", Operation = BehaviorOperation.Less, A = "health", B = "threshold" },
            new() { Id = "native", Name = "Native", Operation = BehaviorOperation.Native },
            new() { Id = "result", Name = "Result", Operation = BehaviorOperation.Select, A = "condition", B = "value", C = "native" }
        }); graph.Output = "result";
        var runtime = Compile(graph);
        Assert.Equal(1, runtime.Evaluate(graph.Hook, graph.Kind, 1, new(.7, 0, 0, .2, 0)));
        Assert.Equal(.7, runtime.Evaluate(graph.Hook, graph.Kind, 1, new(.7, 0, 0, .8, 0)));
    }
    [Fact]
    public void InvalidGraphsAndAmbiguousBindingsAreRejected()
    {
        var graph = Constant("mill.process", "mill", 1);
        Assert.Throws<InvalidDataException>(() => Compile(graph, Constant("mill.process", "mill", 2)));
        graph.Nodes[0].Operation = BehaviorOperation.Add; graph.Nodes[0].A = "value"; graph.Nodes[0].B = "value";
        Assert.Throws<InvalidDataException>(() => Compile(graph));
        graph.Nodes[0].A = "missing";
        Assert.Throws<InvalidDataException>(() => Compile(graph));
        Assert.Throws<InvalidDataException>(() => Compile(Constant("not.implemented", "tree", 1)));
        Assert.Throws<InvalidDataException>(() => Compile(Constant("mill.process", "mill", double.NaN)));
        Assert.Throws<InvalidDataException>(() => Compile(Constant("tuning.DieselPrice", "world", 1, 7)));
    }
    [Fact]
    public void RuntimeArithmeticFailureKeepsNativeResultAndReportsObject()
    {
        var graph = Constant("mill.process", "mill", 0);
        graph.Nodes.Add(new() { Id = "divide", Name = "Divide", Operation = BehaviorOperation.Divide, A = "value", B = "value" });
        graph.Output = "divide"; var runtime = Compile(graph);
        Assert.Equal(.25, runtime.Evaluate(graph.Hook, graph.Kind, 12, new(.25, 1, 0, 0, 2)));
        Assert.Contains("#12", runtime.LastError);
    }
    [Fact]
    public void UnselectedAndDisconnectedExpressionsDoNotRun()
    {
        var graph = Constant("mill.process", "mill", 0);
        graph.Nodes.AddRange(new BehaviorNode[] {
            new() { Id = "bad", Name = "Divide by zero", Operation = BehaviorOperation.Divide, A = "value", B = "value" },
            new() { Id = "native", Name = "Native", Operation = BehaviorOperation.Native },
            new() { Id = "select", Name = "Select", Operation = BehaviorOperation.Select, A = "value", B = "bad", C = "native" }
        }); graph.Output = "select";
        var runtime = Compile(graph);
        Assert.Equal(.25, runtime.Evaluate(graph.Hook, graph.Kind, 1, new(.25, 1, 0, 0, 2)));
        Assert.Null(runtime.LastError);
    }

    [Fact]
    public void SnowAndInfiltrationGraphsPreserveWaterAndTargetIndividualTiles()
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(9, 42), (_, _) => 4);
        var environment = new EnvironmentSystem(map, null);
        environment.Behaviors = new WorldBehaviorPolicy(new() { Graphs = new() {
            Constant("water.snowmelt", "tile", 0), Constant("water.snowmelt", "tile", 1e9, 0), Constant("water.infiltration", "tile", 1e9)
        }});
        environment.ForceWeather(WeatherPreset.Snow, 10, 120); environment.Update(30);
        Assert.Equal(0, environment.SnowWaterAt(0)); Assert.True(environment.SnowWaterAt(1) > 0);
        Assert.InRange(Math.Abs(environment.BalanceError), 0, 1e-6);
    }
    [Fact]
    public void MillBindingChangesOnlyItsTargetAndConservesTimber()
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(9, 42), (_, _) => 4);
        var logistics = new ForestryLogistics(map, new ForestSystem(map));
        logistics.Mills.Add(new() { TileId = 1, Stock = 10 }); logistics.Mills.Add(new() { TileId = 2, Stock = 10 });
        logistics.Behaviors = new WorldBehaviorPolicy(new() { Graphs = new() { Constant("mill.process", "mill", 100, 1) }});
        logistics.Update(1);
        Assert.Equal(10, logistics.Mills[0].Processed); Assert.Equal(0, logistics.Mills[0].Stock);
        Assert.Equal(.25f, logistics.Mills[1].Processed); Assert.Equal(9.75f, logistics.Mills[1].Stock);
    }
    [Fact]
    public void StoppedMachineDoesNotChangeAnotherMachinesTimeStep()
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(9, 42), (_, _) => 4);
        var logistics = new ForestryLogistics(map, new ForestSystem(map)) { MachinesEnabled = true };
        for (int i = 1; i <= 2; i++) logistics.Machines.Add(new() {
            Id = i, Kind = ForestMachineKind.Forwarder, Path = new[] { 0 }, State = ForestMachineState.Loading,
            Source = new TimberStack { Volume = 10, Value = 100 }, Upkeep = new VehicleUpkeep((uint)i)
        });
        logistics.Behaviors = new WorldBehaviorPolicy(new() { Graphs = new() { Constant("machine.pace", "forwarder", 0, 1) }});
        logistics.Update(1.0 / 30);
        Assert.Equal(0, logistics.Machines[0].WorkTime);
        Assert.InRange(logistics.Machines[1].WorkTime, .03, .034);
    }

    [Fact]
    public void TreeBindingChangesActualGrowthRateAndSurvivesReload()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        var trees = world.CaptureCheckpoint().Ecology.Forest.Trees.Patches.SelectMany(p => p.Trees).ToArray();
        var target = trees.First(t => t.AnnualGrowth.Height > 0);
        world.QueueBehaviors(new() { Graphs = new() { Constant("forest.growth", "tree", 0, target.Id) }});
        world.ExecutePendingCommands();
        var changed = world.CaptureCheckpoint().Ecology.Forest.Trees.Patches.SelectMany(p => p.Trees).ToArray();
        Assert.Equal(default, changed.Single(t => t.Id == target.Id).AnnualGrowth);
        Assert.Contains(changed, t => t.Id != target.Id && t.AnnualGrowth.Height > 0);
        using var restored = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0; restored.Load(save);
        for (int i = 0; i < 160; i++) { world.Update(.5); restored.Update(.5); }
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(world.CaptureCheckpoint()), System.Text.Json.JsonSerializer.Serialize(restored.CaptureCheckpoint()));
        var after = restored.CaptureCheckpoint().Ecology.Forest.Trees.Patches.SelectMany(p => p.Trees).Single(t => t.Id == target.Id);
        Assert.Equal(target.Dimensions, after.Dimensions);
    }

    [Fact]
    public void BehaviorCommandsAndCheckpointRestoreExecutableBindings()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false);
        using var restored = new GameWorld(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false);
        var model = new BehaviorModel { Graphs = new() { Constant("tuning.DieselPrice", "world", 1.2) }};
        string forestBefore = System.Text.Json.JsonSerializer.Serialize(world.CaptureCheckpoint().Ecology.Forest);
        world.QueueBehaviors(model); world.ExecutePendingCommands();
        Assert.Equal(forestBefore, System.Text.Json.JsonSerializer.Serialize(world.CaptureCheckpoint().Ecology.Forest));
        Assert.Equal(1.2, world.Tuning[Tune.DieselPrice]);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0; restored.Load(save);
        Assert.Equal(model.ToJson(), restored.BehaviorDocument.ToJson());
        Assert.Equal(1.2, restored.Tuning[Tune.DieselPrice]);
        world.Update(.5); restored.Update(.5);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(world.CaptureCheckpoint()), System.Text.Json.JsonSerializer.Serialize(restored.CaptureCheckpoint()));
        model.Graphs[0].Nodes[0].Value = 2;
        world.QueueBehaviors(model);
        using var pending = new MemoryStream(); world.Save(pending); pending.Position = 0; restored.Load(pending);
        Assert.Equal(1.2, restored.Tuning[Tune.DieselPrice]);
        restored.ExecutePendingCommands(); Assert.Equal(2, restored.Tuning[Tune.DieselPrice]);
    }
}
