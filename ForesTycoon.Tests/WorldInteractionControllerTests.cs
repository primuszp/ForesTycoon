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
        // Cancelling a gesture clears both previews; only the road half is asserted here.
        Assert.Equal(new[] { "road:10:15:False" },
            world.Events.FindAll(e => e.StartsWith("road:")));
        Assert.Contains("preview:10:15:False", world.Events);
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
        Assert.Contains("clear", world.Events);
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
        Assert.Contains(expected, world.Events);
    }

    [Fact]
    public void ForestryDrag_PreviewsTheParcelAndPlantsItAsOneAreaCommand()
    {
        RecordingWorld world = new RecordingWorld { HoveredTileId = 10 };
        WorldInteractionController controller = new WorldInteractionController(world)
        {
            PlantingSpecies = ForestSpecies.Beech
        };
        controller.SelectTool(TerrainEditTool.PlantForest);
        controller.BeginPrimaryGesture();
        world.HoveredTileId = 42;
        controller.UpdateGesture();

        bool consumed = controller.EndPrimaryGesture(hasHoveredNode: false);

        Assert.True(consumed);
        Assert.Contains("forestry-preview:10:42:False", world.Events);
        Assert.Contains("plant-area:10:42:Beech", world.Events);
        Assert.DoesNotContain(world.Events, e => e.StartsWith("plant:"));
        Assert.False(controller.IsForestryDragging);
    }

    [Fact]
    public void ForestryDrag_FallsBackToASingleTileWhenTheGestureNeverMoved()
    {
        RecordingWorld world = new RecordingWorld { HoveredTileId = 8 };
        WorldInteractionController controller = new WorldInteractionController(world);
        controller.SelectTool(TerrainEditTool.HarvestForest);
        controller.BeginPrimaryGesture();

        controller.EndPrimaryGesture(hasHoveredNode: false);

        Assert.Contains("harvest:8", world.Events);
        Assert.DoesNotContain(world.Events, e => e.StartsWith("harvest-area:"));
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
        public void QueuePlantForestArea(int startTileId, int endTileId, ForestSpecies species) =>
            Events.Add($"plant-area:{startTileId}:{endTileId}:{species}");
        public void QueueHarvestForestArea(int startTileId, int endTileId) =>
            Events.Add($"harvest-area:{startTileId}:{endTileId}");
        public void SetForestryPreview(int startTileId, int endTileId, bool removal) =>
            Events.Add($"forestry-preview:{startTileId}:{endTileId}:{removal}");
        public void ClearForestryPreview() => Events.Add("forestry-clear");
    }
}
