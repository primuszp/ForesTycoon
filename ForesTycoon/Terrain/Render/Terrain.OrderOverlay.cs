using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>
    /// What the order tool shows on the ground: the tiles it may pick, the tile under the cursor (green if it is a
    /// valid pick, red if not), and routes — the preview of the order being given and the routes of vehicles at work.
    /// </summary>
    internal sealed class OrderOverlay
    {
        internal readonly List<int> Candidates = new();
        internal int Hover = -1;
        internal bool HoverValid;
        internal readonly List<(int[] Tiles, Color Colour, bool Strong)> Routes = new();
        internal void Clear() { Candidates.Clear(); Hover = -1; Routes.Clear(); }
    }

    partial class Terrain
    {
        internal OrderOverlay Orders { get; } = new();

        /// <summary>Stack sites: a levelled, gravelled yard with a timber-coloured frame, so the receiving tile reads at once.</summary>
        internal void DrawStackSites(ForestryLogistics logistics)
        {
            if (logistics == null || logistics.Stacks.Count == 0) return;
            using var state = RenderDevice.CreateStateScope().AlphaBlend().DepthWrite(false);
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                DynamicPrimitiveBatch.Color4(Color.FromArgb(120, 196, 170, 120));
                foreach (var stack in logistics.Stacks) InsetQuad(tiles[stack.Tile], 0.08f, 0.03f);
            });
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                DynamicPrimitiveBatch.Color4(Color.FromArgb(230, 150, 96, 40));
                foreach (var stack in logistics.Stacks) Frame(tiles[stack.Tile], 0.06f, 0.1f);
            });
        }

        internal void DrawOrderOverlay(double time)
        {
            var o = Orders;
            if (o.Candidates.Count == 0 && o.Hover < 0 && o.Routes.Count == 0) return;
            using var state = RenderDevice.CreateStateScope().AlphaBlend().DepthWrite(false);
            float pulse = 0.55f + 0.45f * MathF.Sin((float)time * 4f);
            if (o.Candidates.Count > 0)
                DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
                {
                    DynamicPrimitiveBatch.Color4(Color.FromArgb((int)(150 * pulse), 255, 236, 140));
                    foreach (int id in o.Candidates) Frame(tiles[id], 0.02f, 0.07f);
                });
            if (o.Hover >= 0 && o.Hover < tiles.Length)
                DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
                {
                    DynamicPrimitiveBatch.Color4(o.HoverValid ? Color.FromArgb(110, 110, 230, 120) : Color.FromArgb(110, 235, 80, 70));
                    InsetQuad(tiles[o.Hover], 0f, 0.05f);
                });
            foreach (var (route, colour, strong) in o.Routes) DrawRoute(route, colour, strong, time);
        }

        // A ribbon from tile centre to tile centre, with moving dashes and an arrowhead at the destination.
        private void DrawRoute(int[] route, Color colour, bool strong, double time)
        {
            if (route == null || route.Length < 2) return;
            float width = Math.Min(tileSizeH, tileSizeV) * (strong ? 0.11f : 0.07f);
            var points = new Vector3[route.Length];
            for (int i = 0; i < route.Length; i++)
            {
                map.TryGetTileCenter(route[i], out points[i]);
                if (map.TryGetSurfaceZ(points[i].X, points[i].Y, out float z)) points[i].Z = z;
                points[i].Z += 0.12f;
            }
            float alpha = strong ? 1f : 0.55f;
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                float travelled = 0;
                for (int i = 1; i < points.Length; i++)
                {
                    Vector3 a = points[i - 1], b = points[i];
                    Vector3 d = b - a; float length = d.Xy.Length;
                    if (length < 1e-4f) continue;
                    Vector3 side = new Vector3(-d.Y, d.X, 0) / length * width * 0.5f;
                    // Dashes drift toward the destination: the route reads as a direction, not only a line.
                    const int Dashes = 4;
                    for (int k = 0; k < Dashes; k++)
                    {
                        float phase = (float)((travelled / Math.Min(tileSizeH, tileSizeV) + k / (float)Dashes - time * 0.8) % 1.0);
                        if (phase < 0) phase += 1;
                        float shade = phase < 0.5f ? 1f : 0.65f;
                        DynamicPrimitiveBatch.Color4(Color.FromArgb((int)(220 * alpha), (int)(colour.R * shade), (int)(colour.G * shade), (int)(colour.B * shade)));
                        float t0 = k / (float)Dashes, t1 = (k + 1) / (float)Dashes;
                        Vector3 p0 = a + d * t0, p1 = a + d * t1;
                        DynamicPrimitiveBatch.Vertex3(p0 - side); DynamicPrimitiveBatch.Vertex3(p1 - side);
                        DynamicPrimitiveBatch.Vertex3(p1 + side); DynamicPrimitiveBatch.Vertex3(p0 + side);
                    }
                    travelled += length;
                }
            });
            // Arrowhead into the destination tile.
            Vector3 end = points[^1], from = points[^2];
            Vector3 dir = end - from; if (dir.Xy.LengthSquared < 1e-8f) return;
            dir = new Vector3(dir.X, dir.Y, 0).Normalized();
            Vector3 normal = new(-dir.Y, dir.X, 0);
            float size = width * 2.2f;
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Triangles, () =>
            {
                DynamicPrimitiveBatch.Color4(Color.FromArgb((int)(240 * alpha), colour));
                Vector3 tip = end - dir * width * 0.5f;
                DynamicPrimitiveBatch.Vertex3(tip); DynamicPrimitiveBatch.Vertex3(tip - dir * size + normal * size * 0.6f);
                DynamicPrimitiveBatch.Vertex3(tip - dir * size - normal * size * 0.6f);
            });
        }

        // The tile's ground shrunk toward its centre by `inset` (of the tile), lifted a little.
        private void InsetQuad(Tile t, float inset, float lift)
        {
            Vector3 W = new(t.W.xPos, t.W.yPos, t.W.zPos), S = new(t.S.xPos, t.S.yPos, t.S.zPos),
                E = new(t.E.xPos, t.E.yPos, t.E.zPos), N = new(t.N.xPos, t.N.yPos, t.N.zPos);
            Vector3 up = new(0, 0, lift);
            DynamicPrimitiveBatch.Vertex3(TileUV(W, S, E, N, inset, inset) + up);
            DynamicPrimitiveBatch.Vertex3(TileUV(W, S, E, N, 1 - inset, inset) + up);
            DynamicPrimitiveBatch.Vertex3(TileUV(W, S, E, N, 1 - inset, 1 - inset) + up);
            DynamicPrimitiveBatch.Vertex3(TileUV(W, S, E, N, inset, 1 - inset) + up);
        }

        // A frame band along the tile's edges: from `inset` to `inset + band` (fractions of the tile).
        private void Frame(Tile t, float inset, float band)
        {
            Vector3 W = new(t.W.xPos, t.W.yPos, t.W.zPos), S = new(t.S.xPos, t.S.yPos, t.S.zPos),
                E = new(t.E.xPos, t.E.yPos, t.E.zPos), N = new(t.N.xPos, t.N.yPos, t.N.zPos);
            Vector3 up = new(0, 0, 0.04f);
            float a = inset, b = inset + band, c = 1 - inset - band, d = 1 - inset;
            void Q(float u0, float v0, float u1, float v1)
            {
                DynamicPrimitiveBatch.Vertex3(TileUV(W, S, E, N, u0, v0) + up); DynamicPrimitiveBatch.Vertex3(TileUV(W, S, E, N, u1, v0) + up);
                DynamicPrimitiveBatch.Vertex3(TileUV(W, S, E, N, u1, v1) + up); DynamicPrimitiveBatch.Vertex3(TileUV(W, S, E, N, u0, v1) + up);
            }
            Q(a, a, d, b); Q(a, c, d, d); Q(a, b, b, c); Q(c, b, d, c);
        }
    }
}
