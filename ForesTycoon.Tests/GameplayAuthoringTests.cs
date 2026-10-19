namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class GameplayAuthoringTests
{
    private sealed class Habitat : IForestHabitat
    {
        public int TileCount => 4;
        public int Seed => 42;
        public bool CanSupportForest(int id) => true;
        public float GetMoisture(int id) => .6f;
        public float GetNormalizedElevation(int id) => .4f;
        public int GetAdjacentTileIds(int id, Span<int> target) => 0;
    }

    private static void Constant(RuleProject p, string hook, double value)
    {
        var g = p.Behaviors.Graphs.Single(g => g.Hook == hook);
        g.Nodes = new() { new() { Id = "chosen", Name = "Saját szabály", Operation = BehaviorOperation.Constant, Value = value } };
        g.Output = "chosen";
    }

    [Fact]
    public void EditorDocumentOwnsEveryDefaultBindingAndEachCopyIsIndependent()
    {
        var p = RuleProject.CreateDefault(); var other = RuleProject.CreateDefault();
        Assert.Equal(4, p.Version);
        Assert.NotEmpty(p.Rules);
        Assert.Equal(WorldBehaviorPolicy.Hooks.Select(h => (h.Id, h.Kind)).Order(), p.Behaviors.Graphs.Select(g => (g.Hook, g.Kind)).Order());
        p.Rules.Clear(); p.Behaviors.Graphs.Clear();
        Assert.NotEmpty(other.Rules); Assert.NotEmpty(other.Behaviors.Graphs);
        GameWorld.ValidateRuleProject(other);
    }

    [Fact]
    public void EmptyAuthoredBookStaysEmptyThroughNormalizationApplicationAndSave()
    {
        var p = RuleProject.CreateDefault(); p.Rules.Clear();
        var normalized = GameWorld.NormalizeRuleProject(p);
        Assert.Empty(normalized.Rules); Assert.Empty(normalized.RemovedStarters);
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false);
        world.QueueRuleProject(p); world.ExecutePendingCommands();
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = new GameWorld(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false);
        restored.Load(save);
        Assert.Empty(restored.RuleProjectDocument.Rules);
        Assert.Equal(normalized.ContentRevision(), restored.RuleProjectDocument.ContentRevision());
    }

    [Fact]
    public void LegacyBookIsMigratedOnceAndSubsequentDeletionNeedsNoTombstone()
    {
        var p = RuleProject.CreateDefault(); p.Version = 1; p.Rules.Clear();
        p = GameWorld.NormalizeRuleProject(p);
        Assert.Equal(4, p.Version); Assert.NotEmpty(p.Rules);
        p.Rules.Clear();
        Assert.Empty(GameWorld.NormalizeRuleProject(p).Rules);
    }

    [Fact]
    public void AuthoredInitializationControlsEveryNaturalStandAndPlantingCanBeDenied()
    {
        var p = RuleProject.CreateDefault();
        Constant(p, "forest.initialAllow", 1); Constant(p, "forest.initialSpecies", (int)ForestSpecies.Oak);
        Constant(p, "forest.initialAge", 30); Constant(p, "forest.initialHealth", .7);
        var forest = new ForestSystem(new Habitat(), responseRules: EcologyResponseProgram.FromModel(p.Behaviors));
        for (int i = 0; i < 4; i++) {
            Assert.True(forest.TryGetStand(i, out var stand)); Assert.Equal(ForestSpecies.Oak, stand.Species);
            Assert.InRange(stand.AgeYears, 25, 35); // Individual creation adds the authored stand's seeded age variation.
        }
        forest.Clear(); Constant(p, "forest.plantAllow", 0);
        forest.ResponseRules = EcologyResponseProgram.FromModel(p.Behaviors);
        Assert.Equal(ForestryActionResult.UnsuitableTerrain, forest.Plant(0, ForestSpecies.Oak));
        Assert.False(forest.TryGetStand(0, out _));
    }

    [Fact]
    public void FellingCanBeStoppedWithoutTakingTimberOrRemovingTrees()
    {
        var p = RuleProject.CreateDefault(); Constant(p, "forest.initialAllow", 1); Constant(p, "forest.fellAllow", 0);
        var forest = new ForestSystem(new Habitat(), responseRules: EcologyResponseProgram.FromModel(p.Behaviors));
        int count = forest.IndividualTrees.Patches.Sum(p => p.Value.Count);
        Assert.Equal(0, forest.ExtractTimber(0, 100));
        Assert.Equal(count, forest.IndividualTrees.Patches.Sum(p => p.Value.Count));
        Assert.Equal(ForestryActionResult.NoForest, forest.Harvest(0, out _));
    }

    [Fact]
    public void DeletedCostGraphDoesNotRestoreTheBuiltinPriceAndAuthoredPriceIsCharged()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        var p = RuleProject.CreateDefault(); p.Behaviors.Graphs.RemoveAll(g => g.Hook == "road.buildCost");
        world.QueueRuleProject(p); world.ExecutePendingCommands();
        for (int i = 0; i < world.Map.Tiles.Count - 1 && world.RoadCount == 0; i++) { world.QueueRoadPath(i, i + 1, false); world.ExecutePendingCommands(); }
        Assert.True(world.RoadCount > 0); Assert.Equal(0, world.Expenses);
        p = world.RuleProjectDocument; Constant(p, "road.buildCost", 7);
        world.QueueRuleProject(p); world.ExecutePendingCommands();
        int before = world.RoadCount;
        for (int i = 0; i < world.Map.Tiles.Count - 1 && world.RoadCount == before; i++) { world.QueueRoadPath(i, i + 1, false); world.ExecutePendingCommands(); }
        Assert.True(world.RoadCount > before); Assert.Equal(7, world.Expenses);
    }

    [Fact]
    public void PopulationGraphCanDisableWildlifeInTheRunningWorldAndSave()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        var p = RuleProject.CreateDefault(); Constant(p, "wildlife.population", 0);
        world.QueueRuleProject(p); world.ExecutePendingCommands(); world.Update(1d / 30);
        Assert.Empty(world.CaptureCheckpoint().Wildlife.Animals);
    }
}
