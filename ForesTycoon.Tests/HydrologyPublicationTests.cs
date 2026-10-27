namespace ForesTycoon.Tests;

public class HydrologyPublicationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void BrushEditNeverRemovesDistantWaterBetweenWorkSlices(int delta)
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 1);
        var distant = map.GetNode(1, 1);
        float depth = map.Hydrology.NodeWaterDepth[distant.Id];
        Assert.True(depth > 0);
        var changed = new WorkResult<int[]>();
        int steps = 0;
        foreach (int unit in map.EditElevationAtNodeWork(map.GetNode(8, 8).Id, delta, 1, 1, changed))
        {
            Assert.Equal(depth, map.Hydrology.NodeWaterDepth[distant.Id]);
            steps++;
        }
        Assert.True(steps > 1000);
        Assert.NotEmpty(changed.Value);
        Assert.Equal(depth, map.Hydrology.NodeWaterDepth[distant.Id]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SlicedRebuildKeepsPublishedWaterUntilCompletion(bool cancel)
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42),
            (u, v) => u >= 5 && u <= 11 && v >= 5 && v <= 11 ? 0 : 1);
        var hydro = map.Hydrology;
        var depths = hydro.NodeWaterDepth;
        var moisture = hydro.TileMoisture;
        var standing = hydro.StandingWaterTileIds;
        var oldDepths = depths.ToArray();
        var oldMoisture = moisture.ToArray();
        var oldStanding = standing.OrderBy(id => id).ToArray();
        Assert.Contains(depths, depth => depth > 0);
        // Change the input without publishing a new water state.
        foreach (var node in map.Nodes) node.zPos = 10;
        using var work = hydro.RebuildWork().GetEnumerator();
        int steps = 0;
        while (work.MoveNext())
        {
            Assert.Same(depths, hydro.NodeWaterDepth);
            Assert.Same(moisture, hydro.TileMoisture);
            Assert.Same(standing, hydro.StandingWaterTileIds);
            Assert.Equal(oldDepths, hydro.NodeWaterDepth);
            Assert.Equal(oldMoisture, hydro.TileMoisture);
            Assert.Equal(oldStanding, hydro.StandingWaterTileIds.OrderBy(id => id));
            if (++steps == 1000 && cancel) break;
        }
        Assert.True(steps >= 1000);
        if (cancel)
        {
            work.Dispose();
            Assert.Same(depths, hydro.NodeWaterDepth);
            Assert.Equal(oldDepths, hydro.NodeWaterDepth);
        }
        else
        {
            Assert.NotSame(depths, hydro.NodeWaterDepth);
            Assert.NotSame(moisture, hydro.TileMoisture);
            Assert.All(hydro.NodeWaterDepth, depth => Assert.Equal(0f, depth));
        }
    }
}
