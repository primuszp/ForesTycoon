namespace ForesTycoon.Tests;

public class TerrainElevationPlannerTests
{
    [Fact]
    public void Plan_RelaxesNeighboursWithoutMutatingTerrainData()
    {
        TerrainData data = CreateData(maxHeight: 4);
        Node center = data.GetNode(2, 2);
        center.W = 1;

        bool valid = TerrainElevationPlanner.TryCreate(data, center.Id, +1, 4, out Dictionary<int, int> changes);

        Assert.True(valid);
        Assert.Equal(2, changes[center.Id]);
        Assert.Equal(1, changes[data.GetNode(2, 1).Id]);
        Assert.Equal(1, changes[data.GetNode(3, 2).Id]);
        Assert.Equal(1, center.W); // planning is side-effect free
    }

    [Fact]
    public void Plan_RejectsWholeChangeAtomicallyAtHeightLimit()
    {
        TerrainData data = CreateData(maxHeight: 3);
        Node center = data.GetNode(2, 2);
        center.W = 3;

        bool valid = TerrainElevationPlanner.TryCreate(data, center.Id, +1, 3, out Dictionary<int, int> changes);

        Assert.False(valid);
        Assert.Empty(changes);
        Assert.Equal(3, center.W);
    }

    [Fact]
    public void Plan_PropagatesLargeEditsWithoutRecursion()
    {
        TerrainData data = CreateData(maxHeight: 6);
        Node center = data.GetNode(2, 2);

        bool valid = TerrainElevationPlanner.TryCreate(data, center.Id, +6, 6, out Dictionary<int, int> changes);

        Assert.True(valid);
        Assert.Equal(6, changes[center.Id]);
        Assert.Equal(5, changes[data.GetNode(2, 1).Id]);
        Assert.Equal(4, changes[data.GetNode(2, 0).Id]);
    }

    private static TerrainData CreateData(int maxHeight) => new TerrainData(new TerrainSettings(
        nodeColumns: 5, nodeRows: 5, tileWidth: 5, tileHeight: 5, heightScale: 2,
        minimumWaterDepth: 0.04f, riverWaterHeight: 0.55f, seaLevel: 0f,
        seed: 1, maxHeight: maxHeight));
}
