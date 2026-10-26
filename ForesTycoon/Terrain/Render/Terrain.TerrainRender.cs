using System;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        internal void DrawTerrainBase()
        {
            DrawCachedTerrain();
            DrawFoundationTerrainSurfaces();
        }

        private void DrawTerrainTileQuad(Tile tile)
        {
            Vector3 w = new Vector3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
            Vector3 s = new Vector3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
            Vector3 e = new Vector3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
            Vector3 n = new Vector3(tile.N.xPos, tile.N.yPos, tile.N.zPos);

            // One flat face: one shade, each corner its own blended ground colour.
            DynamicPrimitiveBatch.Color4(TileShading.Shade(CornerColor(tile.W), w, s, e)); DynamicPrimitiveBatch.Vertex3(w);
            DynamicPrimitiveBatch.Color4(TileShading.Shade(CornerColor(tile.S), w, s, e)); DynamicPrimitiveBatch.Vertex3(s);
            DynamicPrimitiveBatch.Color4(TileShading.Shade(CornerColor(tile.E), w, s, e)); DynamicPrimitiveBatch.Vertex3(e);
            DynamicPrimitiveBatch.Color4(TileShading.Shade(CornerColor(tile.N), w, s, e)); DynamicPrimitiveBatch.Vertex3(n);
        }

        internal void DrawTerrainDecals(RenderContext context = default, bool winter = false)
        {
            DrawCachedGrid(winter);
        }

        private void DrawTileGrid(Tile tile, Color terrainLine)
        {
            // A grid consists of all four tile boundaries, independent of material or triangulation.
            DrawTileEdgesByMaterial(tile, GetTileSurface(tile), terrainLine);
        }

        private static Color EdgeLineColor(TileSurfaceMaterial material, Color terrainLine)
        {
            return material == TileSurfaceMaterial.Foundation ? RoadFoundationLineColor : terrainLine;
        }

        private static void DrawTileEdgesByMaterial(Tile tile, TileSurface visual, Color terrainLine)
        {
            DynamicPrimitiveBatch.Color4(EdgeLineColor(visual.EdgeWS, terrainLine));
            DynamicPrimitiveBatch.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos); DynamicPrimitiveBatch.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos);

            DynamicPrimitiveBatch.Color4(EdgeLineColor(visual.EdgeSE, terrainLine));
            DynamicPrimitiveBatch.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos); DynamicPrimitiveBatch.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos);

            DynamicPrimitiveBatch.Color4(EdgeLineColor(visual.EdgeEN, terrainLine));
            DynamicPrimitiveBatch.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos); DynamicPrimitiveBatch.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos);

            DynamicPrimitiveBatch.Color4(EdgeLineColor(visual.EdgeNW, terrainLine));
            DynamicPrimitiveBatch.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos); DynamicPrimitiveBatch.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
        }

        /// <summary>The soil profile under a tile, top down; set by the world. Without it the sides show plain earth.</summary>
        internal Func<int, SideLayer[]> SideProfile;

        private const float PlinthBase = -16.0f;
        private static readonly Color PlainEarth = Color.FromArgb(185, 148, 100);
        private static readonly Color BedrockColor = GameWorld.Hex(SoilHorizon.Bedrock);
        private static readonly Color PlinthBottom = Color.FromArgb(130, 100, 65);
        // The bedrock darkens towards the plinth, which ends in a dark rim: the diorama stands out from the backdrop.
        private static readonly Color BedrockDeep = Color.FromArgb(62, 56, 50);
        private static readonly Color PlinthRim = Color.FromArgb(22, 20, 18);
        private const float RimHeight = .7f;

        /// <summary>
        /// The diorama's four sides, cut through the ground. The top follows the terrain edge exactly; the horizon boundaries
        /// below follow a smoothed copy of it, sampled four times per tile, so they run in soft curves instead of the terrain's
        /// height steps, and where the soil changes their thickness and colour fade over a tile. Bedrock fills the rest to the
        /// plinth. South and north faces are drawn lighter than west and east.
        /// </summary>
        internal void DrawSkirts()
        {
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                int tilesU = nodeCols - 1, tilesV = nodeRows - 1;
                CurvedSide(tilesU, j => getNodeByCoords(j, 0), t => getTileByCoords(t, 0), reverse: false, light: 1f);
                CurvedSide(tilesU, j => getNodeByCoords(j, nodeRows - 1), t => getTileByCoords(t, nodeRows - 2), reverse: true, light: 1f);
                CurvedSide(tilesV, j => getNodeByCoords(0, j), t => getTileByCoords(0, t), reverse: true, light: .9f);
                CurvedSide(tilesV, j => getNodeByCoords(nodeCols - 1, j), t => getTileByCoords(nodeCols - 2, t), reverse: false, light: .9f);

                DynamicPrimitiveBatch.Color3(PlinthBottom);
                Node sw = getNodeByCoords(0, 0), se = getNodeByCoords(nodeCols - 1, 0);
                Node ne = getNodeByCoords(nodeCols - 1, nodeRows - 1), nw = getNodeByCoords(0, nodeRows - 1);
                DynamicPrimitiveBatch.Vertex3(sw.xPos, sw.yPos, PlinthBase);
                DynamicPrimitiveBatch.Vertex3(se.xPos, se.yPos, PlinthBase);
                DynamicPrimitiveBatch.Vertex3(ne.xPos, ne.yPos, PlinthBase);
                DynamicPrimitiveBatch.Vertex3(nw.xPos, nw.yPos, PlinthBase);
            });
        }

        private const int SideSamplesPerTile = 4;
        private static readonly SideLayer[] PlainEarthProfile = { new(1e6f, PlainEarth) };

        /// <summary>One side: <paramref name="tiles"/> edge tiles between nodes 0..tiles, drawn in outward winding order.</summary>
        private void CurvedSide(int tiles, Func<int, Node> node, Func<int, Tile> tile, bool reverse, float light)
        {
            // The edge heights, and a smoothed copy (binomial 1-4-6-4-1) the horizon boundaries follow.
            var height = new float[tiles + 1];
            for (int j = 0; j <= tiles; j++) height[j] = node(j).zPos;
            var smooth = new float[tiles + 1];
            for (int j = 0; j <= tiles; j++)
            {
                float sum = 0, weight = 0;
                for (int k = -2; k <= 2; k++)
                {
                    int at = Math.Clamp(j + k, 0, tiles); float w = k == 0 ? 6 : Math.Abs(k) == 1 ? 4 : 1;
                    sum += height[at] * w; weight += w;
                }
                smooth[j] = sum / weight;
            }
            var profiles = new SideLayer[tiles][];
            for (int t = 0; t < tiles; t++)
            {
                var layers = SideProfile?.Invoke(tile(t).Id);
                profiles[t] = layers is { Length: > 0 } ? layers : PlainEarthProfile;
            }

            int samples = tiles * SideSamplesPerTile + 1;
            var position = new Vector3[samples]; var top = new float[samples];
            int bands = 1; foreach (var p in profiles) bands = Math.Max(bands, p.Length);
            // Boundary k is the bottom of horizon k (below the surface, in world units), the last one the bedrock's top.
            var bottom = new float[samples, bands]; var colour = new Color[samples, bands];
            for (int i = 0; i < samples; i++)
            {
                float s = (float)i / SideSamplesPerTile;                     // position along the side, in tiles
                int j = Math.Min((int)s, tiles - 1); float f = s - j;
                Node a = node(j), b = node(j + 1);
                position[i] = new Vector3(a.xPos + (b.xPos - a.xPos) * f, a.yPos + (b.yPos - a.yPos) * f, 0);
                top[i] = height[j] + (height[j + 1] - height[j]) * f;
                float surface = CatmullRom(smooth, j, f);
                // Blend the two tiles whose centres surround this point; profiles of different length use the nearer one.
                float c = s - .5f; int left = Math.Clamp((int)MathF.Floor(c), 0, tiles - 1), right = Math.Clamp(left + 1, 0, tiles - 1);
                float w = Math.Clamp(c - left, 0, 1); w = w * w * (3 - 2 * w);
                var pl = profiles[left]; var pr = profiles[right];
                if (pl.Length != pr.Length) { pl = pr = w < .5f ? pl : pr; }
                float depth = 0;
                for (int k = 0; k < bands; k++)
                {
                    if (k < pl.Length)
                    {
                        depth += pl[k].Depth + (pr[k].Depth - pl[k].Depth) * w;
                        colour[i, k] = Mix(pl[k].Color, pr[k].Color, w);
                    }
                    else colour[i, k] = colour[i, Math.Max(0, k - 1)];
                    // Never above the true edge, so thin top horizons do not pop out over a terrain step.
                    bottom[i, k] = Math.Min(surface - depth, top[i] - .05f * (k + 1));
                }
            }

            // A band between two boundaries; the colour may differ along the side (soils) and from top to bottom (bedrock).
            void Quad(int i0, int i1, float a0, float a1, float b0, float b1, Color c0, Color c1, Color? low0 = null, Color? low1 = null)
            {
                (int from, int to) = reverse ? (i1, i0) : (i0, i1);
                (float fromTop, float toTop, float fromLow, float toLow) = reverse ? (a1, a0, b1, b0) : (a0, a1, b0, b1);
                Color l0 = low0 ?? c0, l1 = low1 ?? c1;
                (Color cf, Color ct, Color lf, Color lt) = reverse ? (c1, c0, l1, l0) : (c0, c1, l0, l1);
                float floor = PlinthBase + RimHeight;
                fromTop = Math.Max(fromTop, floor); toTop = Math.Max(toTop, floor);
                fromLow = Math.Max(fromLow, floor); toLow = Math.Max(toLow, floor);
                if (fromTop <= floor && toTop <= floor) return;
                DynamicPrimitiveBatch.Color3(Lit(cf, light)); DynamicPrimitiveBatch.Vertex3(position[from].X, position[from].Y, fromTop);
                DynamicPrimitiveBatch.Color3(Lit(ct, light)); DynamicPrimitiveBatch.Vertex3(position[to].X, position[to].Y, toTop);
                DynamicPrimitiveBatch.Color3(Lit(lt, light)); DynamicPrimitiveBatch.Vertex3(position[to].X, position[to].Y, toLow);
                DynamicPrimitiveBatch.Color3(Lit(lf, light)); DynamicPrimitiveBatch.Vertex3(position[from].X, position[from].Y, fromLow);
            }
            for (int i = 0; i + 1 < samples; i++)
            {
                float upper0 = top[i], upper1 = top[i + 1];
                for (int k = 0; k < bands; k++)
                {
                    Quad(i, i + 1, upper0, upper1, bottom[i, k], bottom[i + 1, k], colour[i, k], colour[i + 1, k]);
                    upper0 = bottom[i, k]; upper1 = bottom[i + 1, k];
                }
                // Bedrock: its own colour where it starts, darkening linearly to the rim.
                float floor = PlinthBase + RimHeight;
                Color Rock(float z) => Mix(BedrockDeep, BedrockColor, Math.Clamp((z - floor) / 22f, 0, 1));
                Quad(i, i + 1, upper0, upper1, floor, floor, Rock(upper0), Rock(upper1), BedrockDeep, BedrockDeep);
                // The rim: a thin dark band along the plinth's foot.
                (int from, int to) = reverse ? (i + 1, i) : (i, i + 1);
                DynamicPrimitiveBatch.Color3(Lit(PlinthRim, light));
                DynamicPrimitiveBatch.Vertex3(position[from].X, position[from].Y, floor); DynamicPrimitiveBatch.Vertex3(position[to].X, position[to].Y, floor);
                DynamicPrimitiveBatch.Vertex3(position[to].X, position[to].Y, PlinthBase); DynamicPrimitiveBatch.Vertex3(position[from].X, position[from].Y, PlinthBase);
            }
        }

        /// <summary>A smooth curve through the values, between index <paramref name="j"/> and the next, at <paramref name="t"/>.</summary>
        private static float CatmullRom(float[] v, int j, float t)
        {
            float p0 = v[Math.Max(j - 1, 0)], p1 = v[j], p2 = v[Math.Min(j + 1, v.Length - 1)], p3 = v[Math.Min(j + 2, v.Length - 1)];
            return .5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
        }
        private static Color Mix(Color a, Color b, float t) =>
            Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        private static Color Lit(Color c, float light) => Color.FromArgb(255, (int)(c.R * light), (int)(c.G * light), (int)(c.B * light));
    }
}
