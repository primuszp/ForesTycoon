namespace ForesTycoon.Tests;

public class WorldInteractionControllerTests
{
    [Fact]
    public void TerrainTool_QueuesValidatedBrushCommand()
    {
        RecordingWorld world = new RecordingWorld { SelectedNodeId = 42 };
        WorldInteractionController controller = new WorldInteractionController(world)
        {
            BrushSize = 3,
            BrushStrength = 2
        };
        controller.SelectTool(TerrainEditTool.Lower);

        bool consumed = controller.EndPrimaryGesture(hasHoveredNode: true);

        Assert.True(consumed);
        Assert.Equal("elevation:42:-1:2:2", world.Events[^1]);
    }

    [Fact]
    public void RoadGesture_PreviewsAndCommitsByStableTileIds()
    {
        RecordingWorld world = new RecordingWorld { HoveredTileId = 10 };
        WorldInteractionController controller = new WorldInteractionController(world);
        controller.SelectTool(TerrainEditTool.Road);
        controller.BeginPrimaryGesture();
        world.HoveredTileId = 15;

        controller.UpdateGesture();
        bool consumed = controller.EndPrimaryGesture(hasHoveredNode: false);

        Assert.True(consumed);
        Assert.Equal(new[] { "clear", "preview:10:15:False", "road:10:15:False", "clear" }, world.Events);
        Assert.False(controller.IsRoadDragging);
    }

    [Fact]
    public void ToolChange_CancelsActiveRoadGesture()
    {
        RecordingWorld world = new RecordingWorld { HoveredTileId = 7 };
        WorldInteractionController controller = new WorldInteractionController(world);
        controller.SelectTool(TerrainEditTool.RoadRemove);
        controller.BeginPrimaryGesture();

        controller.SelectTool(TerrainEditTool.Inspect);

        Assert.False(controller.IsRoadDragging);
        Assert.Equal("clear", world.Events[^1]);
    }

    [Theory]
    [InlineData((int)TerrainEditTool.PlantForest, "plant:23:Spruce")]
    [InlineData((int)TerrainEditTool.HarvestForest, "harvest:23")]
    public void ForestryTool_QueuesTileCommandOnClick(int toolValue, string expected)
    {
        RecordingWorld world = new RecordingWorld { HoveredTileId = 23 };
        WorldInteractionController controller = new WorldInteractionController(world)
        {
            PlantingSpecies = ForestSpecies.Spruce
        };
        controller.SelectTool((TerrainEditTool)toolValue);

        bool consumed = controller.EndPrimaryGesture(hasHoveredNode: false);

        Assert.True(consumed);
        Assert.Equal(expected, world.Events[^1]);
    }

    private sealed class RecordingWorld : IWorldInteractionTarget
    {
        public int HoveredTileId { get; set; } = -1;
        public int SelectedNodeId { get; set; } = -1;
        public List<string> Events { get; } = new();

        public void QueueElevationEdit(int nodeId, int delta, int radius, int strength) =>
            Events.Add($"elevation:{nodeId}:{delta}:{radius}:{strength}");
        public void QueueRoadPath(int startTileId, int endTileId, bool remove) =>
            Events.Add($"road:{startTileId}:{endTileId}:{remove}");
        public void QueuePlantForest(int tileId, ForestSpecies species) =>
            Events.Add($"plant:{tileId}:{species}");
        public void QueueHarvestForest(int tileId) => Events.Add($"harvest:{tileId}");
        public void SetRoadPreview(int startTileId, int endTileId, bool remove) =>
            Events.Add($"preview:{startTileId}:{endTileId}:{remove}");
        public void ClearRoadPreview() => Events.Add("clear");
    }
}
