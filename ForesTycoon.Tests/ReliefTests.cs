namespace ForesTycoon.Tests;

public sealed class ReliefTests
{
    private static GameWorld Build(int maxHeight)
    {
        var project = GameWorld.DefaultRuleProject(); project.Algorithms = null;
        project.World = GameWorld.DefaultScenario(42, 64);
        project.World.MaxHeight = maxHeight;
        project.World.ElevationZones = ElevationZoneDefinition.Defaults(maxHeight);
        return WorldLoader.CreateProject(project);
    }

    [Fact]
    public void ATallSceneRisesIntoHillsAndMountainsWithSteepSlopes()
    {
        using var world = Build(16);
        Assert.Equal(16, world.Map.MaximumHeight);
        int tiles = world.TileGrid.Columns * world.TileGrid.Rows;
        var zones = new int[4]; int steep = 0, top = 0;
        for (int t = 0; t < tiles; t++) {
            zones[world.TileZoneIndex(t)]++; top = Math.Max(top, world.TileLevel(t));
            if (world.TileSlope(t) == TerrainSlope.Steep) steep++;
        }
        Assert.True(top > 6, $"the relief should rise above the old six levels, reached {top}");
        Assert.All(zones, count => Assert.True(count > tiles / 50, "every zone should cover a real share of the map: " + string.Join(",", zones)));
        Assert.True(steep > 0);
    }

    [Fact]
    public void ScenesKeepTheirOldReliefAndTheReliefStaysInRange()
    {
        var old = RuleProject.FromJson("{\"schema\":\"forest-rule-project\",\"version\":5,\"name\":\"régi\",\"world\":{\"name\":\"régi pálya\"}}").World;
        Assert.Equal(6, old.MaxHeight);
        Assert.Equal(new[] { 1, 3, 5, 99 }, ElevationZoneDefinition.Defaults(6).Select(z => z.MaxLevel));
        Assert.Equal(new[] { 1, 4, 8, 99 }, ElevationZoneDefinition.Defaults(16).Select(z => z.MaxLevel));
        var scenario = GameWorld.DefaultScenario();
        scenario.MaxHeight = 17;
        Assert.Throws<InvalidDataException>(scenario.Validate);
        scenario.MaxHeight = 3;
        Assert.Throws<InvalidDataException>(scenario.Validate);
    }
}
