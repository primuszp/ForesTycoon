using System.Drawing;

namespace ForesTycoon
{
    partial class Terrain
    {
        // RGBA per tile id (alpha 0 = untinted); owned by the management view, read every frame.
        private uint[] managementOverlay;

        internal void SetManagementOverlay(uint[] colours) => managementOverlay = colours;

        /// <summary>The active management lens as translucent tile fills, on the visible tiles only.</summary>
        internal void DrawManagementOverlay()
        {
            var colours = managementOverlay;
            if (colours == null) return;
            using var state = RenderDevice.CreateStateScope().AlphaBlend().DepthWrite(false);
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                foreach (Tile tile in visibleTiles)
                {
                    if ((uint)tile.Id >= (uint)colours.Length) continue;
                    uint c = colours[tile.Id];
                    if (c >> 24 == 0) continue;
                    DynamicPrimitiveBatch.Color4(Color.FromArgb((int)(c >> 24), (int)(c & 255), (int)(c >> 8 & 255), (int)(c >> 16 & 255)));
                    TileQuad(tile);
                }
            });
        }
    }
}
