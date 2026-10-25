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

        /// <summary>
        /// The diorama's four sides, cut through the ground: each edge tile's soil horizons follow the surface down, the
        /// bedrock fills the rest to the plinth. South and north faces are drawn lighter than west and east, as before.
        /// </summary>
        internal void DrawSkirts()
        {
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                for (int u = 0; u < nodeCols - 1; u++)
                {
                    Side(getNodeByCoords(u, 0), getNodeByCoords(u + 1, 0), getTileByCoords(u, 0), 1f);
                    Side(getNodeByCoords(u + 1, nodeRows - 1), getNodeByCoords(u, nodeRows - 1), getTileByCoords(u, nodeRows - 2), 1f);
                }
                for (int v = 0; v < nodeRows - 1; v++)
                {
                    Side(getNodeByCoords(0, v + 1), getNodeByCoords(0, v), getTileByCoords(0, v), .9f);
                    Side(getNodeByCoords(nodeCols - 1, v), getNodeByCoords(nodeCols - 1, v + 1), getTileByCoords(nodeCols - 2, v), .9f);
                }
                DynamicPrimitiveBatch.Color3(PlinthBottom);
                Node sw = getNodeByCoords(0, 0), se = getNodeByCoords(nodeCols - 1, 0);
                Node ne = getNodeByCoords(nodeCols - 1, nodeRows - 1), nw = getNodeByCoords(0, nodeRows - 1);
                DynamicPrimitiveBatch.Vertex3(sw.xPos, sw.yPos, PlinthBase);
                DynamicPrimitiveBatch.Vertex3(se.xPos, se.yPos, PlinthBase);
                DynamicPrimitiveBatch.Vertex3(ne.xPos, ne.yPos, PlinthBase);
                DynamicPrimitiveBatch.Vertex3(nw.xPos, nw.yPos, PlinthBase);
            });
        }

        /// <summary>One side segment from <paramref name="a"/> to <paramref name="b"/> (outward winding): horizon bands, then bedrock.</summary>
        private void Side(Node a, Node b, Tile tile, float light)
        {
            var layers = SideProfile?.Invoke(tile.Id);
            if (layers == null || layers.Length == 0) { Band(a, b, 0, float.MaxValue, PlainEarth, light); return; }
            float depth = 0;
            foreach (var layer in layers)
            {
                if (Band(a, b, depth, depth + layer.Depth, layer.Color, light)) return;
                depth += layer.Depth;
            }
            Band(a, b, depth, float.MaxValue, BedrockColor, light);
        }

        /// <summary>Draws the band between two depths below the surface, cut at the plinth; true once it reaches the plinth.</summary>
        private static bool Band(Node a, Node b, float top, float bottom, Color color, float light)
        {
            float topA = Math.Max(a.zPos - top, PlinthBase), topB = Math.Max(b.zPos - top, PlinthBase);
            float lowA = Math.Max(a.zPos - bottom, PlinthBase), lowB = Math.Max(b.zPos - bottom, PlinthBase);
            if (topA <= PlinthBase && topB <= PlinthBase) return true;
            DynamicPrimitiveBatch.Color3(Color.FromArgb((int)(color.R * light), (int)(color.G * light), (int)(color.B * light)));
            DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, topA); DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, topB);
            DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, lowB); DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, lowA);
            return lowA <= PlinthBase && lowB <= PlinthBase;
        }

    }
}
