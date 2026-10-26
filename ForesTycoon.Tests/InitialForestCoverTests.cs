namespace ForesTycoon.Tests;

public sealed class InitialForestCoverTests
{
    [Theory]
    [InlineData(42, 6)] [InlineData(7, 6)] [InlineData(42, 16)] [InlineData(7, 16)]
    public void AGeneratedMapGrowsWoodsAndClearingsOnLowlandsAndHillsAlike(int seed, int relief)
    {
        var project = GameWorld.DefaultRuleProject(); project.Algorithms = null;
        project.World = GameWorld.DefaultScenario(seed, 64); project.World.MaxHeight = relief;
        project.World.ElevationZones = ElevationZoneDefinition.Defaults(relief);
        using var world = WorldLoader.CreateProject(project);
        var habitat = (IForestHabitat)world.Map;
        int columns = world.TileGrid.Columns, rows = world.TileGrid.Rows, support = 0, forested = 0, clustered = 0;
        bool Forest(int c, int r) => c >= 0 && r >= 0 && c < columns && r < rows && world.TileForest(c * rows + r) > 0;
        for (int t = 0; t < columns * rows; t++) {
            if (habitat.CanSupportForest(t)) support++;
            if (world.TileForest(t) <= 0) continue;
            forested++;
            int c = t / rows, r = t % rows;
            if ((Forest(c - 1, r) ? 1 : 0) + (Forest(c + 1, r) ? 1 : 0) + (Forest(c, r - 1) ? 1 : 0) + (Forest(c, r + 1) ? 1 : 0) >= 2) clustered++;
        }
        double cover = (double)forested / support, inWoods = (double)clustered / forested;
        Assert.InRange(cover, .20, .55);
        Assert.True(inWoods > .85, $"trees should stand in woods, not scattered: {inWoods:P0}");
    }
}
