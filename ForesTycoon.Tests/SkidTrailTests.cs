using System.Text.Json;

namespace ForesTycoon.Tests;

public class SkidTrailTests
{
    private static readonly TerrainSettings Settings = TerrainSettings.Default.WithNodeSize(17, 42);
    private static TerrainMap Flat() => new(Settings, (_, _) => 4);

    [Fact]
    public void MarkedTrailCrossesRoadsWithoutBecomingRoad()
    {
        var map = Flat(); map.BuildRoadTilePath(50, 50);
        int[] marked = map.MarkSkidTrailPath(34, 98);
        Assert.Equal(new[] { 34, 66, 82, 98 }, marked);
        Assert.False(map.IsSkidTrail(50)); Assert.True(map.IsRoadTile(50));
        Assert.Equal(0f, map.GetSkidTrailWear(66));
        Assert.Empty(map.MarkSkidTrailPath(34, 98)); // nothing new
    }

    [Fact]
    public void CheckpointRoundTripsTrailsAndRejectsBadOnesWithoutChanges()
    {
        var map = Flat(); map.MarkSkidTrailPath(34, 82); map.SetSkidTrailState(50, .5f, 0); map.SetSkidTrailState(50, .4f, .25f);
        var state = JsonSerializer.Deserialize<TerrainCheckpoint>(JsonSerializer.Serialize(map.Capture()))!;
        var restored = Flat(); restored.Restore(state);
        Assert.Equal(map.GetSkidTrailWear(50), restored.GetSkidTrailWear(50));
        Assert.Equal(map.GetSkidTrailIdle(66), restored.GetSkidTrailIdle(66));
        Assert.Equal(map.GetSkidTrailEdges(66), restored.GetSkidTrailEdges(66));
        var bad = state with { SkidTrails = new[] { new SkidTrailCheckpoint(34, RoadEdge.WS, 2f, 0) } };
        var target = Flat(); target.MarkSkidTrailPath(130, 146);
        Assert.ThrowsAny<System.Exception>(() => target.Restore(bad));
        Assert.True(target.IsSkidTrail(130));
        var old = Flat(); old.Restore(state with { SkidTrails = null });
        Assert.Equal(0, old.SkidTrailCount);
    }

    [Fact]
    public void TrailCommandRecordsReplay()
    {
        var record = new SkidTrailPathCommand(3, 9, true).ToRecord(5);
        Assert.Equal(WorldCommandKind.SkidTrailPath, record.Kind);
        Assert.IsType<SkidTrailPathCommand>(WorldCommandFactory.Create(record));
    }
}
