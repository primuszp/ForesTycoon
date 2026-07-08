namespace ForesTycoon.Tests;

public class TerrainSettingsTests
{
    [Fact]
    public void DefaultSettings_AreValidForCurrentGenerator()
    {
        TerrainSettings settings = TerrainSettings.Default;

        Assert.Equal(65, settings.NodeColumns);
        Assert.Equal(65, settings.NodeRows);
        Assert.Equal(64, settings.TileColumns);
        Assert.Equal(64, settings.TileRows);
    }

    [Fact]
    public void Constructor_RejectsNonSquareGrid()
    {
        Assert.Throws<ArgumentException>(() => CreateSettings(nodeColumns: 65, nodeRows: 33));
    }

    [Fact]
    public void Constructor_RejectsNodeCountThatIsNotPowerOfTwoPlusOne()
    {
        Assert.Throws<ArgumentException>(() => CreateSettings(nodeColumns: 64, nodeRows: 64));
    }

    [Fact]
    public void Constructor_RejectsInvalidMaxHeight()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSettings(maxHeight: 0));
    }

    private static TerrainSettings CreateSettings(
        int nodeColumns = 65,
        int nodeRows = 65,
        int tileWidth = 5,
        int tileHeight = 5,
        int heightScale = 2,
        float minimumWaterDepth = 0.04f,
        float riverWaterHeight = 0.55f,
        float seaLevel = 3.0f,
        int seed = 42,
        int maxHeight = 6) =>
        new TerrainSettings(
            nodeColumns,
            nodeRows,
            tileWidth,
            tileHeight,
            heightScale,
            minimumWaterDepth,
            riverWaterHeight,
            seaLevel,
            seed,
            maxHeight);
}
