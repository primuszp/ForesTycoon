using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        private void DrawTerrainBase()
        {
            // A kitöltött terep írja a depth buffert; a koplanáris overlay rétegek
            // később depth írás nélkül rajzolódnak, így nincs Z-fighting.
            foreach (Tile tile in tiles)
            {
                // Az út-csempék terep-meshe helyett a platform/földmű renderelődik
                // (DrawRoadFoundations) — különben bevágásnál a magasabb terep eltakarná az utat.
                if (roads.Has(tile.Id)) continue;
                if (GetTileRenderMaterial(tile) != TileRenderMaterial.Grass) continue;

                bool tileFlip = flippedDiagonalTiles.Contains(tile.Id);
                VertexBuffer vbo = vbos[tile.Code + "_" + tile.Low + (tileFlip ? "_f" : "")];

                GL.PushMatrix();
                {
                    GL.Translate(tile.W.xPos, tile.W.yPos, tile.LowPos);
                    vbo.DrawArray();
                }
                GL.PopMatrix();
            }
        }

        private void BeginTerrainDecals()
        {
            terrainDecalState?.Dispose();
            terrainDecalState = new RenderStateScope()
                .Disable(EnableCap.DepthTest)
                .DepthWrite(false);
        }

        private void DrawTerrainDecals()
        {
            DrawLandGrid();
        }

        private void EndTerrainDecals()
        {
            terrainDecalState?.Dispose();
            terrainDecalState = null;
        }

        private void DrawLandGrid()
        {
            Color terrainLine = Color.FromArgb(82, 115, 38);
            ImmediateRenderer.Draw(PrimitiveType.Lines, () =>
            {
                foreach (Tile tile in tiles)
                {
                    if (ShouldDrawStandingWater(tile)) continue;
                    DrawTerrainGridBase(tile, terrainLine);
                }

                foreach (Tile tile in tiles)
                {
                    if (ShouldDrawStandingWater(tile)) continue;
                    DrawFoundationGridOverlay(tile);
                }
            });
        }

        private void DrawTerrainGridBase(Tile tile, Color terrainLine)
        {
            FoundationTileInfo info = GetFoundationTileInfo(tile);

            if (info.Material == TileRenderMaterial.Grass)
            {
                GL.Color4(terrainLine);
                DrawTileOutline(tile);
                return;
            }

            if (info.SplitGrid)
            {
                MixedTrianglePoints(tile, info.Edge, false, out Vector3 ga, out Vector3 gb, out Vector3 gc);
                GL.Color4(terrainLine);
                DrawTriangleEdges(ga, gb, gc);
            }
        }

        private void DrawFoundationGridOverlay(Tile tile)
        {
            FoundationTileInfo info = GetFoundationTileInfo(tile);
            if (info.Material == TileRenderMaterial.Grass) return;

            GL.Color4(RoadFoundationLineColor);
            if (!info.SplitGrid)
            {
                DrawTileOutline(tile);
                return;
            }

            MixedTrianglePoints(tile, info.Edge, true, out Vector3 ba, out Vector3 bb, out Vector3 bc);
            DrawTriangleEdges(ba, bb, bc);
        }

        private static void DrawTileOutline(Tile tile)
        {
            GL.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos); GL.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos);
            GL.Vertex3(tile.S.xPos, tile.S.yPos, tile.S.zPos); GL.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos);
            GL.Vertex3(tile.E.xPos, tile.E.yPos, tile.E.zPos); GL.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos);
            GL.Vertex3(tile.N.xPos, tile.N.yPos, tile.N.zPos); GL.Vertex3(tile.W.xPos, tile.W.yPos, tile.W.zPos);
        }

        private static void DrawTriangleEdges(Vector3 a, Vector3 b, Vector3 c)
        {
            GL.Vertex3(a); GL.Vertex3(b);
            GL.Vertex3(b); GL.Vertex3(c);
            GL.Vertex3(c); GL.Vertex3(a);
        }

        private void DrawSkirts()
        {
            const float BASE_Z      = -16.0f;
            const float RIM_H       =   1.5f;
            Color colorFront  = Color.FromArgb(205, 163, 112);
            Color colorSide   = Color.FromArgb(185, 148, 100);
            Color colorBottom = Color.FromArgb(130, 100,  65);
            Color colorRim    = Color.FromArgb( 68,  48,  25);

            ImmediateRenderer.Draw(PrimitiveType.Quads, () =>
            {

            // ── South edge (v = 0) ────────────────────────────────────────────
            for (int u = 0; u < nodeCols - 1; u++)
            {
                Node a = getNodeByCoords(u,     0);
                Node b = getNodeByCoords(u + 1, 0);
                float rimA = a.zPos - RIM_H;
                float rimB = b.zPos - RIM_H;
                // sötét peremcsík (felső sáv)
                GL.Color3(colorRim);
                GL.Vertex3(a.xPos, a.yPos, a.zPos); GL.Vertex3(b.xPos, b.yPos, b.zPos);
                GL.Vertex3(b.xPos, b.yPos, rimB);   GL.Vertex3(a.xPos, a.yPos, rimA);
                // világos oldallap (perem alatt → aljáig)
                GL.Color3(colorFront);
                GL.Vertex3(a.xPos, a.yPos, rimA);   GL.Vertex3(b.xPos, b.yPos, rimB);
                GL.Vertex3(b.xPos, b.yPos, BASE_Z); GL.Vertex3(a.xPos, a.yPos, BASE_Z);
            }

            // ── North edge (v = nodeRows-1) ───────────────────────────────────
            for (int u = 0; u < nodeCols - 1; u++)
            {
                Node a = getNodeByCoords(u,     nodeRows - 1);
                Node b = getNodeByCoords(u + 1, nodeRows - 1);
                float rimA = a.zPos - RIM_H;
                float rimB = b.zPos - RIM_H;
                GL.Color3(colorRim);
                GL.Vertex3(b.xPos, b.yPos, b.zPos); GL.Vertex3(a.xPos, a.yPos, a.zPos);
                GL.Vertex3(a.xPos, a.yPos, rimA);   GL.Vertex3(b.xPos, b.yPos, rimB);
                GL.Color3(colorFront);
                GL.Vertex3(b.xPos, b.yPos, rimB);   GL.Vertex3(a.xPos, a.yPos, rimA);
                GL.Vertex3(a.xPos, a.yPos, BASE_Z); GL.Vertex3(b.xPos, b.yPos, BASE_Z);
            }

            // ── West edge (u = 0) ─────────────────────────────────────────────
            for (int v = 0; v < nodeRows - 1; v++)
            {
                Node a = getNodeByCoords(0, v);
                Node b = getNodeByCoords(0, v + 1);
                float rimA = a.zPos - RIM_H;
                float rimB = b.zPos - RIM_H;
                GL.Color3(colorRim);
                GL.Vertex3(a.xPos, a.yPos, a.zPos); GL.Vertex3(a.xPos, a.yPos, rimA);
                GL.Vertex3(b.xPos, b.yPos, rimB);   GL.Vertex3(b.xPos, b.yPos, b.zPos);
                GL.Color3(colorSide);
                GL.Vertex3(a.xPos, a.yPos, rimA);   GL.Vertex3(a.xPos, a.yPos, BASE_Z);
                GL.Vertex3(b.xPos, b.yPos, BASE_Z); GL.Vertex3(b.xPos, b.yPos, rimB);
            }

            // ── East edge (u = nodeCols-1) ────────────────────────────────────
            for (int v = 0; v < nodeRows - 1; v++)
            {
                Node a = getNodeByCoords(nodeCols - 1, v);
                Node b = getNodeByCoords(nodeCols - 1, v + 1);
                float rimA = a.zPos - RIM_H;
                float rimB = b.zPos - RIM_H;
                GL.Color3(colorRim);
                GL.Vertex3(b.xPos, b.yPos, b.zPos); GL.Vertex3(b.xPos, b.yPos, rimB);
                GL.Vertex3(a.xPos, a.yPos, rimA);   GL.Vertex3(a.xPos, a.yPos, a.zPos);
                GL.Color3(colorSide);
                GL.Vertex3(b.xPos, b.yPos, rimB);   GL.Vertex3(b.xPos, b.yPos, BASE_Z);
                GL.Vertex3(a.xPos, a.yPos, BASE_Z); GL.Vertex3(a.xPos, a.yPos, rimA);
            }

            // ── Aljlap ────────────────────────────────────────────────────────
            GL.Color3(colorBottom);
            Node sw = getNodeByCoords(0,            0);
            Node se = getNodeByCoords(nodeCols - 1, 0);
            Node ne = getNodeByCoords(nodeCols - 1, nodeRows - 1);
            Node nw = getNodeByCoords(0,            nodeRows - 1);
            GL.Vertex3(sw.xPos, sw.yPos, BASE_Z);
            GL.Vertex3(se.xPos, se.yPos, BASE_Z);
            GL.Vertex3(ne.xPos, ne.yPos, BASE_Z);
            GL.Vertex3(nw.xPos, nw.yPos, BASE_Z);

            });
        }

    }
}

