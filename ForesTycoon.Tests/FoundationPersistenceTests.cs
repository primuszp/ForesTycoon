using System.Text.Json;
using System.Text.Json.Nodes;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class FoundationPersistenceTests
{
    internal static GameWorld FlatWorld()
    {
        var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        var scene = GameWorld.DefaultScenario(42, 16);
        for (int id = 0; id < 17 * 17; id++) scene.HeightPaint[id] = 4;
        world.StartScenario(scene);
        return world;
    }

    [Fact]
    public void TreeCustomMemoryIsBoundedByLiveTreesAcrossRepeatedReplacementAndSaveLoad()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        world.Forest.Clear();
        var project = world.RuleProjectDocument;
        foreach (var rule in project.Rules) rule.Enabled = false;
        project.Fields.Add(new() { Name = "saját.memory", Scope = "tree", Initial = 3 });
        project.Rules.Add(new() { Id = "remember-tree", Name = "Remember tree", Scope = "tree",
            Effects = { new() { Target = "saját.memory", Mode = "set", Formula = "treeId" } } });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        var trees = world.Forest.IndividualTrees;
        for (int cycle = 0; cycle < 64; cycle++) {
            trees.RemoveTile(0);
            trees.Create(0, new ForestStand(ForestSpecies.Oak, 60, .7f, 1), 0, 42, authoredCount: 1);
            world.RuleBook.Run(world, 0, 0);
            var state = world.RuleBook.Capture();
            Assert.False(state.Values.ContainsKey("saját.memory"));
            Assert.True(state.KeyedValues["saját.memory"].Count <= trees.TreeCount);
        }
        Assert.True(trees.StateSize > 60); Assert.Equal(1, trees.TreeCount);
        double memory = world.RuleBook.ReadCustom(world, "saját.memory", 0);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        restored.Load(save);
        RuleEngine.Scope("tree").Elements(restored);
        Assert.Equal(memory, restored.RuleBook.ReadCustom(restored, "saját.memory", 0));
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(restored.CaptureCheckpoint()));
    }

    [Fact]
    public void SparseMemoryPreparationDoesNotReplaceTheBoundGrowthPatch()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        world.Forest.Clear();
        var store = world.Forest.IndividualTrees;
        foreach (int tile in new[] { 0, 10 })
            store.Create(tile, new ForestStand(ForestSpecies.Oak, 60, .7f, 1), 0, 42, authoredCount: 2);
        var project = world.RuleProjectDocument;
        foreach (var rule in project.Rules) rule.Enabled = false;
        project.Fields.Add(new() { Name = "saját.memory", Scope = "tree", Initial = 0 });
        project.Rules.Add(new() { Id = "growth-memory", Name = "Growth memory", Scope = "tree", Schedule = "growth",
            Effects = { new() { Target = "heightGrowth", Mode = "set", Formula = "treeId" } } });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        SimulationWork.Complete(world.TreeGrowthWork(10, 0));
        var scope = RuleEngine.Scope("tree"); var identity = scope.Fields.Single(f => f.Name == "treeId");
        var rate = scope.Fields.Single(f => f.Name == "heightGrowth");
        foreach (int element in scope.Elements(world)) {
            double key = identity.Read(world, element);
            Assert.Equal(key > 2 ? key : 0, rate.Read(world, element));
        }
    }

    [Fact]
    public void ChangingTheInitialValuePreservesExistingImplicitValuesAndInitializesNewTrees()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        world.Forest.Clear(); var store = world.Forest.IndividualTrees;
        store.Create(0, new ForestStand(ForestSpecies.Oak, 60, .7f, 1), 0, 42, authoredCount: 1);
        var project = world.RuleProjectDocument;
        foreach (var rule in project.Rules) rule.Enabled = false;
        project.Fields.Add(new() { Name = "saját.memory", Scope = "tree", Initial = 3 });
        project.Rules.Add(new() { Id = "keep-memory", Name = "Keep memory", Scope = "tree",
            Effects = { new() { Target = "saját.memory", Mode = "set", Formula = "saját.memory" } } });
        world.QueueRuleProject(project); world.ExecutePendingCommands(); world.RuleBook.Run(world, 0, 0);
        Assert.Empty(world.RuleBook.Capture().KeyedValues["saját.memory"]);
        project.Fields.Single(f => f.Name == "saját.memory").Initial = 5;
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        store.Create(10, new ForestStand(ForestSpecies.Oak, 60, .7f, 1), 0, 42, authoredCount: 1);
        world.RuleBook.Run(world, 0, 0);
        Assert.Equal(3, world.RuleBook.ReadCustom(world, "saját.memory", 0));
        Assert.Equal(5, world.RuleBook.ReadCustom(world, "saját.memory", 1));
    }

    [Fact]
    public void OldTreeArraysMigrateByTreeIdentityAndDoNotKeepHistoricalSlots()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        world.Forest.Clear(); var store = world.Forest.IndividualTrees;
        for (int cycle = 0; cycle < 8; cycle++) {
            store.RemoveTile(0); store.Create(0, new ForestStand(ForestSpecies.Oak, 60, .7f, 1), 0, 42, authoredCount: 2);
        }
        var project = world.RuleProjectDocument;
        project.Fields.Add(new() { Name = "saját.memory", Scope = "tree", Initial = 3 });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        var old = new double[store.StateSize]; Array.Fill(old, 3);
        store.TryGet(0, out var patch);
        foreach (var tree in patch.Trees.Take(patch.Count)) old[checked((int)tree.Id)] = 100 + tree.Id;
        world.RuleBook.Restore(new() { ["saját.memory"] = old });
        SimulationWork.Complete(world.RuleBook.MigrateCustomValuesWork(world));
        Assert.False(world.RuleBook.Capture().Values.ContainsKey("saját.memory"));
        Assert.Equal(2, world.RuleBook.Capture().KeyedValues["saját.memory"].Count);
        var scope = RuleEngine.Scope("tree"); var identity = scope.Fields.Single(f => f.Name == "treeId");
        foreach (int id in scope.Elements(world)) Assert.Equal(100 + identity.Read(world, id), world.RuleBook.ReadCustom(world, "saját.memory", id));
    }

    [Fact]
    public void OldTruckIndexValuesMigrateBeforeTheFleetChanges()
    {
        using var world = FlatWorld(); var map = world.Map;
        map.BuildRoadTilePath(34, 82, RoadPaving.Macadam);
        world.VehicleState.SpawnTransit(new[] { 34, 50, 66, 82 });
        world.VehicleState.SpawnTransit(new[] { 34, 50, 66, 82 });
        var project = world.RuleProjectDocument;
        project.Fields.Add(new() { Name = "saját.memory", Scope = "truck", Initial = 3 });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        using var save = new MemoryStream(); world.Save(save);
        var json = JsonNode.Parse(save.ToArray())!;
        json["version"] = 18; json["runtimeRulesVersion"] = "forestycoon-simulation/2026-10-30.1";
        json["checkpoint"]!["vehicles"]!.AsObject().Remove("physics");
        json["checkpoint"]!["ruleBook"]!["values"]!["saját.memory"] = new JsonArray(10, 11);
        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        world.Load(input);
        var survivor = world.VehicleState.Vehicles[1];
        world.VehicleState.Remove(world.VehicleState.Vehicles[0]);
        Assert.Equal(11, world.RuleBook.ReadCustom(world, "saját.memory", 0));
        Assert.Same(survivor, world.VehicleState.Vehicles[0]);
        Assert.False(world.RuleBook.Capture().Values.ContainsKey("saját.memory"));
    }

    [Fact]
    public void ReadOnlyOrderPreviewCanProgressWhileALogicalTickIsSuspended()
    {
        using var world = FlatWorld(); world.Map.BuildRoadTilePath(34, 82, RoadPaving.Macadam);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var reference = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        reference.Load(save);
        Assert.False(world.ContinueUpdate(1d / 30, 1));
        using var previews = new SlicedPreviewCache<int, int[]>();
        bool ready = false; int[] route = Array.Empty<int>();
        for (int frame = 0; frame < 1000 && !ready; frame++) {
            Assert.True(world.UpdatePending);
            previews.BeginFrame(world.Map.NavigationVersion);
            ready = previews.Request(1, r => world.Logistics.PreviewTruckRouteWork(35, 83, null, r), out route);
            previews.Advance(3);
        }
        Assert.True(ready); Assert.NotNull(route); Assert.True(route.Length >= 2);
        while (!world.ContinueUpdate(1d / 30, 3)) { }
        reference.Update(1d / 30);
        Assert.Equal(JsonSerializer.Serialize(reference.CaptureCheckpoint()), JsonSerializer.Serialize(world.CaptureCheckpoint()));
    }

    [Fact]
    public void StandaloneVehicleCheckpointRestoresItsSampledPhysicalParameters()
    {
        var original = new VehicleSystem { Spec = TruckSpec.Default with { Power = 150000, RollingDirt = .2f } };
        original.Spawn(new[] { 0, 1 });
        var restored = new VehicleSystem(); restored.Restore(original.Capture(), 2);
        Assert.Equal(original.Spec, restored.Spec);
        Assert.Equal(original.Vehicles[0].Mass, restored.Vehicles[0].Mass);
    }

    [Fact]
    public void InvalidSampledPhysicsCannotReplaceTheRunningWorld()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        using var save = new MemoryStream(); world.Save(save);
        string before = JsonSerializer.Serialize(world.CaptureCheckpoint());
        var json = JsonNode.Parse(save.ToArray())!;
        json["checkpoint"]!["vehicles"]!["physics"]!["power"] = -1;
        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => world.Load(input));
        Assert.Equal(before, JsonSerializer.Serialize(world.CaptureCheckpoint()));
    }

    [Fact]
    public void InventoryPublicationWaitsForCompletionAndIncludesTheLastVehicleStep()
    {
        using var world = FlatWorld(); var map = world.Map;
        map.BuildRoadTilePath(34, 210, RoadPaving.Macadam);
        int tile = Enumerable.Range(0, map.Tiles.Count).First(world.Logistics.CanPlaceStack);
        Assert.True(world.Logistics.PlaceStack(tile), world.Logistics.Status); world.Logistics.StackAt(tile).Add(10, 100);
        world.Update(1d / 30);
        Assert.Equal(10, world.Logistics.PublishedRemaining);
        world.Logistics.StackAt(tile).Take(3);
        Assert.Equal(10, world.Logistics.PublishedRemaining);
        Assert.False(world.ContinueUpdate(1d / 30, 1));
        Assert.Equal(10, world.Logistics.PublishedRemaining);
        while (!world.ContinueUpdate(1d / 30, 3)) { }
        Assert.Equal(world.Logistics.Remaining, world.Logistics.PublishedRemaining);
        Assert.Equal(7, world.Logistics.PublishedRemaining);
        using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0;
        world.Load(saved); Assert.Equal(7, world.Logistics.PublishedRemaining);
    }
}
