using System.Text.Json;

namespace ForesTycoon.Tests;

public partial class FleetTests
{
    [Fact]
    public void OrderChecksAndRoutesResumeUnderTheGuardWithoutChangingTheWorld()
    {
        var scene = Build();
        var truck = scene.Logistics.Trucks[0]; var machine = Forwarder(scene); var processor = Processor(scene);
        string before = JsonSerializer.Serialize(scene.Logistics.Capture());
        void Check(string expected, Func<WorkResult<string>, IEnumerable<int>> query) {
            var result = new WorkResult<string>(); int slices = 0;
            using var work = query(result).GetEnumerator();
            while (SimulationWork.MoveNext(work, 1)) { Assert.Equal(1, work.Current); Assert.True(++slices < 100000); }
            Assert.Equal(expected, result.Value);
        }
        Check(scene.Logistics.CheckSource(ForestStack, false, machine.Tile, machine),
            r => scene.Logistics.CheckSourceWork(ForestStack, false, machine.Tile, machine, r));
        Check(scene.Logistics.CheckDestination(RoadStack, Mill, true, unit: truck),
            r => scene.Logistics.CheckDestinationWork(RoadStack, Mill, true, null, truck, r));
        Check(scene.Logistics.CheckProcessorStack(processor, ForestStack),
            r => scene.Logistics.CheckProcessorStackWork(processor, ForestStack, r));
        using var cache = new SlicedPreviewCache<int, int[]>(refreshFrames: 10000);
        int[] expected = scene.Logistics.PreviewTruckRoute(RoadStack, Mill, truck);
        bool ready = false; int[] route = Array.Empty<int>();
        for (int frame = 0; frame < 10000 && !ready; frame++) {
            cache.BeginFrame(scene.Map.NavigationVersion);
            ready = cache.Request(1, r => scene.Logistics.PreviewTruckRouteWork(RoadStack, Mill, truck, r), out route);
            cache.Advance(3); Assert.InRange(cache.LastWorkUnits, 0, 3);
        }
        Assert.True(ready); Assert.Equal(expected, route);
        Assert.Equal(before, JsonSerializer.Serialize(scene.Logistics.Capture()));
    }

    [Fact]
    public void CargoCallbacksFollowThePhysicalVehiclesCurrentFleetOwner()
    {
        var scene = Build(); var truck = scene.Logistics.Trucks[0];
        var source = scene.Logistics.StackAt(RoadStack); source.Add(10, 100);
        Assert.True(scene.Logistics.AssignTruck(truck, source, Mill));
        var previous = truck.Vehicle;
        // The replacement is physically at the source dock, ready to load.
        var replacement = scene.Vehicles.SpawnTransit(scene.Map.FindNetworkPath(146, 194));
        truck.Vehicle = replacement;
        Assert.Equal(0, scene.Logistics.Load(previous, 2));
        Assert.Equal(2, scene.Logistics.Load(replacement, 2));
        Assert.Equal(8, source.Volume); Assert.Equal(20, truck.CargoValue);
        scene.Vehicles.Remove(previous); Assert.False(scene.Vehicles.Contains(previous)); Assert.True(scene.Vehicles.Contains(replacement));
        scene.Vehicles.Clear(); Assert.False(scene.Vehicles.Contains(replacement));
    }
}
