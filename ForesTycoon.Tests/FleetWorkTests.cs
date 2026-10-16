using System.Text.Json;

namespace ForesTycoon.Tests;

public partial class FleetTests
{
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
