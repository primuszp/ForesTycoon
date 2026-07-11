namespace ForesTycoon.Tests;

public class RoadAndFoundationRulesTests
{
    [Fact]
    public void RoadNetwork_MergesAndPartiallyRemovesConnections()
    {
        RoadNetwork roads = new RoadNetwork();
        roads.Add(5, RoadEdge.WS | RoadEdge.EN);
        roads.Add(5, RoadEdge.SE);

        Assert.Equal(RoadEdge.WS | RoadEdge.EN | RoadEdge.SE, roads.GetEdges(5));

        roads.Remove(5, RoadEdge.EN);
        Assert.Equal(RoadEdge.WS | RoadEdge.SE, roads.GetEdges(5));
        Assert.True(roads.Has(5));

        roads.Remove(5, RoadEdge.WS | RoadEdge.SE);
        Assert.False(roads.Has(5));
    }

    [Fact]
    public void LockedRoadSurface_RecognizesFoundationWithoutChangingRoadHeight()
    {
        LockedRoadSurfaceResult result = RoadPlacementRules.ValidateLockedSurface(
            RoadEdge.WS | RoadEdge.EN,
            terrainW: 2, terrainS: 2, terrainE: 2, terrainN: 2,
            surfaceW: 3, surfaceS: 3, surfaceE: 3, surfaceN: 3);

        Assert.Equal(LockedRoadSurfaceResult.FoundationSurface, result);
    }

    [Theory]
    [InlineData(2, 4)] // foundation higher than the supported one-level gap
    [InlineData(3, 2)] // locked road surface below terrain
    public void LockedRoadSurface_RejectsInvalidTerrainGap(int terrainHeight, int surfaceHeight)
    {
        LockedRoadSurfaceResult result = RoadPlacementRules.ValidateLockedSurface(
            RoadEdge.WS | RoadEdge.EN,
            terrainHeight, terrainHeight, terrainHeight, terrainHeight,
            surfaceHeight, surfaceHeight, surfaceHeight, surfaceHeight);

        Assert.Equal(LockedRoadSurfaceResult.Invalid, result);
    }

    [Fact]
    public void RampRoad_MustFollowRampDirection()
    {
        TileShapeInfo ramp = TileShapeInfo.FromCorners(2, 2, 3, 3);

        Assert.True(RoadPlacementRules.IsRampAligned(ramp, RoadEdge.WS | RoadEdge.EN));
        Assert.False(RoadPlacementRules.IsRampAligned(ramp, RoadEdge.SE | RoadEdge.NW));
    }
}
