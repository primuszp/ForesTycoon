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
        // The water balance is the project's flow rules; road traffic wear is a behaviour graph.
        Assert.Equal(new[] { "climate.regional", "fleet.depot", "fleet.orders", "forest.canopy", "forest.competition", "forest.deadwood", "forest.growth", "forest.health", "forest.initialSize", "forest.mortality", "forest.regeneration", "forest.site", "forest.stress", "loading.policy", "machine.forwarder", "machine.logTransfer", "machine.movement", "machine.processor", "mill.process", "route.policy", "traffic.wear", "trail.trafficability", "upkeep.effects", "upkeep.repair", "upkeep.service", "upkeep.wear", "vehicle.fuel", "vehicle.mass", "vehicle.motion", "vehicle.resistance", "vehicle.transfer", "water.drainage", "water.evaporation", "water.infiltration", "water.precipitation", "water.runoff", "water.snowdrift", "water.snowmelt", "water.stress", "water.uptake", "weather.evaporation", "weather.events" },
            catalog.Rules.Where(r => r.Execution == GameRuleExecution.EditableGraph && !GameplayRules.CatalogHooks.ContainsKey(r.Id)).Select(r => r.Id).OrderBy(id => id, StringComparer.Ordinal));
        foreach (string id in GameplayRules.CatalogHooks.Keys)
            Assert.Equal(GameRuleExecution.EditableGraph, catalog.Rules.Single(r => r.Id == id).Execution);
        Assert.Equal("machine.controller", catalog.Rules.Single(r => r.Execution == GameRuleExecution.EditableController).Id);
        Assert.Contains(catalog.Rules, r => r.Id == "route.policy" && r.Execution == GameRuleExecution.EditableGraph);
        Assert.Contains(catalog.Rules, r => r.Id == "loading.policy" && r.Execution == GameRuleExecution.EditableGraph);
    }

    [Fact]
    public void MapsEcologicalFeedbackAndRoadTransportFeedback()
    {
        var catalog = CurrentGameRules.Build(); var links = catalog.Connections();
        Assert.Contains(new("forest.canopy", "water.uptake", "forest.canopy"), links);
        Assert.Contains(new("water.uptake", "water.stress", "water.root"), links);
        Assert.Contains(new("water.stress", "forest.growth", "water.growthFactor"), links);
        Assert.Contains(new("forest.growth", "forest.canopy", "forest.trees"), links);
        Assert.Contains(new("traffic.wear", "vehicle.motion", "road.condition"), links);
        Assert.Contains(new("vehicle.motion", "traffic.wear", "vehicle.position"), links);
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
        var loaded = GameRuleCatalog.FromJson(catalog.ToJson());
        Assert.Equal(catalog.ToJson(), loaded.ToJson());
        Assert.Equal(175, loaded.Rules[0].X); Assert.Equal(catalog.Rules[0].Notes, loaded.Rules[0].Notes);
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
        Assert.Contains(new("water.precipitation", "water.snowmelt", "water.snow"), links);
        Assert.Contains(new("water.snowdrift", "visual.weather", "water.snow"), links);
        Assert.Contains(new("water.snowmelt", "water.infiltration", "water.surface"), links);
        Assert.Contains(new("water.snowmelt", "water.balance", "water.snow"), links);
        Assert.Contains(new("water.snowmelt", "save.restore", "water.snow"), links);
        Assert.Contains(new("machine.logTransfer", "save.restore", "machine.grapple"), links);
        Assert.Contains(new("machine.logTransfer", "visual.machines", "machine.grapple"), links);
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
        var current = CurrentGameRules.Build(240, new ClimateDefinition(1, 12, 2, .3, 4, .1));
        string description = current.Rules.Single(r => r.Id == "route.find").Description;
        var refreshed = CurrentGameRules.Refresh(saved, current);
        var route = refreshed.Rules.Single(r => r.Id == "route.find");
        Assert.Equal(description, route.Description);
        Assert.Equal("My route experiment", route.Notes); Assert.Equal(987, route.X);
        Assert.Equal(2, refreshed.Rules.Count(r => r.Id.StartsWith("water.snow")));
        Assert.Equal(1.2, refreshed.TuningOverrides()["DieselPrice"]);
        Assert.Contains(new RuleParameter("Erdőév", 240, "játék-s"), refreshed.Rules.Single(r => r.Id == "time.world").Parameters);
    }

    [Fact]
    public void ValidationRejectsBrokenCatalogDocuments()
    {
        var catalog = CurrentGameRules.Build(); catalog.Rules[1].Id = catalog.Rules[0].Id;
        Assert.Throws<InvalidDataException>(catalog.Validate);
        catalog = CurrentGameRules.Build(); catalog.Rules[0].Sources.Clear();
        Assert.Throws<InvalidDataException>(catalog.Validate);
        catalog = CurrentGameRules.Build();
        var parameter = catalog.Rules.SelectMany(r => r.Parameters).First(p => p.Tunable);
        catalog.Rules[0].Parameters.Add(parameter);
        Assert.Throws<InvalidDataException>(catalog.Validate);
    }
}
