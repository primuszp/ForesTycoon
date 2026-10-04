namespace ForesTycoon.Tests;

public class ForestMonthlyPreparationTests
{
    private sealed class Habitat(int side = 8) : IForestHabitat
    {
        public int TileCount => side * side;
        public int Seed => 42;
        public bool CanSupportForest(int id) => true;
        public float GetMoisture(int id) => .65f;
        public float GetNormalizedElevation(int id) => .45f;
        public ForestTileGeometry GetForestTileGeometry(int id) => new(id % side * 16, id / side * 16, 16, 16);
        public int GetAdjacentTileIds(int id, Span<int> target)
        {
            int n = 0, x = id % side, y = id / side;
            if (x > 0) target[n++] = id - 1;
            if (x < side - 1) target[n++] = id + 1;
            if (y > 0) target[n++] = id - side;
            if (y < side - 1) target[n++] = id + side;
            return n;
        }
    }

    private static (ForestSystem Forest, EnvironmentSystem Water, ForestEnvironmentCoordinator Clock) Create(bool prepared, double year = 1200, int side = 8)
    {
        var habitat = new Habitat(side);
        var forest = new ForestSystem(habitat, secondsPerYear: year);
        var water = new EnvironmentSystem(habitat, forest);
        water.ForceWeather(WeatherPreset.Storm, 32, 90);
        return (forest, water, new ForestEnvironmentCoordinator(forest, water, prepared));
    }

    [Theory]
    [InlineData(1200)]
    [InlineData(13)]
    public void PreparedMonthsExactlyMatchSynchronousWaterGrowthAndMortality(double year)
    {
        var prepared = Create(true, year);
        var reference = Create(false, year);
        prepared.Clock.Update(420);
        reference.Clock.Update(420);
        Equal(prepared, reference);
        Assert.True(prepared.Forest.LastMonthlyPreparedTrees > 0);
        Assert.Equal(0, reference.Forest.LastMonthlyPreparedTrees);
    }

    [Theory]
    [InlineData(.5)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(99.5)]
    public void EditingDuringPreparationCannotPublishOldTreesOrRates(double editTime)
    {
        var prepared = Create(true);
        var reference = Create(false);
        prepared.Clock.Update(editTime);
        reference.Clock.Update(editTime);
        int tile = prepared.Forest.IndividualTrees.Patches.First(p => p.Value.Count > 0).Key;
        prepared.Forest.Harvest(tile, out _);
        reference.Forest.Harvest(tile, out _);
        prepared.Forest.Plant(tile, ForestSpecies.Spruce);
        reference.Forest.Plant(tile, ForestSpecies.Spruce);
        prepared.Water.ForceWeather(WeatherPreset.Cloudy, 0, 600);
        reference.Water.ForceWeather(WeatherPreset.Cloudy, 0, 600);
        prepared.Clock.Update(300 - editTime);
        reference.Clock.Update(300 - editTime);
        Equal(prepared, reference);
    }

    [Fact]
    public void LateLocalEditRepairsBoundedAreaRatherThanWholeMap()
    {
        var prepared = Create(true, side: 64);
        var reference = Create(false, side: 64);
        prepared.Clock.Update(99.5); reference.Clock.Update(99.5);
        int tile = prepared.Forest.IndividualTrees.Patches.First(p => p.Value.Count > 0 && p.Key > 1000).Key;
        prepared.Forest.Harvest(tile, out _); reference.Forest.Harvest(tile, out _);
        prepared.Forest.Plant(tile, ForestSpecies.Spruce); reference.Forest.Plant(tile, ForestSpecies.Spruce);
        prepared.Clock.Update(.5); reference.Clock.Update(.5);
        Assert.InRange(prepared.Forest.LastPreparationSnapshotPatches, 1, 13);
        Assert.InRange(prepared.Forest.LastPreparationResourcePatches, 1, 100);
        Assert.True(prepared.Forest.LastMonthlyPreparedTrees > 1000);
        Equal(prepared, reference);
    }

    [Fact]
    public void RepeatedExtractionAndAreaPlantingPreserveSynchronousResults()
    {
        var prepared = Create(true, side: 16);
        var reference = Create(false, side: 16);
        for (int edit = 0; edit < 35; edit++)
        {
            prepared.Clock.Update(3); reference.Clock.Update(3);
            int tile = prepared.Forest.IndividualTrees.Patches.First(p => p.Value.Count > 0).Key;
            prepared.Forest.ExtractTimber(tile, .5f); reference.Forest.ExtractTimber(tile, .5f);
        }
        int first = prepared.Forest.AllocatePlantationId(), second = reference.Forest.AllocatePlantationId();
        for (int id = 0; id < 10; id++)
        {
            prepared.Forest.Harvest(id, out _); reference.Forest.Harvest(id, out _);
            prepared.Forest.PlantInArea(id, ForestSpecies.Beech, first);
            reference.Forest.PlantInArea(id, ForestSpecies.Beech, second);
        }
        prepared.Forest.FinishPlantingArea(first); reference.Forest.FinishPlantingArea(second);
        prepared.Clock.Update(195); reference.Clock.Update(195);
        Equal(prepared, reference);
    }

    [Fact]
    public void GeometryAndCurvePreparationPublishesNoLiveState()
    {
        var world = Create(true);
        var before = world.Forest.IndividualTrees.Patches.ToDictionary(p => p.Key, p => p.Value.Trees.ToArray());
        var statistics = world.Forest.Statistics;
        ulong revision = world.Forest.Revision;
        var water = world.Water.Cell(0);
        for (int step = 0; step < 250; step++) world.Forest.PrepareNextMonthStep();
        Assert.Equal(revision, world.Forest.Revision);
        Assert.Equal(statistics, world.Forest.Statistics);
        Assert.Equal(0, world.Forest.ForestYear);
        Assert.Equal(water, world.Water.Cell(0));
        foreach (var entry in world.Forest.IndividualTrees.Patches)
            Assert.Equal(before[entry.Key], entry.Value.Trees);
    }

    [Fact]
    public void GlobalRefreshStillRestartsPreparationAfterUntrackedGeometryChange()
    {
        var prepared = Create(true, side: 32);
        var reference = Create(false, side: 32);
        prepared.Clock.Update(99.5); reference.Clock.Update(99.5);
        foreach (var forest in new[] { prepared.Forest, reference.Forest })
        {
            foreach (var entry in forest.IndividualTrees.Patches)
                for (int i = 0; i < entry.Value.Count; i++)
                    entry.Value.Trees[i] = entry.Value.Trees[i] with { Dimensions = new(.4f, 15, 3) };
            forest.NotifyIndividualVisualEdit();
            forest.RefreshEnvironmentRates();
        }
        prepared.Clock.Update(.5); reference.Clock.Update(.5);
        Assert.True(prepared.Forest.LastPreparationSnapshotPatches > 100);
        Equal(prepared, reference);
    }

    [Fact]
    public void ClearDiscardsPreparedGeometryAndZeroTimeDoesNoWork()
    {
        var prepared = Create(true);
        var reference = Create(false);
        prepared.Clock.Update(75);
        reference.Clock.Update(75);
        prepared.Forest.Clear(); reference.Forest.Clear();
        prepared.Forest.Plant(32, ForestSpecies.Oak); reference.Forest.Plant(32, ForestSpecies.Oak);
        var before = prepared.Forest.IndividualTrees.Patches.First().Value.Trees.ToArray();
        prepared.Clock.Update(0);
        Assert.Equal(before, prepared.Forest.IndividualTrees.Patches.First().Value.Trees);
        prepared.Clock.Update(200); reference.Clock.Update(200);
        Equal(prepared, reference);
    }

    private static void Equal(
        (ForestSystem Forest, EnvironmentSystem Water, ForestEnvironmentCoordinator Clock) a,
        (ForestSystem Forest, EnvironmentSystem Water, ForestEnvironmentCoordinator Clock) b)
    {
        Assert.Equal(a.Forest.ForestYear, b.Forest.ForestYear);
        Assert.Equal(a.Forest.Statistics, b.Forest.Statistics);
        Assert.Equal(a.Forest.LastAnnualGrowthCubicMetres, b.Forest.LastAnnualGrowthCubicMetres);
        Assert.Equal(a.Forest.IndividualTreeCount, b.Forest.IndividualTreeCount);
        Assert.Equal(a.Water.Transpired, b.Water.Transpired);
        Assert.Equal(a.Water.StoredWater, b.Water.StoredWater);
        for (int id = 0; id < a.Water.CellCount; id++)
        {
            Assert.Equal(a.Water.Cell(id), b.Water.Cell(id));
            Assert.Equal(a.Forest.IndividualTrees.TryGet(id, out var first), b.Forest.IndividualTrees.TryGet(id, out var second));
            if (first == null) continue;
            Assert.Equal(first.Count, second.Count);
            Assert.Equal(first.Trees, second.Trees);
            Assert.Equal(first.Depot, second.Depot);
            Assert.Equal(first.DeadTrees, second.DeadTrees);
            Assert.Equal(first.Stumps, second.Stumps);
        }
    }
}
