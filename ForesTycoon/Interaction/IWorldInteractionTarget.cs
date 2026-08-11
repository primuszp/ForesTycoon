namespace ForesTycoon
{
    interface IWorldInteractionTarget
    {
        int HoveredTileId { get; }
        int SelectedNodeId { get; }
        void QueueElevationEdit(int nodeId, int delta, int radius, int strength);
        void QueueRoadPath(int startTileId, int endTileId, bool remove);
        void SetRoadPreview(int startTileId, int endTileId, bool remove);
        void ClearRoadPreview();
    }
}
