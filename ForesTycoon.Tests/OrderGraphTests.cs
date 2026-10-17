using System.Text.Json;
using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

public partial class FleetTests
{
    [Fact]
    public void OrderDenialPreservesExistingAssignmentsAndMatchesThePreviewForEveryAgent()
    {
        var scene = Build(); var processor = Processor(scene); var forwarder = Forwarder(scene);
        var truck = scene.Logistics.Trucks.Single();
        var source = scene.Logistics.StackAt(ForestStack); var road = scene.Logistics.StackAt(RoadStack);
        Assert.True(scene.Logistics.AssignProcessor(processor, source));
        Assert.True(scene.Logistics.AssignForwarder(forwarder, source, RoadStack));
        forwarder.Cargo = 2; forwarder.LogTransferVolume = .4f;
        SetTransportModel(scene, new() { Graphs = new() {
            TransportConstant("orders.allow", "processor", 0), TransportConstant("orders.allow", "forwarder", 0),
            TransportConstant("orders.allow", "truck", 0) } });
        string before = JsonSerializer.Serialize(scene.Logistics.Capture() with { Status = "" });
        Assert.NotNull(scene.Logistics.CheckProcessorStack(processor, ForestStack));
        Assert.NotNull(scene.Logistics.CheckDestination(ForestStack, RoadStack, false, forwarder));
        Assert.NotNull(scene.Logistics.CheckDestination(RoadStack, Mill, true, unit: truck));
        Assert.False(scene.Logistics.AssignProcessor(processor, source));
        Assert.False(scene.Logistics.AssignForwarder(forwarder, source, RoadStack));
        Assert.False(scene.Logistics.AssignTruck(truck, road, Mill));
        Assert.Equal(before, JsonSerializer.Serialize(scene.Logistics.Capture() with { Status = "" }));
    }

    [Fact]
    public void ProcessorTimberRequirementIsAnEditableDecisionAndNotADuplicateNativeRejection()
    {
        var scene = Build(); scene.Logistics.Sites.Clear(); var machine = Processor(scene);
        var stack = scene.Logistics.StackAt(RoadStack);
        Assert.NotNull(scene.Logistics.CheckProcessorStack(machine, RoadStack));
        Assert.False(scene.Logistics.AssignProcessor(machine, stack));
        SetTransportModel(scene, new() { Graphs = new() { TransportConstant("orders.allow", "processor", 1) } });
        Assert.Null(scene.Logistics.CheckProcessorStack(machine, RoadStack));
        Assert.True(scene.Logistics.AssignProcessor(machine, stack));
        Assert.False(machine.HasWork); Assert.Same(stack, machine.Target);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuthoredOrderUsesProposedSourceDestinationWearAndHeldCargo(bool truckOrder)
    {
        var scene = Build(); var source = scene.Logistics.StackAt(RoadStack);
        source.Volume = 8;
        string kind = truckOrder ? "truck" : "forwarder";
        var machine = Forwarder(scene); var truck = scene.Logistics.Trucks.Single();
        machine.Cargo = 1; machine.LogTransferVolume = .4f; machine.Upkeep.Wear = .2f;
        truck.Upkeep.Wear = .2f;
        var hook = LogisticsRules.Hooks.Single(h => h.Id == "orders.allow" && h.Kind == LogisticsRules.Kind(kind));
        var graph = RuleFormula.Compile("ha(source.stock > 7; ha(order.destinationIsMill > 0.5; ha(machine.wear < 0.5; ha(machine.cargo < 1.2; 1; 0); 0); 0); 0)",
            new() { Id = "order", Hook = hook.Id, Kind = hook.Kind }, hook.Inputs.Select(k => new FormulaName(k, BehaviorOperation.Input, k)).ToArray());
        SetTransportModel(scene, new() { Graphs = new() { graph } });
        // The forwarder's held log is already committed cargo, so 1 + .4 fails the 1.2 limit.
        Assert.Equal(truckOrder, scene.Logistics.CheckDestination(RoadStack, Mill, truckOrder, machine, truck) == null);
        if (!truckOrder) { machine.LogTransferVolume = 0; Assert.Null(scene.Logistics.CheckDestination(RoadStack, Mill, false, machine)); }
        source.Volume = 6;
        Assert.NotNull(scene.Logistics.CheckDestination(RoadStack, Mill, truckOrder, machine, truck));
        source.Volume = 8;
        Assert.NotNull(scene.Logistics.CheckDestination(RoadStack, ForestStack, truckOrder, machine, truck));
        Assert.True(truckOrder ? scene.Logistics.AssignTruck(truck, source, Mill) : scene.Logistics.AssignForwarder(machine, source, Mill));
    }

    [Fact]
    public void AllowGraphCannotCreateARouteOrMakeSourceItsOwnDestination()
    {
        var scene = Build(); var truck = scene.Logistics.Trucks.Single(); var source = scene.Logistics.StackAt(RoadStack);
        SetTransportModel(scene, new() { Graphs = new() { TransportConstant("orders.allow", "truck", 1),
            TransportConstant("route.allow", "truck", 0) } });
        Assert.False(scene.Logistics.AssignTruck(truck, source, Mill));
        Assert.False(scene.Logistics.AssignTruck(truck, source, RoadStack));
        Assert.Equal(TruckPhase.Parked, truck.Phase); Assert.Null(truck.Source); Assert.Empty(scene.Vehicles.Vehicles);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AuthoredOrdersHaveExactStateWithGuardedSlicedExecution(int kind)
    {
        var whole = Build(); var sliced = Build();
        foreach (var scene in new[] { whole, sliced }) SetTransportModel(scene, new() { Graphs = new() {
            TransportConstant("orders.allow", kind == 0 ? "processor" : kind == 1 ? "forwarder" : "truck", 1) } });
        var result = new WorkResult<bool>();
        IEnumerable<int> Work(Scene scene, WorkResult<bool> output) => kind switch {
            0 => scene.Logistics.AssignProcessorWork(Processor(scene), scene.Logistics.StackAt(ForestStack), output),
            1 => scene.Logistics.AssignForwarderWork(Forwarder(scene), scene.Logistics.StackAt(ForestStack), RoadStack, output),
            _ => scene.Logistics.AssignTruckWork(scene.Logistics.Trucks.Single(), scene.Logistics.StackAt(RoadStack), Mill, output)
        };
        SimulationWork.Complete(Work(whole, result)); Assert.True(result.Value);
        result.Value = false; int units = 0;
        using (var work = Work(sliced, result).GetEnumerator()) while (SimulationWork.MoveNext(work)) units++;
        Assert.True(result.Value); Assert.True(units > 1);
        Assert.Equal(JsonSerializer.Serialize(whole.Logistics.Capture()), JsonSerializer.Serialize(sliced.Logistics.Capture()));
        Assert.Equal(JsonSerializer.Serialize(whole.Vehicles.Capture()), JsonSerializer.Serialize(sliced.Vehicles.Capture()));
    }
}
