namespace ForesTycoon.Tests;

[Collection("Graphics backend")]
public class TerrainVisibilityTests
{
    [Fact]
    public void CameraAndShadowFootprintsReuseTheirSelectionsWithoutLosingTerrainEdits()
    {
        var environment = new RenderEnvironment(new GraphicsBackendTests.RecordingGraphicsBackend());
        using var active = environment.Activate();
        using var resources = environment;
        using var terrain = new Terrain(new TerrainMap(TerrainSettings.Default.WithNodeSize(65, 42), (_, _) => 4), deferGeometry: true);
        var main = Context(-64, -36, 64, 36);
        var shadow = Context(-104, -76, 104, 76);
        terrain.UpdateVisibleTiles(main); int mainChunks = terrain.VisibleChunkCount;
        terrain.GetVisibleWeatherBounds(out var min, out var max);
        terrain.UpdateVisibleTiles(shadow); int shadowChunks = terrain.VisibleChunkCount;
        Assert.True(shadowChunks >= mainChunks);
        for (int i = 0; i < 20; i++)
        {
            terrain.UpdateVisibleTiles(main); Assert.Equal(mainChunks, terrain.VisibleChunkCount);
            terrain.GetVisibleWeatherBounds(out var actualMin, out var actualMax);
            Assert.Equal(min, actualMin); Assert.Equal(max, actualMax);
            terrain.UpdateVisibleTiles(shadow); Assert.Equal(shadowChunks, terrain.VisibleChunkCount);
        }
        Assert.Equal(2, terrain.VisibilitySelectionsBuilt);
        terrain.Map.EditElevationAtNode(32 * 65 + 32, 1, 0, 1);
        terrain.UpdateVisibleTiles(main);
        Assert.Equal(2, terrain.VisibilitySelectionsBuilt);
        terrain.UpdateVisibleTiles(Context(10000, 10000, 11000, 11000));
        Assert.Equal(0, terrain.VisibleChunkCount);
        terrain.UpdateVisibleTiles(main); Assert.Equal(mainChunks, terrain.VisibleChunkCount);
        Assert.Equal(3, terrain.VisibilitySelectionsBuilt);
        terrain.UpdateVisibleTiles(Context(-64, -36, 64, 36, -30, 45));
        Assert.Equal(4, terrain.VisibilitySelectionsBuilt);
    }

    private static RenderContext Context(double minX, double minY, double maxX, double maxY, float tilt = -60, float yaw = -45) =>
        new(0, 0, 0, 0, 0, 0, false, false, 1, tilt, yaw, minX, minY, maxX, maxY, 10);
}
