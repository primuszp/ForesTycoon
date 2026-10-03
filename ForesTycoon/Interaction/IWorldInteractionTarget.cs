namespace ForesTycoon
{
    interface IWorldInteractionTarget
    {
        void QueuePlaceSawmill(int tileId) => throw new System.NotSupportedException();
        int HoveredTileId { get; }
        int SelectedNodeId { get; }
        void QueueElevationEdit(int nodeId, int delta, int radius, int strength);
        void QueueRoadPath(int startTileId, int endTileId, bool remove);
        void QueuePlantForest(int tileId, ForestSpecies species);
        void QueueHarvestForest(int tileId);
        void QueuePlantForestArea(int startTileId, int endTileId, ForestSpecies species);
        void QueueHarvestForestArea(int startTileId, int endTileId);
        void SetForestryPreview(int startTileId, int endTileId, bool removal);
        void ClearForestryPreview();
        void SetRoadPreview(int startTileId, int endTileId, bool remove);
        void ClearRoadPreview();
    }
}
