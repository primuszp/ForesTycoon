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
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == "machine.pace" && g.Kind == "logisticsForwarder");
        graph.Nodes = new() { new() { Id = "speed", Name = "Tempó", Operation = BehaviorOperation.NamedValue, Key = "stock.speed" } }; graph.Output = "speed";
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
        Assert.Equal(2, active.Evaluate(new("machine.pace", "logisticsForwarder", 1, 0)));
    }
    [Fact]
    public void AnObjectConditionOverridesTheDefaultGrowthThroughTheSameRuleBook()
    {
        // Use enough habitat for a live growth subject; a tiny generated map can legitimately have no forest.
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false); world.Update(1d / 30);
        var target = world.CaptureCheckpoint().Ecology.Forest.Trees.Patches.SelectMany(p => p.Trees).First(t => t.AnnualGrowth.Height > 0);
        var project = world.RuleProjectDocument;
        project.Rules.Single(r => r.Id == "forest.growth").Enabled = false;
        world.QueueRuleProject(project); world.ExecutePendingCommands(); world.Update(1d / 30);
        Assert.Equal(default, world.CaptureCheckpoint().Ecology.Forest.Trees.Patches.SelectMany(p => p.Trees).Single(t => t.Id == target.Id).AnnualGrowth);
        var individual = TreeGrowthRule.Default(); individual.Id = "individual";
        foreach (var effect in individual.Effects)
            foreach (var term in effect.Influence.Terms)
                term.Conditions.Add(new() { Field = "treeId", Comparison = "equal", Value = target.Id });
        project.Rules.Add(individual);
        world.QueueRuleProject(project); world.ExecutePendingCommands(); world.Update(1d / 30);
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
    public void FileLinkRejectsProjectWithUnreadableAlgorithmBundleAsAWhole()
    {
        string directory = Path.Combine(Path.GetTempPath(), "forestycoon-stale-project-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try {
            using var world = World();
            string original = world.RuleProjectDocument.Name;
            string file = Path.Combine(directory, "project.json");
            var project = Project(); project.Name = "must not install";
            project.Algorithms = new() { Revision = new string('a', 64), BundlePath = Path.Combine(directory, "missing.json"), WorkspaceRoot = directory };
            RuleProject.WriteAtomic(file, project.ToJson());
            var link = new RuleProjectLink(file, bootstrapAlgorithms: false);
            link.Poll(world, 1);
            Assert.Contains("nem lett alkalmazva", link.Error);
            Assert.Equal(0, world.ExecutePendingCommands());
            Assert.Equal(original, world.RuleProjectDocument.Name);
        } finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void FileLinkWaitsForUnstartedInputWithoutExecutingItOnThePollingFrame()
    {
        string directory = Path.Combine(Path.GetTempPath(), "forestycoon-project-pending-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try {
            using var world = World(); string file = Path.Combine(directory, "project.json");
            var project = Project(); project.Name = "After input"; RuleProject.WriteAtomic(file, project.ToJson());
            var link = new RuleProjectLink(file, bootstrapAlgorithms: false);
            world.QueueWeather(WeatherPreset.Storm, 30, 20);
            link.Poll(world, 1);
            Assert.Equal(1, world.CaptureCheckpoint().PendingCommands);
            Assert.NotEqual(project.Name, world.RuleProjectDocument.Name);
            do { world.ContinueCommands(1); } while (world.CommandPending);
            Assert.Equal(WeatherPreset.Storm, world.Environment.Preset);
            link.Poll(world, 1);
            do { world.ContinueCommands(1); } while (world.CommandPending);
            Assert.Equal(project.Name, world.RuleProjectDocument.Name);
        } finally { Directory.Delete(directory, true); }
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
