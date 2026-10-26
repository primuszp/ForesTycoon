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

    [Theory]
    [InlineData(42)] [InlineData(3)] [InlineData(11)]
    public void ZonesShapeTheNaturalInitialForest(int seed)
    {
        var project = GameWorld.DefaultRuleProject(); project.Algorithms = null;
        project.World = GameWorld.DefaultScenario(seed, 64); project.World.MaxHeight = 16;
        project.World.ElevationZones = ElevationZoneDefinition.Defaults(16);
        using var world = WorldLoader.CreateProject(project);
        var habitat = (IForestHabitat)world.Map;
        Assert.Equal(ForestPattern.Natural, habitat.ForestPattern);
        var support = new int[4]; var forested = new int[4]; var upland = new int[4];
        for (int t = 0; t < world.TileGrid.Columns * world.TileGrid.Rows; t++) {
            if (!habitat.CanSupportForest(t)) continue;
            int zone = Math.Clamp(world.TileZoneIndex(t), 0, 3);
            support[zone]++;
            ForestSpecies species = world.TileSpecies(t);
            if (world.TileForest(t) <= 0) continue;
            forested[zone]++;
            if (species is ForestSpecies.Beech or ForestSpecies.Spruce or ForestSpecies.Fir or ForestSpecies.Larch) upland[zone]++;
        }
        double Cover(int z) => (double)forested[z] / Math.Max(1, support[z]);
        string report = string.Join(" ", Enumerable.Range(0, 4).Select(z => $"{z}:{forested[z]}/{support[z]} up{upland[z]}"));
        Assert.True(Cover(2) > Cover(1), "hills should be denser than the plain: " + report);
        Assert.True(forested[3] == 0 || upland[3] > forested[3] / 2, "mountain woods should be beech and conifers: " + report);
        Assert.True(upland[1] < forested[1] / 3, "the plain should keep broadleaves: " + report);
    }
}
