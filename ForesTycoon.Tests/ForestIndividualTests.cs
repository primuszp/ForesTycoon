namespace ForesTycoon.Tests;

public class ForestIndividualTests
{
    private static ForestSystem Create(int seed = 42)
    {
        var forest = new ForestSystem(new ForestSystemTests.TestHabitat(96, seed: seed), secondsPerYear: 12);
        return forest;
    }

    private static (int Tile, ForestTreeStore.Patch Patch) First(ForestSystem forest) =>
        forest.IndividualTrees.Patches.Select(e => (e.Key, e.Value)).First(e => e.Value.Count > 0);

    [Fact]
    public void SeedAndReplayPreserveIndividualsAcrossDifferentFrameDurations()
    {
        var first = Create(); var second = Create();
        for (int i = 0; i < 240; i++) first.Update(0.05);
        for (int i = 0; i < 24; i++) second.Update(0.5);
        Assert.Equal(first.IndividualTreeCount, second.IndividualTreeCount);
        foreach (var entry in first.IndividualTrees.Patches)
        {
            Assert.True(second.IndividualTrees.TryGet(entry.Key, out var other));
            Assert.Equal(entry.Value.Count, other.Count);
            for (int i = 0; i < other.Count; i++)
            {
                var a = entry.Value.Trees[i]; var b = other.Trees[i];
                Assert.Equal(a.Id, b.Id); Assert.Equal(a.U, b.U); Assert.Equal(a.V, b.V);
                Assert.Equal(a.At(first.ForestYear).Diameter, b.At(second.ForestYear).Diameter, 5);
                Assert.Equal(a.At(first.ForestYear).Height, b.At(second.ForestYear).Height, 5);
            }
        }
    }

    [Fact]
    public void GrowthIsContinuousWithinAMonthAndAtTheBoundary()
    {
        var forest = Create();
        forest.Clear(); forest.Plant(0, ForestSpecies.Oak);
        forest.IndividualTrees.TryGet(0, out var patch);
        var initial = patch.Trees[0];
        forest.Update(0.49);
        var half = patch.Trees[0].At(forest.ForestYear);
        Assert.True(half.Diameter > initial.Dimensions.Diameter);
        Assert.True(half.Height > initial.Dimensions.Height);
        forest.Update(0.51);
        var boundary = patch.Trees[0];
        Assert.Equal(initial.At(1.0 / 12), boundary.Dimensions);
        Assert.Equal(initial.Id, boundary.Id);
        Assert.Equal(initial.U, boundary.U); Assert.Equal(initial.V, boundary.V);
        Assert.True(boundary.Dimensions.Diameter > half.Diameter);
    }

    [Fact]
    public void MatureTreesCanStillThicken()
    {
        var tree = new ForestTree(1, 0, ForestSpecies.Oak, 0.5f, 0.5f, 42, -180, 0,
            ForestTreeGrowth.Initial(ForestSpecies.Oak, 180, 1), default, 1);
        var rates = ForestTreeGrowth.Rates(tree, 0.25, 1, 0, 1);
        Assert.True(rates.Diameter > 0);
        Assert.True(rates.Height > 0);
        Assert.True(rates.Height < 0.9);
    }

    [Fact]
    public void AnnualIncrementEqualsTheActualIncreaseOfStandingTimber()
    {
        var forest = Create(); forest.Clear(); forest.Plant(0, ForestSpecies.Oak);
        float before = forest.AvailableTimber(0);
        forest.Update(12);
        Assert.True(forest.LastAnnualGrowthCubicMetres > 0);
        Assert.Equal(forest.AvailableTimber(0) - before, forest.LastAnnualGrowthCubicMetres, 5);
        forest.Clear();
        Assert.Equal(0, forest.LastAnnualGrowthCubicMetres);
    }

    [Fact]
    public void PartialLoadFellsWholeTreesWithoutShrinkingTheSurvivorsAndBalancesDepot()
    {
        var forest = Create();
        var (tile, patch) = First(forest);
        Assert.True(patch.Count > 1);
        var original = patch.Trees.Take(patch.Count).ToArray();
        float before = forest.AvailableTimber(tile);
        float requested = ForestTree.Volume(original[0].At(forest.ForestYear)) * 0.25f;
        Assert.Equal(requested, forest.ExtractTimber(tile, requested), 6);
        Assert.Equal(original.Length - 1, patch.Count);
        for (int i = 0; i < patch.Count; i++) Assert.Equal(original[i + 1], patch.Trees[i]);
        Assert.Single(patch.Stumps);
        Assert.Equal(original[0].Id, patch.Stumps[0].Felled.Id);
        Assert.Equal(before, forest.AvailableTimber(tile) + requested, 4);
        int remainingCount = patch.Count;
        float fromDepot = patch.Depot * 0.5f;
        Assert.Equal(fromDepot, forest.ExtractTimber(tile, fromDepot), 6);
        Assert.Equal(remainingCount, patch.Count);
        float remaining = forest.AvailableTimber(tile);
        Assert.Equal(remaining, forest.ExtractTimber(tile, float.MaxValue), 4);
        Assert.Equal(0, patch.Count); Assert.Equal(0, forest.AvailableTimber(tile));
    }

    [Fact]
    public void ReplantingCreatesNewIdsAndRetainsTheActualCutStemUntilDecay()
    {
        var forest = Create(); var (tile, patch) = First(forest);
        ulong id = patch.Trees[0].Id;
        forest.Harvest(tile, out _);
        Assert.NotEmpty(patch.Stumps);
        Assert.Equal(ForestryActionResult.Planted, forest.Plant(tile, ForestSpecies.Birch));
        Assert.NotEqual(id, patch.Trees[0].Id);
        Assert.NotEmpty(patch.Stumps);
        forest.Update(ForestTreeStump.LifetimeYears * 12 + 1);
        Assert.Empty(patch.Stumps);
    }

    [Fact]
    public void ResetRegeneratesTheOriginalIndividuals()
    {
        var forest = Create(); forest.Update(24); forest.Reset(new ForestSystemTests.TestHabitat(96, seed: 42));
        var regenerated = new ForestSystem(new ForestSystemTests.TestHabitat(96, seed: 42), secondsPerYear: 12);
        for (int id = 0; id < 96; id++)
        {
            Assert.Equal(regenerated.TryGetStand(id, out var expected), forest.TryGetStand(id, out var actual));
            Assert.Equal(expected, actual);
            bool hasPatch = regenerated.IndividualTrees.TryGet(id, out var expectedPatch);
            Assert.Equal(hasPatch, forest.IndividualTrees.TryGet(id, out var actualPatch));
            if (hasPatch) Assert.Equal(expectedPatch.Trees.Take(expectedPatch.Count), actualPatch.Trees.Take(actualPatch.Count));
        }
    }

    [Fact]
    public void PhysicalAndVertexGrowthAgreeWithoutMovingTheRoot()
    {
        var tree = new ForestTree(1, 0, ForestSpecies.Oak, 0.5f, 0.5f, 42, 0, 0,
            new(0.3f, 12, 2), new(0.01f, 0.5f, 0.1f), 1);
        var origin = new OpenTK.Mathematics.Vector3(7, 9, 4);
        var size = tree.Dimensions;
        var vertexGrowth = new ForestVertexGrowth(origin, new(size.Diameter == 0 ? 0 : tree.AnnualGrowth.Diameter / size.Diameter,
            tree.AnnualGrowth.Diameter / size.Diameter, tree.AnnualGrowth.Height / size.Height));
        Assert.Equal(origin, vertexGrowth.At(origin, 0.04f));
        var rim = vertexGrowth.At(origin + new OpenTK.Mathematics.Vector3(size.Diameter / 2, 0, size.Height), 0.04f);
        var actual = tree.At(0.04);
        Assert.Equal(actual.Diameter / 2, rim.X - origin.X, 5);
        Assert.Equal(actual.Height, rim.Z - origin.Z, 5);
    }

    [Theory]
    [InlineData(ForestSpecies.Spruce)]
    [InlineData(ForestSpecies.Birch)]
    [InlineData(ForestSpecies.Oak)]
    [InlineData(ForestSpecies.Beech)]
    internal void CutSnapshotPreservesSeparateTrunkAndCrownDimensions(ForestSpecies species)
    {
        var forest = Create(); forest.Clear(); forest.Plant(0, species);
        forest.IndividualTrees.TryGet(0, out var patch);
        var original = patch.Trees[0];
        forest.Update(0.5);
        var before = original.At(forest.ForestYear);
        float totalVolume = Enumerable.Range(0, patch.Count).Sum(i => ForestTree.Volume(patch.Trees[i].At(forest.ForestYear)));
        int count = patch.Count;
        forest.Harvest(0, out var harvest);
        Assert.Equal(totalVolume, harvest.TimberVolume, 5);
        Assert.Equal(count, patch.Stumps.Count);
        var stump = patch.Stumps.Single(s => s.Felled.Id == original.Id);
        Assert.Equal(before, stump.Felled.Dimensions);
        Assert.Equal(default, stump.Felled.AnnualGrowth);
        var terrain = new TerrainData(TerrainSettings.Default.WithNodeSize(17, 42));
        var stem = Terrain.IndividualStem(terrain.Tiles[0], stump.Felled, forest.ForestYear + 1);
        var model = Terrain.TreeModel.For(species);
        Assert.Equal(before.Diameter * Terrain.TreeMetresToWorld * 0.85f, model.TrunkRadius * stem.TrunkScale, 5);
        Assert.Equal(before.CrownRadius * Terrain.TreeMetresToWorld, model.CrownRadius * stem.Scale * stem.CrownWidth, 5);
    }
}
