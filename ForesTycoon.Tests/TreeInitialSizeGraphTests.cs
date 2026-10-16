using System.Text.Json;
using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class TreeInitialSizeGraphTests
{
    private sealed class Habitat : IForestHabitat {
        public int TileCount => 3; public int Seed => 42;
        public bool CanSupportForest(int id) => true;
        public float GetMoisture(int id) => .7f;
        public float GetNormalizedElevation(int id) => .4f;
        public int GetAdjacentTileIds(int id, Span<int> target) {
            int n = 0; if (id > 0) target[n++] = id - 1; if (id < 2) target[n++] = id + 1; return n;
        }
    }
    private static void Constant(RuleProject p, string hook, double value) {
        var g = p.Behaviors.Graphs.Single(g => g.Hook == hook);
        g.Nodes = new() { new() { Id = "value", Name = "Chosen dimension", Operation = BehaviorOperation.Constant, Value = value } }; g.Output = "value";
    }
    private static void Source(RuleProject p, string hook, string field) {
        var g = p.Behaviors.Graphs.Single(g => g.Hook == hook);
        g.Nodes = new() { new() { Id = "source", Name = "Chosen field", Operation = BehaviorOperation.Input, Key = field } }; g.Output = "source";
    }
    private static RuleProject Project() {
        var p = GameWorld.DefaultRuleProject();
        Constant(p, "forest.initialHeight", 2); Constant(p, "forest.initialDiameter", .2);
        Source(p, "forest.initialCrown", "initial.diameter"); return p;
    }
    private static IEnumerable<ForestTree> Trees(ForestSystem f) => f.IndividualTrees.Patches.SelectMany(p => p.Value.Trees.Take(p.Value.Count));
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), forestYearSeconds: 120, enableRendering: false);
    private static string State(GameWorld w) => JsonSerializer.Serialize(w.CaptureCheckpoint());

    [Fact]
    public void AllCreationKindsUseTheActualProjectAndCrownReadsAuthoredDimensions() {
        var p = Project(); var rules = EcologyResponseProgram.FromModel(p.Behaviors);
        var f = new ForestSystem(new Habitat(), responseRules: rules);
        f.PaintInitialForest(new Dictionary<int, ForestSpecies> { [0] = ForestSpecies.Oak }); Assert.NotEmpty(Trees(f));
        Assert.All(Trees(f), t => Assert.Equal(new ForestTreeDimensions(.2f, 2, .2f), t.Dimensions));
        SimulationWork.Complete(f.PaintInitialForestWork(new Dictionary<int, ForestSpecies> { [0] = ForestSpecies.Beech }, preserveUnpainted: true));
        Assert.All(Trees(f), t => Assert.Equal(new ForestTreeDimensions(.2f, 2, .2f), t.Dimensions));
        f.Clear(); f.Plant(0, ForestSpecies.Oak);
        Constant(p, "forest.seedSupply", 1); Constant(p, "forest.seedSelection", 1); Constant(p, "forest.germination", 1);
        f.ResponseRules = EcologyResponseProgram.FromModel(p.Behaviors); f.Update(ForestSystem.DefaultSecondsPerYear / 12);
        Assert.True(f.IndividualTrees.TryGet(1, out var seedlings));
        Assert.All(seedlings.Trees.Take(seedlings.Count), t => Assert.Equal(new ForestTreeDimensions(.2f, 2, .2f), t.Dimensions));
    }
    [Fact]
    public void HeightFeedsDiameterAndCrownWithoutNativeRecomputation() {
        var p = Project(); Source(p, "forest.initialDiameter", "initial.height");
        var rules = EcologyResponseProgram.FromModel(p.Behaviors);
        Assert.Equal(new ForestTreeDimensions(2, 2, 2), ForestTreeGrowth.Initial(ForestSpecies.Oak, 30, 1, rules));
        Constant(p, "forest.initialHeight", 0); Constant(p, "forest.initialDiameter", 0); Constant(p, "forest.initialCrown", 0);
        Assert.Equal(default, ForestTreeGrowth.Initial(ForestSpecies.Oak, 30, 1, EcologyResponseProgram.FromModel(p.Behaviors)));
    }
    [Fact]
    public void PerTreeCreationSlicesAndDeterministicRandomRemainIdenticalAtMaximumRecruitCount() {
        var p = Project(); Source(p, "forest.initialHeight", "tree.random");
        var rules = EcologyResponseProgram.FromModel(p.Behaviors); var whole = new ForestTreeStore(); var sliced = new ForestTreeStore();
        var stand = new ForestStand(ForestSpecies.Oak, .1f, .01f, 1);
        whole.Create(0, stand, 0, 42, authoredCount: 128, responses: rules);
        int count = 0;
        foreach (int unit in sliced.CreateWork(0, stand, 0, 42, authoredCount: 128, responses: rules)) { Assert.Equal(1, unit); count++; }
        Assert.True(count >= 128); whole.TryGet(0, out var a); sliced.TryGet(0, out var b);
        Assert.Equal(128, b.Count); Assert.Equal(a.Trees.Take(a.Count), b.Trees.Take(b.Count));
        Assert.True(b.Trees.Take(b.Count).Select(t => t.Dimensions.Height).Distinct().Count() > 100);
        Assert.All(b.Trees.Take(b.Count), t => Assert.InRange(t.Dimensions.Height, 0, 1));
        var restored = new ForestTreeStore(); restored.Restore(sliced.Capture(), 3, 0); restored.TryGet(0, out var c);
        Assert.Equal(b.Trees.Take(b.Count), c.Trees.Take(c.Count));
    }
    [Fact]
    public void PlantedInputCanChooseDifferentInitialSizes() {
        var p = Project(); Source(p, "forest.initialHeight", "tree.planted");
        var f = new ForestSystem(new Habitat(), responseRules: EcologyResponseProgram.FromModel(p.Behaviors));
        f.PaintInitialForest(new Dictionary<int, ForestSpecies> { [0] = ForestSpecies.Oak });
        Assert.NotEmpty(Trees(f)); Assert.All(Trees(f), t => Assert.Equal(0, t.Dimensions.Height));
        f.Clear(); f.Plant(0, ForestSpecies.Oak); Assert.All(Trees(f), t => Assert.Equal(1, t.Dimensions.Height));
    }
    [Fact]
    public void AuthoredGenerationMatchesTheSlicedGenerationPipeline() {
        var p = Project(); p.World = GameWorld.DefaultScenario(42, 16);
        using var whole = WorldLoader.CreateProject(p); using var sliced = World(); sliced.RequestScenario(p.World, p);
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (sliced.GenerationPending) { sliced.ContinueGeneration(7); Thread.Yield(); Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(30), sliced.GenerationStage); }
        Assert.Null(sliced.GenerationError); Assert.Equal(State(whole), State(sliced)); Assert.NotEmpty(Trees(sliced.Forest));
        Assert.All(Trees(sliced.Forest), t => Assert.Equal(new ForestTreeDimensions(.2f, 2, .2f), t.Dimensions));
    }
    [Fact]
    public void ProjectEditsOnlyAffectNewTreesAndSlicedAreaPlantingSaveReplayStayExact() {
        using var whole = World(); using var sliced = World(); var existing = Trees(whole.Forest).First();
        var p = Project(); p.Rules = RuleEngine.StarterRules(); foreach (var r in p.Rules) r.Enabled = false;
        foreach (var w in new[] { whole, sliced }) { w.QueueRuleProject(p); w.ExecutePendingCommands(); }
        Assert.Equal(existing.Dimensions, Trees(whole.Forest).Single(t => t.Id == existing.Id).Dimensions);
        int tile = Enumerable.Range(0, whole.Map.Tiles.Count).First(i => whole.Forest.Habitat.CanSupportForest(i));
        foreach (var w in new[] { whole, sliced }) { w.QueueHarvestForest(tile); w.QueuePlantForestArea(tile, tile, ForestSpecies.Oak); }
        using var during = new MemoryStream(); bool saved = false;
        whole.ExecutePendingCommands(); Assert.Equal(0, sliced.ContinueCommands(1));
        sliced.AtTickBoundary(() => { sliced.Save(during); saved = true; }); Assert.False(saved);
        do { sliced.ContinueCommands(3); } while (sliced.CommandPending);
        Assert.True(saved); Assert.Equal(State(whole), State(sliced));
        Assert.True(whole.Forest.IndividualTrees.TryGet(tile, out var patch)); Assert.Equal(36, patch.Count);
        Assert.All(patch.Trees.Take(patch.Count), t => Assert.Equal(new ForestTreeDimensions(.2f, 2, .2f), t.Dimensions));
        during.Position = 0; sliced.Load(during); Assert.Equal(State(whole), State(sliced));
        for (int tick = 0; tick < 310; tick++) { whole.Update(1d / 30); while (!sliced.ContinueUpdate(1d / 30, 3)) { } }
        Assert.Equal(State(whole), State(sliced));
        using var save = new MemoryStream(); whole.Save(save); save.Position = 0; var data = WorldSaveSerializer.Read(save);
        using var journal = new MemoryStream(); WorldSaveSerializer.Write(journal, new WorldSaveData { Terrain = data.Terrain, SoilModel = data.SoilModel,
            Climate = data.Climate, Tick = data.Tick, TickRate = data.TickRate, ForestYearSeconds = data.ForestYearSeconds, Commands = data.Commands });
        journal.Position = 0; sliced.Load(journal); Assert.Equal(State(whole), State(sliced));
    }
    [Fact]
    public void InitialHeightCannotReadItsOwnOrFutureOutput() {
        var p = Project(); Source(p, "forest.initialHeight", "initial.height");
        Assert.Throws<InvalidDataException>(() => EcologyResponseProgram.FromModel(p.Behaviors));
    }
}
