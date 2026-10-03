using TestHabitat = ForesTycoon.Tests.ForestSystemTests.TestHabitat;

namespace ForesTycoon.Tests;

public class ForestStumpTests
{
    private static (ForestSystem Forest, TestHabitat Habitat, int Tile) MatureStand(int seed)
    {
        TestHabitat habitat = new TestHabitat(96, seed: seed);
        ForestSystem forest = new ForestSystem(habitat, secondsPerYear: 1.0);
        for (int tileId = 0; tileId < habitat.TileCount; tileId++)
            if (forest.TryGetStand(tileId, out _)) return (forest, habitat, tileId);
        throw new InvalidOperationException("No stand generated.");
    }

    [Fact]
    public void Harvest_LeavesStumpOfTheFelledStand_AndPlantingStillWorks()
    {
        var (forest, _, tile) = MatureStand(42);
        forest.TryGetStand(tile, out ForestStand before);
        Assert.False(forest.TryGetStump(tile, out _));

        Assert.Equal(ForestryActionResult.Harvested, forest.Harvest(tile, out _));

        Assert.True(forest.TryGetStump(tile, out ForestStump stump));
        Assert.Equal(before, stump.Felled);
        Assert.Equal(0f, stump.YearsSinceFelled);
        Assert.Equal(ForestryActionResult.Planted, forest.Plant(tile, ForestSpecies.Oak));
    }

    [Fact]
    public void GradualExtraction_KeepsTheSnapshotFromBeforeTheFirstCut()
    {
        var (forest, _, tile) = MatureStand(7);
        forest.TryGetStand(tile, out ForestStand before);
        float total = ForestSystem.TimberCubicMetres(before);

        forest.ExtractTimber(tile, total * 0.3f);
        forest.ExtractTimber(tile, total * 0.3f);

        Assert.True(forest.TryGetStump(tile, out ForestStump stump));
        Assert.Equal(before, stump.Felled);
        Assert.True(forest.TryGetStand(tile, out ForestStand remaining));
        Assert.True(remaining.Biomass < before.Biomass);
    }

    [Fact]
    public void Stumps_RotAwayAfterTheirLifetime()
    {
        var (forest, _, tile) = MatureStand(11);
        forest.Harvest(tile, out _);

        forest.Update(ForestStump.LifetimeYears * 0.5);
        Assert.True(forest.TryGetStump(tile, out ForestStump halfway));
        Assert.InRange(halfway.Decay, 0.4f, 0.6f);

        forest.Update(ForestStump.LifetimeYears * 0.5 + 0.2);
        Assert.False(forest.TryGetStump(tile, out _));
        Assert.Equal(0, forest.StumpCount);
    }

    [Fact]
    public void Stumps_AreClearedWhenTheTileStopsSupportingForest()
    {
        var (forest, habitat, tile) = MatureStand(15);
        forest.Harvest(tile, out _);

        habitat.SetSupported(tile, false);
        forest.RefreshHabitat();

        Assert.False(forest.TryGetStump(tile, out _));
    }
}
