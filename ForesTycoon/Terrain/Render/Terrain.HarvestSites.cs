using System.Drawing;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    partial class Terrain
    {
        /// <summary>Tints the tiles of forestry harvest sites that still hold timber.</summary>
        internal void DrawHarvestSites(ForestryLogistics logistics)
        {
            if (logistics == null) return;
            using var state = new RenderStateScope().AlphaBlend().DepthWrite(false);
            DynamicPrimitiveBatch.Draw(PrimitiveType.Quads, () =>
            {
                DynamicPrimitiveBatch.Color4(Color.FromArgb(35, 255, 177, 50));
                foreach (var site in logistics.Sites) { if (logistics.Volume(site) <= 0.001f) continue; foreach (int id in site.Tiles) TileQuad(tiles[id]); }
            });
            DynamicPrimitiveBatch.Draw(PrimitiveType.Lines, () =>
            {
                DynamicPrimitiveBatch.Color4(Color.FromArgb(235, 255, 180, 55));
                foreach (var site in logistics.Sites)
                {
                    if (logistics.Volume(site) <= 0.001f) continue;
                    foreach (int id in site.Tiles) { Tile t = tiles[id]; DrawTileEdgesByMaterial(t, GetTileSurface(t), Color.FromArgb(255, 180, 55)); }
                }
            });
        }
    }
}
