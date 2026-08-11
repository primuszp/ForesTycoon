using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    partial class Terrain
    {
        internal void DrawTrees(ForestSystem forest)
        {
            DynamicPrimitiveBatch.Draw(PrimitiveType.Quads, () =>
            {
                foreach (Tile tile in visibleTiles)
                    if (TryGetTree(forest, tile, out ForestStand stand, out float x, out float y, out float groundZ, out float surfaceZ))
                        DrawTreeTrunk(x, y, groundZ, surfaceZ, stand);
            });

            DynamicPrimitiveBatch.Draw(PrimitiveType.Triangles, () =>
            {
                foreach (Tile tile in visibleTiles)
                    if (TryGetTree(forest, tile, out ForestStand stand, out float x, out float y, out _, out float surfaceZ))
                        DrawTreeFoliage(x, y, surfaceZ, stand);
            });
        }

        private bool TryGetTree(ForestSystem forest, Tile tile, out ForestStand stand,
            out float x, out float y, out float groundZ, out float surfaceZ)
        {
            x = y = groundZ = surfaceZ = 0f;
            if (!forest.TryGetStand(tile.Id, out stand)) return false;

            x = (tile.W.xPos + tile.S.xPos + tile.E.xPos + tile.N.xPos) * 0.25f;
            y = (tile.W.yPos + tile.S.yPos + tile.E.yPos + tile.N.yPos) * 0.25f;
            groundZ = tile.Low * tileSizeM;
            surfaceZ = Math.Max(Math.Max(tile.W.zPos, tile.S.zPos), Math.Max(tile.E.zPos, tile.N.zPos));
            return true;
        }

        private static void DrawTreeTrunk(float x, float y, float groundZ, float surfaceZ, ForestStand stand)
        {
            float scale = TreeVisualScale(stand);
            float trunkRadius = 0.32f * scale;
            float trunkBot = groundZ - 1.0f;
            float trunkTop = surfaceZ + 0.6f * scale;

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

        private static void DrawTreeFoliage(float x, float y, float surfaceZ, ForestStand stand)
        {
            float scale = TreeVisualScale(stand);
            float trunkTop = surfaceZ + 0.6f * scale;
            float baseRadius = 1.8f * scale;
            float layerHeight = 2.4f * scale;
            (Color light, Color dark) = stand.Species switch
            {
                ForestSpecies.Spruce => (Color.FromArgb(42, 112, 63), Color.FromArgb(22, 70, 42)),
                ForestSpecies.Birch => (Color.FromArgb(91, 151, 57), Color.FromArgb(48, 103, 35)),
                _ => (Color.FromArgb(55, 128, 42), Color.FromArgb(30, 85, 25))
            };

            for (int layer = 0; layer < 3; layer++)
            {
                float baseZ = trunkTop + layer * layerHeight * 0.65f;
                float tipZ = baseZ + layerHeight;
                float radius = baseRadius * (1.0f - layer * 0.22f);

                DrawFoliageFace(light, x, y + radius, x + radius, y, baseZ, tipZ, x, y);
                DrawFoliageFace(dark, x + radius, y, x, y - radius, baseZ, tipZ, x, y);
                DrawFoliageFace(dark, x, y - radius, x - radius, y, baseZ, tipZ, x, y);
                DrawFoliageFace(light, x - radius, y, x, y + radius, baseZ, tipZ, x, y);
            }
        }

        private static void DrawFoliageFace(Color color, float ax, float ay, float bx, float by,
            float baseZ, float tipZ, float centerX, float centerY)
        {
            DynamicPrimitiveBatch.Color3(color);
            DynamicPrimitiveBatch.Vertex3(ax, ay, baseZ);
            DynamicPrimitiveBatch.Vertex3(bx, by, baseZ);
            DynamicPrimitiveBatch.Vertex3(centerX, centerY, tipZ);
        }

        // Newly planted stands must remain readable at normal isometric zoom.
        internal static float TreeVisualScale(ForestStand stand) => 0.52f + stand.Maturity * 0.48f;
    }
}
