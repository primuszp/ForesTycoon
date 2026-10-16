using System.Text.Json;

namespace ForesTycoon.Tests;

/// <summary>
/// Every rule is the project's own: starter rules only seed a project, and a deleted one stays deleted. The state the
/// rules model as stocks (tile water, tree size) can be written by rules.
/// </summary>
[Collection("SpeciesBook")]
public sealed class RuleOwnershipTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static void Run(GameWorld world, double seconds) { for (int i = 0; i < seconds * 30; i++) world.Update(1d / 30); }

    [Fact]
    public void ANewProjectGetsEveryStarterRuleAsAnOwnRule()
    {
        using var world = World();
        var rules = world.RuleProjectDocument.Rules;
        foreach (var starter in RuleEngine.StarterRules()) {
            var rule = Assert.Single(rules, r => r.Id == starter.Id);
            Assert.Equal(starter.Id, rule.Template);
        }
    }

    [Fact]
    public void ADeletedStarterRuleStaysDeletedInTheProjectAndTheSave()
    {
        using var world = World();
        var project = world.RuleProjectDocument; project.Algorithms = null;
        project.Rules.RemoveAll(r => r.Id == "road.weather"); project.RemovedStarters.Add("road.weather");
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        Assert.DoesNotContain(world.RuleProjectDocument.Rules, r => r.Id == "road.weather");
        Assert.Contains("road.weather", world.RuleProjectDocument.RemovedStarters);

        // The project file and a saved game both keep it deleted.
        var reloaded = RuleProject.FromJson(world.RuleProjectDocument.ToJson()); reloaded.Algorithms = null;
        using var other = World();
        other.QueueRuleProject(reloaded); other.ExecutePendingCommands();
        Assert.DoesNotContain(other.RuleProjectDocument.Rules, r => r.Id == "road.weather");

        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = World(); restored.Load(save);
        Assert.DoesNotContain(restored.RuleProjectDocument.Rules, r => r.Id == "road.weather");
        Assert.Contains("road.weather", restored.RuleProjectDocument.RemovedStarters);
    }

    [Fact]
    public void ARuleCanSetATilesWaterStoresAndTheWaterBalanceStaysClosed()
    {
        using var world = World();
        var scope = RuleEngine.Scope("tile");
        var surface = scope.Fields.Single(f => f.Name == "surfaceWater");
        var deep = scope.Fields.Single(f => f.Name == "deepWater");
        Assert.NotNull(surface.Write); Assert.NotNull(deep.Write);
        surface.Write(world, 5, 12.5); deep.Write(world, 5, 0);
        Assert.Equal(12.5, surface.Read(world, 5), 6);
        Assert.Equal(0, deep.Read(world, 5), 6);
        Assert.InRange(world.Environment.BalanceError, -1e-6, 1e-6);
        Run(world, 1);
        Assert.InRange(world.Environment.BalanceError, -1e-6, 1e-6);
    }

    [Fact]
    public void ARuleCanSetATreesSizeAndItGrowsOnFromThere()
    {
        using var world = World();
        var scope = RuleEngine.Scope("tree");
        Assert.True(scope.Elements(world).Length > 0);
        var height = scope.Fields.Single(f => f.Name == "height");
        Assert.NotNull(height.Write);
        height.Write(world, 0, 3);
        Assert.Equal(3, height.Read(world, 0), 4);
        Run(world, 2);
        Assert.InRange(height.Read(world, 0), 3 - 1e-4, 30);
    }
}
