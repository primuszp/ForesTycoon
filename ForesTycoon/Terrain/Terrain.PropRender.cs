using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>
    /// Species-specific tree models. A tree is a tapered bole, a few spreading limbs and a
    /// crown swept from a per-species outline, all lit per face from the terrain's own sun.
    /// Wood goes into the quad batch and foliage into the triangle batch, so the whole
    /// forest still costs the two draw calls the prop pass is allowed.
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
            public readonly int TileId;
            public readonly float X;
            public readonly float Y;
            public readonly float BaseZ;
            public readonly float Scale;
            // Deterministic yaw so neighbouring crowns do not all face the same way.
            public readonly float Yaw;
            // Per-tree colour drift, so a closed stand does not look like one flat green blob.
            public readonly float Tint;

            public TreeInstance(ForestStand stand, int tileId, float x, float y, float baseZ,
                float scale, float yaw, float tint)
            {
                Stand = stand;
                TileId = tileId;
                X = x;
                Y = y;
                BaseZ = baseZ;
                Scale = scale;
                Yaw = yaw;
                Tint = tint;
            }

            /// <summary>Saplings are tiny on screen and do not deserve a full-detail lathe.</summary>
            public int Sides => Scale >= 0.72f ? 8 : 5;

            /// <summary>Limbs and side lobes only pay for themselves once a tree is big enough to see.</summary>
            public bool IsFullDetail => Scale >= 0.80f;

            public float TrunkTop(in TreeModel model) => BaseZ + model.TrunkHeight * Scale;
        }

        /// <summary>Species silhouette in tile-space metres, before the per-tree scale is applied.</summary>
        internal readonly record struct TreeModel(
            float TrunkHeight,
            float TrunkRadius,
            float CrownRadius,
            float CrownHeight,
            // How far the crown is pulled down over the bole, as a fraction of its height.
            float CrownDrop,
            // Limbs spreading from the bole into the underside of the crown.
            int BranchCount,
            // Secondary crown lobes; a single blob never reads as a broadleaf canopy.
            int LobeCount,
            float[] CrownOutline,
            Color TrunkColor,
            Color CrownColor)
        {
            public float TotalHeight => TrunkHeight + CrownHeight;

            /// <summary>Share of the tree's height that is bare stem, before the crown starts.</summary>
            public float BareStemFraction => TotalHeight <= 0f ? 0f : TrunkHeight / TotalHeight;

            public static TreeModel For(ForestSpecies species) => species switch
            {
                // Narrow tiered spire branching almost from the ground; no bare bole to show.
                ForestSpecies.Spruce => new TreeModel(
                    0.7f, 0.28f, 1.65f, 8.4f, 0.02f, 0, 0, SpruceOutline,
                    Color.FromArgb(74, 54, 38), Color.FromArgb(32, 82, 58)),

                // Slender white stem, light airy crown carried on a few fine limbs.
                ForestSpecies.Birch => new TreeModel(
                    2.8f, 0.20f, 1.70f, 4.1f, 0.10f, 3, 2, BirchOutline,
                    Color.FromArgb(208, 208, 196), Color.FromArgb(162, 200, 92)),

                // Short heavy bole forking into thick limbs under a broad spreading dome.
                ForestSpecies.Oak => new TreeModel(
                    2.6f, 0.52f, 3.10f, 3.3f, 0.08f, 5, 3, OakOutline,
                    Color.FromArgb(110, 84, 54), Color.FromArgb(60, 104, 42)),

                // Tall smooth grey column under a high egg-shaped crown.
                ForestSpecies.Beech => new TreeModel(
                    3.2f, 0.38f, 2.20f, 4.8f, 0.10f, 4, 2, BeechOutline,
                    Color.FromArgb(146, 134, 116), Color.FromArgb(118, 158, 66)),

                _ => new TreeModel(
                    2.0f, 0.30f, 1.60f, 3.6f, 0.25f, 3, 2, BirchOutline,
                    Color.FromArgb(115, 72, 32), Color.FromArgb(70, 128, 52))
            };
        }

        // Crown outlines as (heightFraction, radiusFraction) pairs, bottom to top. Each is
        // swept around the crown axis; the leading and trailing zero radii close the shape,
        // and the short segment before the tip rounds the cap off instead of leaving a spike.
        private static readonly float[] SpruceOutline =
        {
            0.00f, 0.62f, 0.16f, 1.00f, 0.34f, 0.66f, 0.52f, 0.86f,
            0.70f, 0.48f, 0.86f, 0.52f, 1.00f, 0.00f
        };

        // Side lobes are half-hidden inside the main crown, so they get a cheap three-band
        // blob rather than a second full sweep of the species outline.
        private static readonly float[] LobeOutline =
        {
            0.00f, 0.00f, 0.18f, 0.72f, 0.55f, 1.00f, 1.00f, 0.00f
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
                        DrawTreeWood(tree);
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
                tile.Id,
                centerX + jitterX,
                centerY + jitterY,
                centerZ,
                TreeVisualScale(stand) * sizeVariation,
                yaw,
                tint);
            return true;
        }

        // ── Wood ────────────────────────────────────────────────────────────

        private static void DrawTreeWood(in TreeInstance tree)
        {
            TreeModel model = TreeModel.For(tree.Stand.Species);
            Color wood = Weather(Tinted(model.TrunkColor, tree.Tint * 0.5f), tree.Stand.Health);
            int sides = tree.Sides >= 8 ? 6 : 4;

            float top = tree.TrunkTop(model);
            // Sink the base below the surface so the stem never shows a gap on uneven tiles.
            Vector3 boleBottom = new Vector3(tree.X, tree.Y, tree.BaseZ - 1.0f);
            Vector3 boleTop = new Vector3(tree.X, tree.Y, top);
            // A flare at the base reads as a root collar and stops the stem looking like a pipe.
            float bottomRadius = model.TrunkRadius * tree.Scale * 1.40f;
            float topRadius = model.TrunkRadius * tree.Scale * 0.72f;

            DrawLimb(boleBottom, boleTop, bottomRadius, topRadius, wood, sides, tree.Yaw);

            if (model.BranchCount == 0 || !tree.IsFullDetail) return;
            DrawBranches(tree, model, wood, top, topRadius);
        }

        /// <summary>
        /// Limbs leave the bole below the crown and climb into its lower half, so the canopy
        /// looks carried by the tree instead of balanced on top of a pole.
        /// </summary>
        private static void DrawBranches(in TreeInstance tree, in TreeModel model, Color wood,
            float boleTopZ, float boleTopRadius)
        {
            float crownHeight = model.CrownHeight * tree.Scale;
            float crownBase = boleTopZ - crownHeight * model.CrownDrop;
            float crownRadius = model.CrownRadius * tree.Scale;
            // Fork below the crown so the limbs are visible before they disappear into foliage.
            float forkZ = crownBase - (crownBase - tree.BaseZ) * 0.55f;

            for (int branch = 0; branch < model.BranchCount; branch++)
            {
                int seed = tree.TileId * 31 + branch;
                float angle = tree.Yaw + MathF.Tau * branch / model.BranchCount
                    + (UnitFloat(TreeHash(seed, 11u)) - 0.5f) * 0.7f;
                // Tips stop just inside the canopy edge, so limbs show through the crown
                // without leaving bare sticks poking into open air.
                float reach = crownRadius * (0.66f + UnitFloat(TreeHash(seed, 12u)) * 0.20f);
                float rise = crownHeight * (0.14f + UnitFloat(TreeHash(seed, 13u)) * 0.20f);

                Vector3 from = new Vector3(tree.X, tree.Y, forkZ);
                Vector3 to = new Vector3(
                    tree.X + MathF.Cos(angle) * reach,
                    tree.Y + MathF.Sin(angle) * reach,
                    crownBase + rise);

                DrawLimb(from, to, boleTopRadius * 0.78f, boleTopRadius * 0.30f, wood, 4, angle);
            }
        }

        /// <summary>Tapered prism between two points, used for both the bole and its limbs.</summary>
        private static void DrawLimb(Vector3 from, Vector3 to, float radiusFrom, float radiusTo,
            Color color, int sides, float roll)
        {
            Vector3 axis = to - from;
            float length = axis.Length;
            if (length < 0.0001f) return;
            axis /= length;

            // Any reference that is not parallel to the axis gives a usable cross-section basis.
            Vector3 reference = MathF.Abs(axis.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ;
            Vector3 u = Vector3.Normalize(Vector3.Cross(reference, axis));
            Vector3 v = Vector3.Cross(axis, u);

            for (int side = 0; side < sides; side++)
            {
                float a0 = roll + MathF.Tau * side / sides;
                float a1 = roll + MathF.Tau * (side + 1) / sides;
                Vector3 d0 = u * MathF.Cos(a0) + v * MathF.Sin(a0);
                Vector3 d1 = u * MathF.Cos(a1) + v * MathF.Sin(a1);

                Vector3 normal = d0 + d1;
                if (normal.LengthSquared > 0.0001f) normal = Vector3.Normalize(normal);
                DynamicPrimitiveBatch.Color3(Shade(color, Diffuse(normal)));

                DynamicPrimitiveBatch.Vertex3(from + d0 * radiusFrom);
                DynamicPrimitiveBatch.Vertex3(from + d1 * radiusFrom);
                DynamicPrimitiveBatch.Vertex3(to + d1 * radiusTo);
                DynamicPrimitiveBatch.Vertex3(to + d0 * radiusTo);
            }
        }

        // ── Foliage ─────────────────────────────────────────────────────────

        private static void DrawTreeCrown(in TreeInstance tree)
        {
            TreeModel model = TreeModel.For(tree.Stand.Species);
            Color crown = Weather(Tinted(model.CrownColor, tree.Tint), tree.Stand.Health);
            float height = model.CrownHeight * tree.Scale;
            float radius = model.CrownRadius * tree.Scale;
            float crownBase = tree.TrunkTop(model) - height * model.CrownDrop;

            DrawCrownLobe(tree, crown, tree.X, tree.Y, crownBase, radius, height,
                model.CrownOutline, tree.Sides);

            if (model.LobeCount == 0 || !tree.IsFullDetail) return;

            // Smaller lobes pushed out around the main mass break the single-blob silhouette.
            for (int lobe = 0; lobe < model.LobeCount; lobe++)
            {
                int seed = tree.TileId * 17 + lobe;
                float angle = tree.Yaw + MathF.Tau * lobe / model.LobeCount
                    + (UnitFloat(TreeHash(seed, 21u)) - 0.5f) * 0.8f;
                float offset = radius * (0.40f + UnitFloat(TreeHash(seed, 22u)) * 0.20f);
                float lobeRadius = radius * (0.48f + UnitFloat(TreeHash(seed, 23u)) * 0.16f);
                float lobeHeight = height * (0.44f + UnitFloat(TreeHash(seed, 24u)) * 0.18f);
                float lobeBase = crownBase + height * (0.12f + UnitFloat(TreeHash(seed, 25u)) * 0.22f);

                DrawCrownLobe(tree,
                    Shade(crown, 0.92f + UnitFloat(TreeHash(seed, 26u)) * 0.16f),
                    tree.X + MathF.Cos(angle) * offset,
                    tree.Y + MathF.Sin(angle) * offset,
                    lobeBase, lobeRadius, lobeHeight, LobeOutline, 5);
            }
        }

        /// <summary>Sweeps one crown outline around a vertical axis.</summary>
        private static void DrawCrownLobe(in TreeInstance tree, Color color,
            float centerX, float centerY, float baseZ, float radius, float height,
            float[] outline, int sides)
        {
            for (int point = 0; point + 3 < outline.Length; point += 2)
            {
                float z0 = baseZ + outline[point] * height;
                float r0 = outline[point + 1] * radius;
                float z1 = baseZ + outline[point + 2] * height;
                float r1 = outline[point + 3] * radius;

                // Foliage catches more light towards the top of the crown.
                float gradient = 0.74f + 0.30f * outline[point + 2];

                for (int side = 0; side < sides; side++)
                {
                    float a0 = tree.Yaw + MathF.Tau * side / sides;
                    float a1 = tree.Yaw + MathF.Tau * (side + 1) / sides;
                    DynamicPrimitiveBatch.Color3(Shade(color, BandShade(a0, a1, z1 - z0, r1 - r0) * gradient));

                    if (r0 <= 0.0001f)
                    {
                        // Collapsed lower ring: the band closes into a single triangle.
                        Ring(centerX, centerY, a0, 0f, z0);
                        Ring(centerX, centerY, a0, r1, z1);
                        Ring(centerX, centerY, a1, r1, z1);
                        continue;
                    }

                    if (r1 <= 0.0001f)
                    {
                        Ring(centerX, centerY, a0, r0, z0);
                        Ring(centerX, centerY, a1, r0, z0);
                        Ring(centerX, centerY, a0, 0f, z1);
                        continue;
                    }

                    Ring(centerX, centerY, a0, r0, z0);
                    Ring(centerX, centerY, a1, r0, z0);
                    Ring(centerX, centerY, a1, r1, z1);

                    Ring(centerX, centerY, a0, r0, z0);
                    Ring(centerX, centerY, a1, r1, z1);
                    Ring(centerX, centerY, a0, r1, z1);
                }
            }
        }

        private static void Ring(float centerX, float centerY, float angle, float radius, float z) =>
            DynamicPrimitiveBatch.Vertex3(
                centerX + MathF.Cos(angle) * radius,
                centerY + MathF.Sin(angle) * radius,
                z);

        // ── Shading ─────────────────────────────────────────────────────────

        /// <summary>
        /// Diffuse term for one band of a surface of revolution. The outward normal follows
        /// from the outline's slope, which is what makes a lathed crown read as round instead
        /// of as a stack of flat rings.
        /// </summary>
        private static float BandShade(float angle0, float angle1, float deltaZ, float deltaRadius)
        {
            float mid = (angle0 + angle1) * 0.5f;
            float length = MathF.Sqrt(deltaZ * deltaZ + deltaRadius * deltaRadius);
            float radial = length > 0.0001f ? deltaZ / length : 1f;
            float vertical = length > 0.0001f ? -deltaRadius / length : 0f;

            return Diffuse(new Vector3(MathF.Cos(mid) * radial, MathF.Sin(mid) * radial, vertical));
        }

        private static float Diffuse(Vector3 normal) =>
            TreeAmbient + (1f - TreeAmbient) * Math.Max(0f, Vector3.Dot(normal, TreeLight));

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
