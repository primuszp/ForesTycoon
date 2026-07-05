using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    partial class Terrain
    {
        private void DrawTrees()
        {
            for (int u = 1; u < nodeCols - 1; u++)
            {
                for (int v = 1; v < nodeRows - 1; v++)
                {
                    Tile tile = getTileByCoords(u, v);
                    float moisture = tileMoisture[tile.Id];

                    if (ShouldDrawStandingWater(tile)) continue;
                    if (tile.Low <= 1) continue;
                    if (tile.Low >= 5) continue;
                    int tileRiverCorners = CountRiverCorners(tile);
                    if (tileRiverCorners >= 2) continue;
                    if (moisture < 0.35f || moisture > 0.95f) continue;

                    int hash = unchecked(u * 374761393 ^ v * 1073741827);
                    int density = moisture >= 0.7f ? 3 : 5;
                    if ((hash & 0x7FFFFFFF) % density != 0) continue;

                    float cx = (tile.W.xPos + tile.S.xPos + tile.E.xPos + tile.N.xPos) * 0.25f;
                    float cy = (tile.W.yPos + tile.S.yPos + tile.E.yPos + tile.N.yPos) * 0.25f;
                    float groundZ = tile.Low * tileSizeM;
                    float surfaceZ = Math.Max(Math.Max(tile.W.zPos, tile.S.zPos),
                                             Math.Max(tile.E.zPos, tile.N.zPos));

                    DrawTree(cx, cy, groundZ, surfaceZ);
                }
            }
        }

        private static void DrawTree(float x, float y, float groundZ, float surfaceZ)
        {
            float trunkRadius = 0.32f;
            float trunkBot = groundZ - 1.0f;
            float trunkTop = surfaceZ + 0.6f;

            Color trunkLight = Color.FromArgb(115, 72, 32);
            Color trunkDark = Color.FromArgb(80, 50, 20);

            ImmediateRenderer.Draw(PrimitiveType.Quads, () =>
            {
                GL.Color3(trunkLight);
                GL.Vertex3(x - trunkRadius, y + trunkRadius, trunkBot); GL.Vertex3(x + trunkRadius, y + trunkRadius, trunkBot);
                GL.Vertex3(x + trunkRadius, y + trunkRadius, trunkTop); GL.Vertex3(x - trunkRadius, y + trunkRadius, trunkTop);

                GL.Color3(trunkDark);
                GL.Vertex3(x + trunkRadius, y + trunkRadius, trunkBot); GL.Vertex3(x + trunkRadius, y - trunkRadius, trunkBot);
                GL.Vertex3(x + trunkRadius, y - trunkRadius, trunkTop); GL.Vertex3(x + trunkRadius, y + trunkRadius, trunkTop);

                GL.Color3(trunkLight);
                GL.Vertex3(x + trunkRadius, y - trunkRadius, trunkBot); GL.Vertex3(x - trunkRadius, y - trunkRadius, trunkBot);
                GL.Vertex3(x - trunkRadius, y - trunkRadius, trunkTop); GL.Vertex3(x + trunkRadius, y - trunkRadius, trunkTop);

                GL.Color3(trunkDark);
                GL.Vertex3(x - trunkRadius, y - trunkRadius, trunkBot); GL.Vertex3(x - trunkRadius, y + trunkRadius, trunkBot);
                GL.Vertex3(x - trunkRadius, y + trunkRadius, trunkTop); GL.Vertex3(x - trunkRadius, y - trunkRadius, trunkTop);
            });

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

                ImmediateRenderer.Draw(PrimitiveType.Triangles, () =>
                {
                    for (int i = 0; i < 4; i++)
                    {
                        int j = (i + 1) % 4;
                        GL.Color3(i == 0 || i == 3 ? light : dark);
                        GL.Vertex3(px[i], py[i], baseZ);
                        GL.Vertex3(px[j], py[j], baseZ);
                        GL.Vertex3(x, y, tipZ);
                    }
                });
            }
        }
    }
}
