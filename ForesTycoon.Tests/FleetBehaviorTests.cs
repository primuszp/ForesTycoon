using System.Text.Json;
using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

public partial class FleetTests
{
    private static BehaviorGraph TransportConstant(string hook, string kind, double value, ulong? id = null) => new() {
        Id = Guid.NewGuid().ToString("N"), Hook = hook, Kind = kind, ObjectId = id, Output = "value",
        Nodes = new() { new() { Id = "value", Name = "Érték", Operation = BehaviorOperation.Constant, Value = value } }
    };
    private static void SetTransportModel(Scene scene, BehaviorModel model)
    {
        var policy = new WorldBehaviorPolicy(model); scene.Logistics.Behaviors = policy; scene.Logistics.Controllers = policy.Controllers;
    }

    [Fact]
    public void TruckControllerWaitsWithCargoAndResumesWithoutChangingValue()
    {
        var s = Build(); var source = s.Logistics.StackAt(RoadStack); source.Add(30, 900);
        var truck = s.Logistics.Trucks[0]; Assert.True(s.Logistics.AssignTruck(truck, source, Mill));
        for (int i = 0; i < 6000 && truck.Vehicle.CargoAmount == 0; i++) Run(s, 1.0 / 30);
        Assert.True(truck.Vehicle.CargoAmount > 0);
        float cargo = truck.Vehicle.CargoAmount; double value = truck.CargoValue, fuel = truck.Vehicle.FuelUsed;
        var pause = BehaviorController.TimedPause("truck"); pause.ObjectId = (ulong)truck.Id;
        SetTransportModel(s, new() { Controllers = new() { pause } });
        Run(s, 4); Assert.Equal(cargo, truck.Vehicle.CargoAmount); Assert.Equal(value, truck.CargoValue); Assert.Equal(fuel, truck.Vehicle.FuelUsed);
        Run(s, 300); Assert.Equal(30, s.Logistics.Mills[0].Received, 3); Assert.Equal(900, s.Logistics.Income, 3);
        Assert.Equal(TruckPhase.Parked, truck.Phase);
    }
    [Theory]
    [InlineData("truck")]
    [InlineData("forwarder")]
    public void GraphTransportCycleCompletesSeveralPartialLoadsAndReturnsHome(string kind)
    {
        var s = Build(); var source = s.Logistics.StackAt(kind == "truck" ? RoadStack : ForestStack); source.Add(12, 360);
        var controller = BehaviorController.TransportCycle(kind);
        SetTransportModel(s, new() { Controllers = new() { controller } });
        if (kind == "truck") Assert.True(s.Logistics.AssignTruck(s.Logistics.Trucks[0], source, Mill));
        else Assert.True(s.Logistics.AssignForwarder(Forwarder(s), source, Mill));
        float largest = 0;
        for (int i = 0; i < 24000; i++) {
            Run(s, 1.0 / 30);
            largest = Math.Max(largest, kind == "truck" ? s.Logistics.Trucks[0].Vehicle?.CargoAmount ?? 0 : Forwarder(s).Cargo);
        }
        Assert.InRange(largest, 5, 6.3f);
        Assert.Equal(12, s.Logistics.Mills[0].Received, 3); Assert.Equal(360, s.Logistics.Income, 2);
        Assert.Equal(0, source.Volume);
        if (kind == "truck") Assert.Equal(TruckPhase.Parked, s.Logistics.Trucks[0].Phase);
        else { Assert.False(Forwarder(s).Working); Assert.Equal(Depot, Forwarder(s).Tile); }
    }
    [Fact]
    public void TruckLoadingGraphsBoundTransfersAndEarlyDeparturePreservesWoodAndMoney()
    {
        var s = Build(); var source = s.Logistics.StackAt(RoadStack); source.Add(10, 500);
        SetTransportModel(s, new() { Graphs = new() {
            TransportConstant("loading.amount", "truck", 1), TransportConstant("loading.depart", "truck", 1), TransportConstant("unloading.amount", "truck", 1e9)
        } });
        Assert.True(s.Logistics.AssignTruck(s.Logistics.Trucks[0], source, Mill));
        float largest = 0;
        for (int i = 0; i < 24000; i++) { Run(s, 1.0 / 30); largest = Math.Max(largest, s.Logistics.Trucks[0].Vehicle?.CargoAmount ?? 0); }
        Assert.InRange(largest, .999f, 1.001f); Assert.Equal(10, s.Logistics.Mills[0].Received, 3); Assert.Equal(500, s.Logistics.Income, 3);
    }
    [Fact]
    public void TruckHomeGraphWithPartialLoadDoesNotTakeMoreTimber()
    {
        var s = Build(); var source = s.Logistics.StackAt(RoadStack); source.Add(30, 900);
        var truck = s.Logistics.Trucks[0]; Assert.True(s.Logistics.AssignTruck(truck, source, Mill));
        for (int i = 0; i < 6000 && truck.Vehicle.CargoAmount == 0; i++) Run(s, 1.0 / 30);
        float cargo = truck.Vehicle.CargoAmount, remaining = source.Volume;
        var home = new BehaviorController { Id = "home", Kind = "truck" }; home.States[0].Action = BehaviorAction.ReturnHome;
        SetTransportModel(s, new() { Controllers = new() { home }, Graphs = new() { TransportConstant("loading.depart", "truck", 0) } });
        Run(s, 200);
        Assert.Equal(remaining, source.Volume); Assert.Equal(cargo, s.Logistics.Mills[0].Received, 4); Assert.Equal(TruckPhase.Parked, truck.Phase);
        Assert.Equal(900, source.Value + s.Logistics.Income, 3);
    }
    [Fact]
    public void ForbiddenReturnPathWaitsInPlaceAndRecoversWhenRuleIsRemoved()
    {
        var s = Build(); var source = s.Logistics.StackAt(RoadStack); source.Add(1, 30);
        var truck = s.Logistics.Trucks[0]; Assert.True(s.Logistics.AssignTruck(truck, source, Mill));
        for (int i = 0; i < 6000 && truck.Phase != TruckPhase.Working; i++) Run(s, 1.0 / 30);
        SetTransportModel(s, new() { Graphs = new() { TransportConstant("route.allow", "truck", 0) } });
        Run(s, 200);
        Assert.NotEqual(TruckPhase.Parked, truck.Phase); Assert.NotNull(truck.Vehicle); Assert.Equal(1, s.Logistics.Mills[0].Received, 3);
        SetTransportModel(s, new()); Run(s, 100); Assert.Equal(TruckPhase.Parked, truck.Phase);
    }
    [Fact]
    public void TruckObjectBindingUsesFleetIdAcrossReplacementRoadVehicles()
    {
        var s = Build(); var source = s.Logistics.StackAt(RoadStack); source.Add(2, 60);
        var type = BehaviorController.TimedPause("truck"); type.States[0].Transitions.Clear();
        var one = new BehaviorController { Id = "one", Kind = "truck", ObjectId = (ulong)s.Logistics.Trucks[0].Id };
        SetTransportModel(s, new() { Controllers = new() { type, one } });
        Assert.True(s.Logistics.AssignTruck(s.Logistics.Trucks[0], source, Mill)); Run(s, 200);
        Assert.Equal(2, s.Logistics.Mills[0].Received, 3); Assert.Equal(TruckPhase.Parked, s.Logistics.Trucks[0].Phase);
        Assert.Equal(one.Id, Assert.Single(s.Logistics.Controllers.Capture()).Controller);
    }
    [Fact]
    public void WorldSaveResumesTruckControllerAndStackDestinationWithIdenticalState()
    {
        var fixture = Build();
        var map = new TerrainMap(Settings); using var world = new GameWorld(map);
        map.Restore(fixture.Map.Capture());
        world.Logistics.Restore(fixture.Logistics.Capture()); world.Logistics.BindVehicles(world.Logistics.Vehicles);
        var source = world.Logistics.StackAt(RoadStack); source.Add(10, 500);
        world.QueueBehaviors(new() { Controllers = new() { BehaviorController.TransportCycle("truck") }, Graphs = new() {
            TransportConstant("loading.amount", "truck", .1), TransportConstant("unloading.amount", "truck", .05)
        } }); world.ExecutePendingCommands();
        Assert.True(world.Logistics.AssignTruck(world.Logistics.Trucks[0], source, ForestStack));
        for (int i = 0; i < 6000 && world.Logistics.Trucks[0].Vehicle.CargoAmount == 0; i++) world.Update(1.0 / 30);
        Assert.True(world.Logistics.Trucks[0].Vehicle.CargoAmount > 0);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var copy = new GameWorld(Settings, enableRendering: false); copy.Load(save);
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(copy.CaptureCheckpoint()));
        for (int i = 0; i < 300; i++) { world.Update(1.0 / 30); copy.Update(1.0 / 30); }
        Assert.Equal(JsonSerializer.Serialize(world.CaptureCheckpoint()), JsonSerializer.Serialize(copy.CaptureCheckpoint()));
    }
    [Fact]
    public void ForwarderCustomGripTimingIsFrozenAcrossModelChangesAndCheckpoint()
    {
        var s = Build(); var source = s.Logistics.StackAt(ForestStack); source.Add(5, 200);
        SetTransportModel(s, new() { Graphs = new() {
            TransportConstant("loading.cycleSeconds", "forwarder", 2), TransportConstant("loading.gripPhase", "forwarder", .2),
            TransportConstant("loading.releasePhase", "forwarder", .6), TransportConstant("loading.amount", "forwarder", .3)
        } });
        var machine = Forwarder(s); Assert.True(s.Logistics.AssignForwarder(machine, source, RoadStack));
        for (int i = 0; i < 6000 && machine.LogTransferVolume == 0; i++) Run(s, 1.0 / 30);
        Assert.Equal(.3f, machine.LogTransferVolume, 4); Assert.Equal(2, machine.LogCycleDuration); Assert.Equal(.2, machine.LogGripPhase); Assert.Equal(.6, machine.LogReleasePhase);
        Assert.True(ForwarderLoading.AnimationPhase(machine) >= ForwarderLoading.Grip - .00001);
        var checkpoint = s.Logistics.Capture(); var copy = Build(stacks: false); copy.Logistics.Restore(checkpoint);
        SetTransportModel(s, new()); SetTransportModel(copy, new());
        Run(s, 1); Run(copy, 1);
        Assert.Equal(machine.Cargo, Forwarder(copy).Cargo); Assert.Equal(machine.LogTransferVolume, Forwarder(copy).LogTransferVolume);
        Assert.Equal(.3f, machine.Cargo, 4); Assert.Equal(0, machine.LogTransferVolume);
        Assert.Equal(5, source.Volume + machine.Cargo, 4); Assert.Equal(200, source.Value + machine.CargoValue, 3);
    }
}
