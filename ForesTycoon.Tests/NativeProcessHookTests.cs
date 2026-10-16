using ForesTycoon.Editor;

namespace ForesTycoon.Tests;

public sealed class NativeProcessHookTests
{
    private static GameWorld Create() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);

    [Fact]
    public void DefaultGraphsDoNotOverrideAndNewHooksExist()
    {
        var model = new CompiledBehaviorModel(WorldBehaviorPolicy.DefaultDocument(), WorldBehaviorPolicy.Hooks);
        foreach (var hook in WorldBehaviorPolicy.Hooks) Assert.False(model.Overrides(hook.Id, hook.Kind));
        Assert.DoesNotContain(WorldBehaviorPolicy.Hooks, h => h.Id == "road.weather");
    }

}

public sealed class LayeredModelTests
{
    [Fact]
    public void EveryPerTileQuantityHasALayerAndSoilAndGroundwaterReachTheTracks()
    {
        var model = new SystemModelAnalysis(CurrentGameRules.Build());
        Assert.Equal(new[] { "atmosphere", "vegetation", "surface", "terrain", "soil", "groundwater", "management" }, model.Vocabulary.Layers.Select(l => l.Id));
        Assert.All(model.Variables.Where(v => SystemVocabulary.Endogenous(v.Kind)), v => Assert.NotEqual("", v.Layer));
        // Soil bearing and root-zone water decide how passable an unpaved track is, and that slows the vehicles.
        var rule = model.Process("trail.trafficability")!;
        Assert.Contains("soil.bearing", rule.Reads); Assert.Contains("water.root", rule.Reads);
        Assert.Contains("trail.trafficability", model.Process("vehicle.resistance")!.Reads);
        var cross = model.CrossLayerRules().ToArray();
        Assert.Contains(cross, c => c.From == "soil" && c.To == "surface" && c.Processes.Any(p => p.Id == "trail.trafficability"));
        Assert.Contains(cross, c => c.From == "soil" && c.To == "vegetation");
        Assert.Contains(cross, c => c.From == "groundwater" && c.To == "vegetation");
    }

    [Fact]
    public void SoakedSoilWithLowBearingIsLessPassableAndFormulaCanOverrideIt()
    {
        var soft = new SoilProperties(180, 130, 25, 12, 0, 1); var draining = new SoilProperties(180, 130, 25, 12, 6, 1);
        Assert.True(EnvironmentSystem.BearingCapacity(soft) < EnvironmentSystem.BearingCapacity(draining));
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        int tile = Enumerable.Range(0, world.Map.Tiles.Count).First(id => !world.Map.IsRoadTile(id));
        float dry = world.Trafficability(tile);
        world.QueueWeather(WeatherPreset.Storm, 32, 20); world.ExecutePendingCommands();
        for (int i = 0; i < 900; i++) world.Update(1.0 / 30);
        Assert.True(world.Trafficability(tile) <= dry);
        var section = world.DescribeTile(tile);
        Assert.Contains("soil.profile", section.Keys); Assert.Contains("water.root", section.Keys); Assert.Contains("trail.trafficability", section.Keys);
        var project = GameWorld.DefaultRuleProject();
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == "trail.trafficability");
        graph.Nodes.Add(new BehaviorNode { Id = "half", Name = "Fél", Operation = BehaviorOperation.Constant, Value = 0.5 }); graph.Output = "half";
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        Assert.Equal(0.5f, world.Trafficability(tile));
    }

    [Fact]
    public void AddedInfluenceCompilesAndScalesTheResultByTheQuantity()
    {
        var document = WorldBehaviorPolicy.DefaultDocument();
        AuthoringOperations.AddInfluence(document.Graphs.Single(g => g.Hook == "forest.growth"), "world.rain", "Csapadék", 1, 0.5);
        var compiled = new CompiledBehaviorModel(document, WorldBehaviorPolicy.Hooks, new HashSet<string>(GameWorld.RuleWorldFields), _ => 3);
        // rain 3, reference 1: factor 1 + 0.5 × 2 = 2.
        Assert.Equal(1.0, compiled.Evaluate("forest.growth", "tree", 7, new BehaviorInputs(0.5, 0, 0, 0, 0)), 9);
        Assert.True(compiled.Overrides("forest.growth", "tree"));
    }
}
