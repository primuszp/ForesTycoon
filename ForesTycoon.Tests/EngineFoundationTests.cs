namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class EngineFoundationTests
{
    [Fact]
    public void RemovingTruckMustKeepSurvivingTrucksCustomState()
    {
        using var world = FoundationPersistenceTests.FlatWorld(); var map = world.Map;
        map.BuildRoadTilePath(34, 82, RoadPaving.Macadam);
        var project = world.RuleProjectDocument;
        foreach (var rule in project.Rules) rule.Enabled = false;
        project.Fields.Add(new() { Name = "saját.memory", Scope = "truck", Initial = 0, Min = 0, Max = 1000 });
        project.Rules.Add(new() { Id = "remember", Name = "Remember", Scope = "truck",
            Effects = { new() { Target = "saját.memory", Mode = "set", Formula = "index + 10" } } });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        var first = world.VehicleState.SpawnTransit(new[] { 34, 50, 66, 82 });
        var second = world.VehicleState.SpawnTransit(new[] { 34, 50, 66, 82 });
        world.RuleBook.Run(world, 0, 0);
        double before = world.RuleBook.ReadCustom(world, "saját.memory", 1);
        Assert.Equal(11, before);
        world.VehicleState.Remove(first);
        Assert.Same(second, world.VehicleState.Vehicles[0]);
        Assert.Equal(before, world.RuleBook.ReadCustom(world, "saját.memory", 0));
        using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0;
        using var restored = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        restored.Load(saved);
        Assert.Equal(before, restored.RuleBook.ReadCustom(restored, "saját.memory", 0));
        var replacement = restored.VehicleState.SpawnTransit(new[] { 34, 50, 66, 82 });
        Assert.NotEqual(second.Id, replacement.Id);
        Assert.Equal(0, restored.RuleBook.ReadCustom(restored, "saját.memory", 1));
    }

    [Fact]
    public void FailedCommandMustAllowRequestedRegeneration()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        world.QueuePassage(new TrafficPassage(world.Map.Tiles.Count, TrafficVehicleKind.Truck, 1, 10000, 0));
        Assert.Throws<ArgumentException>(() => world.ExecutePendingCommands());
        Assert.True(world.IsFaulted);
        world.RequestRegenerate(TerrainSettings.Default.WithNodeSize(17, 19));
        Assert.True(world.GenerationPending);
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (world.GenerationPending) {
            world.ContinueCommands(17);
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(30), world.GenerationStage);
        }
        Assert.Null(world.GenerationError); Assert.False(world.IsFaulted);
        world.Update(1d / 30); Assert.Equal(1UL, world.SimulationTick);
    }

    [Fact]
    public void DynamicTruckPowerMustMatchAfterSaveLoad()
    {
        using var world = FoundationPersistenceTests.FlatWorld();
        world.Map.BuildRoadTilePath(34, 82, RoadPaving.Macadam);
        var project = world.RuleProjectDocument;
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == "tuning.Power");
        graph.Disabled = false;
        graph.Nodes = new() {
            new() { Id = "base", Name = "Base", Operation = BehaviorOperation.Constant, Value = 100000 },
            new() { Id = "income", Name = "Income", Operation = BehaviorOperation.NamedValue, Key = "world.income" },
            new() { Id = "sum", Name = "Sum", Operation = BehaviorOperation.Add, A = "base", B = "income" }
        };
        graph.Output = "sum";
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        world.VehicleState.SpawnTransit(new[] { 34, 50, 66, 82 });
        world.Logistics.Income = 100000;
        Assert.Equal(200000, world.Tuning[Tune.Power]);
        using var stream = new MemoryStream(); world.Save(stream); stream.Position = 0;
        using var restored = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        restored.Load(stream);
        Assert.Equal(world.VehicleState.Spec.Power, restored.VehicleState.Spec.Power);
        for (int tick = 0; tick < 20; tick++) {
            world.Update(1d / 30); restored.Update(1d / 30);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(world.CaptureCheckpoint()),
                System.Text.Json.JsonSerializer.Serialize(restored.CaptureCheckpoint()));
        }
        Assert.True(world.VehicleState.Vehicles[0].Distance > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildingMustNotOverlapLoadedStack(bool depot)
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
        var forest = new ForestSystem(map, new ForestStand[map.Tiles.Count]);
        map.BuildRoadTilePath(34, 210, RoadPaving.Macadam);
        var logistics = new ForestryLogistics(map, forest);
        Assert.True(logistics.PlaceStack(195));
        logistics.StackAt(195).Add(10, 100);
        Assert.False(depot ? logistics.PlaceDepot(195) : logistics.PlaceMill(195));
        Assert.Equal(10, logistics.StackAt(195).Volume);
        Assert.Empty(logistics.Mills); Assert.Empty(logistics.Depots); Assert.Empty(logistics.Machines); Assert.Empty(logistics.Trucks);
    }

    [Fact]
    public void DynamicTruckPowerMustFollowLiveFormula()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        var project = world.RuleProjectDocument;
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == "tuning.Power");
        graph.Disabled = false;
        graph.Nodes = new() {
            new() { Id = "base", Name = "Base", Operation = BehaviorOperation.Constant, Value = 100000 },
            new() { Id = "income", Name = "Income", Operation = BehaviorOperation.NamedValue, Key = "world.income" },
            new() { Id = "sum", Name = "Sum", Operation = BehaviorOperation.Add, A = "base", B = "income" }
        };
        graph.Output = "sum";
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        world.Logistics.Income = 100000;
        world.Update(1d / 30);
        Assert.Equal(world.Tuning[Tune.Power], world.VehicleState.Spec.Power);
    }
}
