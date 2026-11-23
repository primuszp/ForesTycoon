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

    [Theory]
    [InlineData(49)]
    [InlineData(97)]
    public void GeneratorSupportsEditorSizesBetweenPowersOfTwo(int nodes)
    {
        var settings = CreateSettings(nodeColumns: nodes, nodeRows: nodes);
        var generator = new TerrainGenerator(settings.Seed);
        generator.Generate(nodes, nodes, settings.MaxHeight, out var heights, out var rivers);
        new TerrainGenerator(settings.Seed).Generate(nodes, nodes, settings.MaxHeight, out var repeated, out var repeatedRivers);
        Assert.Equal(nodes, heights.GetLength(0));
        Assert.Equal(nodes, heights.GetLength(1));
        Assert.Equal(heights.Cast<int>(), repeated.Cast<int>());
        Assert.Equal(rivers.Cast<bool>(), repeatedRivers.Cast<bool>());
        Assert.True(heights.Cast<int>().Distinct().Count() >= 3);
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
