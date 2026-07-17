namespace ForesTycoon
{
    partial class Terrain
    {
        internal void DrawNodeMarker(float radius)
        {
            if (onpos) DebugOverlayRenderer.DrawNodeMarker(actualNode, radius);
        }

        internal void DrawHoveredTile()
        {
            DebugOverlayRenderer.DrawHoveredTile(hoveredTile);
        }
    }
}
