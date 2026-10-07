using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    partial class Terrain
    {
        private readonly ForestMaterial forestMaterial = new ForestMaterial();
        internal int ForestChunkRebuilds { get; private set; }
        internal long TotalForestChunkRebuilds { get; private set; }

        internal void DrawTrees(ForestSystem forest, RenderContext context, GraphicsSettings graphics = null)
        {
            ForestChunkRebuilds = 0;
            DrawIndividualTrees(forest, context, graphics);
        }

        private static void AppendTreeCrown(List<Vertex> vertices, in TreeInstance tree, ForestLod lod)
        {
            TreeModel model = TreeModel.For(tree.Stand.Species);
            float height = model.CrownHeight * tree.Scale * tree.CrownRise;
            ForestCrownMesh.Append(vertices, tree.Stand.Species,
                new Vector3(tree.X, tree.Y, tree.TrunkTop(model) - height * model.CrownDrop),
                model.CrownRadius * tree.Scale * tree.CrownWidth, height, tree.Yaw, tree.Seed,
                Color.FromArgb(SurfaceSpeciesCode(tree.Stand.Species), Weather(Tinted(model.CrownColor, tree.Tint), tree.Stand.Health)), lod,
                ForestTreeAppearance.Stage(tree.Stand.Species, tree.Stand.AgeYears));
        }

        private void DrawForestFloor(TreeInstance tree)
        {
            float radius = TreeModel.For(tree.Stand.Species).CrownRadius * tree.Scale * 1.35f;
            const int sides = 12;
            for (int side = 0; side < sides; side++)
            {
                Point(tree.X, tree.Y, 65);
                float a = MathF.Tau * side / sides, b = MathF.Tau * (side + 1) / sides;
                Point(tree.X + MathF.Cos(a) * radius, tree.Y + MathF.Sin(a) * radius, 0);
                Point(tree.X + MathF.Cos(b) * radius, tree.Y + MathF.Sin(b) * radius, 0);
            }
            void Point(float x, float y, int alpha)
            {
                int u = Math.Clamp((int)MathF.Floor((x + offsetX) / tileSizeH), 0, settings.TileColumns - 1);
                int v = Math.Clamp((int)MathF.Floor((y + offsetY) / tileSizeV), 0, settings.TileRows - 1);
                Tile tile = getTileByCoords(u, v);
                SurfacePoint(tile, Math.Clamp((x - tile.W.xPos) / tileSizeH, 0, 1),
                    Math.Clamp((y - tile.W.yPos) / tileSizeV, 0, 1), out float px, out float py, out float pz);
                DynamicPrimitiveBatch.Color4(Color.FromArgb(alpha, 31, 48, 18));
                DynamicPrimitiveBatch.Vertex3(px, py, pz + 0.015f);
            }
        }

        private void DisposeForestGeometry()
        {
            DisposeIndividualForest();
            DisposePlantations();
            forestMaterial.Dispose();
        }
    }
}
