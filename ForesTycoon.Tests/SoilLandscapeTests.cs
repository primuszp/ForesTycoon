using System.Text.Json;
using System.Text.Json.Nodes;

namespace ForesTycoon.Tests;

public class SoilLandscapeTests
{
    private sealed class Habitat(int seed = 42) : IForestHabitat
    {
        public int TileCount => 32 * 24;
        public int Seed => seed;
        public (int Columns, int Rows) TileGrid => (32, 24);
        public ForestTileGeometry GetForestTileGeometry(int id) => new(id / 24 * 16, id % 24 * 16, 16, 16);
        public bool CanSupportForest(int id) => true;
        public float GetMoisture(int id) => .1f + id % 24 / 24f * .8f;
        public float GetNormalizedElevation(int id) => .45f;
        public int GetAdjacentTileIds(int id, Span<int> target) => 0;
    }

    private static SoilLandscapeDefinition Only(int profile)
    {
        var json = JsonNode.Parse(SoilCatalog.Default.Json)!;
        var p = json["profiles"]![profile]!.DeepClone();
        json["profiles"] = new JsonArray(p);
        return new(1, new SoilCatalog(json.ToJsonString()));
    }

    [Fact]
    public void SoilRasterIsDeterministicSpatialAndIndependentOfReadingOrder()
    {
        var a = new SoilLandscape(new Habitat(), SoilLandscapeDefinition.Default);
        var b = new SoilLandscape(new Habitat(), new SoilLandscapeDefinition(1, new SoilCatalog(SoilCatalog.Default.Json)));
        var other = new SoilLandscape(new Habitat(829), SoilLandscapeDefinition.Default);
        Assert.Equal(a.ProfileIndices.ToArray(), b.ProfileIndices.ToArray());
        Assert.NotEqual(a.ProfileIndices.ToArray(), other.ProfileIndices.ToArray());
        Assert.True(a.ProfileIndices.ToArray().Distinct().Count() >= 3);
        for (int id = a.Grid.Count - 1; id >= 0; id--) Assert.Equal(a.Profile(id), b.Profile(id));
        Assert.Equal(3 * 24 + 7, a.Grid.TileId(3, 7));
        Assert.Equal(256, a.Grid.CellAreaSquareMeters);
    }

    [Fact]
    public void RasterOwnsItsValuesAndRejectsMismatchedGeometry()
    {
        var grid = new RasterGrid(2, 3, 16, 8);
        var data = new byte[] { 0, 1, 2, 3, 4, 5 };
        var descriptor = new RasterFieldDescriptor("soil", "Talaj", "profile-id", "soil", true);
        var field = new RasterField<byte>(grid, descriptor, data);
        data[0] = 9; Assert.Equal(0, field[0]);
        Assert.Throws<ArgumentException>(() => new RasterField<byte>(grid, descriptor, new byte[5]));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.TileId(0, 3));
    }

    [Fact]
    public void ProfilesDriveActualWaterStoresDrainageAndForestGrowth()
    {
        Ecosystem Create(int p)
        {
            var world = new Ecosystem(new Habitat(), 120, Only(p));
            world.Forest.Clear(); world.Forest.Plant(0, ForestSpecies.Oak);
            return world;
        }
        var sand = Create(0); var loam = Create(1);
        Assert.Equal(100, sand.Environment.Cell(0).Capacity);
        Assert.Equal(180, loam.Environment.Cell(0).Capacity);
        sand.Environment.ForceWeather(WeatherPreset.Sunny, 0, 300);
        loam.Environment.ForceWeather(WeatherPreset.Sunny, 0, 300);
        sand.Update(120); loam.Update(120);
        Assert.NotEqual(sand.Environment.Cell(0).Soil, loam.Environment.Cell(0).Soil);
        Assert.NotEqual(sand.Forest.LastAnnualGrowthCubicMetres, loam.Forest.LastAnnualGrowthCubicMetres);
        Assert.InRange(Math.Abs(sand.Environment.BalanceError), 0, 1e-6);
        Assert.InRange(Math.Abs(loam.Environment.BalanceError), 0, 1e-6);
    }

    [Fact]
    public void TerraformNeverRegeneratesSoilOrAdvancesWater()
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(33, 42), (_, _) => 3);
        var world = new Ecosystem(map, 120, SoilLandscapeDefinition.Default);
        world.Update(13);
        var indices = world.Soils.ProfileIndices.ToArray();
        double time = world.Environment.Time, water = world.Environment.StoredWater;
        var changed = map.EditElevationAtNode(map.GetNode(10, 10).Id, 1, 1, 1);
        world.ApplyTerrainEdit(changed);
        Assert.NotEmpty(changed); Assert.Equal(indices, world.Soils.ProfileIndices.ToArray());
        Assert.Equal(time, world.Environment.Time); Assert.Equal(water, world.Environment.StoredWater);
    }

    [Theory]
    [InlineData("saturation", 20)]
    [InlineData("infiltrationPerHour", -1)]
    [InlineData("fertility", 1.1)]
    public void InvalidCatalogParametersAreRejected(string field, double value)
    {
        var json = JsonNode.Parse(SoilCatalog.Default.Json)!;
        json["profiles"]![0]!["properties"]![field] = value;
        Assert.Throws<ArgumentOutOfRangeException>(() => new SoilCatalog(json.ToJsonString()));
    }

    [Fact]
    public void CatalogRejectsDuplicateIdsBadColorsAndUnsupportedVersions()
    {
        var json = JsonNode.Parse(SoilCatalog.Default.Json)!;
        json["profiles"]![1]!["id"] = "sand";
        Assert.Throws<InvalidDataException>(() => new SoilCatalog(json.ToJsonString()));
        json["profiles"]![1]!["id"] = "loam"; json["profiles"]![1]!["color"] = "brown";
        Assert.Throws<InvalidDataException>(() => new SoilCatalog(json.ToJsonString()));
        json["version"] = 2;
        Assert.Throws<NotSupportedException>(() => new SoilCatalog(json.ToJsonString()));
        Assert.Throws<NotSupportedException>(() => new SoilLandscapeDefinition(2, SoilCatalog.Default));
    }

    [Fact]
    public void CatalogRejectsMisspelledParametersAndMissingGenerationInputs()
    {
        var json = JsonNode.Parse(SoilCatalog.Default.Json)!;
        json["profiles"]![0]!["properties"]!["fertlity"] = .8;
        Assert.Throws<JsonException>(() => new SoilCatalog(json.ToJsonString()));
        ((JsonObject)json["profiles"]![0]!["properties"]!).Remove("fertlity");
        ((JsonObject)json["profiles"]![0]!["properties"]!).Remove("fertility");
        Assert.Throws<JsonException>(() => new SoilCatalog(json.ToJsonString()));
        json["profiles"]![0]!["properties"]!["fertility"] = .65;
        ((JsonObject)json["profiles"]![0]!).Remove("preferredMoisture");
        Assert.Throws<JsonException>(() => new SoilCatalog(json.ToJsonString()));
    }

    [Theory]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void OldSavesPinTheStandardSoilEvenAfterUpgrade(int version)
    {
        var old = new WorldSaveData { Climate = ClimateDefinition.Legacy, Version = version };
        var definition = old.ReplaySoilModel;
        Assert.Equal(0, definition.GeneratorVersion);
        Assert.Equal(SoilProperties.Standard, definition.Catalog[0].Properties);
        var upgraded = new WorldSaveData { Climate = ClimateDefinition.Legacy, SoilModel = SoilModelData.From(definition) };
        using var stream = new MemoryStream(); WorldSaveSerializer.Write(stream, upgraded); stream.Position = 0;
        var loaded = WorldSaveSerializer.Read(stream);
        Assert.Equal(0, loaded.ReplaySoilModel.GeneratorVersion);
        var original = new Ecosystem(new Habitat());
        var pinned = new Ecosystem(new Habitat(), soils: loaded.ReplaySoilModel);
        original.Update(100); pinned.Update(100);
        Assert.Equal(original.Environment.StoredWater, pinned.Environment.StoredWater);
        Assert.Equal(original.Forest.Statistics, pinned.Forest.Statistics);
    }

    [Fact]
    public void SavePreservesCustomCatalogAndRejectsMissingOrTamperedModel()
    {
        var definition = Only(2);
        var save = new WorldSaveData { Climate = ClimateDefinition.Legacy, SoilModel = SoilModelData.From(definition) };
        using var stream = new MemoryStream(); WorldSaveSerializer.Write(stream, save); stream.Position = 0;
        var loaded = WorldSaveSerializer.Read(stream);
        Assert.Equal(definition.Catalog.Hash, loaded.ReplaySoilModel.Catalog.Hash);
        Assert.Equal("clay", loaded.ReplaySoilModel.Catalog[0].Id);
        Assert.Throws<InvalidOperationException>(() => new WorldSaveData().Validate());
        using var missing = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"version\":7}"));
        Assert.Throws<InvalidOperationException>(() => WorldSaveSerializer.Read(missing));
        var corrupted = new SoilModelData { GeneratorVersion = 1, CatalogJson = definition.Catalog.Json, CatalogHash = "wrong" };
        Assert.Throws<InvalidDataException>(() => corrupted.ToDefinition());
    }
}
