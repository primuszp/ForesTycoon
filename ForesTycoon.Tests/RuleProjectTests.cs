using System.Text;
using ForesTycoon;
using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

public class RuleProjectTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false);
    private static RuleProject Project() {
        var project = GameWorld.DefaultRuleProject();
        project.Dynamics.Stocks.Add(new() { Id = "speed", Initial = 1, Maximum = 10 });
        project.Dynamics.Flows.Add(DynamicsRuntimeTests.Flow("increase", "", "speed", 1));
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == "machine.pace" && g.Kind == "forwarder");
        graph.Nodes[0].Operation = BehaviorOperation.NamedValue; graph.Nodes[0].Key = "stock.speed";
        return project;
    }
    [Fact]
    public void DefaultWorldHasEditableGraphForEveryRegisteredProcess()
    {
        using var world = World();
        Assert.Equal(WorldBehaviorPolicy.Hooks.Length, world.BehaviorDocument.Graphs.Count);
        foreach (var hook in WorldBehaviorPolicy.Hooks)
            Assert.Contains(world.BehaviorDocument.Graphs, g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null);
    }
    [Fact]
    public void ProjectSwitchIsAtomicAndInvalidDraftDoesNotQueueAnything()
    {
        using var world = World(); var before = world.RuleProjectDocument.ToJson();
        var project = Project(); project.Tuning["DieselPrice"] = -1;
        Assert.ThrowsAny<Exception>(() => world.QueueRuleProject(project));
        Assert.Equal(0, world.ExecutePendingCommands()); Assert.Equal(before, world.RuleProjectDocument.ToJson());
        project.Tuning.Clear(); world.QueueRuleProject(project);
        Assert.Equal(before, world.RuleProjectDocument.ToJson()); Assert.Equal(1, world.ExecutePendingCommands());
        world.Update(1); Assert.Equal(2, world.DynamicsValues["speed"]);
        var active = new WorldBehaviorPolicy(world.BehaviorDocument, DynamicsRuntime.Keys(project.Dynamics, GameWorld.RuleWorldFields), world.ReadRuleValue);
        Assert.Equal(2, active.Evaluate(new("machine.pace", "forwarder", 1, 0)));
    }
    [Fact]
    public void NativeObjectOverrideOfCustomTreeTypeRefreshesActualGrowth()
    {
        using var world = World();
        var target = world.CaptureCheckpoint().Ecology.Forest.Trees.Patches.SelectMany(p => p.Trees).First(t => t.AnnualGrowth.Height > 0);
        var project = GameWorld.DefaultRuleProject();
        var growth = project.Behaviors.Graphs.Single(g => g.Hook == "forest.growth");
        growth.Nodes[0].Operation = BehaviorOperation.Constant; growth.Nodes[0].Value = 0;
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        Assert.Equal(default, world.CaptureCheckpoint().Ecology.Forest.Trees.Patches.SelectMany(p => p.Trees).Single(t => t.Id == target.Id).AnnualGrowth);
        project.Behaviors.Graphs.Add(new() { Id = "individual", Hook = "forest.growth", Kind = "tree", ObjectId = target.Id });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        Assert.True(world.CaptureCheckpoint().Ecology.Forest.Trees.Patches.SelectMany(p => p.Trees).Single(t => t.Id == target.Id).AnnualGrowth.Height > 0);
    }
    [Fact]
    public void CheckpointPendingCommandsJournalAndAdoptionPreserveFeedback()
    {
        using var world = World(); using var restored = World();
        world.QueueRuleProject(Project()); world.ExecutePendingCommands();
        for (int i = 0; i < 30; i++) world.Update(1d / 30);
        var edited = Project(); edited.Name = "next"; world.QueueRuleProject(edited);
        using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0; restored.Load(saved);
        Assert.Equal(world.DynamicsValues["speed"], restored.DynamicsValues["speed"]);
        Assert.NotEqual("next", restored.RuleProjectDocument.Name);
        world.ExecutePendingCommands(); restored.ExecutePendingCommands();
        for (int i = 0; i < 30; i++) { world.Update(1d / 30); restored.Update(1d / 30); }
        Assert.Equal(world.DynamicsValues["speed"], restored.DynamicsValues["speed"]);
        Assert.Equal(world.RuleProjectDocument.ToJson(), restored.RuleProjectDocument.ToJson());
        saved.Position = 0; var data = WorldSaveSerializer.Read(saved);
        using var journal = new MemoryStream(); WorldSaveSerializer.Write(journal, new WorldSaveData {
            Tick = data.Tick, TickRate = data.TickRate, Terrain = data.Terrain, ForestYearSeconds = data.ForestYearSeconds,
            SoilModel = data.SoilModel, Climate = data.Climate, Commands = data.Commands });
        journal.Position = 0; restored.Load(journal);
        Assert.Equal(2, restored.DynamicsValues["speed"], 12);
        // A journal without a checkpoint has no pending cursor; terminal-tick commands execute during replay.
        Assert.Equal(0, restored.ExecutePendingCommands()); Assert.Equal("next", restored.RuleProjectDocument.Name);
    }
    [Fact]
    public void FileLinkAppliesChangesOnceAndKeepsLastValidRuntimeOnInvalidFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "forestycoon-project-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try {
            using var world = World(); string file = Path.Combine(directory, "project.json");
            var link = new RuleProjectLink(file); RuleProject.WriteAtomic(file, Project().ToJson());
            link.Poll(world, 1); Assert.Equal(1, world.ExecutePendingCommands());
            link.Poll(world, 1); Assert.Equal(0, world.ExecutePendingCommands());
            File.WriteAllText(file, "{ broken"); link.Poll(world, 1);
            Assert.NotNull(link.Error); Assert.Equal(0, world.ExecutePendingCommands()); world.Update(1); Assert.Equal(2, world.DynamicsValues["speed"]);
            var fixedProject = Project(); fixedProject.Name = "fixed"; RuleProject.WriteAtomic(file, fixedProject.ToJson());
            link.Poll(world, 1); world.ExecutePendingCommands(); Assert.Null(link.Error); Assert.Equal("fixed", world.RuleProjectDocument.Name);
            var catalog = RuleWorldCatalog.FromJson(File.ReadAllText(file + ".world.json"));
            Assert.Contains(catalog.Objects, o => o.Kind == "tile" && o.Id == 0); Assert.Equal(WorldBehaviorPolicy.Hooks.Length, catalog.Hooks.Count);
        } finally { Directory.Delete(directory, true); }
    }
}
