namespace ForesTycoon.Tests;

public sealed class RuleFormulaTests
{
    private static readonly BehaviorHook Traffic = WorldBehaviorPolicy.Hooks.Single(h => h.Id == "trail.trafficability");
    private static BehaviorGraph Target(BehaviorHook hook) => new() { Id = "g", Name = hook.Name, Hook = hook.Id, Kind = hook.Kind };
    private static double Run(string text, double wetness, double bearing, double native = 1)
    {
        var graph = RuleFormula.Compile(text, Target(Traffic), RuleFormula.Names(Traffic));
        var model = new CompiledBehaviorModel(new BehaviorModel { Graphs = { graph } }, WorldBehaviorPolicy.Hooks);
        return model.Evaluate("trail.trafficability", "tile", 1, new BehaviorInputs(native, 0, 0, wetness, bearing));
    }

    [Fact]
    public void TheMovedTrafficabilityRuleComputesWhatTheGameComputed()
    {
        string text = RuleFormula.Default(Traffic);
        Assert.Equal(1 - 0.8 * (1 - 0.3), Run(text, 0.8, 0.3), 9);
        Assert.Equal(1, Run(text, 0, 0.3), 9);
    }

    [Fact]
    public void SpreadsheetSyntaxWithLocalsCommaDecimalsAndFunctions()
    {
        string text = "# köztes érték\nlágyulás = átnedvesedés * (1 - talaj.teherbírás)\nkorlát(1 - lágyulás * 1,5; 0,1; 1)";
        Assert.Equal(Math.Clamp(1 - 0.8 * 0.7 * 1.5, 0.1, 1), Run(text, 0.8, 0.3), 9);
        Assert.Equal(0.25, Run("ha(átnedvesedés > 0,5; 0,25; beépített)", 0.9, 0.5, 0.75), 9);
        Assert.Equal(0.75, Run("ha(átnedvesedés > 0,5; 0,25; beépített)", 0.1, 0.5, 0.75), 9);
        Assert.Equal(0.3, Run("min(max(-1; 0,3); 2)", 0, 0), 9);
    }

    [Fact]
    public void ErrorsNameTheLineAndSuggestTheIntendedName()
    {
        var error = Assert.Throws<FormulaException>(() => RuleFormula.Compile("x = 1\n1 - átnedvesedes", Target(Traffic), RuleFormula.Names(Traffic)));
        Assert.Equal(2, error.Line);
        Assert.Contains("átnedvesedés", error.Message);
        Assert.Throws<FormulaException>(() => RuleFormula.Compile("max(1)", Target(Traffic), RuleFormula.Names(Traffic)));
        Assert.Throws<FormulaException>(() => RuleFormula.Compile("", Target(Traffic), RuleFormula.Names(Traffic)));
    }

    [Fact]
    public void GraphsReadBackAsFormulas()
    {
        var names = RuleFormula.Names(Traffic);
        var graph = RuleFormula.Compile("1 - átnedvesedés * (1 - talaj.teherbírás)", Target(Traffic), names);
        string text = RuleFormula.Decompile(graph, names);
        Assert.Equal("1 - átnedvesedés * (1 - talaj.teherbírás)", text);
        Assert.Equal(Run(text, 0.6, 0.4), Run("1 - átnedvesedés * (1 - talaj.teherbírás)", 0.6, 0.4), 12);
    }

    [Fact]
    public void EveryHookHasUsableDistinctNames()
    {
        foreach (var hook in WorldBehaviorPolicy.Hooks) {
            var names = RuleFormula.Names(hook);
            Assert.Equal(names.Count, names.Select(n => n.Name).Distinct().Count());
            _ = RuleFormula.Compile(RuleFormula.Default(hook), Target(hook), names);
        }
    }
}

public sealed class WorldScenarioTests
{
    private static GameWorld Create() => new(GameWorld.ScenarioSettings(GameWorld.DefaultScenario()), enableRendering: false);

    [Fact]
    public void PaintedSoilBuildsTheWorldAndSurvivesSaveAndLoad()
    {
        var scenario = GameWorld.DefaultScenario(7);
        scenario.SoilTypes.Single(s => s.Id == "clay").BearingCapacity = 0.1;
        using var world = Create();
        world.StartScenario(scenario);
        int tile = Enumerable.Range(0, world.Map.Tiles.Count).First(id => world.SoilIndex(id) != 2);
        scenario.SoilPaint[tile] = "clay";
        world.StartScenario(scenario);
        Assert.Equal(2, world.SoilIndex(tile));
        Assert.Equal(0.1, world.TileTrafficInputs(tile).Bearing, 9);
        Assert.Equal("clay", world.RuleProjectDocument.World!.SoilPaint[tile]);
        using var stream = new MemoryStream(); world.Save(stream); stream.Position = 0;
        using var loaded = Create(); loaded.Load(stream);
        Assert.Equal(2, loaded.SoilIndex(tile));
        Assert.Equal(0.1, loaded.TileTrafficInputs(tile).Bearing, 9);
    }

    [Fact]
    public void DefaultScenarioReproducesTheBuiltInSoilsAndKeepsOldSavesValid()
    {
        var scenario = GameWorld.DefaultScenario();
        var model = GameWorld.SoilModel(scenario);
        Assert.Equal(SoilCatalog.Default.Profiles.Length, model.Catalog.Profiles.Length);
        for (int i = 0; i < model.Catalog.Profiles.Length; i++)
            Assert.Equal(EnvironmentSystem.BearingCapacity(SoilCatalog.Default[i].Properties), EnvironmentSystem.BearingCapacity(model.Catalog[i].Properties), 2);
        // The built-in catalog has no explicit bearing: its JSON and hash are unchanged for existing saves.
        Assert.DoesNotContain("bearing", SoilCatalog.Default.Json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartingAScenarioKeepsTheProjectsFormulas()
    {
        using var world = Create();
        var project = GameWorld.DefaultRuleProject();
        var hook = WorldBehaviorPolicy.Hooks.Single(h => h.Id == "trail.trafficability");
        int index = project.Behaviors.Graphs.FindIndex(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null);
        project.Behaviors.Graphs[index] = RuleFormula.Compile("0,5", project.Behaviors.Graphs[index], RuleFormula.Names(hook));
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        world.StartScenario(GameWorld.DefaultScenario(9));
        int tile = Enumerable.Range(0, world.Map.Tiles.Count).First(world.IsUnpaved);
        Assert.Equal(0.5f, world.Trafficability(tile));
        Assert.Equal(9, world.RuleProjectDocument.World!.Seed);
    }
}

public sealed class StaleAlgorithmBundleTests
{
    [Fact]
    public void ABundleOlderThanTheCodeIsIgnoredInsteadOfBlockingTheGame()
    {
        string folder = Path.Combine(Path.GetTempPath(), "forest-stale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try {
            string workspace = ForesTycoon.RuleCompiler.AlgorithmBuild.FindWorkspace()!;
            var bundle = new ForesTycoon.RuleCompiler.AlgorithmBundle();
            bundle.Files.Add(ForesTycoon.RuleCompiler.AlgorithmBundle.Import("ForesTycoon/World/Economy.cs", File.ReadAllText(Path.Combine(workspace, "ForesTycoon/World/Economy.cs"))));
            var reference = ForesTycoon.RuleCompiler.AlgorithmBuild.Store(bundle, workspace, Path.Combine(folder, "project.json"));
            string? reason = ForesTycoon.RuleCompiler.AlgorithmBuild.StaleReason(reference);
            Assert.NotNull(reason);
            Assert.Contains("hiányzik", reason);
            Assert.Null(ForesTycoon.RuleCompiler.AlgorithmBuild.StaleReason(null));
        } finally { Directory.Delete(folder, recursive: true); }
    }
}

[CollectionDefinition("SpeciesBook", DisableParallelization = true)]
public sealed class SpeciesBookCollection { }

[Collection("SpeciesBook")]
public sealed class WorldPaintingTests
{
    private static GameWorld Create(WorldScenario scenario) => new(GameWorld.ScenarioSettings(scenario), enableRendering: false);

    [Fact]
    public void WaterTerrainAndForestPaintBuildTheWorldAndSurviveSaving()
    {
        var scenario = GameWorld.DefaultScenario(11, 32);
        using var world = Create(scenario); world.StartScenario(scenario);
        int tiles = world.TileGrid.Columns * world.TileGrid.Rows;
        int dry = Enumerable.Range(0, tiles).First(t => !world.IsWaterTile(t) && t % world.TileGrid.Rows is > 3 and < 28 && t / world.TileGrid.Rows is > 3 and < 28);
        foreach (var (node, height) in world.LakeTargets(new[] { dry, dry + 1 }, scenario.HeightPaint)) scenario.HeightPaint[node] = height;
        int forest = Enumerable.Range(0, tiles).First(t => t != dry && !world.IsWaterTile(t) && world.IsUnpaved(t) && Math.Abs(t - dry) > 70);
        scenario.ForestPaint[forest] = "Oak";
        int cleared = Enumerable.Range(0, tiles).First(t => world.TileSpecies(t) != ForestSpecies.None && t != forest && Math.Abs(t - dry) > 70);
        scenario.ForestPaint[cleared] = "";
        world.StartScenario(scenario);
        Assert.True(world.IsWaterTile(dry));
        Assert.Equal(ForestSpecies.Oak, world.TileSpecies(forest));
        Assert.Equal(ForestSpecies.None, world.TileSpecies(cleared));
        using var stream = new MemoryStream(); world.Save(stream); stream.Position = 0;
        using var loaded = Create(scenario); loaded.Load(stream);
        Assert.True(loaded.IsWaterTile(dry));
        Assert.Equal(ForestSpecies.Oak, loaded.TileSpecies(forest));
    }

    [Fact]
    public void EditedSpeciesNumbersReachGrowthAndTheTimberPriceAndResetWithTheProject()
    {
        var scenario = GameWorld.DefaultScenario(5, 32);
        var oak = scenario.Species.Single(s => s.Id == "Oak");
        double growth = oak.AnnualGrowthRate, price = oak.TimberPrice;
        oak.AnnualGrowthRate = growth * 2; oak.TimberPrice = 99;
        using var world = Create(scenario);
        try {
            world.StartScenario(scenario);
            Assert.Equal((float)(growth * 2), ForestSpeciesProfile.For(ForestSpecies.Oak).AnnualGrowthRate, 5);
            Assert.Equal(99, ForestryLogistics.TimberPrice(ForestSpecies.Oak));
            Assert.Equal(ForestSpeciesTraits.Builtin(ForestSpecies.Beech).Name, ForestSpeciesTraits.For(ForestSpecies.Beech).Name);
            Assert.Single(GameWorld.SpeciesEdits(scenario));
            world.QueueRuleProject(GameWorld.DefaultRuleProject()); world.ExecutePendingCommands();
            Assert.Equal((float)growth, ForestSpeciesProfile.For(ForestSpecies.Oak).AnnualGrowthRate, 5);
            Assert.Equal(price, ForestryLogistics.TimberPrice(ForestSpecies.Oak));
        } finally { ForestSpeciesBook.Apply(null); }
    }

    [Fact]
    public void RoadWeatheringRatesAreTunable()
    {
        double Condition(Dictionary<string, double> tuning)
        {
            using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
            world.QueueTuning(tuning); world.ExecutePendingCommands();
            for (int id = 0; id < world.Map.Tiles.Count - 1 && world.RoadCount == 0; id++) { world.QueueRoadPath(id, id + 1, false, RoadPaving.Macadam); world.ExecutePendingCommands(); }
            int tile = world.CaptureCheckpoint().Terrain.Roads[0].TileId;
            for (int i = 0; i < 600; i++) world.Update(1.0 / 30);
            return world.GetRoadCondition(tile);
        }
        Assert.True(Condition(new()) < 1);
        Assert.Equal(1, Condition(new() { ["MacadamWeather"] = 0, ["MacadamRainWeather"] = 0 }), 6);
    }
}

public sealed class WaterFormulaEquivalenceTests
{
    private static readonly string[] WaterHooks = { "water.snowmelt", "water.infiltration", "water.evaporation", "water.uptake", "water.drainage", "water.baseflow", "water.stress" };

    [Fact]
    public void TheWaterBalanceRunsTheSameAsFormulasAsInCode()
    {
        (double Stored, double Evaporated, double Transpired, double Outflow, double Balance, int Trees) Run(bool formulas)
        {
            using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
            if (formulas) {
                var project = GameWorld.DefaultRuleProject();
                foreach (var hook in WorldBehaviorPolicy.Hooks.Where(h => WaterHooks.Contains(h.Id))) {
                    int i = project.Behaviors.Graphs.FindIndex(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null);
                    project.Behaviors.Graphs[i] = RuleFormula.Compile(RuleFormula.Default(hook), project.Behaviors.Graphs[i], RuleFormula.Names(hook));
                }
                world.QueueRuleProject(project); world.ExecutePendingCommands();
            }
            world.QueueWeather(WeatherPreset.Storm, 30, 20); world.ExecutePendingCommands();
            for (int i = 0; i < 1800; i++) world.Update(1.0 / 30);
            var e = world.Environment;
            return (e.StoredWater, e.Evaporated, e.Transpired, e.Outflow, e.BalanceError, world.ForestTreeCount);
        }
        var native = Run(false); var formula = Run(true);
        Assert.Equal(native.Stored, formula.Stored, 6);
        Assert.Equal(native.Evaporated, formula.Evaporated, 6);
        Assert.Equal(native.Transpired, formula.Transpired, 6);
        Assert.Equal(native.Outflow, formula.Outflow, 6);
        Assert.Equal(native.Trees, formula.Trees);
        Assert.True(Math.Abs(formula.Balance) < 1e-6 * Math.Max(1, formula.Stored));
    }

    [Fact]
    public void EveryWaterRuleHasItsFormulaAndInputs()
    {
        foreach (var hook in WorldBehaviorPolicy.Hooks.Where(h => WaterHooks.Contains(h.Id))) {
            Assert.NotNull(hook.Formula);
            var graph = RuleFormula.Compile(hook.Formula!, new BehaviorGraph { Id = "g", Name = hook.Name, Hook = hook.Id, Kind = hook.Kind }, RuleFormula.Names(hook));
            Assert.DoesNotContain(graph.Nodes, n => n.Operation == BehaviorOperation.Native);
        }
    }
}
