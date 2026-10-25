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

public sealed class BiomeSiteRuleTests
{
    private static BehaviorNode Input(string id, string key) => new() { Id = id, Name = id, Operation = BehaviorOperation.Input, Key = key };
    private static BehaviorNode Constant(string id, double value) => new() { Id = id, Name = id, Operation = BehaviorOperation.Constant, Value = value };
    private static BehaviorNode Op(string id, BehaviorOperation op, string a, string b) => new() { Id = id, Name = id, Operation = op, A = a, B = b };

    [Fact]
    public void ASiteRuleCanFavourASpeciesInABiome()
    {
        var project = GameWorld.DefaultRuleProject();
        var site = project.Behaviors.Graphs.Single(g => g.Hook == "forest.site");
        double spruce = (int)ForestSpecies.Spruce;
        // 0.2 + 0.8 · [biome = 3] · [species = spruce], equality as two comparisons.
        site.Nodes = new() {
            Input("biome", "tile.biome"), Input("species", "species.kind"),
            Constant("b-low", 2.5), Constant("b-high", 3.5), Constant("s-low", spruce - .5), Constant("s-high", spruce + .5),
            Op("b1", BehaviorOperation.Greater, "biome", "b-low"), Op("b2", BehaviorOperation.Less, "biome", "b-high"),
            Op("s1", BehaviorOperation.Greater, "species", "s-low"), Op("s2", BehaviorOperation.Less, "species", "s-high"),
            Op("b", BehaviorOperation.Multiply, "b1", "b2"), Op("s", BehaviorOperation.Multiply, "s1", "s2"),
            Op("both", BehaviorOperation.Multiply, "b", "s"), Constant("gain", .8), Op("bonus", BehaviorOperation.Multiply, "both", "gain"),
            Constant("base", .2), Op("fitness", BehaviorOperation.Add, "base", "bonus") };
        site.Output = "fitness";
        var program = EcologyResponseProgram.FromModel(project.Behaviors);
        Assert.Equal(1, program.Fitness(.5, .5, 1, .5, .4, zone: 3, biome: 3, slope: 1, species: (int)ForestSpecies.Spruce), 9);
        Assert.Equal(.2, program.Fitness(.5, .5, 1, .5, .4, zone: 3, biome: 3, slope: 1, species: (int)ForestSpecies.Oak), 9);
        Assert.Equal(.2, program.Fitness(.5, .5, 1, .5, .4, zone: 1, biome: 1, slope: 0, species: (int)ForestSpecies.Spruce), 9);
    }

    [Fact]
    public void TheForestReadsTheLandscapeOfTheRunningSceneThroughItsMap()
    {
        using var world = WorldLoader.CreateStart(WorldLoader.OpeningScenePath()!, enableRendering: false);
        var habitat = (IForestHabitat)world.Map;
        int tiles = world.TileGrid.Columns * world.TileGrid.Rows;
        for (int id = 0; id < tiles; id += 7)
            Assert.Equal((world.TileZoneIndex(id), world.TileBiomeIndex(id), (int)world.TileSlope(id)), habitat.GetLandscape(id));
    }
}
