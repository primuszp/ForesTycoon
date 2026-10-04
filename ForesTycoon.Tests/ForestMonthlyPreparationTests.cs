namespace ForesTycoon.Tests;

public class ForestMonthlyPreparationTests
{
    private sealed class Habitat : IForestHabitat
    {
        public int TileCount => 64;
        public int Seed => 42;
        public bool CanSupportForest(int id) => true;
        public float GetMoisture(int id) => .65f;
        public float GetNormalizedElevation(int id) => .45f;
        public ForestTileGeometry GetForestTileGeometry(int id) => new(id % 8 * 16, id / 8 * 16, 16, 16);
        public int GetAdjacentTileIds(int id, Span<int> target)
        {
            int n = 0, x = id % 8, y = id / 8;
            if (x > 0) target[n++] = id - 1;
            if (x < 7) target[n++] = id + 1;
            if (y > 0) target[n++] = id - 8;
            if (y < 7) target[n++] = id + 8;
            return n;
        }
    }

    private static (ForestSystem Forest, EnvironmentSystem Water, ForestEnvironmentCoordinator Clock) Create(bool prepared, double year = 1200)
    {
        var habitat = new Habitat();
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
    [InlineData(30)]
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
        for (int id = 0; id < 64; id++)
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
