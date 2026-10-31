using System.Text.Json;

namespace ForesTycoon.Tests;

/// <summary>The rule book: rules as data (schedule, scope, condition, effects) run by the game instead of C#.</summary>
[Collection("SpeciesBook")]
public sealed class RuleBookTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);

    private static int[] MarkTrail(GameWorld world)
    {
        int start = Enumerable.Range(0, 256).First(t => world.Map.CanCarrySkidTrail(t) && world.Map.CanCarrySkidTrail(t + 1) && world.Map.CanCarrySkidTrail(t + 2));
        // Isolated rule fixture; player trail commands require a road connection.
        world.Map.MarkSkidTrailPath(start, start + 2);
        var tiles = world.Map.SkidTrailTiles.ToArray();
        Assert.NotEmpty(tiles);
        return tiles;
    }

    private static void Run(GameWorld world, double seconds) { for (int i = 0; i < seconds * 30; i++) world.Update(1d / 30); }

    [Fact]
    public void TrailAgeingIsAStarterDataRuleThatMatchesTheFormerCode()
    {
        using var world = World();
        var rule = Assert.Single(world.RuleProjectDocument.Rules, r => r.Id == "trail.age");
        Assert.Equal("trail.age", rule.Template); Assert.Equal("trail", rule.Scope);
        int tile = MarkTrail(world)[0];
        world.Map.SetSkidTrailState(tile, 0.8f, 0);
        Run(world, 10);
        // Former code: wear − 0,4 × years and idle + years, per step. Idle holds exactly the years that passed.
        double years = world.Map.GetSkidTrailIdle(tile);
        Assert.InRange(years, 8 / EcologyTime.DefaultGameSecondsPerYear, 10.5 / EcologyTime.DefaultGameSecondsPerYear);
        Assert.InRange(world.Map.GetSkidTrailWear(tile), 0.8 - 0.4 * years - 1e-5, 0.8 - 0.4 * years + 1e-5);
    }

    [Fact]
    public void AnOvergrownTrailDisappearsAndADisabledRuleStopsTheAgeing()
    {
        using var world = World();
        var tiles = MarkTrail(world);
        foreach (int t in tiles) world.Map.SetSkidTrailState(t, 0, 100);
        Run(world, 1);
        Assert.Empty(world.Map.SkidTrailTiles);

        using var frozen = World();
        int tile = MarkTrail(frozen)[0];
        var project = frozen.RuleProjectDocument; project.Algorithms = null;
        project.Rules.Single(r => r.Id == "trail.age").Enabled = false;
        frozen.QueueRuleProject(project); frozen.ExecutePendingCommands();
        frozen.Map.SetSkidTrailState(tile, 0.5f, 0);
        Run(frozen, 5);
        Assert.Equal(0.5f, frozen.Map.GetSkidTrailWear(tile));
    }

    [Fact]
    public void ANewRuleWithItsOwnFieldRunsOnTilesAndSurvivesSaveAndLoad()
    {
        using var world = World();
        var project = world.RuleProjectDocument; project.Algorithms = null;
        project.Fields.Add(new RuleFieldDefinition { Name = "saját.nedves_idő", Scope = "tile", Unit = "erdőév", Min = 0, Max = 100 });
        project.Rules.Add(new WorldRule { Id = "nedves", Name = "Nedves idő számlálása", Scope = "tile",
            Condition = "rootWater > 0", Effects = { new() { Target = "saját.nedves_idő", Mode = "add", Formula = "lépés" } } });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        int tile = MarkTrail(world)[0];
        Run(world, 3);
        double counted = world.RuleBook.ReadCustom(world, "saját.nedves_idő", tile);
        // Every tile has root-zone water here, so the counter runs with the trail's idle clock.
        Assert.True(counted > 0);
        Assert.InRange(counted, world.Map.GetSkidTrailIdle(tile) - 1e-6, world.Map.GetSkidTrailIdle(tile) + 1e-6);
        Assert.Contains(world.RuleProjectDocument.Rules, r => r.Id == "nedves");

        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = World(); restored.Load(save);
        Assert.Equal(counted, restored.RuleBook.ReadCustom(restored, "saját.nedves_idő", tile));
        Run(world, 1); Run(restored, 1);
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(restored.CaptureCheckpoint()));
    }

    [Fact]
    public void InvalidRulesAreRejectedWithTheRuleNamed()
    {
        using var world = World();
        RuleProject With(WorldRule rule) { var p = world.RuleProjectDocument; p.Algorithms = null; p.Rules.Add(rule); return p; }
        var readOnly = With(new WorldRule { Id = "x", Name = "Csak olvasható", Scope = "tile", Effects = { new() { Target = "vízellátottság", Formula = "1" } } });
        Assert.Contains("Csak olvasható", Assert.Throws<InvalidDataException>(() => world.QueueRuleProject(readOnly)).Message);
        var action = With(new WorldRule { Id = "y", Name = "Ismeretlen művelet", Scope = "tile", Effects = { new() { Target = "missing.action", Mode = "action" } } });
        Assert.Throws<InvalidDataException>(() => world.QueueRuleProject(action));
        var formula = With(new WorldRule { Id = "z", Name = "Hibás képlet", Scope = "trail", Effects = { new() { Target = "wear", Formula = "nincs_ilyen + 1" } } });
        Assert.Contains("Hibás képlet", Assert.Throws<InvalidDataException>(() => world.QueueRuleProject(formula)).Message);
    }
}
