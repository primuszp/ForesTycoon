using System.Text.Json;

namespace ForesTycoon.Tests;

public partial class FleetTests
{
    [Fact]
    public void ControlGraphsOwnWaitingHomeRequestsAndUpkeep()
    {
        var s = Build(); var m = Forwarder(s);
        m.Source = s.Logistics.StackAt(ForestStack); m.Destination = RoadStack;
        m.Path = new[] { 130, 131 }; m.State = ForestMachineState.Driving; m.Upkeep.Wear = .5f;
        var control = new BehaviorController { Id = "wait", Kind = "forwarder" };
        control.States[0].Action = BehaviorAction.Wait;
        var model = new BehaviorModel { Controllers = new() { control }, Graphs = new() {
            TransportConstant("control.allow", "forwarder", 1), TransportConstant("control.home", "forwarder", 0),
            TransportConstant("control.upkeep", "forwarder", 0) } };
        SetTransportModel(s, model); Run(s, 1d / 30);
        Assert.True(m.PathPosition > 0); Assert.Equal(.5f, m.Upkeep.Wear);
        control.States[0].Action = BehaviorAction.ReturnHome; SetTransportModel(s, model); Run(s, 1d / 30);
        Assert.False(m.HomeRequested);
        model.Graphs[1].Nodes[0].Value = 1; SetTransportModel(s, model); Run(s, 1d / 30);
        Assert.True(m.HomeRequested); Assert.True(m.Working);
    }

    [Fact]
    public void ForwarderDepartureGraphCanHoldDespiteHomeAndControllerRequests()
    {
        var m = new ForestMachine { Kind = ForestMachineKind.Forwarder, State = ForestMachineState.Loading,
            Source = new TimberStack { Volume = 10, Value = 100 }, Cargo = 2, HomeRequested = true, Control = BehaviorAction.DepartLoaded };
        m.Behaviors = new WorldBehaviorPolicy(new() { Graphs = new() { TransportConstant("loading.depart", "forwarder", 0) } });
        Assert.False(ForwarderLoading.Step(m, .1, (_, _) => throw new Exception("Not unloading")));
        Assert.True(m.WorkTime > 0); Assert.Equal(2, m.Cargo); Assert.Equal(10, m.Source.Volume);
        m.WorkTime = m.LogCycleDuration = 0;
        m.Behaviors = new WorldBehaviorPolicy(new() { Graphs = new() { TransportConstant("loading.depart", "forwarder", 1) } });
        Assert.True(ForwarderLoading.Step(m, .1, (_, _) => throw new Exception("Not unloading")));
        Assert.Equal(10, m.Source.Volume);
    }
}

[Collection("SpeciesBook")]
public sealed class RuleCompositionTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static string State(GameWorld world) => JsonSerializer.Serialize(world.CaptureCheckpoint());

    [Fact]
    public void CreationEventsAddressOnlyTheNewObjectsAndKeepTheirContextInACheckpoint()
    {
        using var w = World();
        var existing = new WildlifeSystem.Deer { Id = 1, Hunger = .1f };
        var created = new WildlifeSystem.Deer { Id = 2, Hunger = 0 };
        w.Wildlife.Animals.AddRange(new[] { existing, created });
        var rules = RuleProject.CreateDefault().Rules.Where(r => r.Schedule == "birth").ToList();
        var engine = new RuleEngine(rules, new(), Array.Empty<string>(), w.ReadRuleValue);
        engine.Begin(w, 0, 0, "birth", eventScope: "animal", eventElements: new[] { 2 });
        var checkpoint = engine.CaptureStep(); Assert.Equal("birth", checkpoint.Schedule);
        Assert.Equal(new[] { 2 }, checkpoint.EventElements);
        var restored = new RuleEngine(rules, new(), Array.Empty<string>(), w.ReadRuleValue);
        restored.RestoreStep(w, checkpoint); foreach (int _ in restored.ContinueWork(w)) { }
        Assert.Equal(.1f, existing.Hunger); Assert.False(existing.Initialized);
        Assert.Equal(.5f, created.Hunger); Assert.True(created.Initialized);
    }

    [Fact]
    public void LocalAnimalAndTreeRelationshipsExecuteAndAppearOnTheCausalMap()
    {
        using var w = World();
        int tile = w.Forest.IndividualTrees.Patches.First(p => p.Value.Count > 0).Key;
        w.Wildlife.Animals.Add(new WildlifeSystem.Deer { Id = 1, TileId = tile, TargetTile = tile, Hunger = .8f, Initialized = true });
        var rule = new WorldRule { Id = "browse", Name = "Helyi növényevés", Scope = "tree", Schedule = "month", Condition = "tile.animals.count > 0",
            Effects = { new() { Target = "health", Formula = "1 - tile.animals.avg.hunger" } } };
        var engine = new RuleEngine(new() { rule }, new(), Array.Empty<string>(), w.ReadRuleValue);
        engine.Run(w, 1, 0, "month"); Assert.Null(engine.LastError);
        var trees = RuleEngine.Scope("tree"); var position = trees.Fields.Single(f => f.Name == "tile.index");
        var health = trees.Fields.Single(f => f.Name == "health");
        var affected = trees.Elements(w).Where(id => position.Read(w, id) == tile).ToArray(); Assert.NotEmpty(affected);
        Assert.All(affected, id => Assert.Equal(.2, health.Read(w, id), 5));
        var map = RuleCausalMap.Build(GameWorld.RuleObjects, new[] { rule });
        Assert.Contains(map.Edges, e => e.From == "tile.animals.avg.hunger" && e.To == "tree.health");
    }

    [Fact]
    public void MissionPriorityAndAvailableMetricsComeFromTheProjectAndDiscoveredWorld()
    {
        using var w = World(); var p = w.RuleProjectDocument;
        foreach (var id in new[] { "mission.reached", "mission.failed", "mission.state" }) {
            var graph = p.Behaviors.Graphs.Single(g => g.Hook == id);
            graph.Nodes = new() { new() { Id = "fixed", Name = "Állandó", Operation = BehaviorOperation.Constant, Value = id == "mission.state" ? 0 : 1 } };
            graph.Output = "fixed";
        }
        w.QueueRuleProject(p); w.ExecutePendingCommands();
        var goal = new MissionGoal { Id = "test", Title = "Teszt", Metric = "world.animals.count", Target = 0 };
        Assert.Equal(MissionState.Active, Assert.Single(w.MissionStatus(new[] { goal })).State);
        Assert.Contains(GameWorld.MissionMetrics, m => m.Key == "world.animals.avg.hunger");
        Assert.Contains(GameWorld.MissionMetrics, m => m.Key == "world.deliveredTimber");
        p = w.RuleProjectDocument; p.Behaviors.Graphs.Single(g => g.Hook == "mission.state").Nodes[0].Value = 2;
        w.QueueRuleProject(p); w.ExecutePendingCommands();
        Assert.Equal(MissionState.Failed, Assert.Single(w.MissionStatus(new[] { goal })).State);
    }

    [Fact]
    public void GenericPopulationPrimitiveCreatesANewAnimalClassWithDiscoveredDisplayOptions()
    {
        using var w = World(); var p = w.RuleProjectDocument; p.Rules.Clear();
        w.QueueRuleProject(p); w.ExecutePendingCommands(); w.Wildlife.RequestPopulation();
        w.Wildlife.Update(0, w.Map, w.Forest, w.Environment); Assert.Empty(w.Wildlife.Animals);
        var schema = RuleSchema.Discover(typeof(SimulationWorld).Assembly.GetTypes().Concat(typeof(SchemaFox).Assembly.GetTypes()), RuleEntityTexts.Hungarian);
        var fox = w.Wildlife.CreateTypes(schema).Single(t => t.Entity.Type == typeof(SchemaFox));
        Assert.True(fox.HabitatCount > 0); fox.DesiredPopulation = 2; fox.ApplyPopulation();
        w.Wildlife.Update(0, w.Map, w.Forest, w.Environment);
        Assert.Equal(2, w.Wildlife.Animals.Count); Assert.All(w.Wildlife.Animals, a => Assert.IsType<SchemaFox>(a));
        Assert.Equal(2, w.Wildlife.Animals.Select(a => a.Id).Distinct().Count());
        var option = schema.Find("animalType").Fields.Single(f => f.Name == "typeId").Options.Single(o => o.Name == "schemaFox");
        Assert.Equal("Próbaróka", option.Display); Assert.Equal(fox.TypeId, option.Value);
    }

    [Fact]
    public void PopulationAndObjectEquationsReadGlobalAggregatesAndResumeExactly()
    {
        using var w = World(); var p = w.RuleProjectDocument;
        p.Rules.Single(r => r.Id == "wildlife.populationTarget").Effects[0].Formula = "min(2; habitatCount)";
        p.Fields.Add(new() { Name = "saját.total", Scope = "world" });
        p.Rules.Add(new() { Id = "coupling", Name = "Összekapcsolt készletek", Scope = "world", Schedule = "population",
            Effects = { new() { Target = "saját.total", Formula = "world.animals.count + world.animalTypes.sum.desiredPopulation" } } });
        w.QueueRuleProject(p); w.ExecutePendingCommands();
        w.Update(1d / 30); w.Update(1d / 30);
        Assert.Equal(2, w.Wildlife.Animals.Count); Assert.Equal(4, w.RuleBook.ReadCustom(w, "saját.total", 0));
        using var save = new MemoryStream(); w.Save(save); save.Position = 0;
        using var restored = World(); restored.Load(save); Assert.Equal(State(w), State(restored));
        for (int i = 0; i < 15; i++) { w.Update(1d / 30); restored.Update(1d / 30); }
        Assert.Equal(State(w), State(restored));
        p = w.RuleProjectDocument; p.Rules.RemoveAll(r => r.Schedule == "population");
        w.QueueRuleProject(p); w.ExecutePendingCommands(); w.Wildlife.Animals.Clear(); w.Update(1d / 30);
        Assert.Empty(w.Wildlife.Animals);
    }

    [Fact]
    public void V3PoliciesMigrateOnceAndV4DeletionsRemainAuthoritative()
    {
        var p = RuleProject.CreateDefault(); p.Version = 3;
        p.Behaviors.Graphs.RemoveAll(g => g.Hook.StartsWith("control."));
        p.Rules.RemoveAll(r => r.Schedule == "population" || r.Id == "wildlife.initialize");
        p = GameWorld.NormalizeRuleProject(p);
        Assert.Equal(RuleProject.CurrentVersion, p.Version); Assert.Contains(p.Rules, r => r.Schedule == "population");
        Assert.Contains(p.Behaviors.Graphs, g => g.Hook == "control.allow");
        p.Rules.RemoveAll(r => r.Schedule == "population" || r.Id == "wildlife.initialize");
        Assert.DoesNotContain(GameWorld.NormalizeRuleProject(p).Rules, r => r.Schedule == "population" || r.Id == "wildlife.initialize");
    }
}
