namespace ForesTycoon.Tests;

/// <summary>
/// The water balance as the project's stock-and-flow rules, in a running world: rain fills the stores, a dry spell
/// empties them, water runs downhill, snow builds up and melts, and the water balance stays closed throughout.
/// </summary>
[Collection("SpeciesBook")]
public sealed class WaterRulesTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static void Run(GameWorld world, double seconds) { for (int i = 0; i < seconds * 30; i++) world.Update(1d / 30); }
    private static RuleScopeField Field(string name) => RuleEngine.Scope("tile").Fields.Single(f => f.Name == name);
    private static double Total(GameWorld world, string field) => Enumerable.Range(0, world.Map.Tiles.Count).Sum(t => Field(field).Read(world, t));
    private static void Closed(GameWorld world) =>
        Assert.InRange(Math.Abs(world.Environment.BalanceError), 0, 1e-6 * Math.Max(1, world.Environment.StoredWater));

    [Fact]
    public void EveryWaterProcessIsAnEditableStarterRule()
    {
        using var world = World();
        var rules = world.RuleProjectDocument.Rules;
        foreach (string id in new[] { "water.precipitation", "water.snowmelt", "water.infiltration", "water.evaporation", "water.uptake", "water.drainage", "water.runoff" }) {
            var rule = Assert.Single(rules, r => r.Id == id);
            Assert.True(rule.Enabled); Assert.Equal("tile", rule.Scope);
            Assert.All(rule.Effects, e => { Assert.Equal("flow", e.Mode); Assert.NotNull(e.Influence); });
        }
    }

    [Fact]
    public void RainFillsTheStoresAndTheBalanceStaysClosed()
    {
        using var world = World();
        world.Environment.ForceWeather(WeatherPreset.Rain, 30, 120);
        double stored = world.Environment.StoredWater, soil = Total(world, "rootWater");
        Run(world, 40);
        Assert.Null(world.RuleBook.LastError);
        Assert.True(world.RuleBook.FlowTotals.GetValueOrDefault("eső a talajra") > 0);
        Assert.True(world.Environment.StoredWater > stored);
        Assert.True(Total(world, "rootWater") > soil);
        Closed(world);
    }

    [Fact]
    public void ADrySpellEmptiesTheStoresWithoutInventingWater()
    {
        using var world = World();
        world.Environment.ForceWeather(WeatherPreset.Sunny, 0, 300);
        Run(world, 2);
        double stored = world.Environment.StoredWater, rain = world.RuleBook.FlowTotals.GetValueOrDefault("eső a talajra");
        Run(world, 30);
        Assert.Equal(rain, world.RuleBook.FlowTotals.GetValueOrDefault("eső a talajra"));
        Assert.True(world.RuleBook.FlowTotals.GetValueOrDefault("talajpárolgás") + world.RuleBook.FlowTotals.GetValueOrDefault("párologtatás (fák)") > 0);
        Assert.True(world.Environment.StoredWater < stored);
        Closed(world);
    }

    [Fact]
    public void StandingWaterRunsDownhillAndTheSoilStaysBounded()
    {
        using var world = World();
        // Only the runoff moves water here, so where the water ends up is the runoff's doing.
        var project = world.RuleProjectDocument; project.Algorithms = null;
        foreach (var r in project.Rules.Where(r => r.Id.StartsWith("water.") && r.Id != "water.runoff")) r.Enabled = false;
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        var level = Field("waterLevel"); var surface = Field("surfaceWater"); var neighbours = new int[4];
        int tile = Enumerable.Range(0, world.Map.Tiles.Count).First(t => world.Map.GetTileNeighbours(t, neighbours) == 4
            && Field("outlet").Read(world, t) == 0 && neighbours.All(m => Field("outlet").Read(world, m) == 0)
            && neighbours.Any(m => level.Read(world, m) < level.Read(world, t) - .05));
        surface.Write(world, tile, 200);
        int tiles = world.Map.Tiles.Count;
        double Water() => Enumerable.Range(0, tiles).Sum(t => surface.Read(world, t));
        // The water's centre of mass: water running downhill lowers it.
        double Height() => Enumerable.Range(0, tiles).Sum(t => surface.Read(world, t) * Field("elevation").Read(world, t)) / Water();
        double water = Water(), height = Height(), outflow = world.RuleBook.FlowTotals.GetValueOrDefault("kifolyás");
        Run(world, 3);
        Assert.True(surface.Read(world, tile) < 200, $"source kept {surface.Read(world, tile)} mm");
        Assert.True(world.RuleBook.FlowTotals.GetValueOrDefault("lefolyás") > 0, "no runoff");
        Assert.True(Height() < height, $"water did not run downhill: {height} -> {Height()} m");
        Assert.Equal(water, Water() + world.RuleBook.FlowTotals.GetValueOrDefault("kifolyás") - outflow, 6);
        for (int t = 0; t < tiles; t++) Assert.InRange(Field("rootWater").Read(world, t), 0, Field("saturation").Read(world, t) + 1e-9);
        Closed(world);
    }

    [Fact]
    public void SnowBuildsUpInTheColdAndMeltsWhenItIsWarm()
    {
        using var world = World();
        world.Environment.ForceWeather(WeatherPreset.Snow, 20, 120);
        double snow = Total(world, "snow");
        Run(world, 30);
        double built = Total(world, "snow");
        Assert.True(built > snow);
        Closed(world);
        world.Environment.ForceWeather(WeatherPreset.Sunny, 0, 600);
        Assert.True(world.Environment.Temperature > 0);
        Run(world, 30);
        Assert.True(Total(world, "snow") < built);
        Assert.True(world.RuleBook.FlowTotals.GetValueOrDefault("hóolvadás") > 0);
        Closed(world);
    }

    [Fact]
    public void WindDriftsLyingSnowWithoutCreatingWater()
    {
        using var world = World();
        var snow = Field("snow");
        for (int t = 0; t < world.Map.Tiles.Count; t++) snow.Write(world, t, 20);
        // No snowfall, only wind: the precipitation rule is switched off for this test.
        var project = world.RuleProjectDocument; project.Algorithms = null;
        project.Rules.Single(r => r.Id == "water.precipitation").Enabled = false;
        project.Rules.Single(r => r.Id == "water.snowmelt").Enabled = false;
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        world.Environment.ForceWeather(WeatherPreset.Snow, 0, 300);
        double total = Total(world, "snow");
        Run(world, 20);
        var depths = Enumerable.Range(0, world.Map.Tiles.Count).Select(t => snow.Read(world, t)).ToArray();
        Assert.True(depths.Max() - depths.Min() > .5, "Wind failed to vary snow depth across tiles.");
        Assert.Equal(total, depths.Sum(), 6);
        Closed(world);
    }
}
