namespace ForesTycoon.Tests;

public class WorldSaveSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesVersionedSettingsAndCommandTicks()
    {
        WorldSaveData expected = new WorldSaveData
        {
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
