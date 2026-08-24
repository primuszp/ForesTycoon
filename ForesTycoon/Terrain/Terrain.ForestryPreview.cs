using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>
    /// Preview for area planting and felling: the tiles the gesture would touch are tinted,
    /// and the parcel is ringed with a dashed line. The dashes are drawn as ribbons rather
    /// than as GL lines, because core profiles on macOS only give one-pixel hardware lines.
    /// </summary>
    partial class Terrain
    {
        private int forestryPreviewStart = -1;
        private int forestryPreviewEnd = -1;
        private bool forestryPreviewRemoval;
        // Reused between frames: the preview is rebuilt every frame, and a Span cannot cross
        // into the batch callbacks the primitive batch is driven by.
        private readonly int[] forestryPreviewTiles = new int[MaximumAreaTiles];

        /// <summary>How high above the ground the parcel outline floats, in world units.</summary>
        private const float ParcelLift = 0.12f;
        private const float ParcelDashWidth = 0.5f;
        private const float ParcelDashLength = 1.6f;
        private const float ParcelGapLength = 1.2f;

        public int ForestryPreviewCount
        {
            get
            {
                if (forestryPreviewStart < 0 || forestryPreviewEnd < 0) return 0;
                return GetTileRectangle(forestryPreviewStart, forestryPreviewEnd, forestryPreviewTiles);
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
            int count = GetTileRectangle(forestryPreviewStart, forestryPreviewEnd, tileIds);
            if (count == 0) return;

            Color fill = forestryPreviewRemoval
                ? Color.FromArgb(70, 232, 158, 72)
                : Color.FromArgb(70, 126, 214, 110);

            using (new RenderStateScope().AlphaBlend())
            {
                DynamicPrimitiveBatch.Draw(PrimitiveType.Quads, () =>
                {
                    DynamicPrimitiveBatch.Color4(fill);
                    for (int i = 0; i < count; i++)
                    {
                        Tile tile = tiles[tileIds[i]];
                        DynamicPrimitiveBatch.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos + ParcelLift);
                        DynamicPrimitiveBatch.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos + ParcelLift);
                        DynamicPrimitiveBatch.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos + ParcelLift);
                        DynamicPrimitiveBatch.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos + ParcelLift);
                    }
                });

                DrawParcelOutline(tileIds, count);
            }
        }

        /// <summary>Dashes the outer boundary of the previewed rectangle.</summary>
        private void DrawParcelOutline(int[] tileIds, int count)
        {
            int tilesPerColumn = settings.TileRows;
            int firstU = tileIds[0] / tilesPerColumn;
            int firstV = tileIds[0] % tilesPerColumn;
            int lastU = tileIds[count - 1] / tilesPerColumn;
            int lastV = tileIds[count - 1] % tilesPerColumn;

            DynamicPrimitiveBatch.Draw(PrimitiveType.Quads, () =>
            {
                DynamicPrimitiveBatch.Color4(Color.FromArgb(235, 250, 250, 244));
                for (int i = 0; i < count; i++)
                {
                    Tile tile = tiles[tileIds[i]];
                    int u = tileIds[i] / tilesPerColumn;
                    int v = tileIds[i] % tilesPerColumn;

                    // Corner layout: W = (u, v), S = (u + 1, v), E = (u + 1, v + 1), N = (u, v + 1).
                    if (u == firstU) DashedEdge(ParcelCorner(tile.W), ParcelCorner(tile.N));
                    if (u == lastU) DashedEdge(ParcelCorner(tile.S), ParcelCorner(tile.E));
                    if (v == firstV) DashedEdge(ParcelCorner(tile.W), ParcelCorner(tile.S));
                    if (v == lastV) DashedEdge(ParcelCorner(tile.N), ParcelCorner(tile.E));
                }
            });
        }

        private static Vector3 ParcelCorner(Node node) =>
            new Vector3(node.xPos, node.yPos, node.zPos + ParcelLift);

        private static void DashedEdge(Vector3 from, Vector3 to)
        {
            Vector3 span = to - from;
            float length = span.Length;
            if (length < 0.0001f) return;

            Vector3 direction = span / length;
            // The ribbon is widened in the ground plane; an edge is never vertical, so a
            // plain perpendicular in XY is enough and keeps the dash readable from above.
            Vector3 side = Vector3.Normalize(new Vector3(-direction.Y, direction.X, 0f))
                * (ParcelDashWidth * 0.5f);

            for (float at = 0f; at < length; at += ParcelDashLength + ParcelGapLength)
            {
                Vector3 dashFrom = from + direction * at;
                Vector3 dashTo = from + direction * MathF.Min(at + ParcelDashLength, length);

                DynamicPrimitiveBatch.Vertex3(dashFrom - side);
                DynamicPrimitiveBatch.Vertex3(dashFrom + side);
                DynamicPrimitiveBatch.Vertex3(dashTo + side);
                DynamicPrimitiveBatch.Vertex3(dashTo - side);
            }
        }
    }
}
