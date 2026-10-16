using System.Text.Json;
using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class ForestRegenerationGraphTests
{
    private sealed class Habitat : IForestHabitat
    {
        public int TileCount => 4;
        public int Seed => 42;
        public bool CanSupportForest(int id) => true;
        public float GetMoisture(int id) => id == 1 ? .8f : .2f;
        public float GetNormalizedElevation(int id) => .4f;
        public int GetAdjacentTileIds(int id, Span<int> target) {
            int count = 0; if (id > 0) target[count++] = id - 1; if (id < 3) target[count++] = id + 1; return count;
        }
    }
    private static void Constant(RuleProject p, string hook, double value) {
        var g = p.Behaviors.Graphs.Single(g => g.Hook == hook);
        g.Nodes = new() { new() { Id = "value", Name = "Chosen effect", Operation = BehaviorOperation.Constant, Value = value } }; g.Output = "value";
    }
    private static RuleProject Project() {
        var p = GameWorld.DefaultRuleProject();
        Constant(p, "forest.seedSupply", 1); Constant(p, "forest.seedSelection", 1);
        Constant(p, "forest.germination", 1); Constant(p, "forest.seedlingHealth", .72);
        return p;
    }
    private static ForestSystem Forest(RuleProject p) {
        var forest = new ForestSystem(new Habitat(), secondsPerYear: 12, responseRules: EcologyResponseProgram.FromModel(p.Behaviors));
        forest.Clear(); Assert.Equal(ForestryActionResult.Planted, forest.Plant(0, ForestSpecies.Oak)); return forest;
    }

    [Fact]
    public void YoungSourcesCanSeedWhenTheAuthorRemovesMaturityThresholdAndNewbornsCannotCascade() {
        var forest = Forest(Project()); forest.Update(1);
        Assert.True(forest.TryGetStand(1, out var seedling)); Assert.Equal(ForestSpecies.Oak, seedling.Species);
        Assert.Equal(.72f, seedling.Health, 5);
        Assert.False(forest.TryGetStand(2, out _)); Assert.False(forest.TryGetStand(3, out _));
        forest.Update(1); Assert.True(forest.TryGetStand(2, out _)); Assert.False(forest.TryGetStand(3, out _));
    }
    [Theory]
    [InlineData("forest.seedSupply")]
    [InlineData("forest.seedSelection")]
    [InlineData("forest.germination")]
    [InlineData("forest.seedlingCount")]
    [InlineData("forest.seedlingHealth")]
    public void AuthorCanBlockEachStageWithoutAHiddenNativeFallback(string hook) {
        var p = Project(); Constant(p, hook, 0); var forest = Forest(p); forest.Update(1);
        Assert.False(forest.TryGetStand(1, out _));
    }
    [Fact]
    public void MoistureCanDriveHealthAndDistanceCanBlockSeedSupply() {
        var p = Project(); var g = p.Behaviors.Graphs.Single(g => g.Hook == "forest.seedlingHealth");
        g.Nodes = new() { new() { Id = "moisture", Name = "moisture", Operation = BehaviorOperation.Input, Key = "tile.moisture" } }; g.Output = "moisture";
        var forest = Forest(p); forest.Update(1); Assert.True(forest.TryGetStand(1, out var seedling)); Assert.Equal(.8f, seedling.Health, 5);
        g = p.Behaviors.Graphs.Single(g => g.Hook == "forest.seedSupply");
        g.Nodes = new() {
            new() { Id = "distance", Name = "distance", Operation = BehaviorOperation.Input, Key = "source.distance" },
            new() { Id = "limit", Name = "limit", Operation = BehaviorOperation.Constant, Value = 5 },
            new() { Id = "near", Name = "near", Operation = BehaviorOperation.Less, A = "distance", B = "limit" }
        }; g.Output = "near";
        forest = Forest(p); forest.Update(1); Assert.False(forest.TryGetStand(1, out _));
    }
    [Fact]
    public void AuthoredSeedlingAgeControlsCreatedDimensions() {
        var young = Forest(Project()); var p = Project(); Constant(p, "forest.seedlingAge", 4); var older = Forest(p);
        young.Update(1); older.Update(1); young.IndividualTrees.TryGet(1, out var a); older.IndividualTrees.TryGet(1, out var b);
        Assert.True(b.Trees[0].Dimensions.Height > a.Trees[0].Dimensions.Height);
    }
    [Fact]
    public void AuthoredCountAndSpeciesScoreControlActualRecruits() {
        var p = Project(); Constant(p, "forest.seedlingCount", 7);
        var g = p.Behaviors.Graphs.Single(g => g.Hook == "forest.seedSelection");
        g.Nodes = new() { new() { Id = "shade", Name = "Shade tolerance", Operation = BehaviorOperation.Input, Key = "species.shadeTolerance" } }; g.Output = "shade";
        var forest = Forest(p); forest.Clear(); forest.Plant(0, ForestSpecies.Birch); forest.Plant(2, ForestSpecies.Oak);
        forest.Update(1); Assert.True(forest.IndividualTrees.TryGet(1, out var patch));
        Assert.Equal(7, patch.Count); Assert.All(patch.Trees.Take(patch.Count), t => Assert.Equal(ForestSpecies.Oak, t.Species));
    }
    [Fact]
    public void DiagnosticCrowdingUsesTheSameAuthoredGraph() {
        var p = Project(); Constant(p, "forest.seedCrowding", .23); var forest = Forest(p);
        Assert.Equal(.23f, forest.GetCrowding(1), 6);
    }
    [Fact]
    public void SlicingSaveRestoreAndJournalReplayPreserveActualRegeneration() {
        GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), forestYearSeconds: 120, enableRendering: false);
        string State(GameWorld w) => JsonSerializer.Serialize(w.CaptureCheckpoint());
        using var whole = World(); using var sliced = World(); var p = Project();
        // Keep the established trees alive and suppress unrelated growth while forcing reproducible recruitment.
        foreach (var r in p.Rules.Where(r => r.Schedule == "month")) r.Enabled = false;
        whole.QueueRuleProject(p); sliced.QueueRuleProject(p);
        whole.ExecutePendingCommands(); do { sliced.ContinueCommands(3); } while (sliced.CommandPending);
        var ids = whole.Forest.IndividualTrees.Patches.SelectMany(p => p.Value.Trees.Take(p.Value.Count)).Select(t => t.Id).ToHashSet();
        for (int tick = 0; tick < 605; tick++) {
            whole.Update(1d / 30);
            if (tick == 299) {
                using var boundary = new MemoryStream(); bool savedAtBoundary = false;
                Assert.False(sliced.ContinueUpdate(1d / 30, 1));
                sliced.AtTickBoundary(() => { sliced.Save(boundary); savedAtBoundary = true; });
                Assert.False(savedAtBoundary); Assert.Equal(0, boundary.Length);
                while (!sliced.ContinueUpdate(1d / 30, 3)) { Assert.False(savedAtBoundary); Assert.InRange(sliced.LastUpdateWorkUnits, 1, 3); }
                Assert.True(savedAtBoundary); boundary.Position = 0; sliced.Load(boundary);
            }
            else while (!sliced.ContinueUpdate(1d / 30, 3)) { Assert.InRange(sliced.LastUpdateWorkUnits, 1, 3); }
            if (tick == 335) {
                using var mid = new MemoryStream(); whole.Save(mid); mid.Position = 0; sliced.Load(mid);
            }
        }
        Assert.Contains(whole.Forest.IndividualTrees.Patches.SelectMany(p => p.Value.Trees.Take(p.Value.Count)), t => !ids.Contains(t.Id));
        Assert.Equal(State(whole), State(sliced));
        using var saved = new MemoryStream(); whole.Save(saved); saved.Position = 0; var data = WorldSaveSerializer.Read(saved);
        using var journal = new MemoryStream();
        WorldSaveSerializer.Write(journal, new WorldSaveData { Terrain = data.Terrain, SoilModel = data.SoilModel, Climate = data.Climate,
            Tick = data.Tick, TickRate = data.TickRate, ForestYearSeconds = data.ForestYearSeconds, Commands = data.Commands });
        journal.Position = 0; sliced.Load(journal); Assert.Equal(State(whole), State(sliced));
    }
}
