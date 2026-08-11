using System;

namespace ForesTycoon
{
    /// <summary>
    /// Testable translation from user intent to world commands. Window-system input,
    /// camera gestures and ImGui capture stay in the platform-facing viewport.
    /// </summary>
    sealed class WorldInteractionController
    {
        private readonly IWorldInteractionTarget world;
        private int roadDragStartTileId = -1;
        private int brushSize = 1;
        private int brushStrength = 1;

        public WorldInteractionController(IWorldInteractionTarget world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public TerrainEditTool ActiveTool { get; private set; } = TerrainEditTool.Inspect;
        public int BrushSize
        {
            get => brushSize;
            set => brushSize = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        public int BrushStrength
        {
            get => brushStrength;
            set => brushStrength = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }
        public bool IsRoadDragging => roadDragStartTileId >= 0;
        public bool IsRoadRemoval { get; private set; }

        public static bool IsRoadTool(TerrainEditTool tool) =>
            tool == TerrainEditTool.Road || tool == TerrainEditTool.RoadRemove;

        public void SelectTool(TerrainEditTool tool)
        {
            CancelGesture();
            ActiveTool = tool;
        }

        public void BeginPrimaryGesture()
        {
            if (!IsRoadTool(ActiveTool) || world.HoveredTileId < 0) return;
            roadDragStartTileId = world.HoveredTileId;
            IsRoadRemoval = ActiveTool == TerrainEditTool.RoadRemove;
        }

        public void UpdateGesture()
        {
            if (!IsRoadDragging) return;
            world.SetRoadPreview(roadDragStartTileId, world.HoveredTileId, IsRoadRemoval);
        }

        /// <returns>True when the active world-edit tool consumed the release.</returns>
        public bool EndPrimaryGesture(bool hasHoveredNode)
        {
            if (IsRoadTool(ActiveTool))
            {
                int endTileId = world.HoveredTileId;
                if (IsRoadDragging && endTileId >= 0)
                    world.QueueRoadPath(roadDragStartTileId, endTileId, IsRoadRemoval);
                CancelGesture();
                return true;
            }

            if (ActiveTool == TerrainEditTool.Raise || ActiveTool == TerrainEditTool.Lower)
            {
                if (hasHoveredNode && world.SelectedNodeId >= 0)
                {
                    int delta = ActiveTool == TerrainEditTool.Raise ? 1 : -1;
                    world.QueueElevationEdit(world.SelectedNodeId, delta,
                        BrushSize - 1, BrushStrength);
                }
                return true;
            }

            return false;
        }

        public void CancelGesture()
        {
            world.ClearRoadPreview();
            roadDragStartTileId = -1;
            IsRoadRemoval = false;
        }
    }
}
