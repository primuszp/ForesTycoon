namespace ForesTycoon.Tests;

public class ForestSystemTests
{
    [Fact]
    public void Initialization_IsDeterministicForSameHabitat()
    {
        TestHabitat habitat = new TestHabitat(96, seed: 741);
        ForestSystem first = new ForestSystem(habitat);
        ForestSystem second = new ForestSystem(habitat);

        Assert.True(first.Count > 0);
        Assert.Equal(first.Statistics, second.Statistics);
        for (int tileId = 0; tileId < habitat.TileCount; tileId++)
        {
            Assert.Equal(first.TryGetStand(tileId, out ForestStand a), second.TryGetStand(tileId, out ForestStand b));
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void Update_AdvancesAgeAndBiomassOnMonthlyBoundary()
    {
        TestHabitat habitat = new TestHabitat(96, seed: 42);
        ForestSystem forest = new ForestSystem(habitat, secondsPerYear: 1.0);
        int tileId = FindFirstStand(forest, habitat.TileCount);
        Assert.True(forest.TryGetStand(tileId, out ForestStand before));

        forest.Update(1.0 / 12.0 + 0.000001);

        Assert.True(forest.TryGetStand(tileId, out ForestStand after));
        Assert.True(after.AgeYears > before.AgeYears);
        Assert.True(after.Biomass >= before.Biomass);
    }

    [Fact]
    public void RefreshHabitat_RemovesStandWhenTileBecomesUnavailable()
    {
        TestHabitat habitat = new TestHabitat(96, seed: 15);
        ForestSystem forest = new ForestSystem(habitat);
        int tileId = FindFirstStand(forest, habitat.TileCount);

        habitat.SetSupported(tileId, false);
        forest.RefreshHabitat();

        Assert.False(forest.TryGetStand(tileId, out _));
    }

    [Fact]
    public void Clear_RemovesAllForestState()
    {
        ForestSystem forest = new ForestSystem(new TestHabitat(96, seed: 91));
        Assert.True(forest.Count > 0);

        forest.Clear();

        Assert.Equal(0, forest.Count);
        Assert.Equal(default, forest.Statistics);
    }

    private static int FindFirstStand(ForestSystem forest, int tileCount)
    {
        for (int tileId = 0; tileId < tileCount; tileId++)
            if (forest.TryGetStand(tileId, out _)) return tileId;
        throw new InvalidOperationException("The deterministic test habitat generated no forest stands.");
    }

    private sealed class TestHabitat : IForestHabitat
    {
        private readonly bool[] supported;

        public TestHabitat(int tileCount, int seed)
        {
            supported = Enumerable.Repeat(true, tileCount).ToArray();
            Seed = seed;
        }

        public int TileCount => supported.Length;
        public int Seed { get; }
        public bool CanSupportForest(int tileId) => supported[tileId];
        public float GetMoisture(int tileId) => 0.46f + tileId % 5 * 0.09f;
        public float GetNormalizedElevation(int tileId) => tileId % 7 / 6f;

        public int GetAdjacentTileIds(int tileId, Span<int> destination)
        {
            int count = 0;
            if (tileId > 0) destination[count++] = tileId - 1;
            if (tileId + 1 < TileCount) destination[count++] = tileId + 1;
            return count;
        }

        public void SetSupported(int tileId, bool value) => supported[tileId] = value;
    }
}
