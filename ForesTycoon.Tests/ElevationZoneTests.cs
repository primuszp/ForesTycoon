namespace ForesTycoon.Tests;

public sealed class ElevationZoneTests
{
    [Fact]
    public void EachLevelBelongsToTheFirstZoneThatReachesItAndTheLastZoneIsOpenUpwards()
    {
        var scenario = new WorldScenario();
        string Zone(int level) => scenario.ElevationZones[scenario.ZoneOf(level)].Id;
        Assert.Equal("coast", Zone(0)); Assert.Equal("coast", Zone(1));
        Assert.Equal("plain", Zone(2)); Assert.Equal("plain", Zone(3));
        Assert.Equal("hills", Zone(4)); Assert.Equal("hills", Zone(5));
        Assert.Equal("mountain", Zone(6)); Assert.Equal("mountain", Zone(500));
    }

    [Fact]
    public void ASceneSavedBeforeZonesReadsTheBuiltInZonesAndInvalidZonesAreRefused()
    {
        var old = RuleProject.FromJson("{\"schema\":\"forest-rule-project\",\"version\":5,\"name\":\"régi\",\"world\":{\"name\":\"régi pálya\"}}").World;
        Assert.Equal(ElevationZoneDefinition.Defaults().Select(z => z.Id), old.ElevationZones.Select(z => z.Id));

        var scenario = GameWorld.DefaultScenario();
        scenario.Validate();
        scenario.ElevationZones[2].MaxLevel = scenario.ElevationZones[1].MaxLevel;
        Assert.Throws<InvalidDataException>(scenario.Validate);
        scenario.ElevationZones = ElevationZoneDefinition.Defaults(); scenario.ElevationZones[1].Id = "coast";
        Assert.Throws<InvalidDataException>(scenario.Validate);
        scenario.ElevationZones = new();
        Assert.Throws<InvalidDataException>(scenario.Validate);
    }

    [Fact]
    public void ATileTakesItsZoneFromItsLowestCornerAndItsSlopeFromItsCorners()
    {
        Assert.Equal(TerrainSlope.Flat, TileShapeInfo.FromCorners(2, 2, 2, 2).Slope);
        Assert.Equal(TerrainSlope.Gentle, TileShapeInfo.FromCorners(2, 3, 3, 2).Slope);
        Assert.Equal(TerrainSlope.Steep, TileShapeInfo.FromCorners(2, 3, 4, 3).Slope);

        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        var zones = new WorldScenario();
        int tiles = world.TileGrid.Columns * world.TileGrid.Rows;
        for (int tile = 0; tile < tiles; tile++)
            Assert.Equal(zones.ElevationZones[zones.ZoneOf(world.TileLevel(tile))].Id, world.TileZone(tile).Id);
    }
}
