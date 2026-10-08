namespace ForesTycoon.Tests;

public class WorldSaveSerializerTests
{
    [Fact]
    public void VersionFourKeepsLegacyCalendarEvenWithoutTempoField()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            "{\"version\":4,\"tickRate\":30,\"terrain\":{},\"commands\":[]}"));
        Assert.Equal(1200, WorldSaveSerializer.Read(stream).ReplayForestYearSeconds);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void SaveVersionPreservesCalendarAndTerrainEditRules(int version, bool legacyEdits)
    {
        using var stream = new MemoryStream();
        WorldSaveSerializer.Write(stream, new WorldSaveData { Climate = ClimateDefinition.Legacy, Version = version, ForestYearSeconds = 120 });
        stream.Position = 0;
        var loaded = WorldSaveSerializer.Read(stream);
        Assert.Equal(version, loaded.Version);
        Assert.Equal(legacyEdits, loaded.ReplayLegacyTerrainEdits);
        Assert.Equal(120, loaded.ReplayForestYearSeconds);
        var edit = new WorldCommandRecord(1, WorldCommandKind.EditElevation, 7, 1, 0, 1, false);
        var historical = loaded.ReplayCommand(edit);
        Assert.Equal(legacyEdits, historical.Flag);
        Assert.Equal(historical, WorldCommandFactory.Create(historical).ToRecord(1));
        Assert.False(new EditElevationCommand(7, 1, 0, 1).ToRecord(1).Flag);
    }

    [Theory]
    [InlineData(0)] [InlineData(119)] [InlineData(1201)] [InlineData(double.NaN)]
    public void InvalidCalendarIsRejectedBeforeReplay(double tempo)
        => Assert.Throws<InvalidOperationException>(() => new WorldSaveData { Climate = ClimateDefinition.Legacy, ForestYearSeconds = tempo }.ValidateReplay());

    [Fact]
    public void RoundTrip_PreservesVersionedSettingsAndCommandTicks()
    {
        WorldSaveData expected = new WorldSaveData
        { Climate = ClimateDefinition.Legacy,
            SoilModel = SoilModelData.From(SoilLandscapeDefinition.Default),
            Tick = 12,
            TickRate = 30,
            Terrain = TerrainSettingsData.From(TerrainSettings.Default.WithSeed(1234)),
            Commands = new List<WorldCommandRecord>
            {
                new WorldCommandRecord(2, WorldCommandKind.EditElevation, 7, 1, 2, 3, false),
                new WorldCommandRecord(8, WorldCommandKind.RoadPath, 10, 14, 0, 0, false),
                new WorldCommandRecord(10, WorldCommandKind.PlantForest, 18, (int)ForestSpecies.Spruce, 0, 0, false),
                new WorldCommandRecord(11, WorldCommandKind.HarvestForest, 19, 0, 0, 0, false)
            }
        };
        using MemoryStream stream = new MemoryStream();

        WorldSaveSerializer.Write(stream, expected);
        stream.Position = 0;
        WorldSaveData actual = WorldSaveSerializer.Read(stream);

        Assert.Equal(WorldSaveData.CurrentVersion, actual.Version);
        Assert.Equal((ulong)12, actual.Tick);
        Assert.Equal(1234, actual.Terrain.Seed);
        Assert.Equal(expected.Commands, actual.Commands);
    }

    [Fact]
    public void Read_RejectsObsoleteFormatRatherThanSelectingLegacySystems()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            "{\"version\":1,\"tickRate\":30,\"terrain\":{},\"commands\":[]}"));
        Assert.Throws<NotSupportedException>(() => WorldSaveSerializer.Read(stream));
    }

    [Fact]
    public void Read_RejectsUnknownSaveVersion()
    {
        using MemoryStream stream = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes("{\"version\":999,\"tickRate\":30,\"terrain\":{},\"commands\":[]}"));

        Assert.Throws<NotSupportedException>(() => WorldSaveSerializer.Read(stream));
    }
}
