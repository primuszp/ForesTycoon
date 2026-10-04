namespace ForesTycoon.Tests;

public class ForestDynamicsTests
{
    private sealed class Habitat(int side = 3, float moisture = .64f) : IForestHabitat
    {
        public int TileCount => side * side;
        public int Seed => 1297;
        public bool CanSupportForest(int id) => id >= 0 && id < TileCount;
        public float GetMoisture(int id) => moisture;
        public float GetNormalizedElevation(int id) => .45f;
        public ForestTileGeometry GetForestTileGeometry(int id) => new(id / side * 16, id % side * 16, 16, 16);
        public int GetAdjacentTileIds(int id, Span<int> target)
        {
            int x = id / side, y = id % side, n = 0;
            if (x > 0) target[n++] = id - side;
            if (x < side - 1) target[n++] = id + side;
            if (y > 0) target[n++] = id - 1;
            if (y < side - 1) target[n++] = id + 1;
            return n;
        }
    }

    [Theory]
    [InlineData((int)ForestSpecies.Spruce)] [InlineData((int)ForestSpecies.Birch)]
    [InlineData((int)ForestSpecies.Oak)] [InlineData((int)ForestSpecies.Beech)]
    public void PlantationHasAlignedSeedlingsAndPersistentIdentity(int speciesId)
    {
        var species = (ForestSpecies)speciesId;
        var forest = new ForestSystem(new Habitat()); forest.Clear();
        int area = forest.AllocatePlantationId();
        Assert.Equal(ForestryActionResult.Planted, forest.PlantInArea(4, species, area));
        Assert.Equal(ForestryActionResult.Planted, forest.PlantInArea(5, species, area));
        forest.FinishPlantingArea(area);
        foreach (int id in new[] { 4, 5 })
        {
            Assert.True(forest.TryGetPlantationStatus(id, out var status));
            Assert.Equal(area, status.Plantation.AreaId); Assert.Equal(ForestTreeStore.PlantedTreesPerTile, status.Living);
            Assert.Equal(ForestTreeStore.PlantedTreesPerTile, status.Plantation.InitialTrees);
            forest.IndividualTrees.TryGet(id, out var patch);
            Assert.Single(patch.Trees.Take(patch.Count).Select(t => t.BirthYear).Distinct());
            for (int i = 0; i < patch.Count; i++)
            {
                Assert.Equal((i % ForestTreeStore.PlantingRows + .5f) / ForestTreeStore.PlantingRows, patch.Trees[i].U);
                Assert.Equal((i / ForestTreeStore.PlantingRows + .5f) / ForestTreeStore.PlantingRows, patch.Trees[i].V);
                Assert.Equal(TreeLifeStage.Seedling, ForestTreeAppearance.Stage(species, patch.Trees[i].Age(forest.ForestYear)));
            }
        }
        Assert.Equal(ForestryActionResult.TileOccupied, forest.Plant(4, species));
        forest.Harvest(4, out _);
        Assert.True(forest.TryGetPlantationStatus(4, out var empty));
        Assert.Equal(0, empty.Living); Assert.Equal(area, empty.Plantation.AreaId);
        forest.Clear(); Assert.Equal(0, forest.PlantationTileCount);
    }

    [Fact]
    public void TallerDiagonalNeighbourReducesLightWaterAndSpaceAndRemovalReleasesThem()
    {
        var habitat = new Habitat(); var store = new ForestTreeStore();
        store.Create(0, new(ForestSpecies.Birch, 1, .1f, 1), 0, 4);
        store.Create(4, new(ForestSpecies.Birch, 30, .1f, 1), 0, 4);
        store.TryGet(0, out var small); store.TryGet(4, out var large);
        while (large.Count > 1) store.RemoveLiving(large, large.Count - 1);
        small.Trees[0] = small.Trees[0] with { U = .99f, V = .99f, Dimensions = new(.03f, 1, .25f), AnnualGrowth = default };
        large.Trees[0] = large.Trees[0] with { U = .01f, V = .01f, Dimensions = new(.5f, 20, 6), AnnualGrowth = default };
        var competition = new ForestCompetition(); competition.Snapshot(habitat, store, 0);
        var shaded = competition.Evaluate(habitat, small.Trees[0], 0, 1);
        var dominant = competition.Evaluate(habitat, large.Trees[0], 0, 1);
        Assert.True(shaded.Light < dominant.Light);
        Assert.True(shaded.Water < 1); Assert.True(shaded.Space < 1);
        Assert.True(shaded.LightResponse(ForestSpecies.Beech) > shaded.LightResponse(ForestSpecies.Birch));
        store.RemoveLiving(large, 0); competition.Snapshot(habitat, store, 0);
        Assert.Equal(new ForestResources(1, 1, 1), competition.Evaluate(habitat, small.Trees[0], 0, 1));
    }

    [Fact]
    public void PersistentSuppressionCreatesDeadwoodWithoutCreditingHarvestTimber()
    {
        var forest = new ForestSystem(new Habitat(1), secondsPerYear: 12); forest.Clear();
        forest.Plant(0, ForestSpecies.Birch); forest.IndividualTrees.TryGet(0, out var patch);
        for (int i = 0; i < patch.Count; i++)
            patch.Trees[i] = patch.Trees[i] with { U = .5f, V = .5f, Dimensions = new(.02f, 1, .2f), AnnualGrowth = default, Health = 1 };
        patch.Trees[0] = patch.Trees[0] with { Dimensions = new(.5f, 22, 10), BirthYear = -30 };
        forest.Update(12 * 6);
        Assert.InRange(patch.Count, 1, ForestTreeStore.PlantedTreesPerTile - 1);
        Assert.NotEmpty(patch.DeadTrees!);
        Assert.Equal(0, patch.Depot); Assert.Null(patch.Stumps);
        Assert.True(forest.TryGetPlantation(0, out _));
        Assert.All(patch.DeadTrees!, dead => { Assert.Equal(0, dead.Tree.Health); Assert.Equal(default, dead.Tree.AnnualGrowth); });
        forest.Update(12 * 10);
        Assert.Empty(patch.DeadTrees!);
    }

    [Fact]
    public void DenseHealthyBirchPlantationSelfThinsBeforeMaximumAge()
    {
        var forest = new ForestSystem(new Habitat(1), secondsPerYear: 12); forest.Clear();
        forest.Plant(0, ForestSpecies.Birch);
        forest.Update(12 * 80);
        Assert.True(forest.TryGetPlantationStatus(0, out var status));
        Assert.InRange(status.Living, 1, ForestTreeStore.PlantedTreesPerTile - 1);
        Assert.True(status.Resources.Light < 1);
        Assert.True(80 < ForestSpeciesProfile.For(ForestSpecies.Birch).MaximumAgeYears);
    }

    [Fact]
    public void MonthlyCompetitionAndMortalityAreIndependentOfFrameGrouping()
    {
        var a = new ForestSystem(new Habitat(1), secondsPerYear: 12);
        var b = new ForestSystem(new Habitat(1), secondsPerYear: 12);
        a.Clear(); b.Clear(); a.Plant(0, ForestSpecies.Birch); b.Plant(0, ForestSpecies.Birch);
        a.Update(960); for (int i = 0; i < 3840; i++) b.Update(.25);
        a.IndividualTrees.TryGet(0, out var first); b.IndividualTrees.TryGet(0, out var second);
        Assert.Equal(first.Count, second.Count);
        Assert.True(first.Count < ForestTreeStore.PlantedTreesPerTile);
        Assert.Equal(first.Trees, second.Trees);
        Assert.Equal(first.DeadTrees, second.DeadTrees);
        Assert.Equal(a.Statistics, b.Statistics);
        a.TryGetPlantationStatus(0, out var sa); b.TryGetPlantationStatus(0, out var sb); Assert.Equal(sa, sb);
    }
}
