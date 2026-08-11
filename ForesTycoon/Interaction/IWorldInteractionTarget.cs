namespace ForesTycoon
{
    interface IWorldInteractionTarget
    {
        int HoveredTileId { get; }
        int SelectedNodeId { get; }
        void QueueElevationEdit(int nodeId, int delta, int radius, int strength);
        void QueueRoadPath(int startTileId, int endTileId, bool remove);
        void QueuePlantForest(int tileId, ForestSpecies species);
        void QueueHarvestForest(int tileId);
        void SetRoadPreview(int startTileId, int endTileId, bool remove);
        void ClearRoadPreview();
    }
}
