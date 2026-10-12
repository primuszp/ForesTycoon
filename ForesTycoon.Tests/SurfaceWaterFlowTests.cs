namespace ForesTycoon.Tests;

public sealed class SurfaceWaterFlowTests
{
    // A width × height grid of tiles, id = x * height + y, four-neighbour links.
    private static (SurfaceWaterFlow Flow, double[] Surface) Grid(int width, int height, Func<int, int, double> groundMm, Func<int, int, bool> outlet = null)
    {
        int n = width * height;
        var ground = new double[n]; var links = new int[n][]; var outlets = new bool[n];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++) {
                int id = x * height + y;
                ground[id] = groundMm(x, y); outlets[id] = outlet?.Invoke(x, y) ?? false;
                var near = new List<int>();
                if (x > 0) near.Add(id - height); if (x < width - 1) near.Add(id + height);
                if (y > 0) near.Add(id - 1); if (y < height - 1) near.Add(id + 1);
                links[id] = near.ToArray();
            }
        return (new SurfaceWaterFlow(ground, links, outlets), new double[n]);
    }
    private static void Run(SurfaceWaterFlow flow, double[] surface, int steps) { for (int i = 0; i < steps; i++) flow.Step(surface, 0.5); }

    [Fact]
    public void WaterSpreadsOnFlatGroundAndKeepsItsVolume()
    {
        var (flow, surface) = Grid(10, 1, (_, _) => 0);
        surface[0] = 1000;
        Run(flow, surface, 400);
        Assert.Equal(1000, surface.Sum(), 6);
        Assert.True(surface[9] > 50, "water should reach the far end of a flat strip");
        Assert.True(surface.Max() - surface.Min() < 20, "a flat pool levels out");
    }

    [Fact]
    public void WaterNeverClimbsAboveItsOwnLevel()
    {
        // Pool behind a 2 m wall: the water surface stays below the wall top, so nothing crosses.
        var (flow, surface) = Grid(5, 1, (x, _) => x == 2 ? 2000 : 0);
        surface[0] = 800; surface[1] = 800;
        Run(flow, surface, 300);
        Assert.Equal(0, surface[3] + surface[4], 9);
        Assert.Equal(1600, surface[0] + surface[1], 6);
    }

    [Fact]
    public void ADitchCarriesWaterAlongLevelGroundBetweenHigherBanks()
    {
        // A level ditch (row y = 1) 1 m below its banks; water poured at its head runs to its far end, the banks stay dry.
        var (flow, surface) = Grid(12, 3, (_, y) => y == 1 ? 0 : 1000);
        surface[0 * 3 + 1] = 600;
        Run(flow, surface, 600);
        Assert.True(surface[11 * 3 + 1] > 10, "the water follows the ditch");
        Assert.True(Enumerable.Range(0, 12).All(x => surface[x * 3] == 0 && surface[x * 3 + 2] == 0), "banks above the water stay dry");
    }

    [Fact]
    public void AnOverfullChannelFloodsItsBanks()
    {
        var (flow, surface) = Grid(6, 3, (_, y) => y == 1 ? 0 : 300);
        for (int x = 0; x < 6; x++) surface[x * 3 + 1] = 2000; // far more than the 0.3 m deep channel holds
        Run(flow, surface, 300);
        Assert.True(Enumerable.Range(0, 6).Sum(x => surface[x * 3] + surface[x * 3 + 2]) > 3000, "the flood spreads onto the banks");
        Assert.Equal(12000, surface.Sum(), 6);
    }

    [Fact]
    public void WaterRunsDownhillToTheEdgeAndLeavesTheMap()
    {
        var (flow, surface) = Grid(8, 1, (x, _) => (8 - x) * 500.0, (x, _) => x == 7);
        surface[0] = 500;
        double exported = 0;
        for (int i = 0; i < 600; i++) exported += flow.Step(surface, 0.5);
        Assert.True(exported > 400);
        Assert.Equal(500, surface.Sum() + exported, 6);
        Assert.True(surface[0] <= SurfaceWaterFlow.RetainedDepth + 1e-9, "the slope above drains to its puddle film");
    }

    [Fact]
    public void ASpringInTheWorldKeepsTheWaterBalanceThroughSaveAndLoad()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        int tile = Enumerable.Range(0, world.Map.Tiles.Count).First(t => !world.IsWaterTile(t) && world.TileElevation(t) > .3f);
        world.Environment.Springs = new Dictionary<int, double> { [tile] = 400 };
        for (int i = 0; i < 900; i++) world.Update(1.0 / 30);
        var e = world.Environment;
        Assert.True(e.SpringReceived > 0);
        Assert.True(Math.Abs(e.BalanceError) < 1e-6 * Math.Max(1, e.StoredWater));
        using var stream = new MemoryStream(); world.Save(stream); stream.Position = 0;
        using var loaded = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false); loaded.Load(stream);
        Assert.Equal(e.SpringReceived, loaded.Environment.SpringReceived, 6);
    }
}
