using System;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        /// <summary>
        /// Skid trails are only wheel ruts in the soil: two dark strips along the trail whose depth (opacity and width)
        /// follows the wear. A freshly marked, still unused trail shows as a faint pair of lines.
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
                    DrawRuts(tile, map.GetNetworkEdges(tile.Id), colour, 0.07f + 0.07f * wear);
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
            // Quarter arcs round a tile corner: how a trail turns left or right, in a bend or at a junction.
            void Arc(float startDeg)
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
            }
            bool ws = (edges & RoadEdge.WS) != 0, se = (edges & RoadEdge.SE) != 0, en = (edges & RoadEdge.EN) != 0, nw = (edges & RoadEdge.NW) != 0;
            int count = TerrainMap.CountEdges(edges);
            if (count == 1)
            {
                // A dead end: the ruts run in to the tile centre.
                if (ws) Arm(0.5f, 0f); if (se) Arm(1f, 0.5f); if (en) Arm(0.5f, 1f); if (nw) Arm(0f, 0.5f);
                return;
            }
            // Straight through where two opposite edges meet …
            if (ws && en) { Arm(0.5f, 0f); Arm(0.5f, 1f); }
            if (se && nw) { Arm(1f, 0.5f); Arm(0f, 0.5f); }
            // … and an arc for every turn a vehicle can take: a bend, or each branch of a junction off the through line.
            bool Turn(bool a, bool b, bool aOpposite, bool bOpposite) => a && b && (count == 2 || count == 4 || !aOpposite || !bOpposite);
            if (Turn(nw, ws, se, en)) Arc(0f);    // W corner
            if (Turn(ws, se, en, nw)) Arc(90f);   // S corner
            if (Turn(se, en, nw, ws)) Arc(180f);  // E corner
            if (Turn(en, nw, ws, se)) Arc(270f);  // N corner
        }

        private static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 lift = new(0, 0, 0.02f);
            DynamicPrimitiveBatch.Vertex3(a + lift); DynamicPrimitiveBatch.Vertex3(b + lift);
            DynamicPrimitiveBatch.Vertex3(c + lift); DynamicPrimitiveBatch.Vertex3(d + lift);
        }

        // The trail lies on the natural ground (it is no built surface).
        private void TrailCorners(Tile t, out Vector3 W, out Vector3 S, out Vector3 E, out Vector3 N)
        {
            map.GetTrailSurfaceCorners(t.Id, out W, out S, out E, out N);
        }
    }
}
