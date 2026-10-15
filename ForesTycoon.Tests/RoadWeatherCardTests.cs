using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class RoadWeatherCardTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static int Road(GameWorld world, RoadPaving paving = RoadPaving.Asphalt)
    {
        for (int id = 0; id < 255 && world.RoadCount == 0; id++) world.Map.BuildRoadTilePath(id, id + 1, paving);
        return world.Map.RoadTiles.First();
    }
    private static RuleProject Edit(GameWorld world, Action<RoadWeatherCard> edit)
    {
        var project = world.RuleProjectDocument; project.Algorithms = null;
        var rule = project.Rules.Single(r => r.Id == "road.weather");
        rule.Effects.Clear(); rule.UseLegacyRoadOverride = false;
        rule.RoadWeather = new(); edit(rule.RoadWeather);
        return project;
    }

    [Theory]
    [InlineData(false, 0, 0, 0.1)]
    [InlineData(false, 0.5, 0.25, 0.1)]
    [InlineData(false, 1, 1, 2)]
    [InlineData(true, 0, 0, 0.1)]
    [InlineData(true, 0.5, 0.25, 0.1)]
    [InlineData(true, 1, 1, 2)]
    public void CompiledCardMatchesTheOriginalEquation(bool macadam, double wet, double rain, double years)
    {
        var card = new RoadWeatherCard { AsphaltBase = .015, AsphaltRain = .08, MacadamBase = .10, MacadamRain = 1.6 };
        var formula = card.CompileEffects().Single().Formula;
        var names = new[] {
            new FormulaName("surface", BehaviorOperation.Input, "surface"),
            new FormulaName("tile.moisture", BehaviorOperation.Input, "tile.moisture"),
            new FormulaName("rainIntensity", BehaviorOperation.Input, "rainIntensity"),
            new FormulaName("deltaYears", BehaviorOperation.Delta) };
        var hook = new BehaviorHook("test", "test", "road", "", -1e12, 1e12, "", "", "", Inputs: new[] { "surface", "tile.moisture", "rainIntensity" });
        var graph = RuleFormula.Compile(formula, new BehaviorGraph { Id = "test", Name = "test", Hook = "test", Kind = "road" }, names);
        var runtime = new CompiledBehaviorModel(new BehaviorModel { Graphs = { graph } }, new[] { hook });
        double result = runtime.Evaluate("test", "road", 0, new(0, years, 0, 0, 0), new[] { macadam ? 1d : 0d, wet, rain });
        double rate = macadam ? .1 * (1 + 2.5 * wet) + 1.6 * rain * (.4 + wet) : .015 * (1 + wet) + .08 * rain;
        Assert.Equal(-rate * years, result, 12);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultRuleMatchesNativeLoopAndPreviewWithoutMutatingOnPreview(bool macadam)
    {
        var paving = macadam ? RoadPaving.Macadam : RoadPaving.Asphalt;
        using var world = World(); int id = Road(world, paving);
        var checkpoint = world.Map.Capture();
        var old = new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42)); old.Restore(checkpoint);
        float rain = (float)Math.Clamp(world.Environment.RainRate / 20, 0, 1);
        var preview = world.RuleBook.PreviewRoadWeather(world, "road.weather", id, .1f);
        Assert.Equal(1, world.Map.GetRoadCondition(id));
        old.WeatherRoads(.1f, rain);
        world.RuleBook.Run(world, .1f, 0);
        Assert.Equal(old.GetRoadCondition(id), world.Map.GetRoadCondition(id), 6);
        Assert.Equal(preview.After, world.Map.GetRoadCondition(id), 6);
        Assert.Null(world.RuleBook.LastError);
        Assert.Contains(GameWorld.RuleObjects.Find("road").Fields, f => f.Name == "condition" && f.Writable);
        Assert.Contains(RuleEngine.Scope("road").Fields, f => f.Name == "tile.moisture");
    }

    [Fact]
    public void CardEditPersistsAndRunsInRealWorldUpdatesWithNoNativeDoubleWear()
    {
        using var world = World(); int id = Road(world);
        var project = Edit(world, c => { c.AsphaltBase = .5; c.AsphaltRain = 0; c.AsphaltMoistureFactor = 0; });
        // JSON is the editor/game interchange; no generated formula is needed in the saved project.
        project = RuleProject.FromJson(project.ToJson());
        Assert.Empty(project.Rules.Single(r => r.Id == "road.weather").Effects);
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        // A custom clock measures exactly the same elapsed rule time used by the card.
        project = world.RuleProjectDocument; project.Algorithms = null;
        project.Fields.Add(new() { Name = "saját.clock", Scope = "world" });
        project.Rules.Add(new() { Id = "clock", Name = "Clock", Scope = "world", Effects = { new() { Target = "saját.clock", Mode = "add", Formula = "deltaYears" } } });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        for (int i = 0; i < 35; i++) world.Update(1d / 30);
        double years = world.RuleBook.ReadCustom(world, "saját.clock", 0);
        Assert.True(years > 0);
        Assert.Equal(1 - .5 * years, world.Map.GetRoadCondition(id), 6);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = World(); restored.Load(save);
        Assert.Equal(.5, restored.RuleBook.Rules.Single(r => r.Id == "road.weather").Effects[0].Influence.Terms[0].Amount);
        for (int i = 0; i < 30; i++) { world.Update(1d / 30); restored.Update(1d / 30); }
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(restored.CaptureCheckpoint()));
        project = world.RuleProjectDocument; project.Algorithms = null;
        project.Rules.Single(r => r.Id == "road.weather").Enabled = false;
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        float before = world.Map.GetRoadCondition(id);
        for (int i = 0; i < 30; i++) world.Update(1d / 30);
        Assert.Equal(before, world.Map.GetRoadCondition(id));
        world.ApplyTrafficWear(id, .1f);
        Assert.True(world.Map.GetRoadCondition(id) < before);
    }

    [Fact]
    public void OldProjectsRetainTuningAndLegacyGraphsAndCanChooseCardOnly()
    {
        using var world = World(); int id = Road(world);
        var old = world.RuleProjectDocument; old.Algorithms = null; old.Rules.RemoveAll(r => r.Id == "road.weather");
        old.Tuning["AsphaltWeather"] = .7;
        old.Tuning["AsphaltRainWeather"] = 0;
        world.QueueRuleProject(old); world.ExecutePendingCommands();
        double wet = world.Map.GetRuleMoisture(id);
        Assert.Equal(.7 * (1 + wet), world.RuleBook.PreviewRoadWeather(world, "road.weather", id, .1).Rate, 6);
        old.Behaviors.Graphs.RemoveAll(g => g.Hook == "road.weather");
        var hook = WorldBehaviorPolicy.Hooks.Single(h => h.Id == "road.weather");
        old.Behaviors.Graphs.Add(RuleFormula.Compile("0.25", new() { Id = "legacy", Name = "Legacy", Hook = hook.Id, Kind = hook.Kind }, RuleFormula.Names(hook)));
        world.QueueRuleProject(old); world.ExecutePendingCommands();
        Assert.Equal(.25, world.RuleBook.PreviewRoadWeather(world, "road.weather", id, .1).Rate, 8);
        var project = Edit(world, c => { c.UseLegacyOverride = false; c.AsphaltBase = 2; c.AsphaltMoistureFactor = 0; });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        world.RuleBook.Run(world, 1, 0);
        Assert.Equal(0, world.Map.GetRoadCondition(id));
        Assert.Contains(world.BehaviorDocument.Graphs, g => g.Id == "legacy");
    }

    [Fact]
    public void InvalidCardsAndAmbiguousEffectsAreRejected()
    {
        using var world = World();
        var project = Edit(world, c => c.AsphaltBase = -1);
        Assert.Throws<InvalidDataException>(() => world.QueueRuleProject(project));
        project = Edit(world, c => c.AsphaltBase = 1);
        project.Rules.Single(r => r.Id == "road.weather").Effects.Add(new() { Target = "condition", Formula = "1" });
        Assert.Throws<InvalidDataException>(() => world.QueueRuleProject(project));
        var card = new RoadWeatherCard { MacadamRainBaseline = double.NaN };
        Assert.Throws<InvalidDataException>(card.Validate);
    }
}
