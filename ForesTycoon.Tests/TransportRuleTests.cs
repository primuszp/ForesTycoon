using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

public class TransportRuleTests
{
    private static BehaviorGraph Value(string hook, string kind, double value, ulong? id = null) => new() {
        Id = Guid.NewGuid().ToString("N"), Hook = hook, Kind = kind, ObjectId = id, Output = "value",
        Nodes = new() { new() { Id = "value", Name = "Érték", Operation = BehaviorOperation.Constant, Value = value } }
    };
    private static (TerrainMap Map, ForestryLogistics Logistics) Routes()
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
        map.BuildRoadTilePath(34, 66); map.BuildRoadTilePath(66, 74); map.BuildRoadTilePath(74, 42); map.MarkSkidTrailPath(34, 42);
        return (map, new ForestryLogistics(map, new ForestSystem(map, new ForestStand[256])));
    }
    [Fact]
    public void TruckCostGraphChangesBothDockAndRouteSelectionForOnlyItsFleetId()
    {
        var (map, logistics) = Routes(); var truck = new FleetTruck { Id = 7 };
        var baseline = logistics.PreviewTruckRoute(35, 41, truck); Assert.Contains(66, baseline);
        logistics.Behaviors = new WorldBehaviorPolicy(new() { Graphs = new() { Value("route.cost", "truck", 1, 7) } });
        var edited = logistics.PreviewTruckRoute(35, 41, truck);
        Assert.DoesNotContain(66, edited); Assert.True(edited.Length < baseline.Length);
        Assert.Equal(baseline, logistics.PreviewTruckRoute(35, 41, new() { Id = 8 }));
        for (int i = 1; i < edited.Length; i++) Assert.True(map.AreNetworkNeighbours(edited[i - 1], edited[i]));
    }
    [Theory]
    [InlineData(ForestMachineKind.Forwarder)]
    [InlineData(ForestMachineKind.Harvester)]
    internal void MachinePathUsesSurfaceConditionExpressionsAndKeepsNetworkConstraints(ForestMachineKind kind)
    {
        var (map, logistics) = Routes(); var machine = new ForestMachine { Id = 7, Kind = kind };
        var graph = Value("route.cost", kind == ForestMachineKind.Forwarder ? "forwarder" : "processor", 50);
        graph.Nodes.AddRange(new BehaviorNode[] {
            new() { Id = "surface", Name = "Burkolat", Operation = BehaviorOperation.Surface },
            new() { Id = "two", Name = "Makadám", Operation = BehaviorOperation.Constant, Value = 2 },
            new() { Id = "trail", Name = "Nyom", Operation = BehaviorOperation.Greater, A = "surface", B = "two" },
            new() { Id = "cheap", Name = "Közút költsége", Operation = BehaviorOperation.Constant, Value = .1 },
            new() { Id = "result", Name = "Költség", Operation = BehaviorOperation.Select, A = "trail", B = "value", C = "cheap" }
        }); graph.Output = "result";
        Assert.DoesNotContain(66, logistics.PreviewMachinePath(34, 42, machine));
        logistics.Behaviors = new WorldBehaviorPolicy(new() { Graphs = new() { graph } });
        var route = logistics.PreviewMachinePath(34, 42, machine); Assert.Contains(66, route);
        for (int i = 1; i < route.Length; i++) Assert.True(map.AreNetworkNeighbours(route[i - 1], route[i]));
    }
    [Fact]
    public void AllowGraphCanForbidButCannotCreateConnectionsAndArithmeticErrorsFallBack()
    {
        var (map, logistics) = Routes();
        logistics.Behaviors = new WorldBehaviorPolicy(new() { Graphs = new() { Value("route.allow", "truck", 0, 7) } });
        Assert.Null(logistics.PreviewTruckRoute(35, 41, new() { Id = 7 }));
        Assert.NotNull(logistics.PreviewTruckRoute(35, 41, new() { Id = 8 }));
        var invalid = Value("route.cost", "truck", 0);
        invalid.Nodes.Add(new() { Id = "bad", Name = "Hibás osztás", Operation = BehaviorOperation.Divide, A = "value", B = "value" }); invalid.Output = "bad";
        var policy = new WorldBehaviorPolicy(new() { Graphs = new() { invalid } }); logistics.Behaviors = policy;
        Assert.Contains(66, logistics.PreviewTruckRoute(35, 41, new() { Id = 7 })); Assert.NotNull(policy.LastError);
        Assert.Null(logistics.PreviewTruckRoute(0, 255, new() { Id = 7 }));
        Assert.Throws<InvalidOperationException>(() => map.FindNetworkPath(34, 42, out _, (_, _, _) => -1));
    }
    [Fact]
    public void NewInputsAreEvaluatedButCannotBeUsedAsWorldTuningInputs()
    {
        var graph = Value("loading.depart", "truck", 0);
        graph.Nodes = new() {
            new() { Id = "cargo", Name = "Rakomány", Operation = BehaviorOperation.Amount },
            new() { Id = "capacity", Name = "Kapacitás", Operation = BehaviorOperation.Capacity },
            new() { Id = "ratio", Name = "Arány", Operation = BehaviorOperation.Divide, A = "cargo", B = "capacity" }
        }; graph.Output = "ratio";
        var policy = new WorldBehaviorPolicy(new() { Graphs = new() { graph } });
        Assert.Equal(.5, policy.Evaluate(new("loading.depart", "truck", 1, 0, Amount: 10, Capacity: 20)));
        graph.Hook = "tuning.DieselPrice"; graph.Kind = "world";
        Assert.Throws<InvalidDataException>(() => new WorldBehaviorPolicy(new() { Graphs = new() { graph } }));
    }
    [Fact]
    public void TruckBlockedEventAndTransportStateSurviveSnapshot()
    {
        var controller = BehaviorController.TimedPause("truck");
        controller.States[0].Transitions[0].Conditions = new() { new() { Trigger = BehaviorTrigger.EnteredMachineState, Value = 8 } };
        var model = new BehaviorModel { Controllers = new() { controller } };
        var runtime = new CompiledBehaviorControllers(model); runtime.Step("truck", 7, new(1, 2, 3, true, false));
        var copy = new CompiledBehaviorControllers(model); copy.Restore(runtime.Capture());
        Assert.Equal(BehaviorAction.Autonomous, copy.Step("truck", 7, new(1, 2, 8, true, false)));
        Assert.Throws<InvalidDataException>(() => copy.Restore(new[] { copy.Capture()[0] with { PreviousMachineState = 9 } }));
    }
}
