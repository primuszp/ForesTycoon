using ForesTycoon.Editor;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class SimulationPolicyTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static void Constant(RuleProject project, string hook, double value)
    {
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == hook);
        graph.Nodes = new() { new() { Id = "custom", Name = "Saját érték", Operation = BehaviorOperation.Constant, Value = value } };
        graph.Output = "custom";
    }

    [Fact]
    public void PreviousRuntimeSnapshotMigratesPoliciesBeforeContinuing()
    {
        using var world = World(); using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0;
        var legacy = JsonNode.Parse(saved)!;
        legacy["runtimeRulesVersion"] = "forestycoon-simulation/2026-10-19.3";
        legacy["checkpoint"]!["ruleBook"]!["projectVersion"] = 4;
        var graphs = legacy["checkpoint"]!["behaviors"]!["graphs"]!.AsArray();
        foreach (var graph in graphs.Where(g => g!["hook"]!.GetValue<string>() is "wildlife.timeScale" or "trail.drivingCondition").ToArray()) graphs.Remove(graph);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(legacy.ToJsonString()));
        world.Load(stream);
        Assert.Equal(RuleProject.CurrentVersion, world.CaptureCheckpoint().RuleBook.ProjectVersion);
        Assert.Contains(world.BehaviorDocument.Graphs, g => g.Hook == "wildlife.timeScale");
        Assert.Contains(world.BehaviorDocument.Graphs, g => g.Hook == "trail.drivingCondition");
        world.Update(1d / 30);
    }

    [Fact]
    public void WildlifeClockUsesTheAuthoredGraphAndSurvivesEditorReopenAndSave()
    {
        using var world = World(); var project = world.RuleProjectDocument;
        project.Rules.RemoveAll(r => r.Schedule == "population" || r.Id == "wildlife.reconcile");
        var clock = project.Behaviors.Graphs.Single(g => g.Hook == "wildlife.timeScale");
        clock.Nodes = new() { new() { Id = "count", Name = "Állományfüggő idő", Operation = BehaviorOperation.Input, Key = "animals.count" } };
        clock.Output = "count";
        var reopened = EditorProjectLoader.Prepare(RuleProject.FromJson(project.ToJson()));
        world.QueueRuleProject(reopened); world.ExecutePendingCommands();
        world.Wildlife.Animals.Add(new WildlifeSystem.Deer { Id = 1, TileId = 5, TargetTile = 5 });
        world.Wildlife.NextAnimalId = 2;
        world.Update(.1); Assert.Equal(.1, world.Wildlife.Animals[0].Age, 8);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = World(); restored.Load(save);
        world.Update(.1); restored.Update(.1);
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(restored.CaptureCheckpoint()));
        project = restored.RuleProjectDocument; project.Behaviors.Graphs.RemoveAll(g => g.Hook == "wildlife.timeScale");
        restored.QueueRuleProject(project); restored.ExecutePendingCommands();
        double age = restored.Wildlife.Animals[0].Age; restored.Update(.1);
        Assert.Equal(age, restored.Wildlife.Animals[0].Age);
    }

    [Fact]
    public void DirtDrivingConditionReadsTheAuthoredGraphWithoutANativeFallback()
    {
        using var world = World();
        int tile = Enumerable.Range(0, world.Map.Tiles.Count).First(id => !world.Map.IsRoadTile(id));
        var project = world.RuleProjectDocument;
        Constant(project, "trail.drivingCondition", .73);
        world.QueueRuleProject(EditorProjectLoader.Prepare(RuleProject.FromJson(project.ToJson()))); world.ExecutePendingCommands();
        Assert.Equal(RoadSurface.Dirt, world.DrivingSurface(tile).Surface);
        Assert.Equal(.73f, world.DrivingSurface(tile).Condition);
        project = world.RuleProjectDocument; project.Behaviors.Graphs.RemoveAll(g => g.Hook == "trail.drivingCondition");
        world.QueueRuleProject(project); world.ExecutePendingCommands(); Assert.Equal(0, world.DrivingSurface(tile).Condition);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = World(); restored.Load(save); Assert.Equal(0, restored.DrivingSurface(tile).Condition);
    }

    [Fact]
    public void VersionFourPoliciesMigrateOnceAndPreserveCustomGraphs()
    {
        var source = RuleProject.CreateDefault(); source.Version = 4;
        source.Behaviors.Graphs.RemoveAll(g => g.Hook is "wildlife.timeScale" or "trail.drivingCondition");
        var migrated = EditorProjectLoader.Prepare(source);
        Assert.Equal(RuleProject.CurrentVersion, migrated.Version);
        var gameplay = new GameplayProgram(migrated.Behaviors);
        Assert.Equal(4, gameplay.Evaluate("wildlife.timeScale", 0, 10));
        Assert.Equal(.075, gameplay.Evaluate("trail.drivingCondition", .25, .5), 8);
        Constant(migrated, "wildlife.timeScale", 7); migrated.Version = 4;
        Assert.Equal(7, new GameplayProgram(EditorProjectLoader.Prepare(migrated).Behaviors).Evaluate("wildlife.timeScale", 0, 1));
        migrated.Version = RuleProject.CurrentVersion;
        migrated.Behaviors.Graphs.RemoveAll(g => g.Hook is "wildlife.timeScale" or "trail.drivingCondition");
        Assert.DoesNotContain(EditorProjectLoader.Prepare(migrated).Behaviors.Graphs, g => g.Hook is "wildlife.timeScale" or "trail.drivingCondition");
    }
}
