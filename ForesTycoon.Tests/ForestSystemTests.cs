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

    [Fact]
    public void PlantThenHarvest_ProducesTimberAndClearsTile()
    {
        TestHabitat habitat = new TestHabitat(96, seed: 31);
        ForestSystem forest = new ForestSystem(habitat);
        int tileId = FindFirstEmptyTile(forest, habitat.TileCount);

        Assert.Equal(ForestryActionResult.Planted, forest.Plant(tileId, ForestSpecies.Spruce));
        Assert.Equal(ForestryActionResult.TileOccupied, forest.Plant(tileId, ForestSpecies.Oak));
        Assert.Equal(ForestryActionResult.Harvested, forest.Harvest(tileId, out ForestHarvest harvest));

        Assert.Equal(ForestSpecies.Spruce, harvest.Species);
        Assert.True(harvest.TimberVolume > 0f);
        Assert.False(forest.TryGetStand(tileId, out _));
        Assert.Equal(ForestryActionResult.NoForest, forest.Harvest(tileId, out _));
    }

    [Fact]
    public void Plant_ReportsUnsuitableTerrainInsteadOfFailingSilently()
    {
        TestHabitat habitat = new TestHabitat(96, seed: 22);
        ForestSystem forest = new ForestSystem(habitat);
        int tileId = FindFirstEmptyTile(forest, habitat.TileCount);
        habitat.SetSupported(tileId, false);

        ForestryActionResult result = forest.Plant(tileId, ForestSpecies.Oak);

        Assert.Equal(ForestryActionResult.UnsuitableTerrain, result);
        Assert.False(forest.TryGetStand(tileId, out _));
    }

    [Fact]
    public void VisualScale_KeepsSaplingsReadableButClearlySmallerThanMatureStands()
    {
        ForestStand sapling = new ForestStand(ForestSpecies.Spruce, 1f / 12f, 0.015f, 0.8f);
        ForestStand mature = new ForestStand(ForestSpecies.Spruce, 60f, 1.1f, 0.9f);

        float saplingScale = Terrain.TreeVisualScale(sapling);

        Assert.True(saplingScale >= 0.25f);
        Assert.True(saplingScale <= Terrain.TreeVisualScale(mature) * 0.5f);
    }

    [Fact]
    public void InitialForest_MixesSpeciesInsteadOfCollapsingToOne()
    {
        // Regression: species used to be drawn from the same hash the stocking filter had
        // already tested, so only tiles with random % density == 0 survived and every
        // mixture fell to its first branch — the map generated a single species.
        MixedSiteHabitat habitat = new MixedSiteHabitat(4000);
        ForestSystem forest = new ForestSystem(habitat);

        HashSet<ForestSpecies> species = new HashSet<ForestSpecies>();
        for (int tileId = 0; tileId < habitat.TileCount; tileId++)
            if (forest.TryGetStand(tileId, out ForestStand stand))
                species.Add(stand.Species);

        Assert.Contains(ForestSpecies.Spruce, species);
        Assert.True(species.Count >= 3, $"Expected a mixed forest, got: {string.Join(", ", species)}.");
    }

    /// <summary>Lowland map with fresh-to-dry soils: the site the game actually starts on.</summary>
    private sealed class MixedSiteHabitat : IForestHabitat
    {
        public MixedSiteHabitat(int tileCount) => TileCount = tileCount;

        public int TileCount { get; }
        public int Seed => 42;
        public bool CanSupportForest(int tileId) => true;
        public float GetMoisture(int tileId) => 0.38f + tileId % 7 * 0.05f;
        public float GetNormalizedElevation(int tileId) => 0.18f + tileId % 5 * 0.06f;

        public int GetAdjacentTileIds(int tileId, Span<int> destination)
        {
            int count = 0;
            if (tileId > 0) destination[count++] = tileId - 1;
            if (tileId + 1 < TileCount) destination[count++] = tileId + 1;
            return count;
        }
    }

    [Fact]
    public void Statistics_StayConsistentWithStandsAcrossGrowthPlantingAndHarvesting()
    {
        TestHabitat habitat = new TestHabitat(96, seed: 55);
        ForestSystem forest = new ForestSystem(habitat, secondsPerYear: 1.0);

        forest.Update(20.0);
        forest.Plant(FindFirstEmptyTile(forest, habitat.TileCount), ForestSpecies.Oak);
        forest.Harvest(FindFirstStand(forest, habitat.TileCount), out _);
        forest.Update(5.0);

        AssertStatisticsMatchStands(forest, habitat.TileCount);
    }

    [Fact]
    public void Fitness_RanksSpeciesByHowWellTheSiteMatchesThem()
    {
        // Wet, high ground is spruce country; dry lowland belongs to oak.
        Assert.True(ForestSystem.Fitness(ForestSpecies.Spruce, 0.78f, 0.74f)
            > ForestSystem.Fitness(ForestSpecies.Oak, 0.78f, 0.74f));
        Assert.True(ForestSystem.Fitness(ForestSpecies.Oak, 0.50f, 0.30f)
            > ForestSystem.Fitness(ForestSpecies.Spruce, 0.50f, 0.30f));
    }

    [Fact]
    public void Fitness_PenalisesTheWrongElevationOnOtherwisePerfectSoil()
    {
        float onSlope = ForestSystem.Fitness(ForestSpecies.Spruce, 0.76f, 0.72f);
        float inValley = ForestSystem.Fitness(ForestSpecies.Spruce, 0.76f, 0.00f);

        Assert.True(onSlope > inValley);
        Assert.True(inValley > 0f, "Moisture must still carry a site the species can otherwise use.");
    }

    [Fact]
    public void Harvest_YieldsMoreRoundwoodFromDenserTimberSpecies()
    {
        ForestStand oak = new ForestStand(ForestSpecies.Oak, 60f, 1.0f, 0.9f);
        ForestStand spruce = new ForestStand(ForestSpecies.Spruce, 60f, 1.0f, 0.9f);

        Assert.True(ForestSystem.TimberYield(oak) > ForestSystem.TimberYield(spruce));
        Assert.Equal(0f, ForestSystem.TimberYield(default));
    }

    [Fact]
    public void Crowding_SuppressesALightDemandingStandComparedToAnOpenGrownOne()
    {
        // The test habitat varies moisture with tileId % 5 and elevation with tileId % 7,
        // so the two birches are 35 tiles apart to guarantee an identical site. Every other
        // tile is closed to forest, which keeps natural regeneration out of the comparison.
        const int crowded = 52;
        const int open = 87;
        TestHabitat habitat = new TestHabitat(96, seed: 7);
        for (int tileId = 0; tileId < habitat.TileCount; tileId++)
            habitat.SetSupported(tileId, tileId is crowded - 1 or crowded or crowded + 1 or open);

        ForestSystem forest = new ForestSystem(habitat, secondsPerYear: 1.0);
        // Start from bare ground so the comparison only sees the stands planted here.
        forest.Clear();
        forest.Plant(crowded - 1, ForestSpecies.Spruce);
        forest.Plant(crowded, ForestSpecies.Birch);
        forest.Plant(crowded + 1, ForestSpecies.Spruce);
        forest.Plant(open, ForestSpecies.Birch);

        // Nine years in, the neighbouring spruce are established but the birch is still
        // alive; later on it is thinned out entirely, which the next test covers.
        forest.Update(9.0);

        Assert.True(forest.GetCrowding(crowded) > forest.GetCrowding(open));
        Assert.True(forest.TryGetStand(crowded, out ForestStand suppressed));
        Assert.True(forest.TryGetStand(open, out ForestStand openGrown));
        Assert.True(openGrown.Biomass > suppressed.Biomass,
            "A birch hemmed in by spruce must accumulate less biomass than one grown in the open.");
        Assert.True(openGrown.Health > suppressed.Health,
            "Shade must cost the suppressed birch some of its health.");
    }

    [Fact]
    public void SuppressedPioneer_IsEventuallyThinnedOutByItsShadeBearingNeighbours()
    {
        const int crowded = 52;
        TestHabitat habitat = new TestHabitat(96, seed: 7);
        for (int tileId = 0; tileId < habitat.TileCount; tileId++)
            habitat.SetSupported(tileId, tileId is crowded - 1 or crowded or crowded + 1);

        ForestSystem forest = new ForestSystem(habitat, secondsPerYear: 1.0);
        // Start from bare ground so the comparison only sees the stands planted here.
        forest.Clear();
        forest.Plant(crowded - 1, ForestSpecies.Spruce);
        forest.Plant(crowded, ForestSpecies.Birch);
        forest.Plant(crowded + 1, ForestSpecies.Spruce);

        forest.Update(12.0);

        Assert.False(forest.TryGetStand(crowded, out _),
            "Self-thinning must remove a light-demanding stand that loses the canopy race.");
        Assert.True(forest.TryGetStand(crowded - 1, out _));
        Assert.True(forest.TryGetStand(crowded + 1, out _));
    }

    [Fact]
    public void EmptyForest_NeverRegeneratesWithoutASeedSource()
    {
        TestHabitat habitat = new TestHabitat(96, seed: 88);
        ForestSystem forest = new ForestSystem(habitat, secondsPerYear: 1.0);
        forest.Clear();

        forest.Update(50.0);

        Assert.Equal(0, forest.Count);
    }

    [Fact]
    public void EverySpecies_HasAProfileAndADistinctCrownShape()
    {
        ForestSpecies[] species =
        {
            ForestSpecies.Spruce, ForestSpecies.Birch, ForestSpecies.Oak, ForestSpecies.Beech
        };

        HashSet<TreeCrownShape> shapes = new HashSet<TreeCrownShape>();
        foreach (ForestSpecies value in species)
        {
            ForestSpeciesProfile profile = ForestSpeciesProfile.For(value);
            Assert.True(profile.MatureAgeYears > 0f);
            Assert.True(profile.MaximumAgeYears > profile.MatureAgeYears);
            Assert.True(profile.MaximumBiomass > 0f);
            Assert.True(profile.MoistureTolerance > 0f && profile.ElevationTolerance > 0f);
            Assert.InRange(profile.ShadeTolerance, 0f, 1f);
            Assert.True(shapes.Add(profile.CrownShape), $"{value} reuses another species' crown shape.");
        }
    }

    [Fact]
    public void EverySpecies_HasAVisiblyDistinctTreeSilhouette()
    {
        ForestSpecies[] species =
        {
            ForestSpecies.Spruce, ForestSpecies.Birch, ForestSpecies.Oak, ForestSpecies.Beech
        };

        foreach (ForestSpecies value in species)
        {
            Terrain.TreeModel model = Terrain.TreeModel.For(value);
            Assert.True(model.TrunkHeight > 0f && model.TrunkRadius > 0f);
            Assert.True(model.CrownRadius > model.TrunkRadius);
            Assert.True(model.CrownHeight > 0f);

            float[] outline = model.CrownOutline;
            Assert.True(outline.Length >= 6 && outline.Length % 2 == 0);
            Assert.Equal(0f, outline[0]);
            Assert.Equal(1f, outline[^2]);
            Assert.Equal(0f, outline[^1]);
            for (int i = 2; i < outline.Length; i += 2)
            {
                Assert.True(outline[i] > outline[i - 2], $"{value} outline must climb monotonically.");
                Assert.InRange(outline[i + 1], 0f, 1f);
            }
        }

        Terrain.TreeModel spruce = Terrain.TreeModel.For(ForestSpecies.Spruce);
        Terrain.TreeModel birch = Terrain.TreeModel.For(ForestSpecies.Birch);
        Terrain.TreeModel oak = Terrain.TreeModel.For(ForestSpecies.Oak);
        Terrain.TreeModel beech = Terrain.TreeModel.For(ForestSpecies.Beech);

        // The silhouettes each species is supposed to read as, from across the map.
        Assert.True(spruce.TotalHeight > oak.TotalHeight, "Spruce must tower over oak.");
        Assert.True(oak.CrownRadius > spruce.CrownRadius, "Oak must spread wider than spruce.");
        Assert.True(beech.BareStemFraction > spruce.BareStemFraction,
            "Beech must show a long clean bole where spruce branches almost from the ground.");
        Assert.True(oak.TrunkRadius > birch.TrunkRadius, "Birch must look slender next to oak.");
        Assert.True(oak.CrownRadius * 2f > oak.CrownHeight, "The oak crown must be wider than it is tall.");
        Assert.True(spruce.CrownHeight > spruce.CrownRadius * 2f * 2f, "The spruce crown must be a narrow spire.");
        // Conifer tiering now comes from the whorl build in the renderer rather than from a
        // wavy outline; what the model has to carry is the silhouette family that selects it.
        Assert.Equal(TreeCrownShape.Spire, spruce.Shape);
        // A broadleaf crown swells once and then closes again: one smooth dome.
        foreach (ForestSpecies broadleaf in new[] { ForestSpecies.Birch, ForestSpecies.Oak, ForestSpecies.Beech })
            Assert.Equal(1, RadiusReversals(Terrain.TreeModel.For(broadleaf).CrownOutline));
    }

    /// <summary>Counts how many times a crown outline switches between widening and narrowing.</summary>
    private static int RadiusReversals(float[] outline)
    {
        int reversals = 0;
        int previous = 0;
        for (int i = 3; i < outline.Length; i += 2)
        {
            int direction = Math.Sign(outline[i] - outline[i - 2]);
            if (direction != 0 && previous != 0 && direction != previous) reversals++;
            if (direction != 0) previous = direction;
        }
        return reversals;
    }

    private static void AssertStatisticsMatchStands(ForestSystem forest, int tileCount)
    {
        int count = 0;
        int mature = 0;
        float biomass = 0f;
        float health = 0f;
        for (int tileId = 0; tileId < tileCount; tileId++)
        {
            if (!forest.TryGetStand(tileId, out ForestStand stand)) continue;
            count++;
            if (stand.Maturity >= 1f) mature++;
            biomass += stand.Biomass;
            health += stand.Health;
        }

        ForestStatistics statistics = forest.Statistics;
        Assert.Equal(count, statistics.StandCount);
        Assert.Equal(mature, statistics.MatureStandCount);
        Assert.Equal(biomass, statistics.TotalBiomass, 3);
        Assert.Equal(count == 0 ? 0f : health / count, statistics.AverageHealth, 3);
    }

    private static int FindFirstStand(ForestSystem forest, int tileCount)
    {
        for (int tileId = 0; tileId < tileCount; tileId++)
            if (forest.TryGetStand(tileId, out _)) return tileId;
        throw new InvalidOperationException("The deterministic test habitat generated no forest stands.");
    }

    private static int FindFirstEmptyTile(ForestSystem forest, int tileCount)
    {
        for (int tileId = 0; tileId < tileCount; tileId++)
            if (!forest.TryGetStand(tileId, out _)) return tileId;
        throw new InvalidOperationException("The deterministic test habitat generated no empty tile.");
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
