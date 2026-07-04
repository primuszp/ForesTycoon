namespace ForesTycoon
{
    partial class Terrain
    {
        private void DrawNodeMarker()
        {
            if (onpos) DebugOverlayRenderer.DrawNodeMarker(actualNode);
        }

        private void DrawHoveredTile()
        {
            DebugOverlayRenderer.DrawHoveredTile(hoveredTile);
        }
    }
}
