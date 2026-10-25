namespace ForesTycoon.Tests;

public sealed class SoilProfileTests
{
    [Fact]
    public void EveryTileOfTheOpeningSceneHasItsSoilsBuiltInProfileScaledForTheDiorama()
    {
        using var world = WorldLoader.CreateStart(WorldLoader.OpeningScenePath()!, enableRendering: false);
        var scene = world.RuleProjectDocument.World!;
        int tiles = world.TileGrid.Columns * world.TileGrid.Rows;
        Assert.True(tiles > 0);
        for (int tile = 0; tile < tiles; tile++)
        {
            var layers = world.SideProfile(tile);
            var soil = scene.SoilTypes[world.SoilIndex(tile)];
            var expected = soil.Profile();
            Assert.Equal(expected.Count, layers.Length);
            for (int i = 0; i < layers.Length; i++)
                Assert.Equal((float)(expected[i].Depth * scene.ProfileScale), layers[i].Depth, 4);
        }
    }

    [Fact]
    public void AuthoredHorizonsReplaceTheBuiltInProfileAndInvalidOnesAreRefused()
    {
        var soil = GameWorld.DefaultScenario().SoilTypes.Single(s => s.Id == "loam");
        Assert.Equal(4, soil.Profile().Count);
        soil.Horizons = new() { new() { Name = "A", Depth = .4, Color = "#553311" } };
        Assert.Single(soil.Profile());

        var scenario = GameWorld.DefaultScenario();
        scenario.SoilTypes[0].Horizons = new() { new() { Name = "A", Depth = 0, Color = "#553311" } };
        Assert.Throws<InvalidDataException>(scenario.Validate);
        scenario.SoilTypes[0].Horizons = new();
        scenario.ProfileScale = 0;
        Assert.Throws<InvalidDataException>(scenario.Validate);
    }
}
