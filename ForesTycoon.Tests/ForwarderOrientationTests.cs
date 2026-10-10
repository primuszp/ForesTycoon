using OpenTK.Mathematics;
namespace ForesTycoon.Tests;
public class ForwarderOrientationTests
{
    [Theory]
    [InlineData(34, 210, 98, 99, false)]
    [InlineData(34, 46, 40, 56, false)]
    [InlineData(34, 210, 98, 98, true)]
    [InlineData(34, 46, 40, 40, true)]
    [InlineData(34, 210, 98, 98, false)]
    public void StackAndLoadingFrameFollowTheRoadTileLongAxis(int start, int end, int dock, int stack, bool trail)
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
        if (trail) map.MarkSkidTrailPath(start, end);
        else map.BuildRoadTilePath(start, end, RoadPaving.Macadam);
        map.TryGetTileCenter(start, out Vector3 a); map.TryGetTileCenter(end, out Vector3 b);
        Vector2 road = (b-a).Xy.Normalized();
        ForestMachineRenderer.StackPlace(map, stack, out _, out Vector2 heading);
        Assert.InRange(Math.Abs(Vector2.Dot(road, heading)), .9999f, 1.0001f);
        Vector2 frame = ForestMachineRenderer.NetworkHeading(map, dock, heading);
        Assert.InRange(Math.Abs(Vector2.Dot(frame, heading)), .9999f, 1.0001f);
    }
}
