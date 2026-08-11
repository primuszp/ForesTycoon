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
        queue.Enqueue(new PlantForestCommand(21, ForestSpecies.Birch));
        queue.Enqueue(new HarvestForestCommand(22));

        int executed = queue.ExecutePending(target);

        Assert.Equal(5, executed);
        Assert.Equal(new[]
        {
            "edit:7:1:2:3", "road:11:15:False", "road:15:11:True", "plant:21:Birch", "harvest:22"
        }, target.Events);
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
        public void ExecutePlantForest(int tileId, ForestSpecies species) =>
            Events.Add($"plant:{tileId}:{species}");
        public void ExecuteHarvestForest(int tileId) => Events.Add($"harvest:{tileId}");
    }
}
