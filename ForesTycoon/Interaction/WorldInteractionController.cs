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
        private int forestryDragStartTileId = -1;
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
        public int RoadDragStartTileId => roadDragStartTileId;
        public bool IsForestryDragging => forestryDragStartTileId >= 0;
        public bool IsRoadRemoval { get; private set; }
        public ForestSpecies PlantingSpecies { get; set; } = ForestSpecies.Spruce;
        /// <summary>The tile clicked while the send tool is active (the viewport turns it into a work order).</summary>
        public Action<int> TargetPicked { get; set; }
        /// <summary>Surface the road tool lays.</summary>
        public RoadPaving RoadSurface { get; set; } = RoadPaving.Macadam;

        public static bool IsRoadTool(TerrainEditTool tool) =>
            tool == TerrainEditTool.Road || tool == TerrainEditTool.RoadRemove || tool == TerrainEditTool.RoadRepair || IsSkidTrailTool(tool);

        /// <summary>Skid trails are dragged like roads: from the road into the felling.</summary>
        public static bool IsSkidTrailTool(TerrainEditTool tool) =>
            tool == TerrainEditTool.SkidTrail || tool == TerrainEditTool.SkidTrailRemove;

        public static bool IsForestryTool(TerrainEditTool tool) =>
            tool == TerrainEditTool.PlantForest || tool == TerrainEditTool.HarvestForest;

        public void SelectTool(TerrainEditTool tool)
        {
            CancelGesture();
            ActiveTool = tool;
        }

        public void BeginPrimaryGesture()
        {
            if (world.HoveredTileId < 0) return;

            if (IsForestryTool(ActiveTool))
            {
                forestryDragStartTileId = world.HoveredTileId;
                world.SetForestryPreview(forestryDragStartTileId, forestryDragStartTileId,
                    ActiveTool == TerrainEditTool.HarvestForest);
                return;
            }

            if (!IsRoadTool(ActiveTool)) return;
            roadDragStartTileId = world.HoveredTileId;
            IsRoadRemoval = ActiveTool == TerrainEditTool.RoadRemove;
        }

        public void UpdateGesture()
        {
            if (IsForestryDragging && world.HoveredTileId >= 0)
            {
                world.SetForestryPreview(forestryDragStartTileId, world.HoveredTileId,
                    ActiveTool == TerrainEditTool.HarvestForest);
                return;
            }

            if (!IsRoadDragging) return;
            if (ActiveTool == TerrainEditTool.RoadRepair) world.SetRoadRepairPreview(roadDragStartTileId, world.HoveredTileId);
            else if (IsSkidTrailTool(ActiveTool))
                world.SetSkidTrailPreview(roadDragStartTileId, world.HoveredTileId, ActiveTool == TerrainEditTool.SkidTrailRemove);
            else world.SetRoadPreview(roadDragStartTileId, world.HoveredTileId, IsRoadRemoval);
        }

        /// <returns>True when the active world-edit tool consumed the release.</returns>
        public bool EndPrimaryGesture(bool hasHoveredNode)
        {
            if(ActiveTool==TerrainEditTool.PlaceSawmill){if(world.HoveredTileId>=0)world.QueuePlaceSawmill(world.HoveredTileId);return true;}
            if(ActiveTool==TerrainEditTool.PlaceDepot){if(world.HoveredTileId>=0)world.QueuePlaceDepot(world.HoveredTileId);return true;}
            if(ActiveTool==TerrainEditTool.SendVehicle){if(world.HoveredTileId>=0)TargetPicked?.Invoke(world.HoveredTileId);return true;}
            if(ActiveTool is TerrainEditTool.PlaceStack or TerrainEditTool.RemoveStack){if(world.HoveredTileId>=0)world.QueueStackSite(world.HoveredTileId,ActiveTool==TerrainEditTool.RemoveStack);return true;}
            if (IsRoadTool(ActiveTool))
            {
                int endTileId = world.HoveredTileId;
                if (IsRoadDragging && endTileId >= 0)
                {
                    if (ActiveTool == TerrainEditTool.RoadRepair) world.QueueRoadRepair(roadDragStartTileId, endTileId);
                    else if (IsSkidTrailTool(ActiveTool))
                        world.QueueSkidTrailPath(roadDragStartTileId, endTileId, ActiveTool == TerrainEditTool.SkidTrailRemove);
                    else if (IsRoadRemoval) world.QueueRoadPath(roadDragStartTileId, endTileId, true);
                    else world.QueueRoadPath(roadDragStartTileId, endTileId, false, RoadSurface);
                }
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

            if (IsForestryTool(ActiveTool))
            {
                // The drag start is the anchor; a click that never moved is simply a
                // one-tile rectangle, so both gestures go through the same path.
                int startTileId = IsForestryDragging ? forestryDragStartTileId : world.HoveredTileId;
                int endTileId = world.HoveredTileId;

                if (startTileId >= 0 && endTileId >= 0)
                {
                    if (ActiveTool == TerrainEditTool.PlantForest)
                    {
                        if (startTileId == endTileId) world.QueuePlantForest(endTileId, PlantingSpecies);
                        else world.QueuePlantForestArea(startTileId, endTileId, PlantingSpecies);
                    }
                    else
                    {
                        if (startTileId == endTileId) world.QueueHarvestForest(endTileId);
                        else world.QueueHarvestForestArea(startTileId, endTileId);
                    }
                }

                CancelGesture();
                return true;
            }

            return false;
        }

        public void CancelGesture()
        {
            world.ClearRoadPreview();
            world.ClearForestryPreview();
            roadDragStartTileId = -1;
            forestryDragStartTileId = -1;
            IsRoadRemoval = false;
        }
    }
}
