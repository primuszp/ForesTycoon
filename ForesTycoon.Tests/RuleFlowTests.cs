namespace ForesTycoon.Tests;

/// <summary>
/// Flows: the system-dynamics building block. What leaves one stock arrives in another (or leaves the model through a
/// named sink); never more than the source holds; shared among neighbours evenly or downhill; totalled in a ledger.
/// </summary>
[Collection("SpeciesBook")]
public sealed class RuleFlowTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static RuleScopeField Field(string scope, string name) => RuleEngine.Scope(scope).Fields.Single(f => f.Name == name);

    /// <summary>A rule moving <paramref name="amount"/> per step (not per year) from one stock to another, on one tile only.</summary>
    private static void Flow(GameWorld world, int tile, string from, string to, double amount, string spread = "even", string level = "")
    {
        var project = world.RuleProjectDocument; project.Algorithms = null;
        // Only this flow runs, so the starter water rules do not move the stocks it is measured on.
        foreach (var r in project.Rules) r.Enabled = false;
        project.Rules.Add(new WorldRule { Id = "flow", Name = "Áramlás", Scope = "tile", Enabled = true, Condition = $"abs(index - {tile}) < 0,5",
            Effects = { new() { Target = from, Mode = "flow", To = to, Spread = spread, Level = level, FlowName = "próba",
                Influence = new() { PerYear = false, Terms = { new() { Name = "állandó", Decrease = false, Amount = amount } } } } } });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        Assert.Null(world.RuleBook.LastError);
    }

    [Fact]
    public void AFlowMovesBetweenStocksAndTheLedgerAndWaterBalanceAgree()
    {
        using var world = World();
        const int tile = 5;
        Field("tile", "surfaceWater").Write(world, tile, 10);
        double deep = Field("tile", "deepWater").Read(world, tile);
        Flow(world, tile, "surfaceWater", "deepWater", 4);
        world.RuleBook.Run(world, .01, 0);
        Assert.Equal(6, Field("tile", "surfaceWater").Read(world, tile), 6);
        Assert.Equal(deep + 4, Field("tile", "deepWater").Read(world, tile), 6);
        Assert.Equal(4, world.RuleBook.FlowTotals["próba"], 6);
        Assert.InRange(world.Environment.BalanceError, -1e-6, 1e-6);
    }

    [Fact]
    public void AFlowNeverTakesMoreThanTheSourceHoldsAndASinkTakesItOutOfTheModel()
    {
        using var world = World();
        const int tile = 5;
        Field("tile", "surfaceWater").Write(world, tile, 3);
        double exchanged = world.Environment.RuleExchanged;
        Flow(world, tile, "surfaceWater", "", 100);
        world.RuleBook.Run(world, .01, 0);
        Assert.Equal(0, Field("tile", "surfaceWater").Read(world, tile), 6);
        Assert.Equal(3, world.RuleBook.FlowTotals["próba"], 6);
        Assert.Equal(exchanged - 3, world.Environment.RuleExchanged, 6);
    }

    [Fact]
    public void ADownhillFlowFillsOnlyLowerNeighboursAndConservesTheWater()
    {
        using var world = World();
        var level = Field("tile", "waterLevel"); var surface = Field("tile", "surfaceWater");
        var neighbours = new int[4];
        // A tile with at least one lower and one higher-or-level neighbour.
        int tile = Enumerable.Range(0, world.Map.Tiles.Count).First(t => {
            int n = world.Map.GetTileNeighbours(t, neighbours);
            return n == 4 && neighbours.Any(m => level.Read(world, m) < level.Read(world, t) - 1e-3) && neighbours.Any(m => level.Read(world, m) > level.Read(world, t));
        });
        world.Map.GetTileNeighbours(tile, neighbours);
        surface.Write(world, tile, 50);
        double total = surface.Read(world, tile) + neighbours.Sum(m => surface.Read(world, m));
        var higher = neighbours.Where(m => level.Read(world, m) >= level.Read(world, tile)).ToDictionary(m => m, m => surface.Read(world, m));
        Flow(world, tile, "surfaceWater", "neighbours.surfaceWater", 30, "downhill", "waterLevel");
        world.RuleBook.Run(world, .01, 0);
        Assert.Equal(20, surface.Read(world, tile), 6);
        Assert.Equal(total, surface.Read(world, tile) + neighbours.Sum(m => surface.Read(world, m)), 6);
        foreach (var (m, before) in higher) Assert.Equal(before, surface.Read(world, m), 9);
    }

    [Fact]
    public void ATreeCanDrawWaterFromItsTileThroughTheRelation()
    {
        using var world = World();
        var tree = RuleEngine.Scope("tree");
        Assert.True(tree.Elements(world).Length > 0);
        Assert.NotNull(Field("tree", "tile.rootWater").Write);
        int tile = (int)Field("tree", "tile.index").Read(world, 0);
        double before = Field("tile", "rootWater").Read(world, tile);
        var project = world.RuleProjectDocument; project.Algorithms = null;
        foreach (var r in project.Rules) r.Enabled = false;
        project.Rules.Add(new WorldRule { Id = "uptake", Name = "Felvétel", Scope = "tree", Enabled = true, Condition = "abs(index) < 0,5",
            Effects = { new() { Target = "tile.rootWater", Mode = "flow", To = "", FlowName = "párologtatás",
                Influence = new() { PerYear = false, Terms = { new() { Name = "állandó", Decrease = false, Amount = 1 } } } } } });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        world.RuleBook.Run(world, .01, 0);
        Assert.Null(world.RuleBook.LastError);
        Assert.Equal(Math.Max(0, before - 1), Field("tile", "rootWater").Read(world, tile), 6);
    }
}
