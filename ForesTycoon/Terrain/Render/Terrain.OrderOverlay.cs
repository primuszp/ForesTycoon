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

        /// <summary>
        /// The route as a smooth line: through the edge midpoints, bending inside each tile on a quadratic curve round its
        /// centre — the same rounded corner the vehicles drive.
        /// </summary>
        private Vector3[] SmoothRoute(int[] route)
        {
            var centres = new Vector2[route.Length];
            for (int i = 0; i < route.Length; i++) { map.TryGetTileCenter(route[i], out Vector3 c); centres[i] = c.Xy; }
            const int Steps = 8;
            var points = new List<Vector3>(route.Length * Steps + 1);
            for (int k = 0; k < route.Length; k++)
            {
                Vector2 c = centres[k];
                Vector2 entry = k > 0 ? (centres[k - 1] + c) * 0.5f : c;
                Vector2 exit = k + 1 < route.Length ? (centres[k + 1] + c) * 0.5f : c;
                for (int s = k == 0 ? 0 : 1; s <= Steps; s++)
                {
                    float t = s / (float)Steps;
                    Vector2 p = (1 - t) * (1 - t) * entry + 2 * t * (1 - t) * c + t * t * exit;
                    float z = map.TryGetSurfaceZ(p.X, p.Y, out float ground) ? ground : 0;
                    points.Add(new Vector3(p.X, p.Y, z + 0.12f));
                }
            }
            return points.ToArray();
        }

        // A rounded ribbon with dashes drifting toward the destination, and an arrowhead at its end.
        private void DrawRoute(int[] route, Color colour, bool strong, double time)
        {
            if (route == null || route.Length < 2) return;
            float width = Math.Min(tileSizeH, tileSizeV) * (strong ? 0.11f : 0.07f);
            var points = SmoothRoute(route);
            float alpha = strong ? 1f : 0.55f;
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                // Joined segments: each quad uses the averaged side direction at its ends, so bends have no gaps or notches.
                Vector3 Side(int i)
                {
                    Vector3 d = points[Math.Min(i + 1, points.Length - 1)] - points[Math.Max(i - 1, 0)];
                    float l = d.Xy.Length;
                    return l < 1e-5f ? Vector3.Zero : new Vector3(-d.Y, d.X, 0) / l * width * 0.5f;
                }
                float travelled = 0, tile = Math.Min(tileSizeH, tileSizeV);
                for (int i = 1; i < points.Length; i++)
                {
                    Vector3 a = points[i - 1], b = points[i];
                    float length = (b - a).Xy.Length;
                    // Dashes drift toward the destination: the route reads as a direction, not only a line.
                    float phase = (float)(((travelled + length * 0.5f) / tile * 2 - time * 0.8) % 1.0);
                    if (phase < 0) phase += 1;
                    float shade = phase < 0.5f ? 1f : 0.62f;
                    DynamicPrimitiveBatch.Color4(Color.FromArgb((int)(220 * alpha), (int)(colour.R * shade), (int)(colour.G * shade), (int)(colour.B * shade)));
                    Vector3 sa = Side(i - 1), sb = Side(i);
                    DynamicPrimitiveBatch.Vertex3(a - sa); DynamicPrimitiveBatch.Vertex3(b - sb);
                    DynamicPrimitiveBatch.Vertex3(b + sb); DynamicPrimitiveBatch.Vertex3(a + sa);
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
