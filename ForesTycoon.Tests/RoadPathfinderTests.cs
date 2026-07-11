namespace ForesTycoon.Tests;

public class RoadPathfinderTests
{
    [Fact]
    public void FindPath_FollowsConnectedRoadEdges()
    {
        RoadNetwork roads = new RoadNetwork();
        roads.Add(5, RoadEdge.EN);
        roads.Add(6, RoadEdge.WS | RoadEdge.EN);
        roads.Add(7, RoadEdge.WS);

        int[] path = RoadPathfinder.FindPath(roads, tilesPerColumn: 4, 5, 7);

        Assert.Equal(new[] { 5, 6, 7 }, path);
    }

    [Fact]
    public void FindPath_DoesNotCrossMismatchedOneWayGeometry()
    {
        RoadNetwork roads = new RoadNetwork();
        roads.Add(5, RoadEdge.EN);
        roads.Add(6, RoadEdge.EN); // missing the matching WS connection

        Assert.Empty(RoadPathfinder.FindPath(roads, 4, 5, 6));
    }

    [Fact]
    public void FindDemoRoute_ReturnsConnectedNetworkDiameterCandidate()
    {
        RoadNetwork roads = new RoadNetwork();
        roads.Add(5, RoadEdge.EN);
        roads.Add(6, RoadEdge.WS | RoadEdge.EN);
        roads.Add(7, RoadEdge.WS);

        int[] route = RoadPathfinder.FindDemoRoute(roads, 4);

        Assert.Equal(3, route.Length);
        Assert.Contains(5, route);
        Assert.Contains(7, route);
    }
}
