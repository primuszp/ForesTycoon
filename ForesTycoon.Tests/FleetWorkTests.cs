using System.Text.Json;

namespace ForesTycoon.Tests;

public partial class FleetTests
{
    [Fact]
    public void PublishedTuningIsReadByExistingAndRestoredMachinesWithoutResettingLogCycle()
    {
        var scene = Build();
        var machine = Forwarder(scene);
        Assert.Same(scene.Logistics, machine.TuningSource);
        machine.State = ForestMachineState.Loading;
        machine.Source = scene.Logistics.StackAt(ForestStack);
        machine.Source.Volume = 10;
        machine.Source.Value = 1000;
        ForwarderLoading.Step(machine, 2.57, (_, _) => throw new Exception("Not unloading"));
        Assert.True(machine.LogTransferVolume > 0);
        double duration = machine.LogCycleDuration;
        var checkpoint = scene.Logistics.Capture();

        var changed = GameTuning.FromOverrides(new Dictionary<string, double> {
            [nameof(Tune.ForwarderCapacity)] = 20,
            [nameof(Tune.ForwarderLoadRate)] = 1
        });
        scene.Logistics.Tuning = changed;
        Assert.Same(changed, machine.Tuning);
        Assert.Equal(20, machine.Capacity);
        Assert.Equal(duration, machine.LogCycleDuration);

        var restored = Build();
        restored.Logistics.Tuning = changed;
        restored.Logistics.Restore(checkpoint);
        var resumed = Forwarder(restored);
        Assert.Same(restored.Logistics, resumed.TuningSource);
        Assert.Same(changed, resumed.Tuning);
        Assert.Equal(duration, resumed.LogCycleDuration);
        for (int i = 0; i < 400; i++) {
            ForwarderLoading.Step(machine, 1d / 30, (_, _) => throw new Exception("Not unloading"));
            ForwarderLoading.Step(resumed, 1d / 30, (_, _) => throw new Exception("Not unloading"));
        }
        Assert.Equal(JsonSerializer.Serialize(scene.Logistics.Capture()), JsonSerializer.Serialize(restored.Logistics.Capture()));

        scene.Logistics.Tuning = null;
        Assert.Same(GameTuning.Default, machine.Tuning);
        var standalone = new ForestMachine { Tuning = changed, Kind = ForestMachineKind.Forwarder };
        Assert.Equal(20, standalone.Capacity);
    }

    [Fact]
    public void AuthoredWearIsSharedByWorkingMachinesAndTruckAndKeepsSlicedOrder()
    {
        var expected = Build(); var actual = Build();
        foreach (var scene in new[] { expected, actual }) {
            var model = WorldBehaviorPolicy.DefaultDocument();
            foreach (var (id, value) in new[] { ("upkeep.wear", .003), ("upkeep.breakdown", 0d), ("upkeep.serviceRelief", 0d) }) {
                var graph = model.Graphs.Single(g => g.Kind == "vehicleResponse" && g.Hook == id);
                graph.Disabled = true; graph.DisabledValue = value;
            }
            var tuning = GameTuning.Default.WithBehaviors(null, new VehicleRuleProgram(model));
            scene.Logistics.Tuning = tuning; scene.Vehicles.Spec = tuning.Truck;
            var roadside = scene.Logistics.StackAt(RoadStack); roadside.Volume = 100; roadside.Value = 10000;
            Assert.True(scene.Logistics.AssignProcessor(Processor(scene), scene.Logistics.StackAt(ForestStack)));
            Assert.True(scene.Logistics.AssignTruck(scene.Logistics.Trucks[0], roadside, Mill));
        }
        for (int tick = 0; tick < 300; tick++) {
            expected.Logistics.Update(1d / 30); expected.Vehicles.Update(1d / 30);
            using (var work = actual.Logistics.UpdateWork(1d / 30).GetEnumerator()) while (work.MoveNext()) { }
            using (var work = actual.Vehicles.UpdateWork(1d / 30).GetEnumerator()) while (work.MoveNext()) { }
        }
        Assert.True(Processor(actual).Upkeep.Wear > .1f);
        Assert.True(actual.Logistics.Trucks[0].Upkeep.Wear > .1f);
        Assert.Equal(JsonSerializer.Serialize(expected.Logistics.Capture()), JsonSerializer.Serialize(actual.Logistics.Capture()));
        Assert.Equal(JsonSerializer.Serialize(expected.Vehicles.Capture()), JsonSerializer.Serialize(actual.Vehicles.Capture()));
    }
    [Fact]
    public void FleetWorkCanSuspendThroughHarvestRoutingAndTransfersWithoutChangingState()
    {
        var expected = Build(); var actual = Build();
        foreach (var scene in new[] { expected, actual }) {
            scene.Vehicles.RoadRouteWorkFactory = (route, result) => VehicleRoadRoute.CreateWork(scene.Map, route, result);
            var roadside = scene.Logistics.StackAt(RoadStack);
            roadside.Volume = 100; roadside.Value = 10000;
            Assert.True(scene.Logistics.AssignProcessor(Processor(scene), scene.Logistics.StackAt(ForestStack)));
            Assert.True(scene.Logistics.AssignForwarder(Forwarder(scene), scene.Logistics.StackAt(ForestStack), RoadStack));
            Assert.True(scene.Logistics.AssignTruck(scene.Logistics.Trucks[0], roadside, Mill));
        }
        int units = 0;
        for (int tick = 0; tick < 1200; tick++) {
            expected.Logistics.Update(1d / 30); expected.Vehicles.Update(1d / 30);
            using (var work = actual.Logistics.UpdateWork(1d / 30).GetEnumerator())
                while (work.MoveNext()) units++;
            using (var work = actual.Vehicles.UpdateWork(1d / 30).GetEnumerator())
                while (work.MoveNext()) units++;
        }
        Assert.True(units > 1200);
        Assert.Contains(actual.Forest.Capture().Trees.Patches, p => p.Stumps.Length > 0);
        Assert.Equal(JsonSerializer.Serialize(expected.Logistics.Capture()), JsonSerializer.Serialize(actual.Logistics.Capture()));
        Assert.Equal(JsonSerializer.Serialize(expected.Vehicles.Capture()), JsonSerializer.Serialize(actual.Vehicles.Capture()));
        Assert.Equal(JsonSerializer.Serialize(expected.Forest.Capture()), JsonSerializer.Serialize(actual.Forest.Capture()));
        Assert.Equal(JsonSerializer.Serialize(expected.Map.Capture()), JsonSerializer.Serialize(actual.Map.Capture()));
    }
}
