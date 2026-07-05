namespace ForesTycoon
{
    partial class Terrain
    {
        private void DrawNodeMarker(float radius)
        {
            if (onpos) DebugOverlayRenderer.DrawNodeMarker(actualNode, radius);
        }

        private void DrawHoveredTile()
        {
            DebugOverlayRenderer.DrawHoveredTile(hoveredTile);
        }
    }
}
