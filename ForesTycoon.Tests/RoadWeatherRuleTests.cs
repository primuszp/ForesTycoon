namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class RoadWeatherRuleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GraphAloneRunsWeatheringAndPreviewMatchesItsContributions(bool macadam)
    {
        var paving = macadam ? RoadPaving.Macadam : RoadPaving.Asphalt;
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        for (int id = 0; id < world.Map.Tiles.Count - 1 && world.RoadCount == 0; id++) world.Map.BuildRoadTilePath(id, id + 1, paving);
        int tile = world.Map.RoadTiles.First();
        var project = world.RuleProjectDocument; project.Algorithms = null;
        foreach (var rule in project.Rules) rule.Enabled = rule.Id == "road.weather";
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        var scope = RuleEngine.Scope("road");
        double wet = scope.Fields.Single(f => f.Name == "tile.moisture").Read(world, tile);
        double rain = scope.Fields.Single(f => f.Name == "rainIntensity").Read(world, tile);
        double expectedRate = paving == RoadPaving.Macadam
            ? world.Tuning[Tune.MacadamWeather] * (1 + 2.5 * wet) + world.Tuning[Tune.MacadamRainWeather] * rain * (.4 + wet)
            : world.Tuning[Tune.AsphaltWeather] * (1 + wet) + world.Tuning[Tune.AsphaltRainWeather] * rain;
        var preview = Assert.Single(world.RuleBook.PreviewInfluences(world, "road.weather", tile, .1));
        Assert.Equal(1, world.Map.GetRoadCondition(tile));
        Assert.Equal(-expectedRate * .1, preview.Contributions.Sum(c => c.Change), 8);
        world.RuleBook.Run(world, .1, 0);
        Assert.Equal(preview.After, world.Map.GetRoadCondition(tile), 6);
        Assert.Null(world.RuleBook.LastError);
    }

    [Fact]
    public void EditingTheOnlyRoadGraphPersistsAndDisablingItStopsWeathering()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        for (int id = 0; id < 255 && world.RoadCount == 0; id++) world.Map.BuildRoadTilePath(id, id + 1, RoadPaving.Asphalt);
        int tile = world.Map.RoadTiles.First(); var project = world.RuleProjectDocument; project.Algorithms = null;
        var rule = project.Rules.Single(r => r.Id == "road.weather");
        rule.Effects[0].Influence.Terms = new() { new() { Name = "Authored weathering", Amount = .5 } };
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false); restored.Load(save);
        restored.RuleBook.Run(restored, .1, 0); Assert.Equal(.95, restored.Map.GetRoadCondition(tile), 6);
        project = restored.RuleProjectDocument; project.Algorithms = null;
        project.Rules.Single(r => r.Id == "road.weather").Enabled = false;
        restored.QueueRuleProject(project); restored.ExecutePendingCommands();
        restored.RuleBook.Run(restored, 1, 0); Assert.Equal(.95, restored.Map.GetRoadCondition(tile), 6);
    }
}
