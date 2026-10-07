namespace ForesTycoon
{
    partial class Terrain
    {
        internal void DrawNodeMarker(float radius)
        {
            if (map.HasHoveredNode) DebugOverlayRenderer.DrawNodeMarker(map.SelectedNode, radius);
        }

        internal void DrawHoveredTile()
        {
            DebugOverlayRenderer.DrawHoveredTile(map.HoveredTile);
        }
    }
}
