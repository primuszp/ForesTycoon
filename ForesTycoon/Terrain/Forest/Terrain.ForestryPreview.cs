using System;
using System.Drawing;

namespace ForesTycoon
{
    /// <summary>
    /// Preview for area planting and felling. It deliberately uses the same graphics as the
    /// road placement preview — translucent tile fill plus a bright tile outline, white for
    /// a placement and red for a removal — so one gesture language covers every area tool.
    /// </summary>
    partial class Terrain
    {
        private int forestryPreviewStart = -1;
        private int forestryPreviewEnd = -1;
        private bool forestryPreviewRemoval;
        // Reused between frames: the preview is rebuilt every frame, and a Span cannot cross
        // into the batch callbacks the primitive batch is driven by.
        private readonly int[] forestryPreviewTiles = new int[TerrainMap.MaximumAreaTiles];

        public int ForestryPreviewCount
        {
            get
            {
                if (forestryPreviewStart < 0 || forestryPreviewEnd < 0) return 0;
                return map.GetTileRectangle(forestryPreviewStart, forestryPreviewEnd, forestryPreviewTiles);
            }
        }

        public void SetForestryPreview(int startTileId, int endTileId, bool removal)
        {
            forestryPreviewStart = startTileId;
            forestryPreviewEnd = endTileId;
            forestryPreviewRemoval = removal;
        }

        public void ClearForestryPreview()
        {
            forestryPreviewStart = -1;
            forestryPreviewEnd = -1;
            forestryPreviewRemoval = false;
        }

        internal void DrawForestryPreview()
        {
            if (forestryPreviewStart < 0 || forestryPreviewEnd < 0) return;

            int[] tileIds = forestryPreviewTiles;
            int count = map.GetTileRectangle(forestryPreviewStart, forestryPreviewEnd, tileIds);
            if (count == 0) return;

            // Same palette as the road preview: white places, red removes.
            Color fill = forestryPreviewRemoval
                ? Color.FromArgb(90, 235, 70, 70)
                : Color.FromArgb(70, 255, 255, 255);
            Color line = forestryPreviewRemoval
                ? Color.FromArgb(245, 248, 80, 80)
                : Color.FromArgb(235, 255, 255, 255);

            using (RenderDevice.CreateStateScope().AlphaBlend())
            {
                DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
                {
                    DynamicPrimitiveBatch.Color4(fill);
                    for (int i = 0; i < count; i++) TileQuad(tiles[tileIds[i]]);
                });

                using (RenderDevice.CreateStateScope().ThinLines())
                {
                    DynamicPrimitiveBatch.Color4(line);
                    for (int i = 0; i < count; i++)
                    {
                        Tile tile = tiles[tileIds[i]];
                        DynamicPrimitiveBatch.Draw(PrimitiveTopology.LineLoop, () =>
                        {
                            DynamicPrimitiveBatch.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
                            DynamicPrimitiveBatch.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
                            DynamicPrimitiveBatch.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
                            DynamicPrimitiveBatch.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos);
                        });
                    }
                }
            }
        }

        private static void TileQuad(Tile tile)
        {
            DynamicPrimitiveBatch.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
            DynamicPrimitiveBatch.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
            DynamicPrimitiveBatch.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
            DynamicPrimitiveBatch.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos);
        }
    }
}
