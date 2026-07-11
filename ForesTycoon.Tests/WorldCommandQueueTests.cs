namespace ForesTycoon.Tests;

public class WorldCommandQueueTests
{
    [Fact]
    public void ExecutePending_PreservesInputOrderAndArguments()
    {
        WorldCommandQueue queue = new WorldCommandQueue();
        RecordingTarget target = new RecordingTarget();
        queue.Enqueue(new EditElevationCommand(7, 1, 2, 3));
        queue.Enqueue(new RoadPathCommand(11, 15, false));
        queue.Enqueue(new RoadPathCommand(15, 11, true));

        int executed = queue.ExecutePending(target);

        Assert.Equal(3, executed);
        Assert.Equal(new[] { "edit:7:1:2:3", "road:11:15:False", "road:15:11:True" }, target.Events);
        Assert.Equal(0, queue.Count);
    }

    private sealed class RecordingTarget : IWorldCommandTarget
    {
        public List<string> Events { get; } = new List<string>();

        public void ExecuteElevationEdit(int nodeId, int delta, int radius, int strength) =>
            Events.Add($"edit:{nodeId}:{delta}:{radius}:{strength}");

        public void ExecuteRoadPath(int startTileId, int endTileId, bool remove) =>
            Events.Add($"road:{startTileId}:{endTileId}:{remove}");

        public void ExecuteSpawnVehicle() => Events.Add("spawn-vehicle");
    }
}
