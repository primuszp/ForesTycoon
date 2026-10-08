using System.Text.Json;

namespace ForesTycoon.Tests;

public class RegionalClimateTests
{
    private sealed class Habitat(int seed = 42) : IForestHabitat
    {
        public int TileCount => 32 * 32;
        public int Seed => seed;
        public (int Columns, int Rows) TileGrid => (32, 32);
        public double Elevation;
        public bool CanSupportForest(int id) => true;
        public float GetMoisture(int id) => .5f;
        public float GetNormalizedElevation(int id) => id == 17 ? (float)Elevation : 0;
        public int GetAdjacentTileIds(int id, Span<int> result) => 0;
    }
    private static readonly WeatherForcing Forcing = new(.8, 20, .6, 2);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    [Fact]
    public void RegionsAreSmoothSeededAndIndependentOfQueryOrder()
    {
        var first = new RegionalClimate(new Habitat(), ClimateDefinition.Default);
        var same = new RegionalClimate(new Habitat(), ClimateDefinition.Default);
        var other = new RegionalClimate(new Habitat(43), ClimateDefinition.Default);
        Assert.Equal(first.RainMultipliers.Values.ToArray(), same.RainMultipliers.Values.ToArray());
        Assert.NotEqual(Json(first.RainMultipliers.Values.ToArray()), Json(other.RainMultipliers.Values.ToArray()));
        Assert.True(first.RainMultipliers.Values.ToArray().Max() - first.RainMultipliers.Values.ToArray().Min() > .05);
        for (int id = 1023; id >= 0; id--) Assert.Equal(first.Cell(id, Forcing), same.Cell(id, Forcing));
        for (int x = 0; x < 31; x++) for (int y = 0; y < 31; y++) {
            int id = x * 32 + y;
            Assert.InRange(Math.Abs(first.RainMultipliers[id] - first.RainMultipliers[id + 1]), 0, .1);
            Assert.InRange(Math.Abs(first.RainMultipliers[id] - first.RainMultipliers[id + 32]), 0, .1);
        }
    }

    [Fact]
    public void TerrainEditChangesOnlyLocalTemperatureWithoutRegeneratingRegions()
    {
        var habitat = new Habitat(); var climate = new RegionalClimate(habitat, ClimateDefinition.Default);
        var before = climate.Cell(17, Forcing); var distant = climate.Cell(700, Forcing);
        var rain = climate.RainMultipliers.Values.ToArray(); habitat.Elevation = 1;
        Assert.Equal(before.Forcing.Temperature - ClimateDefinition.Default.ElevationCooling, climate.Cell(17, Forcing).Forcing.Temperature);
        Assert.Equal(before.RainMultiplier, climate.Cell(17, Forcing).RainMultiplier);
        Assert.Equal(distant, climate.Cell(700, Forcing)); Assert.Equal(rain, climate.RainMultipliers.Values.ToArray());
    }

    [Fact]
    public void LocalRainAndEvaporationAffectWaterWithAConservedRegionalLedger()
    {
        var habitat = new Habitat();
        var regional = new EnvironmentSystem(habitat, null, 120, ClimateDefinition.Default);
        var uniform = new EnvironmentSystem(habitat, null, 120, ClimateDefinition.Legacy);
        regional.ForceWeather(WeatherPreset.Storm, 32, 20); uniform.ForceWeather(WeatherPreset.Storm, 32, 20);
        regional.Update(100); uniform.Update(100);
        Assert.NotEqual(regional.Cell(0), regional.Cell(1023));
        Assert.NotEqual(uniform.StoredWater, regional.StoredWater);
        Assert.InRange(Math.Abs(regional.BalanceError), 0, 1e-5);
        Assert.InRange(Math.Abs(regional.RainReceived - regional.TotalRain * regional.Climate.RainMultipliers.Values.ToArray().Sum()), 0, 1e-6);
    }

    [Fact]
    public void RegionalCheckpointContinuesExactlyAcrossEventsAndMonthBoundaries()
    {
        var settings = TerrainSettings.Default.WithNodeSize(17, 42);
        var map = new TerrainMap(settings); var original = new Ecosystem(map, 120, SoilLandscapeDefinition.Default, ClimateDefinition.Default);
        original.Environment.ForceWeather(WeatherPreset.Storm, 32, 20); original.Update(30.733333);
        var state = JsonSerializer.Deserialize<EcologyCheckpoint>(Json(original.Capture()))!;
        var cloneMap = new TerrainMap(settings); var clone = new Ecosystem(cloneMap, 120, SoilLandscapeDefinition.Default, state.Climate);
        cloneMap.Restore(map.Capture()); clone.Restore(state);
        for (int i = 0; i < 4000; i++) { original.Update(1.0 / 30); clone.Update(1.0 / 30); }
        Assert.Equal(Json(original.Capture()), Json(clone.Capture()));
        Assert.InRange(Math.Abs(original.Environment.BalanceError), 0, 1e-5);
        var wrong = new Ecosystem(new TerrainMap(settings), 120, SoilLandscapeDefinition.Default, ClimateDefinition.Legacy);
        Assert.Throws<InvalidDataException>(() => wrong.Restore(state));
        Assert.Throws<InvalidDataException>(() => clone.Restore(state with { Environment = state.Environment with { RainReceived = null } }));
    }

    [Fact]
    public void UniformClimatePreservesAllForcingAndOldSaveModels()
    {
        var climate = new RegionalClimate(new Habitat(), ClimateDefinition.Legacy);
        for (int id = 0; id < 1024; id++) Assert.Equal(new ClimateCell(Forcing, 1), climate.Cell(id, Forcing));
        foreach (int version in new[] { 4, 5, 6, 7, 8 }) {
            var save = new WorldSaveData { Version = version, Climate = ClimateDefinition.Default };
            Assert.Equal(ClimateDefinition.Legacy, save.ReplayClimate);
        }
    }

    [Fact]
    public void ClimateConfigurationIsPinnedAndRequiredInNewSaves()
    {
        var definition = new ClimateDefinition(1, 16, 3, .2, 5, .05);
        var save = new WorldSaveData { Climate = definition, SoilModel = SoilModelData.From(SoilLandscapeDefinition.Default) };
        using var stream = new MemoryStream(); WorldSaveSerializer.Write(stream, save); stream.Position = 0;
        Assert.Equal(definition, WorldSaveSerializer.Read(stream).ReplayClimate);
        Assert.Throws<InvalidOperationException>(() => new WorldSaveData { SoilModel = save.SoilModel }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClimateDefinition(1, 0, 4, .35, 6.5, .1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClimateDefinition(1, 24, double.NaN, .35, 6.5, .1));
        Assert.Throws<ArgumentException>(() => new ClimateDefinition(0, 24, 1, 0, 0, 0));
        Assert.Throws<NotSupportedException>(() => new ClimateDefinition(2, 24, 4, .35, 6.5, .1));
    }
}
