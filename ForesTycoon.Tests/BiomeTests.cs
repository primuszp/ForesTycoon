namespace ForesTycoon.Tests;

public sealed class BiomeTests
{
    [Fact]
    public void ABiomeComesFromTheZoneAndSeveralBiomesShareAZoneInPatchesByTheirShare()
    {
        var scene = new WorldScenario { Seed = 7 };
        Assert.Equal("dune-coast", scene.Biomes[scene.BiomeOf(0, 5, 5)].Id);
        Assert.Equal("beech-spruce", scene.Biomes[scene.BiomeOf(9, 5, 5)].Id);

        scene.Biomes.Add(new BiomeDefinition { Id = "dry-grass", Name = "Száraz gyep", Zone = "plain", Color = "#C8B060", Share = 3 });
        var picks = new Dictionary<string, int>();
        for (int column = 0; column < 128; column++)
            for (int row = 0; row < 128; row++) {
                string id = scene.Biomes[scene.BiomeOf(2, column, row)].Id;
                picks[id] = picks.GetValueOrDefault(id) + 1;
            }
        Assert.Equal(2, picks.Count);
        Assert.True(picks["dry-grass"] > picks["floodplain"], "the larger share should cover more of the zone");
        Assert.Equal(scene.BiomeOf(2, 40, 41), new WorldScenario { Seed = 7, Biomes = scene.Biomes }.BiomeOf(2, 40, 41));

        scene.Biomes.RemoveAll(b => b.Zone == "hills");
        Assert.Equal(-1, scene.BiomeOf(4, 0, 0));
    }

    [Fact]
    public void BiomesMustBelongToAZoneAndASceneWithoutBiomesReadsTheBuiltInOnes()
    {
        var old = RuleProject.FromJson("{\"schema\":\"forest-rule-project\",\"version\":5,\"name\":\"régi\",\"world\":{\"name\":\"régi pálya\"}}").World;
        Assert.Equal(BiomeDefinition.Defaults().Select(b => b.Id), old.Biomes.Select(b => b.Id));
        var scenario = GameWorld.DefaultScenario();
        scenario.Biomes[0].Zone = "nowhere";
        Assert.Throws<InvalidDataException>(scenario.Validate);
        scenario.Biomes = BiomeDefinition.Defaults(); scenario.Biomes[1].Share = 0;
        Assert.Throws<InvalidDataException>(scenario.Validate);
    }

    [Fact]
    public void RulesReadTheTilesZoneBiomeAndSlope()
    {
        using var world = WorldLoader.CreateStart(WorldLoader.OpeningScenePath()!, enableRendering: false);
        var tile = GameWorld.RuleObjects.Find("tile");
        Assert.Contains(tile.Fields, f => f.Name == "zone");
        Assert.Contains(tile.Fields, f => f.Name == "biome");
        Assert.Contains(tile.Fields, f => f.Name == "slope" && f.Kind == RuleFieldKind.Enum && f.Options.Length == 3);
        var scene = world.RuleProjectDocument.World!;
        int rows = world.TileGrid.Rows, tiles = world.TileGrid.Columns * rows;
        for (int id = 0; id < tiles; id++) {
            Assert.Equal(scene.ZoneOf(world.TileLevel(id)), world.TileZoneIndex(id));
            Assert.Equal(scene.BiomeOf(world.TileLevel(id), id / rows, id % rows), world.TileBiomeIndex(id));
        }
    }
}
