namespace ForesTycoon.Tests;

/// <summary>
/// Rules read many objects at once through collections (the trees on a tile, its neighbours) as summaries, and every
/// vehicle exposes its technical data. All of it comes from the object model; nothing is listed by hand.
/// </summary>
[Collection("SpeciesBook")]
public sealed class RuleCollectionTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static RuleScopeField Field(string scope, string name) => RuleEngine.Scope(scope).Fields.Single(f => f.Name == name);

    [Fact]
    public void TheTileSummarisesItsTreesAndNeighbours()
    {
        using var world = World();
        var tree = RuleEngine.Scope("tree");
        Assert.True(tree.Elements(world).Length > 0);
        int tile = (int)Field("tree", "tile.index").Read(world, 0);

        var heights = tree.Elements(world).Where(id => (int)Field("tree", "tile.index").Read(world, id) == tile)
            .Select(id => Field("tree", "height").Read(world, id)).ToArray();
        Assert.Equal(heights.Length, Field("tile", "trees.count").Read(world, tile));
        Assert.Equal(heights.Average(), Field("tile", "trees.avg.height").Read(world, tile), 4);
        Assert.Equal(heights.Max(), Field("tile", "trees.max.height").Read(world, tile), 4);
        Assert.Equal(heights.Sum(), Field("tile", "trees.sum.height").Read(world, tile), 3);

        // A tree reads its stand through its tile; the neighbour count is 2–4 (fewer on the map edge).
        Assert.Equal(heights.Length, Field("tree", "tile.trees.count").Read(world, 0));
        Assert.InRange(Field("tile", "neighbours.count").Read(world, tile), 2, 4);
        Assert.True(Field("tile", "neighbours.avg.rootWater").Read(world, tile) >= 0);
        // Reading a summary does not disturb the tree being visited.
        Assert.Equal(tile, (int)Field("tree", "tile.index").Read(world, 0));
    }

    [Fact]
    public void ARuleCanUseASummaryAsASource()
    {
        using var world = World();
        var project = world.RuleProjectDocument; project.Algorithms = null;
        project.Rules.Add(new WorldRule { Id = "stand", Name = "Állomány", Scope = "tree", Enabled = true,
            Effects = { new() { Target = "stressYears", Mode = "set", Formula = "tile.trees.count" } } });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        world.RuleBook.Run(world, .1, 0);
        Assert.Null(world.RuleBook.LastError);
        Assert.Equal(Field("tree", "tile.trees.count").Read(world, 0), Field("tree", "stressYears").Read(world, 0), 6);
    }

    [Fact]
    public void VehiclesAndMachinesExposeTheirTechnicalData()
    {
        var truck = RuleEngine.Scope("truck").Fields.Select(f => f.Name).ToArray();
        Assert.Contains("power", truck); Assert.Contains("maxTractiveForce", truck); Assert.Contains("fuelPerKilowattHour", truck);
        var machine = RuleEngine.Scope("machine");
        Assert.NotNull(machine);
        Assert.NotNull(machine.Fields.Single(f => f.Name == "wear").Write);
        Assert.Contains(machine.Fields, f => f.Name == "capacity");
    }
}
