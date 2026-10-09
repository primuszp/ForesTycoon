using System;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        /// <summary>
        /// Skid trails are only wheel ruts in the soil: two dark strips along the trail whose depth (opacity and width)
        /// follows the wear. A freshly marked, still unused trail shows as a faint pair of lines with paint marks.
        /// </summary>
        internal void DrawSkidTrails()
        {
            if (map.SkidTrailCount == 0) return;
            using var state = RenderDevice.CreateStateScope().AlphaBlend().DepthWrite(false);
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                foreach (Tile tile in visibleTiles)
                {
                    if (!map.IsSkidTrail(tile.Id)) continue;
                    float wear = map.GetSkidTrailWear(tile.Id);
                    var colour = Color.FromArgb((int)(90 + 150 * wear), 92 - (int)(30 * wear), 70 - (int)(24 * wear), 46 - (int)(14 * wear));
                    DrawRuts(tile, map.GetSkidTrailEdges(tile.Id), colour, 0.07f + 0.07f * wear);
                }
            });
            // Paint marks on the trail line (as foresters blaze the trees): show where an unused trail runs.
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                foreach (Tile tile in visibleTiles)
                {
                    if (!map.IsSkidTrail(tile.Id) || map.GetSkidTrailWear(tile.Id) > 0.25f) continue;
                    TrailCorners(tile, out Vector3 W, out Vector3 S, out Vector3 E, out Vector3 N);
                    DynamicPrimitiveBatch.Color4(Color.FromArgb(200, 236, 150, 60));
                    Mark(TileUV(W, S, E, N, 0.5f, 0.5f), 0.07f * Math.Min(tileSizeH, tileSizeV));
                }
            });
        }

        private void DrawSkidTrailPreview()
        {
            if (previewTiles.Count == 0) return;
            using var state = RenderDevice.CreateStateScope().AlphaBlend().DepthWrite(false);
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                foreach (TerrainMap.RoadPlanStep step in previewTiles)
                {
                    bool ok = !previewRemove && map.CanCarrySkidTrail(step.TileId);
                    if (previewRemove && !map.IsSkidTrail(step.TileId)) continue;
                    if (!ok && !previewRemove && roads.Has(step.TileId)) continue; // crossing a road is fine, nothing to draw
                    var colour = previewRemove ? Color.FromArgb(200, 235, 80, 70) : ok ? Color.FromArgb(200, 240, 200, 120) : Color.FromArgb(200, 235, 80, 70);
                    DrawRuts(tiles[step.TileId], step.Edges | map.GetSkidTrailEdges(step.TileId), colour, 0.05f);
                }
            });
        }

        // Two ruts per connected edge, from the tile centre to the edge midpoint, a wheel track apart.
        private void DrawRuts(Tile tile, RoadEdge edges, Color colour, float widthFraction)
        {
            TrailCorners(tile, out Vector3 W, out Vector3 S, out Vector3 E, out Vector3 N);
            if (edges == RoadEdge.None) edges = RoadEdge.WS | RoadEdge.EN;
            DynamicPrimitiveBatch.Color4(colour);
            const float Gauge = 0.14f;   // half the track width, in tile fractions
            void Arm(float eu, float ev)
            {
                // (eu, ev) is the edge midpoint in tile (u, v) coordinates; ruts run parallel to centre → edge.
                float du = eu - 0.5f, dv = ev - 0.5f;
                float pu = -dv * 2, pv = du * 2;      // unit perpendicular in tile coordinates
                foreach (float side in new[] { -Gauge, Gauge })
                {
                    float w = widthFraction * 0.5f;
                    float a0u = 0.5f + pu * (side - w), a0v = 0.5f + pv * (side - w);
                    float a1u = 0.5f + pu * (side + w), a1v = 0.5f + pv * (side + w);
                    // Run slightly past the centre so straight and bending trails close without gaps.
                    float back = Gauge + w;
                    Quad(TileUV(W, S, E, N, a0u - du * 2 * back, a0v - dv * 2 * back), TileUV(W, S, E, N, a1u - du * 2 * back, a1v - dv * 2 * back),
                        TileUV(W, S, E, N, a1u + du, a1v + dv), TileUV(W, S, E, N, a0u + du, a0v + dv));
                }
            }
            // A bend (two adjacent edges) curves round the shared tile corner like a road bend: two concentric quarter arcs.
            if (TryCornerArc(edges, out float startDeg))
            {
                float cu = startDeg < 90f ? 0f : startDeg < 270f ? 1f : 0f;
                float cv = startDeg < 180f ? 0f : 1f;
                float w = widthFraction * 0.5f;
                const int Segments = 8;
                foreach (float side in new[] { -Gauge, Gauge })
                {
                    float inner = 0.5f + side - w, outer = 0.5f + side + w;
                    for (int i = 0; i < Segments; i++)
                    {
                        float a0 = MathHelper.DegreesToRadians(startDeg + 90f * i / Segments);
                        float a1 = MathHelper.DegreesToRadians(startDeg + 90f * (i + 1) / Segments);
                        Quad(TileUV(W, S, E, N, cu + inner * MathF.Cos(a0), cv + inner * MathF.Sin(a0)),
                            TileUV(W, S, E, N, cu + outer * MathF.Cos(a0), cv + outer * MathF.Sin(a0)),
                            TileUV(W, S, E, N, cu + outer * MathF.Cos(a1), cv + outer * MathF.Sin(a1)),
                            TileUV(W, S, E, N, cu + inner * MathF.Cos(a1), cv + inner * MathF.Sin(a1)));
                    }
                }
                return;
            }
            // Tile (u, v): u runs W→S… as in TileUV; the edge midpoints in those coordinates.
            if ((edges & RoadEdge.WS) != 0) Arm(0.5f, 0f);
            if ((edges & RoadEdge.SE) != 0) Arm(1f, 0.5f);
            if ((edges & RoadEdge.EN) != 0) Arm(0.5f, 1f);
            if ((edges & RoadEdge.NW) != 0) Arm(0f, 0.5f);
        }

        private static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 lift = new(0, 0, 0.02f);
            DynamicPrimitiveBatch.Vertex3(a + lift); DynamicPrimitiveBatch.Vertex3(b + lift);
            DynamicPrimitiveBatch.Vertex3(c + lift); DynamicPrimitiveBatch.Vertex3(d + lift);
        }

        private static void Mark(Vector3 c, float r)
        {
            Quad(c + new Vector3(-r, -r, 0.01f), c + new Vector3(r, -r, 0.01f), c + new Vector3(r, r, 0.01f), c + new Vector3(-r, r, 0.01f));
        }

        // The trail lies on the natural ground (it is no built surface).
        private static void TrailCorners(Tile t, out Vector3 W, out Vector3 S, out Vector3 E, out Vector3 N)
        {
            W = new Vector3(t.W.xPos, t.W.yPos, t.W.zPos);
            S = new Vector3(t.S.xPos, t.S.yPos, t.S.zPos);
            E = new Vector3(t.E.xPos, t.E.yPos, t.E.zPos);
            N = new Vector3(t.N.xPos, t.N.yPos, t.N.zPos);
        }
    }
}
