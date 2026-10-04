namespace ForesTycoon.Tests;

public class ForestEnvironmentCouplingTests
{
    private sealed class NeighbourHabitat : IForestHabitat
    {
        public int TileCount => 2;
        public int Seed => 829;
        public bool CanSupportForest(int id) => (uint)id < 2;
        public float GetMoisture(int id) => .65f;
        public float GetNormalizedElevation(int id) => .45f;
        public int GetAdjacentTileIds(int id, Span<int> target) { target[0] = 1 - id; return 1; }
    }

    private sealed class Habitat(float moisture = .65f, SoilProperties? soil = null) : IForestHabitat
    {
        public int TileCount => 1;
        public int Seed => 829;
        public bool CanSupportForest(int id) => id == 0;
        public float GetMoisture(int id) => moisture;
        public float GetNormalizedElevation(int id) => .45f;
        public int GetAdjacentTileIds(int id, Span<int> target) => 0;
        public SoilProperties GetSoilProperties(int id) => soil ?? SoilProperties.Standard;
    }

    private static (ForestSystem Forest, EnvironmentSystem Environment, ForestEnvironmentCoordinator Clock) Create(
        int trees = 1, float moisture = .65f, SoilProperties? soil = null, double secondsPerYear = 1200)
    {
        var habitat = new Habitat(moisture, soil);
        var forest = new ForestSystem(habitat, secondsPerYear);
        forest.Clear(); forest.Plant(0, ForestSpecies.Oak);
        forest.IndividualTrees.TryGet(0, out var patch);
        while (patch.Count > trees) forest.IndividualTrees.RemoveLiving(patch, patch.Count - 1);
        for (int i = 0; i < patch.Count; i++)
            patch.Trees[i] = patch.Trees[i] with {
                BirthYear = -20, Dimensions = new(.3f, 12, 2), AnnualGrowth = default, Health = 1
            };
        forest.NotifyIndividualVisualEdit();
        var environment = new EnvironmentSystem(habitat, forest);
        return (forest, environment, new ForestEnvironmentCoordinator(forest, environment));
    }

    [Fact]
    public void DenserCanopyConsumesMoreRootWaterAndInterceptsMoreRain()
    {
        var sparse = Create(); var dense = Create(trees: 24);
        sparse.Environment.ForceWeather(WeatherPreset.Sunny, 0, 600);
        dense.Environment.ForceWeather(WeatherPreset.Sunny, 0, 600);
        sparse.Clock.Update(60); dense.Clock.Update(60);
        Assert.True(dense.Environment.Transpired > sparse.Environment.Transpired * 10);
        Assert.True(dense.Forest.HydrologyInputs(0).InterceptionCapacity > sparse.Forest.HydrologyInputs(0).InterceptionCapacity);
        Assert.InRange(Math.Abs(sparse.Environment.BalanceError), 0, 1e-8);
        Assert.InRange(Math.Abs(dense.Environment.BalanceError), 0, 1e-8);
    }

    [Fact]
    public void HarvestImmediatelyReleasesNeighbourLightWithoutChangingItsSizeOrHealth()
    {
        var habitat = new NeighbourHabitat();
        var forest = new ForestSystem(habitat, secondsPerYear: 1200);
        forest.Clear();
        for (int id = 0; id < 2; id++)
        {
            forest.Plant(id, ForestSpecies.Oak);
            forest.IndividualTrees.TryGet(id, out var patch);
            while (patch.Count > 1) forest.IndividualTrees.RemoveLiving(patch, patch.Count - 1);
            patch.Trees[0] = patch.Trees[0] with {
                U = id == 0 ? .99f : .01f, V = .5f, BirthYear = -20,
                Dimensions = new(.3f, id == 0 ? 5 : 20, 2), AnnualGrowth = default, Health = 1
            };
        }
        forest.NotifyIndividualVisualEdit();
        _ = new ForestEnvironmentCoordinator(forest, new EnvironmentSystem(habitat, forest));
        forest.IndividualTrees.TryGet(0, out var survivor);
        var before = survivor.Trees[0];
        forest.Harvest(1, out _);
        var after = survivor.Trees[0];

        Assert.True(after.Resources.Light > before.Resources.Light);
        Assert.True(after.AnnualGrowth.Diameter > before.AnnualGrowth.Diameter);
        Assert.Equal(before with { Resources = after.Resources, AnnualGrowth = after.AnnualGrowth }, after);
    }

    [Fact]
    public void HarvestTransfersInterceptedWaterToGroundAndStopsTreeUptake()
    {
        var world = Create(trees: 24);
        world.Environment.ForceWeather(WeatherPreset.Rain, 24, 90);
        world.Environment.Update(30);
        Assert.True(world.Environment.Cell(0).Canopy > 0);
        double stored = world.Environment.StoredWater;
        double transpired = world.Environment.Transpired;

        world.Forest.Harvest(0, out _);
        Assert.Equal(stored, world.Environment.StoredWater);
        world.Environment.Update(.5);

        Assert.Equal(0, world.Environment.Cell(0).Canopy);
        Assert.Equal(0, world.Environment.Cell(0).DemandPerHour);
        Assert.Equal(transpired, world.Environment.Transpired);
        Assert.InRange(Math.Abs(world.Environment.BalanceError), 0, 1e-8);
    }

    [Fact]
    public void UptakeCannotOverdrawSharedRootZoneOrInventWater()
    {
        var soil = new SoilProperties(3, 1, .5, 12, 0, 1);
        var world = Create(trees: 36, soil: soil);
        world.Environment.ForceWeather(WeatherPreset.Sunny, 0, 600);
        world.Environment.Update(600); // Isolated hydrology; no forest mortality changes this demand fixture.
        var cell = world.Environment.Cell(0);
        Assert.InRange(cell.Soil, soil.WiltingPoint - 1e-10, soil.Saturation);
        Assert.InRange(world.Environment.Transpired, 0, soil.Saturation - soil.WiltingPoint);
        Assert.True(cell.UptakePerHour < cell.DemandPerHour);
        Assert.InRange(Math.Abs(world.Environment.BalanceError), 0, 1e-8);
    }

    [Fact]
    public void OvercastWeatherReducesActualLightAndGrowth()
    {
        var sunny = Create(); var cloudy = Create();
        sunny.Environment.ForceWeather(WeatherPreset.Sunny, 0, 600);
        cloudy.Environment.ForceWeather(WeatherPreset.Cloudy, 0, 600);
        sunny.Clock.Update(300); cloudy.Clock.Update(300);
        sunny.Forest.IndividualTrees.TryGet(0, out var first);
        cloudy.Forest.IndividualTrees.TryGet(0, out var second);
        Assert.True(first.Trees[0].Resources.Light > second.Trees[0].Resources.Light);
        Assert.True(first.Trees[0].AnnualGrowth.Diameter > second.Trees[0].AnnualGrowth.Diameter);
        Assert.True(ForestTree.Volume(first.Trees[0].At(sunny.Forest.ForestYear))
            > ForestTree.Volume(second.Trees[0].At(cloudy.Forest.ForestYear)));
    }

    [Fact]
    public void RainRestoresGrowthOnInitiallyDryButFertileSoil()
    {
        var world = Create(moisture: .05f);
        Assert.Equal(0, ForestSystem.Fitness(ForestSpecies.Oak, .05f, .45f));
        double before = world.Environment.CurrentWaterFactor(0, ForestSpecies.Oak);
        world.Environment.ForceWeather(WeatherPreset.Rain, 24, 90);
        world.Clock.Update(100);
        Assert.True(world.Environment.CurrentWaterFactor(0, ForestSpecies.Oak) > before);
        world.Forest.IndividualTrees.TryGet(0, out var patch);
        Assert.True(patch.Trees[0].AnnualGrowth.Diameter > 0);
        Assert.InRange(Math.Abs(world.Environment.BalanceError), 0, 1e-8);
    }

    [Fact]
    public void SoilFertilityLimitsGrowthIndependentlyOfWaterStorage()
    {
        var rich = Create();
        var poor = Create(soil: SoilProperties.Standard with { Fertility = .25f });
        Assert.Equal(rich.Environment.Cell(0).Soil, poor.Environment.Cell(0).Soil);
        rich.Forest.IndividualTrees.TryGet(0, out var first);
        poor.Forest.IndividualTrees.TryGet(0, out var second);
        Assert.Equal(first.Trees[0].Resources.Water, second.Trees[0].Resources.Water);
        Assert.True(first.Trees[0].AnnualGrowth.Diameter > second.Trees[0].AnnualGrowth.Diameter);
    }

    [Fact]
    public void WaterUpdatesDoNotAdvanceForestryAndPausePreservesBoth()
    {
        var world = Create();
        world.Environment.Update(5);
        Assert.Equal(0, world.Forest.ForestYear);
        var paused = Create();
        var before = paused.Environment.Cell(0);
        paused.Clock.Update(0);
        Assert.Equal(before, paused.Environment.Cell(0));
        Assert.Equal(0, paused.Forest.ForestYear);
    }

    [Theory]
    [InlineData(1200)]
    [InlineData(13)]
    public void CoupledWeatherAndGrowthAreIndependentOfFrameGrouping(double secondsPerYear)
    {
        var first = Create(secondsPerYear: secondsPerYear);
        var second = Create(secondsPerYear: secondsPerYear);
        first.Clock.Update(300);
        for (int frame = 0; frame < 9000; frame++) second.Clock.Update(1.0 / 30);
        Assert.Equal(first.Environment.Time, second.Environment.Time);
        Assert.Equal(first.Environment.Cell(0), second.Environment.Cell(0));
        Assert.Equal(first.Environment.Transpired, second.Environment.Transpired);
        Assert.Equal(first.Forest.Statistics, second.Forest.Statistics);
        first.Forest.IndividualTrees.TryGet(0, out var a);
        second.Forest.IndividualTrees.TryGet(0, out var b);
        Assert.Equal(a.Trees, b.Trees);
    }
}
