using System.Text.Json;
using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

public partial class FleetTests
{
    [Fact]
    public void AuthoredWorkflowChangesActualDispatchAndCannotDiscardCargo()
    {
        var scene = Build(); var machine = Forwarder(scene);
        machine.Source = scene.Logistics.StackAt(ForestStack); machine.Destination = RoadStack;
        machine.Cargo = 2; machine.CargoValue = 200; machine.Path = new[] { ForestStack }; machine.State = ForestMachineState.Parked;
        SetTransportModel(scene, new() { Graphs = new() { TransportConstant("machine.workflow", "forwarder", 1) } });
        Run(scene, 1d / 30); Assert.Equal(ForestMachineState.Parked, machine.State); Assert.Equal(2, machine.Cargo);
        SetTransportModel(scene, new() { Graphs = new() { TransportConstant("machine.workflow", "forwarder", 2) } });
        Run(scene, 1d / 30); Assert.True(machine.Working); Assert.Equal(2, machine.Cargo); Assert.Equal(200, machine.CargoValue);
        SetTransportModel(scene, new() { Graphs = new() { TransportConstant("machine.workflow", "forwarder", 3) } });
        Run(scene, 1d / 30); Assert.Equal(ForestMachineState.Driving, machine.State); Assert.Equal(ForestMachineState.Unloading, machine.Goal);
        Assert.Equal(2, machine.Cargo);
    }

    [Fact]
    public void EveryLogisticsDecisionIsAPureGraphWithDiscoveredObjectAndFieldNames()
    {
        var model = WorldBehaviorPolicy.DefaultDocument();
        foreach (var hook in LogisticsRules.Hooks) {
            var graph = model.Graphs.Single(g => g.Hook == hook.Id && g.Kind == hook.Kind && !g.ObjectId.HasValue);
            Assert.All(graph.Nodes, n => Assert.True(LogisticsRules.AllowedOperation(n.Operation)));
            foreach (string input in hook.Inputs) {
                var parts = input.Split('.');
                var relation = GameWorld.RuleObjects.Find(hook.Kind).Relations.Single(r => r.Name == parts[0]);
                Assert.NotEmpty(GameWorld.RuleObjects.Find(relation.Target).Fields.Single(f => f.Name == parts[1]).Display);
            }
            Assert.NotEmpty(ResponseRuleDescription.For(hook.Id).FixedMeaning);
            graph.Nodes.Add(new() { Id = "retired", Name = "Retired native result", Operation = BehaviorOperation.Native });
            Assert.Throws<InvalidDataException>(() => new WorldBehaviorPolicy(model));
            graph.Nodes.RemoveAt(graph.Nodes.Count - 1);
        }
    }

    [Fact]
    public void AuthoredFellingAndFuelChangeActualProcessorAndZeroStopsExtraction()
    {
        var scene = Build(); var machine = Processor(scene);
        scene.Logistics.Tuning = GameTuning.FromOverrides(new Dictionary<string, double> {
            [nameof(Tune.WearPerSecond)] = 0, [nameof(Tune.BreakdownHazard)] = 0 });
        machine.Target = scene.Logistics.StackAt(ForestStack); machine.Path = new[] { Stand[0] };
        machine.State = ForestMachineState.Felling;
        var model = new BehaviorModel { Graphs = new() {
            TransportConstant("machine.felling", "processor", .05), TransportConstant("machine.fuel", "processor", .2),
            TransportConstant("machine.continueFelling", "processor", 1) } };
        SetTransportModel(scene, model); float before = scene.Forest.AvailableTimber(Stand[0]);
        Run(scene, 1d / 30);
        Assert.Equal(.05f, machine.Cargo, 5); Assert.Equal(.2, machine.FuelUsed, 8);
        Assert.True(scene.Forest.AvailableTimber(Stand[0]) < before);
        model.Graphs[0].Disabled = true; model.Graphs[0].DisabledValue = 0;
        SetTransportModel(scene, model); before = scene.Forest.AvailableTimber(Stand[0]); Run(scene, 1d / 30);
        Assert.Equal(.05f, machine.Cargo, 5); Assert.Equal(before, scene.Forest.AvailableTimber(Stand[0]));
    }

    [Fact]
    public void CargoCurveChangesActualMachineMovementAndDisablingDeliveryKeepsItsCargo()
    {
        var empty = Build(); var loaded = Build();
        foreach (var scene in new[] { empty, loaded }) {
            var m = Forwarder(scene); m.Path = new[] { 130, 131 }; m.State = ForestMachineState.Driving;
            m.Cargo = scene == loaded ? 14 : 0;
            var graph = TransportConstant("machine.speed", "forwarder", 0);
            graph.Nodes = new() {
                new() { Id = "cargo", Name = "Rakomány", Operation = BehaviorOperation.Input, Key = "machine.cargo" },
                new() { Id = "speed", Name = "Rakomány lassítása", Operation = BehaviorOperation.Curve, A = "cargo",
                    Key = new RuleCurve { Input = BehaviorCurves.Input, Points = new() { (0, 2), (20, 0) } }.ToFormula() }
            }; graph.Output = "speed";
            SetTransportModel(scene, new() { Graphs = new() { graph } }); Run(scene, 1d / 30);
        }
        Assert.True(Forwarder(empty).PathPosition > Forwarder(loaded).PathPosition * 3);
        var machine = Forwarder(loaded); machine.Source = loaded.Logistics.StackAt(ForestStack);
        machine.Destination = RoadStack; machine.State = ForestMachineState.Parked;
        SetTransportModel(loaded, new() { Graphs = new() { TransportConstant("machine.deliver", "forwarder", 0) } });
        Run(loaded, 1); Assert.Equal(14, machine.Cargo); Assert.True(machine.Working);
        Assert.Equal(ForestMachineState.Parked, machine.State);
    }

    [Fact]
    public void FastMachineVisitsEveryTileInSeparateUnitsAndCannotSkipABrokenConnection()
    {
        foreach (bool broken in new[] { false, true }) {
            var scene = Build(); var machine = Forwarder(scene);
            if (broken) scene.Map.BuildRoadTilePath(35, 67);
            machine.Path = broken ? new[] { 34, 50, 51, 67 } : new[] { 130, 131, 132, 133, 134, 135 };
            machine.State = ForestMachineState.Driving;
            var mass = TransportConstant("machine.mass", "forwarder", 100);
            mass.Nodes.Add(new() { Id = "dt", Name = "Munkaidő", Operation = BehaviorOperation.Input, Key = "step.seconds" });
            mass.Nodes.Add(new() { Id = "result", Name = "Lépés tömege", Operation = BehaviorOperation.Multiply, A = "dt", B = "value" }); mass.Output = "result";
            SetTransportModel(scene, new() { Graphs = new() {
                TransportConstant("machine.pace", "forwarder", 10), TransportConstant("machine.speed", "forwarder", 100), mass } });
            int units = 0;
            using (var work = scene.Logistics.UpdateWork(1d / 30).GetEnumerator()) while (SimulationWork.MoveNext(work)) units++;
            Assert.True(units > scene.Logistics.Machines.Count + 2);
            Assert.Equal(broken ? 50 : 135, machine.Tile);
            Assert.All(scene.Passages, passage => Assert.Equal(1000, passage.Mass, 6));
            Assert.Equal(broken ? new[] { 50 } : new[] { 131, 132, 133, 134, 135 }, scene.Passages.Select(p => p.TileId));
        }
    }

    [Fact]
    public void SupplySearchFollowsLongChainsAndTerminatesCyclesWithoutRecursionLimit()
    {
        var scene = Build(); scene.Logistics.Machines.Clear();
        var stacks = Enumerable.Range(0, 12).Select(i => new TimberStack { Id = 100 + i, Tile = i }).ToArray();
        foreach (int i in Enumerable.Range(0, stacks.Length - 1)) scene.Logistics.Machines.Add(new() {
            Id = i + 1, Kind = ForestMachineKind.Forwarder, Source = stacks[i + 1], Destination = stacks[i].Tile, Path = new[] { 0 } });
        var processor = new ForestMachine { Id = 100, Kind = ForestMachineKind.Harvester, Target = stacks[^1], HasWork = true, Path = new[] { 0 } };
        scene.Logistics.Machines.Add(processor);
        Assert.True(scene.Logistics.BeingFed(stacks[0]));
        processor.HasWork = false;
        scene.Logistics.Machines.Add(new() { Id = 200, Kind = ForestMachineKind.Forwarder, Source = stacks[0], Destination = stacks[^1].Tile, Path = new[] { 0 } });
        Assert.False(scene.Logistics.BeingFed(stacks[0]));
        stacks[7].Volume = 1; Assert.True(scene.Logistics.BeingFed(stacks[0]));
        stacks[7].Volume = 0; scene.Logistics.Machines[0].LogTransferVolume = .3f;
        Assert.True(scene.Logistics.BeingFed(stacks[0]));
    }

    [Fact]
    public void CycleTimingReadsActualWorkStepAndTruckReturnReadsActualWear()
    {
        BehaviorGraph Scaled(string hook, double scale) {
            var graph = TransportConstant(hook, "forwarder", scale);
            graph.Nodes.Add(new() { Id = "dt", Name = "Eltelt munkaidő", Operation = BehaviorOperation.Input, Key = "step.seconds" });
            graph.Nodes.Add(new() { Id = "result", Name = "Időzítés", Operation = BehaviorOperation.Multiply, A = "dt", B = "value" }); graph.Output = "result";
            return graph;
        }
        var machine = new ForestMachine { Kind = ForestMachineKind.Forwarder, State = ForestMachineState.Loading,
            Source = new TimberStack { Volume = 10, Value = 100 } };
        machine.Behaviors = new WorldBehaviorPolicy(new() { Graphs = new() {
            Scaled("loading.cycleSeconds", 240), Scaled("loading.gripPhase", 3), Scaled("loading.releasePhase", 10) } });
        ForwarderLoading.Step(machine, 1d / 30, (_, _) => throw new Exception("Not unloading"));
        Assert.Equal(8, machine.LogCycleDuration, 8); Assert.Equal(.1, machine.LogGripPhase, 8); Assert.Equal(1d / 3, machine.LogReleasePhase, 8);

        var scene = Build(); var source = scene.Logistics.StackAt(RoadStack); source.Add(100, 10000);
        var truck = scene.Logistics.Trucks[0]; Assert.True(scene.Logistics.AssignTruck(truck, source, Mill));
        SetTransportModel(scene, new() { Graphs = new() { TransportConstant("loading.amount", "truck", 0) } });
        for (int i = 0; i < 6000 && truck.Phase != TruckPhase.Working; i++) Run(scene, 1d / 30);
        Assert.Equal(TruckPhase.Working, truck.Phase); truck.Upkeep.Wear = .8f;
        var departure = TransportConstant("truck.return", "truck", 0);
        departure.Nodes[0].Operation = BehaviorOperation.Input; departure.Nodes[0].Key = "machine.wear";
        SetTransportModel(scene, new() { Graphs = new() { departure } });
        Run(scene, 1d / 30); Assert.Equal(TruckPhase.ToHome, truck.Phase);
    }

    [Fact]
    public void InvalidCycleGraphUsesFiniteMinimumAndDoesNotStrandTheHeldLog()
    {
        var machine = new ForestMachine { Kind = ForestMachineKind.Forwarder, State = ForestMachineState.Loading,
            Source = new TimberStack { Volume = 10, Value = 100 } };
        var graph = TransportConstant("loading.cycleSeconds", "forwarder", 0);
        graph.Nodes.Add(new() { Id = "bad", Name = "Hibás osztás", Operation = BehaviorOperation.Divide, A = "value", B = "value" }); graph.Output = "bad";
        var policy = new WorldBehaviorPolicy(new() { Graphs = new() { graph } }); machine.Behaviors = policy;
        ForwarderLoading.Step(machine, .1, (_, _) => throw new Exception("Not unloading"));
        Assert.NotNull(policy.LastError); Assert.True(machine.Cargo > 0); Assert.Equal(0, machine.LogTransferVolume);
        Assert.True(double.IsFinite(machine.WorkTime));
    }

    [Fact]
    public void AuthoredDepartureCanOverrideHomePriorityWithoutLosingCargo()
    {
        var scene = Build(); var source = scene.Logistics.StackAt(RoadStack); source.Add(100, 10000);
        var truck = scene.Logistics.Trucks[0]; Assert.True(scene.Logistics.AssignTruck(truck, source, Mill));
        SetTransportModel(scene, new() { Graphs = new() { TransportConstant("loading.depart", "truck", 0) } });
        for (int i = 0; i < 6000 && (truck.Vehicle == null || truck.Vehicle.CargoFill < 1); i++) Run(scene, 1d / 30);
        Assert.NotNull(truck.Vehicle); Assert.Equal(1, truck.Vehicle.CargoFill); Run(scene, 1);
        Assert.True(truck.Vehicle.TransportState is VehicleTransportState.Loading or VehicleTransportState.Waiting);
        scene.Logistics.SendHome(truck);
        float stock = source.Volume;
        Run(scene, 5);
        Assert.Equal(stock, source.Volume); Assert.Equal(1, truck.Vehicle.CargoFill);
        Assert.Equal(0, scene.Logistics.Mills.Single().Received);
        Assert.True(truck.Vehicle.TransportState is VehicleTransportState.Loading or VehicleTransportState.Waiting);
        SetTransportModel(scene, new() { Graphs = new() { TransportConstant("loading.depart", "truck", 1) } });
        Run(scene, 240);
        Assert.Equal(TruckPhase.Parked, truck.Phase); Assert.True(scene.Logistics.Mills.Single().Received > 0);
    }
}

[Collection("SpeciesBook")]
public sealed class LogisticsProjectTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static string State(GameWorld world) => JsonSerializer.Serialize(world.CaptureCheckpoint());
    [Fact]
    public void PendingLogisticsProjectSurvivesSnapshotAndJournalReplayWithoutForestRefresh()
    {
        using var world = World(); world.Update(1d / 30); var project = world.RuleProjectDocument;
        foreach (var graph in project.Behaviors.Graphs.Where(g => LogisticsRules.IsKind(g.Kind))) {
            graph.Disabled = true; graph.DisabledValue = ResponseRuleDescription.For(graph.Hook).SuggestedOffValue;
        }
        world.QueueRuleProject(project);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = World(); restored.Load(save);
        world.ExecutePendingCommands(); do { restored.ContinueCommands(1); } while (restored.CommandPending);
        Assert.Equal(State(world), State(restored)); Assert.False(world.CaptureCheckpoint().PendingForestRateRefresh);
        save.Position = 0; var data = WorldSaveSerializer.Read(save);
        using var journal = new MemoryStream(); WorldSaveSerializer.Write(journal, new WorldSaveData {
            Terrain = data.Terrain, SoilModel = data.SoilModel, Climate = data.Climate, Tick = data.Tick,
            TickRate = data.TickRate, ForestYearSeconds = data.ForestYearSeconds, Commands = data.Commands });
        journal.Position = 0; restored.Load(journal); restored.ExecutePendingCommands(); Assert.Equal(State(world), State(restored));
        for (int tick = 0; tick < 60; tick++) {
            world.Update(1d / 30); while (!restored.ContinueUpdate(1d / 30, 3)) { }
        }
        Assert.Equal(State(world), State(restored));
    }
}
