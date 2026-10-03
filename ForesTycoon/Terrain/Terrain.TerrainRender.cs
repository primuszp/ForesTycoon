using System.Drawing;
using OpenTK.Graphics.OpenGL;
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

            DynamicPrimitiveBatch.Color4(ShadedTileColor(TerrainSurfaceColor(tile.Code, tile.Low), w, s, e));
            DynamicPrimitiveBatch.Vertex3(w);
            DynamicPrimitiveBatch.Vertex3(s);
            DynamicPrimitiveBatch.Vertex3(e);
            DynamicPrimitiveBatch.Vertex3(n);
        }

        internal void DrawTerrainDecals(RenderContext context = default)
        {
            DrawCachedGrid(context.PixelsPerWorldUnit);
        }

        private void DrawTileGrid(Tile tile, Color terrainLine)
        {
            // A grid consists of all four tile boundaries, independent of material or triangulation.
            DrawTileEdgesByMaterial(tile, GetTileSurfaceVisual(tile), terrainLine);
        }

        private static Color EdgeLineColor(TileRenderMaterial material, Color terrainLine)
        {
            return material == TileRenderMaterial.Foundation ? RoadFoundationLineColor : terrainLine;
        }

        private static void DrawTileEdgesByMaterial(Tile tile, TileSurfaceVisual visual, Color terrainLine)
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

        private void DrawTileDiagonal(Tile tile, TileSurfaceVisual visual)
        {
            DynamicPrimitiveBatch.Color4(RoadFoundationLineColor);
            if (UseTileDiagonalWE(tile, visual))
            {
                DynamicPrimitiveBatch.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
                DynamicPrimitiveBatch.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
            }
            else
            {
                DynamicPrimitiveBatch.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos);
                DynamicPrimitiveBatch.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
            }
        }

        internal void DrawSkirts()
        {
            const float BASE_Z      = -16.0f;
            const float RIM_H       =   1.5f;
            Color colorFront  = Color.FromArgb(205, 163, 112);
            Color colorSide   = Color.FromArgb(185, 148, 100);
            Color colorBottom = Color.FromArgb(130, 100,  65);
            Color colorRim    = Color.FromArgb( 68,  48,  25);

            DynamicPrimitiveBatch.Draw(PrimitiveType.Quads, () =>
            {

            // ── South edge (v = 0) ────────────────────────────────────────────
            for (int u = 0; u < nodeCols - 1; u++)
            {
                Node a = getNodeByCoords(u,     0);
                Node b = getNodeByCoords(u + 1, 0);
                float rimA = a.zPos - RIM_H;
                float rimB = b.zPos - RIM_H;
                // sötét peremcsík (felső sáv)
                DynamicPrimitiveBatch.Color3(colorRim);
                DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, a.zPos); DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, b.zPos);
                DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, rimB);   DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, rimA);
                // világos oldallap (perem alatt → aljáig)
                DynamicPrimitiveBatch.Color3(colorFront);
                DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, rimA);   DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, rimB);
                DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, BASE_Z); DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, BASE_Z);
            }

            // ── North edge (v = nodeRows-1) ───────────────────────────────────
            for (int u = 0; u < nodeCols - 1; u++)
            {
                Node a = getNodeByCoords(u,     nodeRows - 1);
                Node b = getNodeByCoords(u + 1, nodeRows - 1);
                float rimA = a.zPos - RIM_H;
                float rimB = b.zPos - RIM_H;
                DynamicPrimitiveBatch.Color3(colorRim);
                DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, b.zPos); DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, a.zPos);
                DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, rimA);   DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, rimB);
                DynamicPrimitiveBatch.Color3(colorFront);
                DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, rimB);   DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, rimA);
                DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, BASE_Z); DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, BASE_Z);
            }

            // ── West edge (u = 0) ─────────────────────────────────────────────
            for (int v = 0; v < nodeRows - 1; v++)
            {
                Node a = getNodeByCoords(0, v);
                Node b = getNodeByCoords(0, v + 1);
                float rimA = a.zPos - RIM_H;
                float rimB = b.zPos - RIM_H;
                DynamicPrimitiveBatch.Color3(colorRim);
                DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, a.zPos); DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, rimA);
                DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, rimB);   DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, b.zPos);
                DynamicPrimitiveBatch.Color3(colorSide);
                DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, rimA);   DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, BASE_Z);
                DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, BASE_Z); DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, rimB);
            }

            // ── East edge (u = nodeCols-1) ────────────────────────────────────
            for (int v = 0; v < nodeRows - 1; v++)
            {
                Node a = getNodeByCoords(nodeCols - 1, v);
                Node b = getNodeByCoords(nodeCols - 1, v + 1);
                float rimA = a.zPos - RIM_H;
                float rimB = b.zPos - RIM_H;
                DynamicPrimitiveBatch.Color3(colorRim);
                DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, b.zPos); DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, rimB);
                DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, rimA);   DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, a.zPos);
                DynamicPrimitiveBatch.Color3(colorSide);
                DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, rimB);   DynamicPrimitiveBatch.Vertex3(b.xPos, b.yPos, BASE_Z);
                DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, BASE_Z); DynamicPrimitiveBatch.Vertex3(a.xPos, a.yPos, rimA);
            }

            // ── Aljlap ────────────────────────────────────────────────────────
            DynamicPrimitiveBatch.Color3(colorBottom);
            Node sw = getNodeByCoords(0,            0);
            Node se = getNodeByCoords(nodeCols - 1, 0);
            Node ne = getNodeByCoords(nodeCols - 1, nodeRows - 1);
            Node nw = getNodeByCoords(0,            nodeRows - 1);
            DynamicPrimitiveBatch.Vertex3(sw.xPos, sw.yPos, BASE_Z);
            DynamicPrimitiveBatch.Vertex3(se.xPos, se.yPos, BASE_Z);
            DynamicPrimitiveBatch.Vertex3(ne.xPos, ne.yPos, BASE_Z);
            DynamicPrimitiveBatch.Vertex3(nw.xPos, nw.yPos, BASE_Z);

            });
        }

    }
}
