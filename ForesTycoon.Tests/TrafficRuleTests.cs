using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class TrafficRuleTests
{
    private static GameWorld World(int side = 16) => new(TerrainSettings.Default.WithNodeSize(side + 1, 42), enableRendering: false);
    private static string State(GameWorld world) => JsonSerializer.Serialize(world.CaptureCheckpoint());
    private static int[] Network(GameWorld world, bool trail)
    {
        int start = Enumerable.Range(0, world.Map.Tiles.Count - 3).First(t => t / world.TileGrid.Rows == (t + 2) / world.TileGrid.Rows
            && Enumerable.Range(t, 3).All(world.Map.CanCarrySkidTrail));
        if (trail) world.QueueSkidTrailPath(start, start + 2, false);
        else world.QueueRoadPath(start, start + 2, false, RoadPaving.Macadam);
        world.ExecutePendingCommands();
        return Enumerable.Range(start, 3).ToArray();
    }
    private static void Pass(GameWorld world, int tile, int kind = 0, double mass = 36000, double fill = 1)
    {
        world.QueuePassage(new(tile, (TrafficVehicleKind)kind, 1, mass, fill)); world.ExecutePendingCommands();
    }

    [Fact]
    public void OneDiscoveredVisualRuleOwnsRoadWearTrailWearAndIdleReset()
    {
        using var world = World(); var rule = world.RuleBook.Rules.Single(r => r.Id == "traffic.wear");
        Assert.Equal("passage", rule.Schedule); Assert.Equal("passage", rule.Scope);
        Assert.All(rule.Effects, e => { Assert.NotNull(e.Influence); Assert.False(e.Influence.PerYear); Assert.False(e.Influence.PerHour); Assert.Empty(e.Formula); });
        Assert.DoesNotContain(WorldBehaviorPolicy.Hooks, h => h.Id == "road.trafficWear");
        Assert.Contains(RuleEngine.Scope("passage").Fields, f => f.Name == "road.condition" && f.Write != null);
        Assert.Contains(RuleEngine.Scope("passage").Fields, f => f.Name == "vehicle.mass" && f.Write == null);
        int road = Network(world, false)[0];
        Pass(world, road); Assert.Equal(.9985f, world.GetRoadCondition(road), 6);
        Pass(world, road, mass: 72000); Assert.Equal(.9955f, world.GetRoadCondition(road), 6);
        int track = Network(world, true)[0]; world.Map.SetSkidTrailState(track, .5f, 2);
        Pass(world, track); Assert.Equal(.515f, world.Map.GetSkidTrailWear(track), 6); Assert.Equal(0, world.Map.GetSkidTrailIdle(track));
    }

    [Theory]
    [InlineData(0, .04f)]
    [InlineData(.5, .07f)]
    [InlineData(1, .10f)]
    public void CargoAndExistingRutsInfluenceMachinePassages(double fill, float change)
    {
        using var world = World(); int id = Network(world, true)[0]; world.Map.SetSkidTrailState(id, .5f, 1);
        Pass(world, id, 2, 20000, fill);
        Assert.Equal(.5f + change * .5f, world.Map.GetSkidTrailWear(id), 6);
        Assert.Equal(0, world.Map.GetSkidTrailIdle(id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisablingOrDeletingTheRuleStopsAllTrafficChanges(bool delete)
    {
        using var world = World(); int road = Network(world, false)[0], trail = Network(world, true)[0];
        world.Map.SetSkidTrailState(trail, .4f, 2);
        var project = world.RuleProjectDocument; var rule = project.Rules.Single(r => r.Id == "traffic.wear");
        if (delete) { project.Rules.Remove(rule); project.RemovedStarters.Add(rule.Id); } else rule.Enabled = false;
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        Pass(world, road); Pass(world, trail); Pass(world, trail, 2);
        Assert.Equal(1, world.GetRoadCondition(road)); Assert.Equal(.4f, world.Map.GetSkidTrailWear(trail)); Assert.Equal(2, world.Map.GetSkidTrailIdle(trail));
        if (delete) Assert.DoesNotContain(world.RuleProjectDocument.Rules, r => r.Id == "traffic.wear");
    }

    [Fact]
    public void PreviewIsReadOnlyAndMatchesTheSameExecutableEventGraph()
    {
        using var world = World(); int id = Network(world, true)[0]; world.Map.SetSkidTrailState(id, .2f, 1);
        var passage = new TrafficPassage(id, TrafficVehicleKind.Forwarder, 1, 20000, .5);
        string before = State(world); var preview = world.PreviewPassage("traffic.wear", passage);
        Assert.Equal(before, State(world));
        world.QueuePassage(passage); world.ExecutePendingCommands();
        Assert.Equal(preview.Single(p => p.Target == "trail.wear").After, world.Map.GetSkidTrailWear(id), 6);
        Assert.Equal(preview.Single(p => p.Target == "trail.idleYears").After, world.Map.GetSkidTrailIdle(id));
    }

    [Fact]
    public void PreviewHonoursRuleConditionsAbsentRelationsAndUninitialisedCustomState()
    {
        using var world = World(); int id = Network(world, true)[0];
        var project = world.RuleProjectDocument; var rule = project.Rules.Single(r => r.Id == "traffic.wear");
        project.Fields.Add(new() { Name = "saját.count", Scope = "passage", Initial = 5 });
        rule.Effects.Add(new() { Target = "saját.count", Mode = "add", Influence = new() { PerYear = false, Terms = { new() { Decrease = false, Amount = 1 } } } });
        rule.Effects[0].Influence.Terms.Clear(); rule.Effects[0].Influence.Terms.Add(new() { Amount = .2 });
        rule.Condition = "vehicle.mass > 50000";
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        string before = State(world);
        var inactive = world.PreviewPassage(rule.Id, new(id, TrafficVehicleKind.Truck, 1, 10000, 1));
        Assert.All(inactive, row => Assert.Equal(row.Before, row.After));
        var active = world.PreviewPassage(rule.Id, new(id, TrafficVehicleKind.Truck, 1, 60000, 1));
        Assert.Equal(before, State(world));
        var absent = active.Single(r => r.Target == "road.condition"); Assert.Equal(absent.Before, absent.After);
        Assert.Equal(6, active.Single(r => r.Target == "saját.count").After);
        Pass(world, id, mass: 60000);
        Assert.Equal(6, world.RuleBook.ReadCustom(world, "saját.count", id));
    }

    [Fact]
    public void RetiredProjectDataAndInvalidJournalEventsAreRejectedClearly()
    {
        Assert.Throws<InvalidDataException>(() => RuleProject.FromJson("{\"road\":{}}"));
        // The untouched retired road model equals the passage graph and is dropped; an edited one is refused.
        string Road(double scale) => "{\"road\":{\"nodes\":[{\"id\":\"t\",\"operation\":\"TrafficWear\"},{\"id\":\"s\",\"operation\":\"SurfaceFactor\"},"
            + "{\"id\":\"k\",\"operation\":\"Constant\",\"value\":" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},{\"id\":\"w\",\"operation\":\"Multiply\"}]},"
            + "\"schema\":\"forest-rule-project\",\"version\":1,\"name\":\"régi\"}";
        Assert.Equal("régi", RuleProject.FromJson(Road(1)).Name);
        Assert.Throws<InvalidDataException>(() => RuleProject.FromJson(Road(1.5)));
        Assert.Throws<ArgumentException>(() => new TrafficPassageCommand(new(0, TrafficVehicleKind.Truck, 1, double.NaN, 1)));
    }

    [Theory]
    [InlineData(64)]
    [InlineData(256)]
    public void ALongQueueYieldsAndSaveAndProjectReplacementCannotOvertakeIt(int side)
    {
        using var world = World(side); int id = Network(world, true)[0];
        for (int i = 0; i < 300; i++) world.QueuePassage(new(id, TrafficVehicleKind.Forwarder, i, 20000, .5));
        Assert.Equal(0, world.ContinueCommands(1));
        bool saved = false; using var stream = new MemoryStream(); world.AtTickBoundary(() => { world.Save(stream); saved = true; });
        Assert.Throws<InvalidOperationException>(() => world.CaptureCheckpoint());
        var next = world.RuleProjectDocument; next.Rules.Single(r => r.Id == "traffic.wear").Enabled = false; world.QueueRuleProject(next);
        int slices = 0, completed = 0;
        while (world.CommandPending) {
            Assert.False(saved); int done = world.ContinueCommands(7);
            Assert.InRange(done, 0, 7); completed += done;
            Assert.True(++slices < 50000);
        }
        // Only relevant rules consume units now; verify the real budget and deferred boundary,
        // rather than requiring one suspension for every unrelated rule in the book.
        Assert.Equal(300, completed); Assert.True(saved); Assert.True(slices > 1); Assert.True(world.Map.GetSkidTrailWear(id) > .99f);
        // New commands join the next batch, after the completed old-rule events.
        world.ExecutePendingCommands(); Pass(world, id);
        stream.Position = 0; using var restored = World(); restored.Load(stream); restored.ExecutePendingCommands(); Pass(restored, id);
        Assert.Equal(State(world), State(restored));
    }

    [Fact]
    public void PendingEventsAndEditedGraphsSurviveSaveAndJournalOnlyReplay()
    {
        using var world = World(); int id = Network(world, false)[0];
        var project = world.RuleProjectDocument;
        project.Rules.Single(r => r.Id == "traffic.wear").Effects[0].Influence.Terms[1].Amount = .02;
        world.QueueRuleProject(project); world.QueuePassage(new(id, TrafficVehicleKind.Truck, 1, 36000, 1));
        using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0;
        using var restored = World(); restored.Load(saved);
        while (world.ForegroundPending) world.ContinueCommands(3);
        restored.ExecutePendingCommands(); Assert.Equal(State(world), State(restored)); Assert.Equal(.98f, world.GetRoadCondition(id), 6);
        saved.Position = 0; var data = WorldSaveSerializer.Read(saved);
        using var journal = new MemoryStream(); WorldSaveSerializer.Write(journal, new WorldSaveData {
            Terrain = data.Terrain, SoilModel = data.SoilModel, Climate = data.Climate, Tick = data.Tick,
            TickRate = data.TickRate, ForestYearSeconds = data.ForestYearSeconds, Commands = data.Commands });
        journal.Position = 0; using var replay = World(); replay.Load(journal);
        Assert.Equal(State(world), State(replay));
    }

    [Fact]
    public void LoadingACompleteSnapshotDiscardsAnUnfinishedEventAndItsContinuation()
    {
        using var world = World(); int id = Network(world, true)[0];
        string initial = State(world); using var saved = new MemoryStream(); world.Save(saved);
        world.QueuePassage(new(id, TrafficVehicleKind.Forwarder, 1, 20000, 1));
        Assert.Equal(0, world.ContinueCommands(1)); Assert.True(world.CommandPending); Assert.True(world.PendingTrafficPassages > 0);
        saved.Position = 0; world.Load(saved);
        Assert.Equal(0, world.PendingTrafficPassages); Assert.False(world.BoundaryPending); Assert.False(world.RuleBook.StepPending);
        Assert.Equal(initial, State(world));
        Pass(world, id, 2); Assert.Equal(.1f, world.Map.GetSkidTrailWear(id), 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RealMachineMovementPublishesAnEventAndTheLogicalTickDrainsIt(int kind)
    {
        using var world = World(); var ids = Network(world, true); world.Map.SetSkidTrailState(ids[1], .5f, 1);
        var machine = new ForestMachine { Id = 1, Kind = (ForestMachineKind)kind, Path = ids, PathPosition = .499,
            State = ForestMachineState.Driving, Goal = ForestMachineState.Parked };
        world.Logistics.Machines.Add(machine);
        int slices = 0; while (!world.ContinueUpdate(1d / 30, 1)) Assert.True(++slices < 50000);
        Assert.True(machine.PathPosition > .5); Assert.Equal(.52f, world.Map.GetSkidTrailWear(ids[1]), 6);
        Assert.Equal(0, world.Map.GetSkidTrailIdle(ids[1])); Assert.Equal(0, world.PendingTrafficPassages);
    }

    [Fact]
    public void OnlyPassageSchedulesCanUseTheEventScopeAndNoTimeScaledEventEffectsAreAccepted()
    {
        var rule = TrafficWearRule.Default(); rule.Schedule = "step"; Assert.Throws<InvalidDataException>(rule.Validate);
        rule.Schedule = "passage"; rule.Effects[0].Influence.PerYear = true; Assert.Throws<InvalidDataException>(rule.Validate);
    }

    [Fact]
    public void MovingTruckKeepsItsLastEnteredTileAcrossSaveAndBudgetChanges()
    {
        using var world = World();
        var scenario = GameWorld.DefaultScenario(42, 16);
        for (int id = 0; id < 17 * 17; id++) scenario.HeightPaint[id] = 4;
        world.StartScenario(scenario);
        var route = Network(world, false);
        route = world.Map.FindNetworkPath(route[0], route[^1]);
        Assert.True(route.Length >= 2);
        var project = world.RuleProjectDocument;
        foreach (var rule in project.Rules.Where(r => r.Schedule == "step")) rule.Enabled = false;
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        var truck = world.Logistics.Vehicles.SpawnTransit(route); truck.Load(20);
        world.Update(1d / 30);
        Assert.True(world.CaptureCheckpoint().Vehicles.Vehicles.Single().LastPassageTile >= 0);
        using var stream = new MemoryStream(); world.Save(stream); stream.Position = 0;
        using var restored = World(); restored.Load(stream);
        Assert.Equal(State(world), State(restored));
        for (int tick = 0; tick < 200; tick++) {
            world.Update(1d / 30);
            int slices = 0; while (!restored.ContinueUpdate(1d / 30, 3)) Assert.True(++slices < 50000);
        }
        Assert.Equal(State(world), State(restored));
        Assert.True(world.GetRoadCondition(route[1]) < 1); Assert.Equal(0, world.PendingTrafficPassages);
    }
}
