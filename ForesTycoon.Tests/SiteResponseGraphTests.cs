using System.Text.Json;
using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class SiteResponseGraphTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static void Constant(RuleProject project, string hook, double value)
    {
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == hook && g.Kind == "site");
        graph.Nodes = new() { new() { Id = "value", Name = "Választott válasz", Operation = BehaviorOperation.Constant, Value = value } };
        graph.Output = "value";
    }
    private static string Snapshot(GameWorld w) => JsonSerializer.Serialize(w.CaptureCheckpoint());

    [Fact]
    public void SceneGenerationUsesAuthoredSiteGraphsInBothWorkPipelines()
    {
        var project = GameWorld.DefaultRuleProject(); project.World = GameWorld.DefaultScenario(42, 16);
        Constant(project, "forest.site", .18); Constant(project, "water.stress", .43);
        var unrelated = project.Behaviors.Graphs.First(g => g.Hook == "machine.pace");
        unrelated.Nodes.Add(new() { Id = "world", Name = "Világ faállománya", Operation = BehaviorOperation.NamedValue, Key = "world.treeCount" });
        unrelated.Output = "world";
        using var expected = WorldLoader.CreateProject(project);
        using var sliced = World(); sliced.RequestScenario(project.World, project);
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (sliced.GenerationPending) {
            sliced.ContinueGeneration(19); Thread.Yield();
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(30), sliced.GenerationStage);
        }
        Assert.Null(sliced.GenerationError);
        Assert.Equal(Snapshot(expected), Snapshot(sliced));
        Assert.All(sliced.RecommendSpecies(0), r => Assert.Equal(.18f, r.Suitability, 6));
        using var saved = new MemoryStream(); sliced.Save(saved); saved.Position = 0; expected.Load(saved);
        Assert.Equal(Snapshot(expected), Snapshot(sliced));
    }

    [Fact]
    public void ResponseInputsHaveDiscoveredObjectAndFieldNames()
    {
        foreach (var hook in EcologyResponseProgram.Hooks)
        foreach (string input in hook.Inputs!) {
            var parts = input.Split('.');
            var relation = GameWorld.RuleObjects.Find(hook.Kind).Relations.Single(r => r.Name == parts[0]);
            var field = GameWorld.RuleObjects.Find(relation.Target).Fields.Single(f => f.Name == parts[1]);
            Assert.NotEmpty(relation.Display); Assert.NotEmpty(field.Display);
        }
    }

    [Fact]
    public void SiteGraphIsSharedByTreesPlantingAndSpeciesRecommendations()
    {
        using var world = World();
        var project = world.RuleProjectDocument;
        Constant(project, "forest.site", .23);
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        int tile = Enumerable.Range(0, world.Map.Tiles.Count).First(id => world.Forest.Habitat.CanSupportForest(id));
        foreach (var species in ForestSpeciesTraits.Playable)
            Assert.Equal(.23f, world.Forest.SiteFitness(species, tile), 6);
        Assert.All(world.RecommendSpecies(tile), r => Assert.Equal(.23f, r.Suitability, 6));
        world.Forest.Harvest(tile, out _);
        world.Forest.Plant(tile, ForestSpecies.Oak);
        Assert.Equal(.23f, world.Forest.SiteFitness(ForestSpecies.Oak, tile), 6);
        // Replace the whole calculation by a different source connection, without editing a formula.
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == "forest.site");
        graph.Nodes = new() { new() { Id = "moisture", Name = "Nedvesség", Operation = BehaviorOperation.Input, Key = "tile.moisture" } };
        graph.Output = "moisture";
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        float moisture = world.Forest.Habitat.GetMoisture(tile);
        Assert.Equal(moisture, world.Forest.SiteFitness(ForestSpecies.Oak, tile), 6);
        Assert.All(world.RecommendSpecies(tile), r => Assert.Equal(moisture, r.Suitability, 6));
    }

    [Fact]
    public void SameWaterGraphDrivesCurrentMonthlyAndTreeResources()
    {
        using var world = World();
        var project = world.RuleProjectDocument;
        Constant(project, "water.stress", .37);
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        for (int i = 0; i < world.Map.Tiles.Count; i++) {
            Assert.Equal(.37, world.Environment.CurrentWaterFactor(i, ForestSpecies.Oak), 10);
            Assert.Equal(.37, world.Environment.GrowthFactor(i, ForestSpecies.Spruce), 10);
        }
        world.Update(1d / 30);
        Assert.All(world.Forest.IndividualTrees.Patches.SelectMany(p => p.Value.Trees.Take(p.Value.Count)),
            tree => Assert.Equal(.37f, tree.Resources.Water, 6));
    }

    [Fact]
    public void SlicedStateGraphsSaveAndReplayTheSamePendingProject()
    {
        using var world = World(); using var sliced = World();
        var project = world.RuleProjectDocument;
        Constant(project, "water.drought", .21); Constant(project, "water.waterlogging", .64);
        Constant(project, "water.stress", .48); Constant(project, "forest.site", .72);
        world.QueueRuleProject(project);
        using var pending = new MemoryStream(); world.Save(pending); pending.Position = 0; sliced.Load(pending);
        world.ExecutePendingCommands();
        do { sliced.ContinueCommands(1); Thread.Yield(); } while (sliced.CommandPending);
        for (int i = 0; i < 60; i++) {
            world.Update(1d / 30);
            while (!sliced.ContinueUpdate(1d / 30, 1)) Thread.Yield();
        }
        Assert.Equal(Snapshot(world), Snapshot(sliced));
        Assert.Equal(.21, world.Environment.Cell(0).Drought, 10);
        Assert.Equal(.64, world.Environment.Waterlogging(0), 10);
        using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0;
        var data = WorldSaveSerializer.Read(saved);
        using var journal = new MemoryStream();
        WorldSaveSerializer.Write(journal, new WorldSaveData { Terrain = data.Terrain, SoilModel = data.SoilModel, Climate = data.Climate,
            Tick = data.Tick, TickRate = data.TickRate, ForestYearSeconds = data.ForestYearSeconds, Commands = data.Commands });
        journal.Position = 0; sliced.Load(journal);
        Assert.Equal(Snapshot(world), Snapshot(sliced));
    }

    [Fact]
    public void PureResponseGraphRejectsNativeFallbackAndWorldRecursion()
    {
        var project = GameWorld.DefaultRuleProject();
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == "forest.site");
        graph.Nodes = new() { new() { Id = "native", Operation = BehaviorOperation.Native } }; graph.Output = "native";
        Assert.Throws<InvalidDataException>(() => GameWorld.ValidateRuleProject(project));
        graph.Nodes[0].Operation = BehaviorOperation.NamedValue; graph.Nodes[0].Key = "world.treeCount";
        Assert.Throws<InvalidDataException>(() => GameWorld.ValidateRuleProject(project));
    }

    [Fact]
    public void ArithmeticMistakeReportsTheGraphWithoutCrashingOrCallingNativeCode()
    {
        using var world = World();
        var project = world.RuleProjectDocument;
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == "forest.site");
        graph.Nodes = new() {
            new() { Id = "one", Name = "Egy", Operation = BehaviorOperation.Constant, Value = 1 },
            new() { Id = "zero", Name = "Nulla", Operation = BehaviorOperation.Constant },
            new() { Id = "bad", Name = "Hibás osztás", Operation = BehaviorOperation.Divide, A = "one", B = "zero" }
        }; graph.Output = "bad";
        world.QueueRuleProject(project); world.ExecutePendingCommands(); world.Update(1d / 30);
        Assert.False(world.IsFaulted);
        Assert.Equal(0, world.Forest.SiteFitness(ForestSpecies.Oak, 0));
        Assert.Contains("Termőhelyi alkalmasság", world.BehaviorError);
        Assert.Contains("Nullával osztás", world.BehaviorError);
    }
}
