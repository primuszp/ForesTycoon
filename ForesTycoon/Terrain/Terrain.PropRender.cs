using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>
    /// Species-specific tree models. Every crown is a surface of revolution swept from a
    /// short per-species outline, so the silhouettes stay round and readable while the whole
    /// forest still fits into the two batched draw calls the prop pass is allowed.
    /// </summary>
    partial class Terrain
    {
        // Same sun the terrain is lit by, so trees and ground agree on where the light is.
        private static readonly Vector3 TreeLight = Vector3.Normalize(new Vector3(0.45f, 0.65f, 1.05f));
        private const float TreeAmbient = 0.42f;

        /// <summary>Resolved placement and dimensions of one drawn tree.</summary>
        private readonly struct TreeInstance
        {
            public readonly ForestStand Stand;
            public readonly float X;
            public readonly float Y;
            public readonly float BaseZ;
            public readonly float Scale;
            // Deterministic yaw so neighbouring crowns do not all face the same way.
            public readonly float Yaw;
            // Per-tree colour drift, so a closed stand does not look like one flat green blob.
            public readonly float Tint;

            public TreeInstance(ForestStand stand, float x, float y, float baseZ, float scale, float yaw, float tint)
            {
                Stand = stand;
                X = x;
                Y = y;
                BaseZ = baseZ;
                Scale = scale;
                Yaw = yaw;
                Tint = tint;
            }

            /// <summary>Saplings are tiny on screen and do not deserve a full-detail lathe.</summary>
            public int Sides => Scale >= 0.72f ? 8 : 5;

            public float TrunkTop(in TreeModel model) => BaseZ + model.TrunkHeight * Scale;
        }

        /// <summary>Species silhouette in tile-space metres, before the per-tree scale is applied.</summary>
        internal readonly record struct TreeModel(
            float TrunkHeight,
            float TrunkRadius,
            float CrownRadius,
            float CrownHeight,
            // How far the crown is pulled down over the stem, as a fraction of its height.
            // Without it every tree reads as a lollipop on a stick.
            float CrownDrop,
            float[] CrownOutline,
            Color TrunkColor,
            Color CrownColor)
        {
            public float TotalHeight => TrunkHeight + CrownHeight;

            /// <summary>Share of the tree's height that is bare stem, before the crown starts.</summary>
            public float BareStemFraction => TotalHeight <= 0f ? 0f : TrunkHeight / TotalHeight;

            public static TreeModel For(ForestSpecies species) => species switch
            {
                // Long clean stem under a wide, flat parasol of needles.
                ForestSpecies.Pine => new TreeModel(
                    3.8f, 0.38f, 2.70f, 2.5f, 0.12f, PineOutline,
                    Color.FromArgb(162, 98, 50), Color.FromArgb(96, 132, 76)),

                // Narrow tiered spire that starts branching just above the ground.
                ForestSpecies.Spruce => new TreeModel(
                    0.7f, 0.28f, 1.65f, 8.4f, 0.02f, SpruceOutline,
                    Color.FromArgb(74, 54, 38), Color.FromArgb(32, 82, 58)),

                // Slender white stem with a small, airy, drooping crown.
                ForestSpecies.Birch => new TreeModel(
                    2.3f, 0.22f, 1.70f, 4.1f, 0.34f, BirchOutline,
                    Color.FromArgb(208, 208, 196), Color.FromArgb(162, 200, 92)),

                // Short heavy bole under a broad, spreading dome.
                ForestSpecies.Oak => new TreeModel(
                    1.5f, 0.55f, 3.25f, 3.3f, 0.34f, OakOutline,
                    Color.FromArgb(110, 84, 54), Color.FromArgb(60, 104, 42)),

                // Smooth grey column under a tall egg-shaped crown.
                ForestSpecies.Beech => new TreeModel(
                    2.3f, 0.40f, 2.20f, 4.8f, 0.30f, BeechOutline,
                    Color.FromArgb(146, 134, 116), Color.FromArgb(118, 158, 66)),

                _ => new TreeModel(
                    1.6f, 0.30f, 1.60f, 3.6f, 0.30f, BirchOutline,
                    Color.FromArgb(115, 72, 32), Color.FromArgb(70, 128, 52))
            };
        }

        // Crown outlines as (heightFraction, radiusFraction) pairs, bottom to top. Each is
        // swept around the trunk axis; the trailing zero radius closes the crown, and the
        // short segment before it rounds the cap off instead of leaving a spike.
        private static readonly float[] SpruceOutline =
        {
            0.00f, 0.62f, 0.16f, 1.00f, 0.34f, 0.66f, 0.52f, 0.86f,
            0.70f, 0.48f, 0.86f, 0.52f, 1.00f, 0.00f
        };

        private static readonly float[] PineOutline =
        {
            0.00f, 0.00f, 0.08f, 0.52f, 0.28f, 0.90f, 0.55f, 1.00f, 0.84f, 0.78f, 1.00f, 0.00f
        };

        private static readonly float[] BirchOutline =
        {
            0.00f, 0.00f, 0.08f, 0.50f, 0.24f, 0.82f, 0.50f, 1.00f, 0.78f, 0.86f, 0.93f, 0.54f, 1.00f, 0.00f
        };

        private static readonly float[] OakOutline =
        {
            0.00f, 0.00f, 0.07f, 0.66f, 0.22f, 0.93f, 0.46f, 1.00f, 0.74f, 0.88f, 0.92f, 0.58f, 1.00f, 0.00f
        };

        private static readonly float[] BeechOutline =
        {
            0.00f, 0.00f, 0.08f, 0.44f, 0.24f, 0.74f, 0.50f, 0.94f, 0.74f, 1.00f, 0.92f, 0.62f, 1.00f, 0.00f
        };

        internal void DrawTrees(ForestSystem forest)
        {
            DynamicPrimitiveBatch.Draw(PrimitiveType.Quads, () =>
            {
                foreach (Tile tile in visibleTiles)
                    if (TryGetTree(forest, tile, out TreeInstance tree))
                        DrawTreeTrunk(tree);
            });

            DynamicPrimitiveBatch.Draw(PrimitiveType.Triangles, () =>
            {
                foreach (Tile tile in visibleTiles)
                    if (TryGetTree(forest, tile, out TreeInstance tree))
                        DrawTreeCrown(tree);
            });
        }

        private bool TryGetTree(ForestSystem forest, Tile tile, out TreeInstance tree)
        {
            tree = default;
            if (!forest.TryGetStand(tile.Id, out ForestStand stand)) return false;

            float centerX = (tile.W.xPos + tile.S.xPos + tile.E.xPos + tile.N.xPos) * 0.25f;
            float centerY = (tile.W.yPos + tile.S.yPos + tile.E.yPos + tile.N.yPos) * 0.25f;
            // Sit the trunk on the tile centre rather than its highest corner, otherwise
            // trees on a slope float above the ground they are supposed to grow out of.
            float centerZ = (tile.W.zPos + tile.S.zPos + tile.E.zPos + tile.N.zPos) * 0.25f;

            // Independent draws: reusing shifted slices of one hash would leave the later
            // values with almost no entropy, which flattens the variation to nothing.
            float jitterX = (UnitFloat(TreeHash(tile.Id, 0)) - 0.5f) * 0.45f * tileSizeM;
            float jitterY = (UnitFloat(TreeHash(tile.Id, 1)) - 0.5f) * 0.45f * tileSizeM;
            float sizeVariation = 0.86f + UnitFloat(TreeHash(tile.Id, 2)) * 0.28f;
            float yaw = UnitFloat(TreeHash(tile.Id, 3)) * MathF.Tau;
            float tint = UnitFloat(TreeHash(tile.Id, 4)) - 0.5f;

            tree = new TreeInstance(
                stand,
                centerX + jitterX,
                centerY + jitterY,
                centerZ,
                TreeVisualScale(stand) * sizeVariation,
                yaw,
                tint);
            return true;
        }

        private static void DrawTreeTrunk(in TreeInstance tree)
        {
            TreeModel model = TreeModel.For(tree.Stand.Species);
            Color trunk = Weather(Tinted(model.TrunkColor, tree.Tint * 0.5f), tree.Stand.Health);
            int sides = tree.Sides;
            // Sink the base below the surface so the trunk never shows a gap on uneven tiles.
            float bottom = tree.BaseZ - 1.0f;
            float top = tree.TrunkTop(model);
            // A slight flare at the base reads as a root collar and stops the stem looking like a pipe.
            float bottomRadius = model.TrunkRadius * tree.Scale * 1.35f;
            float topRadius = model.TrunkRadius * tree.Scale * 0.78f;

            for (int side = 0; side < sides; side++)
            {
                float a0 = tree.Yaw + MathF.Tau * side / sides;
                float a1 = tree.Yaw + MathF.Tau * (side + 1) / sides;
                Color shaded = Shade(trunk, FaceShade(a0, a1, top - bottom, topRadius - bottomRadius));

                DynamicPrimitiveBatch.Color3(shaded);
                Emit(tree, a0, bottomRadius, bottom);
                Emit(tree, a1, bottomRadius, bottom);
                Emit(tree, a1, topRadius, top);
                Emit(tree, a0, topRadius, top);
            }
        }

        private static void DrawTreeCrown(in TreeInstance tree)
        {
            TreeModel model = TreeModel.For(tree.Stand.Species);
            Color crown = Weather(Tinted(model.CrownColor, tree.Tint), tree.Stand.Health);
            float height0 = model.CrownHeight * tree.Scale;
            float crownBase = tree.TrunkTop(model) - height0 * model.CrownDrop;
            float radius = model.CrownRadius * tree.Scale;
            float height = height0;
            float[] outline = model.CrownOutline;
            int sides = tree.Sides;

            for (int point = 0; point + 3 < outline.Length; point += 2)
            {
                float z0 = crownBase + outline[point] * height;
                float r0 = outline[point + 1] * radius;
                float z1 = crownBase + outline[point + 2] * height;
                float r1 = outline[point + 3] * radius;

                // Foliage catches more light towards the top of the crown.
                float gradient = 0.74f + 0.30f * outline[point + 2];

                for (int side = 0; side < sides; side++)
                {
                    float a0 = tree.Yaw + MathF.Tau * side / sides;
                    float a1 = tree.Yaw + MathF.Tau * (side + 1) / sides;
                    Color shaded = Shade(crown, FaceShade(a0, a1, z1 - z0, r1 - r0) * gradient);
                    DynamicPrimitiveBatch.Color3(shaded);

                    if (r0 <= 0.0001f)
                    {
                        // Collapsed lower ring: the band closes into a single triangle.
                        Emit(tree, a0, 0f, z0);
                        Emit(tree, a0, r1, z1);
                        Emit(tree, a1, r1, z1);
                        continue;
                    }

                    if (r1 <= 0.0001f)
                    {
                        Emit(tree, a0, r0, z0);
                        Emit(tree, a1, r0, z0);
                        Emit(tree, a0, 0f, z1);
                        continue;
                    }

                    Emit(tree, a0, r0, z0);
                    Emit(tree, a1, r0, z0);
                    Emit(tree, a1, r1, z1);

                    Emit(tree, a0, r0, z0);
                    Emit(tree, a1, r1, z1);
                    Emit(tree, a0, r1, z1);
                }
            }
        }

        private static void Emit(in TreeInstance tree, float angle, float radius, float z) =>
            DynamicPrimitiveBatch.Vertex3(
                tree.X + MathF.Cos(angle) * radius,
                tree.Y + MathF.Sin(angle) * radius,
                z);

        /// <summary>
        /// Diffuse term for one band of a surface of revolution. The outward normal follows
        /// from the outline's slope, which is what makes a lathed crown read as round instead
        /// of as a stack of flat rings.
        /// </summary>
        private static float FaceShade(float angle0, float angle1, float deltaZ, float deltaRadius)
        {
            float mid = (angle0 + angle1) * 0.5f;
            float length = MathF.Sqrt(deltaZ * deltaZ + deltaRadius * deltaRadius);
            float radial = length > 0.0001f ? deltaZ / length : 1f;
            float vertical = length > 0.0001f ? -deltaRadius / length : 0f;

            Vector3 normal = new Vector3(MathF.Cos(mid) * radial, MathF.Sin(mid) * radial, vertical);
            float diffuse = Math.Max(0f, Vector3.Dot(normal, TreeLight));
            return TreeAmbient + (1f - TreeAmbient) * diffuse;
        }

        /// <summary>Fades a healthy colour toward dry autumn brown as the stand declines.</summary>
        private static Color Weather(Color color, float health)
        {
            float t = Math.Clamp(1f - health, 0f, 1f) * 0.55f;
            return Color.FromArgb(
                Mix(color.R, 146, t),
                Mix(color.G, 118, t),
                Mix(color.B, 58, t));
        }

        /// <summary>Nudges a species colour per tree so a closed stand keeps some variety.</summary>
        private static Color Tinted(Color color, float amount) => Color.FromArgb(
            Channel(color.R * (1f + amount * 0.14f)),
            Channel(color.G * (1f + amount * 0.10f)),
            Channel(color.B * (1f - amount * 0.12f)));

        private static Color Shade(Color color, float factor) => Color.FromArgb(
            Channel(color.R * factor), Channel(color.G * factor), Channel(color.B * factor));

        private static int Channel(float value) => Math.Clamp((int)value, 0, 255);

        private static int Mix(int from, int to, float t) =>
            Math.Clamp((int)(from + (to - from) * t), 0, 255);

        private static float UnitFloat(uint value) => (value >> 8) * (1f / 16777216f);

        private static uint TreeHash(int tileId, uint salt)
        {
            uint value = unchecked((uint)tileId * 0x9E3779B9u) ^ unchecked(salt * 0x85EBCA6Bu);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            return value ^ (value >> 16);
        }

        // Newly planted stands must remain readable at normal isometric zoom.
        internal static float TreeVisualScale(ForestStand stand) => 0.52f + stand.Maturity * 0.48f;
    }
}
