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
    public void ValidationRejectsBrokenCatalogDocuments()
    {
        var catalog = CurrentGameRules.Build(); catalog.Rules[1].Id = catalog.Rules[0].Id;
        Assert.Throws<InvalidDataException>(catalog.Validate);
        catalog = CurrentGameRules.Build(); catalog.Rules[0].Sources.Clear();
        Assert.Throws<InvalidDataException>(catalog.Validate);
        catalog = CurrentGameRules.Build(); catalog.RoadTrafficModel.Output = "missing";
        Assert.Throws<InvalidDataException>(catalog.Validate);
    }
}
