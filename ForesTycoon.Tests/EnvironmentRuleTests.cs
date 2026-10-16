using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class EnvironmentRuleTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static void Fixed(BehaviorModel model, string hook, double value)
    {
        var graph = model.Graphs.Single(g => g.Hook == hook && g.Kind == "environment");
        graph.Disabled = true; graph.DisabledValue = value;
    }
    private static string State(GameWorld world) => JsonSerializer.Serialize(world.CaptureCheckpoint());
    private static void Apply(GameWorld world, params (string Hook, double Value)[] changes)
    {
        var project = world.RuleProjectDocument;
        foreach (var c in changes) Fixed(project.Behaviors, c.Hook, c.Value);
        world.QueueRuleProject(project); world.ExecutePendingCommands();
    }
    [Fact]
    public void EveryEnvironmentDecisionIsAnExplicitEditableGraphWithDocumentedMeaning()
    {
        var model = WorldBehaviorPolicy.DefaultDocument();
        Assert.Equal(Enum.GetValues<EnvironmentResponse>().Length, EnvironmentRules.Hooks.Length);
        foreach (var hook in EnvironmentRules.Hooks) {
            var graph = model.Graphs.Single(g => g.Hook == hook.Id && g.Kind == hook.Kind);
            Assert.DoesNotContain(graph.Nodes, n => n.Operation == BehaviorOperation.Native);
            Assert.NotEmpty(ResponseRuleDescription.For(hook.Id).Consumers);
            Assert.NotEmpty(hook.Schedule);
            Assert.All(ResponseRuleDescription.ConnectedInputs(graph), input => Assert.Contains(input, hook.Inputs));
        }
        Assert.DoesNotContain(WorldBehaviorPolicy.Hooks, h => h.Id == "trail.trafficability" && h.Kind == "tile");
    }
    [Fact]
    public void AuthoredWeatherChoosesEventsDurationIntensityAndAtmosphericResponses()
    {
        var model = WorldBehaviorPolicy.DefaultDocument();
        Fixed(model, "weather.eventType", 4); Fixed(model, "weather.eventDuration", 24); Fixed(model, "weather.eventPeak", 47);
        Fixed(model, "weather.temperature", 7); Fixed(model, "weather.humidity", .2); Fixed(model, "weather.wind", 11);
        Fixed(model, "weather.cloud", .4); Fixed(model, "weather.radiation", .8);
        var weather = new WeatherSystem(42, 1200) { ResponseRules = EcologyResponseProgram.FromModel(model) };
        while (weather.Time < 150) weather.AdvanceInterval(1);
        weather.AdvanceInterval(.1);
        Assert.Equal(WeatherPreset.Storm, weather.Preset);
        Assert.Equal(24, weather.EventEnd - weather.EventStart); Assert.Equal(47, weather.PeakRain);
        Assert.Equal(7, weather.Temperature); Assert.Equal(.2, weather.Humidity); Assert.Equal(11, weather.Wind);
        Assert.Equal(.4, weather.Cloud); Assert.Equal(.8, weather.Radiation);
    }
    [Fact]
    public void ForcedWeatherUsesAuthoredDefaultsAndExplicitCommandValuesWin()
    {
        var model = WorldBehaviorPolicy.DefaultDocument(); Fixed(model, "weather.forcedDuration", 23); Fixed(model, "weather.forcedPeak", 17);
        var weather = new WeatherSystem(42, 1200) { ResponseRules = EcologyResponseProgram.FromModel(model) };
        weather.ForceWeather(WeatherPreset.Rain);
        Assert.Equal(23, weather.EventEnd - weather.EventStart); Assert.Equal(17, weather.PeakRain);
        weather.ForceWeather(WeatherPreset.Rain, 19, 30);
        Assert.Equal(30, weather.EventEnd - weather.EventStart); Assert.Equal(19, weather.PeakRain);
    }
    [Fact]
    public void NewWorldUsesTheAuthoredInitialWeather()
    {
        var project = GameWorld.DefaultRuleProject(); project.World = GameWorld.DefaultScenario(42, 16);
        Fixed(project.Behaviors, "weather.initialType", 3); Fixed(project.Behaviors, "weather.initialDuration", 90); Fixed(project.Behaviors, "weather.initialPeak", 12);
        using var world = WorldLoader.CreateProject(project);
        Assert.Equal(WeatherPreset.Snow, world.Environment.Preset); Assert.Equal(12, world.Environment.PeakRain);
        Assert.Equal(90 * world.Environment.ForestYearSeconds / 1200, world.Environment.EventEnd - world.Environment.EventStart);
    }
    private sealed class SnowHabitat : IForestHabitat
    {
        public int TileCount => 3; public int Seed => 42;
        public bool CanSupportForest(int id) => false;
        public float GetMoisture(int id) => .5f;
        public float GetNormalizedElevation(int id) => 0;
        public int GetAdjacentTileIds(int id, Span<int> target) {
            int n = 0; if (id > 0) target[n++] = id - 1; if (id < 2) target[n++] = id + 1; return n;
        }
    }
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    public void GraphChoosesSnowDirectionAndConservativeTransportDoesNotCascade(bool reverse, int target)
    {
        var model = WorldBehaviorPolicy.DefaultDocument();
        Fixed(model, "snow.retention", 0); Fixed(model, "snow.release", 1);
        var direction = model.Graphs.Single(g => g.Hook == "snow.direction");
        direction.Nodes = new() { new() { Id = "x", Name = "Szomszéd iránya", Operation = BehaviorOperation.Input, Key = "neighbor.directionX" },
            new() { Id = "reverse", Name = "Ellentétes irány", Operation = BehaviorOperation.Negate, A = "x" } };
        direction.Output = reverse ? "reverse" : "x";
        var environment = new EnvironmentSystem(new SnowHabitat(), null, responseRules: EcologyResponseProgram.FromModel(model));
        environment.SetWater(1, EnvironmentSystem.WaterStore.Snow, 10); environment.ForceWeather(WeatherPreset.Snow, 0, 30);
        int units = 0; foreach (int unit in environment.AdvanceStepWork(.5)) units += unit;
        Assert.Equal(0, environment.SnowWaterAt(1)); Assert.Equal(10, environment.SnowWaterAt(target));
        Assert.Equal(10, environment.SnowWater); Assert.InRange(Math.Abs(environment.BalanceError), 0, 1e-9);
        Assert.True(units >= 3 * environment.CellCount); // Source, validation and publication all yield.
        environment.AdvanceStep(.5);
        Assert.Equal(10, environment.SnowWaterAt(target)); // Closed map edge retains snow.
    }
    [Fact]
    public void AnAuthoredCurveConnectsAirTemperatureToActualEvaporation()
    {
        using var world = World(); var project = world.RuleProjectDocument;
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == "weather.evaporation");
        graph.Nodes = new() {
            new() { Id = "temperature", Name = "Hőmérséklet", Operation = BehaviorOperation.Input, Key = "weather.temperature" },
            new() { Id = "curve", Name = "Melegben gyorsabb", Operation = BehaviorOperation.Curve, A = "temperature",
                Key = new RuleCurve { Input = BehaviorCurves.Input, Points = new() { (0, 0), (40, 4) } }.ToFormula() }
        }; graph.Output = "curve";
        Fixed(project.Behaviors, "climate.temperature", 10); world.QueueRuleProject(project); world.ExecutePendingCommands();
        Assert.Equal(1, world.Environment.LocalClimate(0).Forcing.Evaporation(world.Environment.ResponseRules), 8);
        Apply(world, ("climate.temperature", 30));
        Assert.Equal(3, world.Environment.LocalClimate(0).Forcing.Evaporation(world.Environment.ResponseRules), 8);
    }
    [Fact]
    public void GraphRampIntegratesPrecipitationAndSurvivesAWeatherCheckpoint()
    {
        var model = WorldBehaviorPolicy.DefaultDocument(); Fixed(model, "weather.ramp", 4);
        var rules = EcologyResponseProgram.FromModel(model);
        var weather = new WeatherSystem(1, 1200) { ResponseRules = rules }; weather.ForceWeather(WeatherPreset.Rain, 30, 30);
        weather.AdvanceInterval(2);
        var clone = new WeatherSystem(1, 1200) { ResponseRules = EcologyResponseProgram.FromModel(model) }; clone.Restore(weather.Capture());
        for (int i = 0; i < 120; i++) Assert.Equal(weather.AdvanceInterval(.25), clone.AdvanceInterval(.25));
        Assert.Equal(weather.Capture(), clone.Capture());
        // Stop exactly at the event boundary, without introducing a new event.
        var full = new WeatherSystem(1, 1200) { ResponseRules = rules }; full.ForceWeather(WeatherPreset.Rain, 30, 30);
        while (full.Time < 30 - 1e-9) full.AdvanceInterval(30 - full.Time);
        Assert.Equal(30 * (30 - 4) / 60d, full.TotalRain, 10);
    }
    [Fact]
    public void LocalClimateAndEvaporationChangeTheActualDiscoveredTileFields()
    {
        using var world = World();
        _ = world.Environment.LocalClimate(0); // Warm the old cache before replacing graphs.
        Apply(world, ("climate.temperature", 37), ("climate.humidity", .13), ("climate.rain", 1.7), ("weather.evaporation", 2.5));
        var local = world.Environment.LocalClimate(0);
        Assert.Equal(37, local.Forcing.Temperature); Assert.Equal(.13, local.Forcing.Humidity); Assert.Equal(1.7, local.RainMultiplier);
        var field = RuleEngine.Scope("tile").Fields.Single(f => f.Name == "potentialEvaporation");
        Assert.Equal(2.5, field.Read(world, 0)); Assert.Equal(2.5, field.ViewRead(world, 0));
        world.QueueWeather(WeatherPreset.Rain, 30, 30); world.ExecutePendingCommands();
        for (int i = 0; i < 90; i++) world.Update(1d / 30);
        Assert.True(world.Environment.RainReceived > world.Environment.TotalRain * world.Environment.CellCount);
        Assert.InRange(Math.Abs(world.Environment.BalanceError), 0, 1e-6);
    }
    [Fact]
    public void CanopyGraphsFeedTheWaterRulesAndChangingThemInvalidatesTheCache()
    {
        using var world = World();
        SimulationWork.Complete(world.Environment.RefreshVegetationWork());
        int tile = Enumerable.Range(0, world.Environment.CellCount).First(i => world.Forest.IndividualTrees.TryGet(i, out var p) && p.Count > 0);
        Apply(world, ("forest.cover", .37), ("forest.canopyCapacity", 8), ("forest.leafIndex", 4));
        SimulationWork.Complete(world.Environment.RefreshVegetationWork());
        Assert.Equal(new ForestHydrologyInputs(.37, 8, 4), world.Environment.HydrologyInputs(tile));
        var field = RuleEngine.Scope("tile").Fields.Single(f => f.Name == "cover");
        Assert.Equal(.37, field.Read(world, tile));
        var result = new WorkResult<ForestHydrologyInputs>(); int units = 0;
        foreach (int unit in ((IForestCanopy)world.Forest).HydrologyInputsWork(tile, result)) units += unit;
        Assert.Equal(world.Forest.HydrologyInputs(tile), result.Value);
        Assert.True(units >= world.Forest.IndividualTrees.Patches.Single(p => p.Key == tile).Value.Count);
    }
    [Fact]
    public void WetnessAndTrafficabilityHaveNoNativeFallback()
    {
        using var world = World();
        Apply(world, ("soil.wetness", .8), ("soil.bearing", .3));
        Assert.Equal(.8, world.Environment.Trafficability(0).Wetness);
        Assert.Equal(.3, world.Environment.Trafficability(0).Bearing);
        Apply(world, ("trail.trafficability", .23));
        Assert.Equal(.23f, world.Trafficability(0));
    }
    [Fact]
    public void LoadingDiscardsPartiallyPublishedSnowFluxAndTheNextTickStillMatches()
    {
        using var whole = World();
        Apply(whole, ("snow.retention", 0), ("snow.release", 1));
        whole.QueueWeather(WeatherPreset.Snow, 0, 30); whole.ExecutePendingCommands();
        int[] neighbors = new int[4];
        int tile = Enumerable.Range(0, whole.Environment.CellCount).First(id => whole.Map.GetTileNeighbours(id, neighbors) == 4);
        whole.Environment.SetWater(tile, EnvironmentSystem.WaterStore.Snow, 10);
        for (int tick = 0; tick < 14; tick++) whole.Update(1d / 30);
        using var before = new MemoryStream(); whole.Save(before); before.Position = 0;
        using var sliced = World(); sliced.Load(before);
        int units = 0;
        while (sliced.Environment.SnowWaterAt(tile) == 10) {
            Assert.False(sliced.ContinueUpdate(1d / 30, 1));
            Assert.True(++units < 100000, "Snow publication was not reached within one bounded tick.");
        }
        Assert.True(sliced.UpdatePending);
        before.Position = 0; sliced.Load(before);
        Assert.Equal(State(whole), State(sliced));
        whole.Update(1d / 30);
        while (!sliced.ContinueUpdate(1d / 30, 7)) { }
        Assert.Equal(State(whole), State(sliced));
    }
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void AuthoredEnvironmentIsIdenticalInSlicesSaveLoadAndJournalReplay(int budget)
    {
        using var whole = World(); using var sliced = World();
        foreach (var w in new[] { whole, sliced }) {
            Apply(w, ("climate.temperature", -3), ("climate.rain", 1.4), ("weather.evaporation", .3), ("snow.release", .6));
            w.QueueWeather(WeatherPreset.Snow, 20, 30); w.ExecutePendingCommands();
        }
        for (int tick = 0; tick < 90; tick++) {
            whole.Update(1d / 30);
            while (!sliced.ContinueUpdate(1d / 30, budget)) Assert.InRange(sliced.LastUpdateWorkUnits, 1, budget);
        }
        Assert.Equal(State(whole), State(sliced));
        Assert.InRange(Math.Abs(sliced.Environment.BalanceError), 0, 1e-6);
        using var save = new MemoryStream(); sliced.Save(save); save.Position = 0;
        using var restored = World(); restored.Load(save);
        Assert.Equal(State(sliced), State(restored));
        for (int tick = 0; tick < 90; tick++) { sliced.Update(1d / 30); restored.Update(1d / 30); }
        Assert.Equal(State(sliced), State(restored));
        using var latest = new MemoryStream(); sliced.Save(latest); latest.Position = 0;
        var data = WorldSaveSerializer.Read(latest);
        using var journal = new MemoryStream();
        WorldSaveSerializer.Write(journal, new WorldSaveData { Terrain = data.Terrain, SoilModel = data.SoilModel,
            Climate = data.Climate, Tick = data.Tick, TickRate = data.TickRate, ForestYearSeconds = data.ForestYearSeconds, Commands = data.Commands });
        journal.Position = 0; restored.Load(journal);
        Assert.Equal(State(sliced), State(restored));
    }
}
