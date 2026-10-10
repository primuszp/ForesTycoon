using ForesTycoon;
using ForesTycoon.Rules;

public class CurrentGameRulesTests
{
    private static string Root()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "ForesTycoon.sln"))) return folder.FullName;
        throw new DirectoryNotFoundException();
    }

    [Fact]
    public void EveryMappedProcessHasAnExistingSourceAnchor()
    {
        var catalog = CurrentGameRules.Build();
        Assert.True(catalog.Rules.Count >= 60);
        foreach (var rule in catalog.Rules)
        foreach (var source in rule.Sources)
        {
            string file = Path.Combine(Root(), source.File);
            Assert.True(File.Exists(file), $"{rule.Id}: missing {source.File}");
            Assert.Contains(source.Symbol, File.ReadAllText(file));
        }
        Assert.Single(catalog.Rules, r => r.Execution == GameRuleExecution.EditableGraph);
        Assert.Equal("road.trafficWear", catalog.Rules.Single(r => r.Execution == GameRuleExecution.EditableGraph).Id);
        Assert.Equal("machine.controller", catalog.Rules.Single(r => r.Execution == GameRuleExecution.EditableController).Id);
        Assert.Contains(catalog.Rules, r => r.Id == "route.policy" && r.Execution == GameRuleExecution.EditableExpressions);
        Assert.Contains(catalog.Rules, r => r.Id == "loading.policy" && r.Execution == GameRuleExecution.EditableExpressions);
    }

    [Fact]
    public void MapsEcologicalFeedbackAndRoadTransportFeedback()
    {
        var catalog = CurrentGameRules.Build(); var links = catalog.Connections();
        Assert.Contains(new("forest.canopy", "water.uptake", "forest.canopy"), links);
        Assert.Contains(new("water.uptake", "water.stress", "water.monthBudget"), links);
        Assert.Contains(new("water.stress", "forest.growth", "water.growthFactor"), links);
        Assert.Contains(new("forest.growth", "forest.canopy", "forest.trees"), links);
        Assert.Contains(new("road.trafficWear", "vehicle.motion", "road.condition"), links);
        Assert.Contains(new("vehicle.motion", "road.trafficWear", "vehicle.position"), links);
        Assert.Contains(new("timber.deliver", "economy.balance", "economy.income"), links);
        Assert.DoesNotContain(links, l => l.From.StartsWith("wildlife.") && l.To == "forest.health");
    }

    [Fact]
    public void ParametersComeFromTheActiveRuntimeProfiles()
    {
        var climate = new ClimateDefinition(1, 12, 2, .3, 4, .1);
        var catalog = CurrentGameRules.Build(240, climate, SoilLandscapeDefinition.Legacy);
        Assert.Contains(new RuleParameter("Erdőév", 240, "játék-s"), catalog.Rules.Single(r => r.Id == "time.world").Parameters);
        Assert.Contains(new RuleParameter("Régióméret", 12, "csempe"), catalog.Rules.Single(r => r.Id == "climate.regional").Parameters);
        Assert.Contains(catalog.Rules.Single(r => r.Id == "upkeep.wear").Parameters, p => p.Value == VehicleUpkeep.WearPerSecond);
        var soil = catalog.Rules.Single(r => r.Id == "soil.catalog");
        Assert.Contains(soil.Parameters, p => p.Name.EndsWith(": telítési készlet") && p.Value == SoilProperties.Standard.Saturation);
        Assert.Equal(6, soil.Parameters.Count);
        Assert.Equal(ForestSpeciesTraits.Playable.Length * 6, catalog.Rules.Single(r => r.Id == "forest.species").Parameters.Count);
    }

    [Fact]
    public void CatalogRoundTripPreservesLayoutNotesAndExecutableSubgraph()
    {
        var catalog = CurrentGameRules.Build();
        catalog.Rules[0].Notes = "Próbáljuk hosszabb erdőévvel."; catalog.Rules[0].X = 175;
        catalog.RoadTrafficModel.Nodes.Single(n => n.Id == "scale").Value = 2;
        var loaded = GameRuleCatalog.FromJson(catalog.ToJson());
        Assert.Equal(catalog.ToJson(), loaded.ToJson());
        Assert.Equal(175, loaded.Rules[0].X); Assert.Equal(catalog.Rules[0].Notes, loaded.Rules[0].Notes);
        Assert.Equal(.003f, new CompiledRoadRule(loaded.RoadTrafficModel).Evaluate(new(.0015, 1, 0)));
        Assert.Equal(catalog.Connections(), loaded.Connections());
    }

    [Fact]
    public void CommittedBaselineMatchesCurrentImplementation()
    {
        var baseline = GameRuleCatalog.FromJson(File.ReadAllText(Path.Combine(Root(), "ForesTycoon.Rules/Catalog/current-game-rules.json")));
        Assert.Equal(CurrentGameRules.Build().ToJson(), baseline.ToJson());
    }

    [Fact]
    public void SnowAndGrappleAreConnectedToConservationPersistenceAndPresentation()
    {
        var catalog = CurrentGameRules.Build(); var links = catalog.Connections();
        Assert.Contains(new("water.snowfall", "water.snowmelt", "water.snow"), links);
        Assert.Contains(new("water.snowdrift", "visual.weather", "water.snow"), links);
        Assert.Contains(new("water.snowmelt", "water.infiltration", "water.surface"), links);
        Assert.Contains(new("water.snowmelt", "water.balance", "water.snow"), links);
        Assert.Contains(new("water.snowmelt", "save.restore", "water.snow"), links);
        Assert.Contains(new("machine.logTransfer", "save.restore", "machine.grapple"), links);
        Assert.Contains(new("machine.logTransfer", "visual.machines", "machine.grapple"), links);
        Assert.Contains(catalog.Rules.Single(r => r.Id == "water.snowmelt").Parameters,
            p => p.Value == EnvironmentSystem.SnowMeltDepthRate);
    }

    [Fact]
    public void RefreshReplacesHistoricalDescriptionsButPreservesAuthoringChoices()
    {
        var saved = CurrentGameRules.Build();
        saved.Rules.RemoveAll(r => r.Id.StartsWith("water.snow"));
        var oldRoute = saved.Rules.Single(r => r.Id == "route.find");
        oldRoute.Description = "Old shortest route"; oldRoute.Notes = "My route experiment"; oldRoute.X = 987;
        var fuel = saved.Rules.Single(r => r.Id == "economy.fuel");
        int i = fuel.Parameters.FindIndex(p => p.Key == "DieselPrice");
        fuel.Parameters[i] = fuel.Parameters[i] with { Value = 1.2 };
        saved.RoadTrafficModel.Nodes.Single(n => n.Id == "scale").Value = 3;
        var current = CurrentGameRules.Build(240, new ClimateDefinition(1, 12, 2, .3, 4, .1));
        string description = current.Rules.Single(r => r.Id == "route.find").Description;
        var refreshed = CurrentGameRules.Refresh(saved, current);
        var route = refreshed.Rules.Single(r => r.Id == "route.find");
        Assert.Equal(description, route.Description);
        Assert.Equal("My route experiment", route.Notes); Assert.Equal(987, route.X);
        Assert.Equal(3, refreshed.Rules.Count(r => r.Id.StartsWith("water.snow")));
        Assert.Equal(1.2, refreshed.TuningOverrides()["DieselPrice"]);
        Assert.Equal(3, refreshed.RoadTrafficModel.Nodes.Single(n => n.Id == "scale").Value);
        Assert.Contains(new RuleParameter("Erdőév", 240, "játék-s"), refreshed.Rules.Single(r => r.Id == "time.world").Parameters);
        refreshed.RoadTrafficModel.Nodes.Single(n => n.Id == "scale").Value = 4;
        Assert.Equal(3, saved.RoadTrafficModel.Nodes.Single(n => n.Id == "scale").Value);
    }

    [Fact]
    public void InvalidTuningCannotPartiallyQueueAnEditorModel()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false);
        string original = world.RuleDocument.ToJson();
        var changed = RuleModel.Default(); changed.Nodes.Single(n => n.Id == "scale").Value = 2;
        Assert.Throws<ArgumentException>(() => world.QueueRuleConfiguration(changed, new Dictionary<string, double> { ["DieselPrice"] = -1 }));
        world.ExecutePendingCommands();
        Assert.Equal(original, world.RuleDocument.ToJson());
        world.QueueRuleConfiguration(changed, new Dictionary<string, double> { ["DieselPrice"] = 1.2 });
        world.ExecutePendingCommands();
        Assert.Equal(changed.ToJson(), world.RuleDocument.ToJson());
        Assert.Equal(1.2, world.Tuning[Tune.DieselPrice]);
    }

    [Fact]
    public void ValidationRejectsBrokenCatalogDocuments()
    {
        var catalog = CurrentGameRules.Build(); catalog.Rules[1].Id = catalog.Rules[0].Id;
        Assert.Throws<InvalidDataException>(catalog.Validate);
        catalog = CurrentGameRules.Build(); catalog.Rules[0].Sources.Clear();
        Assert.Throws<InvalidDataException>(catalog.Validate);
        catalog = CurrentGameRules.Build(); catalog.RoadTrafficModel.Output = "missing";
        Assert.Throws<InvalidDataException>(catalog.Validate);
        catalog = CurrentGameRules.Build();
        var parameter = catalog.Rules.SelectMany(r => r.Parameters).First(p => p.Tunable);
        catalog.Rules[0].Parameters.Add(parameter);
        Assert.Throws<InvalidDataException>(catalog.Validate);
    }
}
