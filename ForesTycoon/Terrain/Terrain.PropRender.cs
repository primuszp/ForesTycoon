using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    partial class Terrain
    {
        internal void DrawTrees()
        {
            DynamicPrimitiveBatch.Draw(PrimitiveType.Quads, () =>
            {
                foreach (Tile tile in visibleTiles)
                    if (TryGetTree(tile, out float x, out float y, out float groundZ, out float surfaceZ))
                        DrawTreeTrunk(x, y, groundZ, surfaceZ);
            });

            DynamicPrimitiveBatch.Draw(PrimitiveType.Triangles, () =>
            {
                foreach (Tile tile in visibleTiles)
                    if (TryGetTree(tile, out float x, out float y, out _, out float surfaceZ))
                        DrawTreeFoliage(x, y, surfaceZ);
            });
        }

        private bool TryGetTree(Tile tile, out float x, out float y, out float groundZ, out float surfaceZ)
        {
            int u = tile.Id / (nodeRows - 1), v = tile.Id % (nodeRows - 1);
            x = y = groundZ = surfaceZ = 0f;
            if (u == 0 || v == 0 || u >= nodeCols - 2 || v >= nodeRows - 2) return false;
            float moisture = tileMoisture[tile.Id];
            if (ShouldDrawStandingWater(tile) || tile.Low <= 1 || tile.Low >= 5 || CountRiverCorners(tile) >= 2)
                return false;
            if (moisture < 0.35f || moisture > 0.95f) return false;

            int hash = unchecked(u * 374761393 ^ v * 1073741827);
            int density = moisture >= 0.7f ? 3 : 5;
            if ((hash & 0x7FFFFFFF) % density != 0) return false;

            x = (tile.W.xPos + tile.S.xPos + tile.E.xPos + tile.N.xPos) * 0.25f;
            y = (tile.W.yPos + tile.S.yPos + tile.E.yPos + tile.N.yPos) * 0.25f;
            groundZ = tile.Low * tileSizeM;
            surfaceZ = Math.Max(Math.Max(tile.W.zPos, tile.S.zPos), Math.Max(tile.E.zPos, tile.N.zPos));
            return true;
        }

        private static void DrawTreeTrunk(float x, float y, float groundZ, float surfaceZ)
        {
            float trunkRadius = 0.32f;
            float trunkBot = groundZ - 1.0f;
            float trunkTop = surfaceZ + 0.6f;

            Color trunkLight = Color.FromArgb(115, 72, 32);
            Color trunkDark = Color.FromArgb(80, 50, 20);

            DynamicPrimitiveBatch.Color3(trunkLight);
            DynamicPrimitiveBatch.Vertex3(x - trunkRadius, y + trunkRadius, trunkBot); DynamicPrimitiveBatch.Vertex3(x + trunkRadius, y + trunkRadius, trunkBot);
            DynamicPrimitiveBatch.Vertex3(x + trunkRadius, y + trunkRadius, trunkTop); DynamicPrimitiveBatch.Vertex3(x - trunkRadius, y + trunkRadius, trunkTop);

            DynamicPrimitiveBatch.Color3(trunkDark);
            DynamicPrimitiveBatch.Vertex3(x + trunkRadius, y + trunkRadius, trunkBot); DynamicPrimitiveBatch.Vertex3(x + trunkRadius, y - trunkRadius, trunkBot);
            DynamicPrimitiveBatch.Vertex3(x + trunkRadius, y - trunkRadius, trunkTop); DynamicPrimitiveBatch.Vertex3(x + trunkRadius, y + trunkRadius, trunkTop);

            DynamicPrimitiveBatch.Color3(trunkLight);
            DynamicPrimitiveBatch.Vertex3(x + trunkRadius, y - trunkRadius, trunkBot); DynamicPrimitiveBatch.Vertex3(x - trunkRadius, y - trunkRadius, trunkBot);
            DynamicPrimitiveBatch.Vertex3(x - trunkRadius, y - trunkRadius, trunkTop); DynamicPrimitiveBatch.Vertex3(x + trunkRadius, y - trunkRadius, trunkTop);

            DynamicPrimitiveBatch.Color3(trunkDark);
            DynamicPrimitiveBatch.Vertex3(x - trunkRadius, y - trunkRadius, trunkBot); DynamicPrimitiveBatch.Vertex3(x - trunkRadius, y + trunkRadius, trunkBot);
            DynamicPrimitiveBatch.Vertex3(x - trunkRadius, y + trunkRadius, trunkTop); DynamicPrimitiveBatch.Vertex3(x - trunkRadius, y - trunkRadius, trunkTop);
        }

        private static void DrawTreeFoliage(float x, float y, float surfaceZ)
        {
            float trunkTop = surfaceZ + 0.6f;
            float baseRadius = 1.8f;
            float layerHeight = 2.4f;
            Color light = Color.FromArgb(55, 128, 42);
            Color dark = Color.FromArgb(30, 85, 25);

            for (int layer = 0; layer < 3; layer++)
            {
                float baseZ = trunkTop + layer * layerHeight * 0.65f;
                float tipZ = baseZ + layerHeight;
                float radius = baseRadius * (1.0f - layer * 0.22f);

                float[] px = { x, x + radius, x, x - radius };
                float[] py = { y + radius, y, y - radius, y };

                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    DynamicPrimitiveBatch.Color3(i == 0 || i == 3 ? light : dark);
                    DynamicPrimitiveBatch.Vertex3(px[i], py[i], baseZ);
                    DynamicPrimitiveBatch.Vertex3(px[j], py[j], baseZ);
                    DynamicPrimitiveBatch.Vertex3(x, y, tipZ);
                }
            }
        }
    }
}
