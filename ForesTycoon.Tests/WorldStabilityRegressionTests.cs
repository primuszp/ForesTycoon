namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class WorldStabilityRegressionTests
{
    private static RuleProject SceneProject(GameWorld world)
    {
        var project = world.RuleProjectDocument;
        project.World = GameWorld.DefaultScenario(42, 16);
        project.World.Missions.Add(new MissionGoal { Id = "review", Title = "Review", Metric = "world.treeCount", Target = 1 });
        project.World.Springs[0] = 2;
        project.World.Biomes[0].Name = "Custom biome";
        project.World.ElevationZones[0].Name = "Custom zone";
        project.World.GroundMaterials[0].Color = "#123456";
        project.World.ProfileScale = 9;
        project.World.SoilTypes[0].Horizons = new() { new() { Name = "Custom horizon", Depth = .7, Color = "#654321" } };
        return project;
    }

    [Fact]
    public void SaveMustPreserveSceneMissionsAndSprings()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        var project = SceneProject(world);
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0;
        world.Load(saved);
        Assert.True(world.Missions.Count == 1 && world.Environment.Springs?.GetValueOrDefault(0) == 2,
            $"Missions={world.Missions.Count}; spring count={world.Environment.Springs?.Count ?? 0}");
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(project.World.CloneContent()),
            System.Text.Json.JsonSerializer.Serialize(world.RuleProjectDocument.World));
        using var restored = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        saved.Position = 0; restored.Load(saved);
        for (int i = 0; i < 20; i++) {
            world.Update(1d / 30); restored.Update(1d / 30);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(world.CaptureCheckpoint()),
                System.Text.Json.JsonSerializer.Serialize(restored.CaptureCheckpoint()));
        }
    }

    [Fact]
    public void RecaptureMustPreserveSceneMissions()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        var project = SceneProject(world);
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        var captured = WorldLoader.CaptureScenario(world);
        Assert.Single(captured.Missions);
        Assert.Equal("Custom biome", captured.Biomes[0].Name);
        Assert.Equal("Custom zone", captured.ElevationZones[0].Name);
        Assert.Equal("#123456", captured.GroundMaterials[0].Color);
        Assert.Equal(9, captured.ProfileScale);
        Assert.Equal("Custom horizon", captured.SoilTypes[0].Horizons[0].Name);
        var embedded = WorldLoader.ReadInitialState(captured);
        Assert.Null(embedded.Checkpoint.Scene);
        using var loaded = WorldLoader.CreateProject(new RuleProject { World = captured,
            Rules = project.Rules, Behaviors = project.Behaviors, Tuning = project.Tuning });
        Assert.Single(loaded.Missions);
        var second = WorldLoader.CaptureScenario(loaded);
        Assert.Null(WorldLoader.ReadInitialState(second).Checkpoint.Scene);
        Assert.Null(project.World.InitialState);
    }

    [Fact]
    public void LegacySnapshotRecoversSceneFromAppliedJournalAndRepairsDeliveryCounter()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        world.QueueRuleProject(SceneProject(world)); world.ExecutePendingCommands();
        using var saved = new MemoryStream(); world.Save(saved);
        var json = System.Text.Json.Nodes.JsonNode.Parse(saved.ToArray())!;
        json["version"] = 17; json["runtimeRulesVersion"] = "forestycoon-simulation/2026-10-21.1";
        json["checkpoint"]!.AsObject().Remove("scene");
        json["checkpoint"]!["deliveredTimber"] = 123;
        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        world.Load(input);
        Assert.Single(world.Missions); Assert.Equal(2, world.Environment.Springs[0]);
        Assert.Equal("Custom biome", world.RuleProjectDocument.World.Biomes[0].Name);
        Assert.Equal(0, world.DeliveredTimber);
    }

    [Fact]
    public void LoweredMachineCapacityMustNotFaultTheWorld()
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
        using var world = new GameWorld(map);
        map.BuildRoadTilePath(34, 82, RoadPaving.Macadam);
        var machine = new ForestMachine { Id = 1, Kind = ForestMachineKind.Forwarder,
            Path = new[] { 34, 50, 66, 82 }, State = ForestMachineState.Driving,
            Goal = ForestMachineState.Parked, Cargo = 10, TuningSource = world.Logistics };
        world.Logistics.Machines.Add(machine);
        world.QueueTuning(new Dictionary<string, double> { ["ForwarderCapacity"] = 1 });
        world.ExecutePendingCommands();
        for (int i = 0; i < 300; i++) world.Update(1.0 / 30);
        Assert.False(world.IsFaulted);
    }

    [Fact]
    public void FormulaVehicleClockMustStayInSyncWithMachineClock()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        int[] route = Array.Empty<int>();
        for (int id = 0; id < world.Map.Tiles.Count - 1 && route.Length < 2; id++) {
            world.QueueRoadPath(id, id + 1, false); world.ExecutePendingCommands();
            route = world.Map.FindNetworkPath(id, id + 1);
        }
        Assert.True(route.Length >= 2);
        var project = world.RuleProjectDocument;
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == "tuning.VehicleTimeScale");
        graph.Nodes = new() {
            new() { Id = "time", Name = "Time", Operation = BehaviorOperation.NamedValue, Key = "world.time" },
            new() { Id = "base", Name = "Base", Operation = BehaviorOperation.Constant, Value = 4 },
            new() { Id = "sum", Name = "Sum", Operation = BehaviorOperation.Add, A = "time", B = "base" }
        };
        graph.Output = "sum";
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        world.Logistics.Vehicles.SpawnTransit(route);
        world.Update(1d / 30);
        var vehicles = world.Logistics.Vehicles;
        Assert.Equal(world.Tuning[Tune.VehicleTimeScale], vehicles.TimeScale);
        using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0;
        using var restored = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        restored.Load(saved);
        for (int i = 0; i < 40; i++) {
            world.Update(1d / 30); restored.Update(1d / 30);
            Assert.Equal(world.Logistics.Vehicles.TimeScale, restored.Logistics.Vehicles.TimeScale);
            Assert.Equal(world.Vehicles[0].RoutePosition, restored.Vehicles[0].RoutePosition);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(world.CaptureCheckpoint()),
                System.Text.Json.JsonSerializer.Serialize(restored.CaptureCheckpoint()));
        }
    }

    [Fact]
    public void ChangedRuntimeRejectsHistoricalReplayAndNestedSceneSnapshotsWithoutMutatingTheWorld()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        world.QueueRuleProject(SceneProject(world)); world.ExecutePendingCommands();
        using var saved = new MemoryStream(); world.Save(saved);
        string before = System.Text.Json.JsonSerializer.Serialize(world.CaptureCheckpoint());
        var json = System.Text.Json.Nodes.JsonNode.Parse(saved.ToArray())!;
        json["version"] = 17; json["runtimeRulesVersion"] = "forestycoon-simulation/2026-10-21.1";
        json["tick"] = 1;
        using var historical = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Throws<NotSupportedException>(() => world.Load(historical));
        json = System.Text.Json.Nodes.JsonNode.Parse(saved.ToArray())!;
        json["checkpoint"]!["scene"]!["initialState"] = "world-gzip-v1:nested";
        using var nested = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => world.Load(nested));
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(world.CaptureCheckpoint()));
    }
}

public partial class FleetTests
{
    [Fact]
    public void ForwarderMillDeliveryMustCountTowardsMission()
    {
        var s = Build();
        s.Logistics.StackAt(ForestStack)!.Add(20, 600);
        Assert.True(s.Logistics.AssignForwarder(Forwarder(s), s.Logistics.StackAt(ForestStack), Mill));
        Run(s, 800);
        Assert.Equal(20, s.Logistics.Mills[0].Received, 2);
        Assert.Equal(20, s.Cargo.Delivered, 2);
    }

    [Fact]
    public void TruckStackTransferMustNotCountAsMillDelivery()
    {
        var s = Build();
        s.Logistics.StackAt(ForestStack)!.Add(20, 600);
        Assert.True(s.Logistics.AssignTruck(s.Logistics.Trucks[0], s.Logistics.StackAt(ForestStack), RoadStack));
        Run(s, 300);
        Assert.True(s.Logistics.StackAt(RoadStack)!.Volume > 0);
        Assert.Equal(0, s.Cargo.Delivered);
    }

    [Fact]
    public void TruckCargoMustRemainInUnDeliveredInventory()
    {
        var s = Build();
        s.Logistics.StackAt(RoadStack)!.Add(20, 600);
        float initial = s.Logistics.Remaining;
        var truck = s.Logistics.Trucks[0];
        Assert.True(s.Logistics.AssignTruck(truck, s.Logistics.StackAt(RoadStack), Mill));
        for (int i = 0; i < 6000 && (truck.Vehicle?.CargoAmount ?? 0) == 0; i++) Run(s, 1.0 / 30);
        Assert.True(truck.Vehicle!.CargoAmount > 0);
        Assert.Equal(initial, s.Logistics.Remaining, 3);
        var result = new WorkResult<float>(); SimulationWork.Complete(s.Logistics.RemainingWork(result));
        Assert.Equal(initial, result.Value, 3);
    }
}
