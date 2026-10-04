using TestHabitat = ForesTycoon.Tests.ForestSystemTests.TestHabitat;

namespace ForesTycoon.Tests;

public class ForestStumpTests
{
    private static (ForestSystem Forest, TestHabitat Habitat, int Tile, ForestTreeStore.Patch Patch) MatureStand(int seed)
    {
        var habitat = new TestHabitat(96, seed);
        var forest = new ForestSystem(habitat, secondsPerYear: 1);
        var entry = forest.IndividualTrees.Patches.First();
        return (forest, habitat, entry.Key, entry.Value);
    }

    [Fact]
    public void Harvest_PreservesEachFelledTreeAndAllowsReplanting()
    {
        var (forest, _, tile, patch) = MatureStand(42);
        var trees = patch.Trees.Take(patch.Count).ToArray();
        Assert.Null(patch.Stumps);
        Assert.Equal(ForestryActionResult.Harvested, forest.Harvest(tile, out _));
        Assert.NotNull(patch.Stumps);
        Assert.Equal(trees.Select(tree => tree with { AnnualGrowth = default }), patch.Stumps.Select(stump => stump.Felled));
        Assert.All(patch.Stumps, stump => Assert.Equal(forest.ForestYear, stump.FelledYear));
        Assert.Equal(ForestryActionResult.Planted, forest.Plant(tile, ForestSpecies.Oak));
        Assert.Equal(trees.Length, patch.Stumps.Count);
    }

    [Fact]
    public void GradualExtraction_PreservesTheFirstCutSnapshotAndUncutTrees()
    {
        var (forest, _, tile, patch) = MatureStand(7);
        var trees = patch.Trees.Take(patch.Count).ToArray();
        float firstVolume = ForestTree.Volume(trees[0].At(forest.ForestYear));
        forest.ExtractTimber(tile, firstVolume * 0.3f);
        var stump = Assert.Single(patch.Stumps);
        forest.ExtractTimber(tile, firstVolume * 0.3f);
        Assert.Equal(stump, Assert.Single(patch.Stumps));
        Assert.Equal(trees[0] with { AnnualGrowth = default }, stump.Felled);
        for (int i = 0; i < patch.Count; i++)
        {
            var survivor = patch.Trees[i];
            Assert.Equal(trees[i + 1] with { AnnualGrowth = survivor.AnnualGrowth, Resources = survivor.Resources }, survivor);
            Assert.True(survivor.Resources.Light >= trees[i + 1].Resources.Light);
        }
        Assert.Equal(firstVolume * 0.4f, patch.Depot, 5);
    }

    [Fact]
    public void Stumps_DecayIndividuallyAndDisappearAfterTheirLifetime()
    {
        var (forest, _, tile, patch) = MatureStand(11);
        forest.Harvest(tile, out _);
        forest.Update(ForestTreeStump.LifetimeYears * 0.5);
        Assert.NotEmpty(patch.Stumps);
        Assert.All(patch.Stumps, stump => Assert.InRange(stump.Decay(forest.ForestYear), 0.4f, 0.6f));
        forest.Update(ForestTreeStump.LifetimeYears * 0.5 + 0.2);
        Assert.Empty(patch.Stumps);
    }

    [Fact]
    public void HabitatRemoval_ClearsTreesStumpsAndDepot()
    {
        var (forest, habitat, tile, patch) = MatureStand(15);
        forest.ExtractTimber(tile, ForestTree.Volume(patch.Trees[0].At(forest.ForestYear)) * 0.1f);
        Assert.NotEmpty(patch.Stumps);
        Assert.True(patch.Depot > 0);
        habitat.SetSupported(tile, false);
        forest.RefreshHabitat();
        Assert.False(forest.IndividualTrees.TryGet(tile, out _));
        Assert.Equal(0, forest.AvailableTimber(tile));
        Assert.False(forest.TryGetStand(tile, out _));
    }
}
