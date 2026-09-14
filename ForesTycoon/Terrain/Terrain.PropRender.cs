using System;
using System.Drawing;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>
    /// Species-specific tree models. A tree is a tapered bole, a few spreading limbs and a
    /// crown swept from a per-species outline, all lit per face from the terrain's own sun.
    /// Wood and foliage are captured into retained triangle buffers per terrain chunk;
    /// only changed chunks regenerate geometry. ForestCrownMesh supplies closed crowns.
    /// </summary>
    partial class Terrain
    {
        // Same sun the terrain is lit by, so trees and ground agree on where the light is.
        private static readonly Vector3 TreeLight = Vector3.Normalize(new Vector3(0.45f, 0.65f, 1.05f));
        private const float TreeAmbient = 0.42f;

        /// <summary>Resolved placement and dimensions of one drawn stem.</summary>
        private readonly struct TreeInstance
        {
            public readonly ForestStand Stand;
            // Per-stem seed; stems in one tile must not share their branch and lobe layout.
            public readonly int Seed;
            public readonly float X;
            public readonly float Y;
            public readonly float BaseZ;
            public readonly float Scale;
            // Deterministic yaw so neighbouring crowns do not all face the same way.
            public readonly float Yaw;
            // Per-stem colour drift, so a closed stand does not look like one flat green blob.
            public readonly float Tint;
            // Crowded stems are drawn up towards the light: narrower and taller than an
            // open-grown tree of the same species. Both are multipliers on the model crown.
            public readonly float CrownWidth;
            public readonly float CrownRise;
            // Detail is decided once, from the stem's real size, not from its scale alone.
            public readonly byte Detail;

            public TreeInstance(ForestStand stand, int seed, float x, float y, float baseZ,
                float scale, float yaw, float tint, float crownWidth, float crownRise, byte detail)
            {
                Stand = stand;
                Seed = seed;
                X = x;
                Y = y;
                BaseZ = baseZ;
                Scale = scale;
                Yaw = yaw;
                Tint = tint;
                CrownWidth = crownWidth;
                CrownRise = crownRise;
                Detail = detail;
            }

            /// <summary>Stems that cover few pixels do not deserve a full-detail lathe.</summary>
            public int Sides => Detail >= 2 ? 8 : Detail == 1 ? 6 : 5;

            /// <summary>Limbs and side lobes only pay for themselves on the larger stems.</summary>
            public bool IsFullDetail => Detail >= 2;

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
            // Stems a fully stocked, mature tile of this species carries. A wide oak crown
            // closes the canopy with far fewer stems than a narrow spruce spire.
            int MatureStems,
            // Whorled conifers are built tier by tier; broadleaves are one swept outline.
            TreeCrownShape Shape,
            float[] CrownOutline,
            Color TrunkColor,
            Color CrownColor)
        {
            public float TotalHeight => TrunkHeight + CrownHeight;

            /// <summary>Share of the tree's height that is bare stem, before the crown starts.</summary>
            public float BareStemFraction => TotalHeight <= 0f ? 0f : TrunkHeight / TotalHeight;

            public static TreeModel For(ForestSpecies species) => species switch
            {
                // Narrow whorled spire branching almost from the ground; barely any bare bole.
                ForestSpecies.Spruce => new TreeModel(
                    0.55f, 0.26f, 1.77f, 8.8f, 0.02f, 0, 0, 14, TreeCrownShape.Spire, SpruceOutline,
                    Color.FromArgb(74, 54, 38), Color.FromArgb(48, 79, 43)),

                // Slender white stem, light airy crown carried on a few fine limbs.
                ForestSpecies.Birch => new TreeModel(
                    2.2f, 0.16f, 1.75f, 5.3f, 0.05f, 3, 2, 12, TreeCrownShape.Rounded, BirchOutline,
                    Color.FromArgb(208, 208, 196), Color.FromArgb(119, 150, 57)),

                // Short heavy bole forking into thick limbs under a broad spreading dome.
                ForestSpecies.Oak => new TreeModel(
                    1.45f, 0.38f, 2.35f, 4.65f, 0.07f, 5, 3, 4, TreeCrownShape.Broad, OakOutline,
                    Color.FromArgb(110, 84, 54), Color.FromArgb(78, 110, 46)),

                // Tall smooth grey column under a high egg-shaped crown.
                ForestSpecies.Beech => new TreeModel(
                    1.80f, 0.25f, 2.10f, 5.50f, 0.06f, 4, 2, 7, TreeCrownShape.Ovoid, BeechOutline,
                    Color.FromArgb(146, 134, 116), Color.FromArgb(92, 126, 50)),

                _ => new TreeModel(
                    2.0f, 0.30f, 1.70f, 3.6f, 0.10f, 3, 2, 9, TreeCrownShape.Rounded, BirchOutline,
                    Color.FromArgb(115, 72, 32), Color.FromArgb(70, 128, 52))
            };
        }

        // Crown outlines as (heightFraction, radiusFraction) pairs, bottom to top. Each is
        // swept around the crown axis; the leading and trailing zero radii close the shape,
        // and the short segment before the tip rounds the cap off instead of leaving a spike.
        // Only used for the suppressed conifers that are too small to earn the whorl build.
        private static readonly float[] SpruceOutline =
        {
            0.00f, 0.92f, 0.30f, 0.72f, 0.62f, 0.46f, 1.00f, 0.00f
        };



        // Side lobes are half-hidden inside the main crown, so they get a cheap three-band
        // blob rather than a second full sweep of the species outline.
        private static readonly float[] LobeOutline =
        {
            0.00f, 0.00f, 0.18f, 0.72f, 0.55f, 1.00f, 1.00f, 0.00f
        };

        // Airy, near-spherical crown, widest just above the middle.
        private static readonly float[] BirchOutline =
        {
            0.00f, 0.00f, 0.04f, 0.58f, 0.16f, 0.82f, 0.36f, 0.96f,
            0.56f, 1.00f, 0.78f, 0.88f, 0.93f, 0.56f, 1.00f, 0.00f
        };

        // Flattened spreading dome: widest low down, and cut off well before it becomes a ball.
        private static readonly float[] OakOutline =
        {
            0.00f, 0.00f, 0.03f, 0.62f, 0.14f, 0.86f, 0.34f, 0.98f,
            0.55f, 1.00f, 0.76f, 0.90f, 0.92f, 0.60f, 1.00f, 0.00f
        };

        // Egg standing on its narrow end: the classic beech crown.
        private static readonly float[] BeechOutline =
        {
            0.00f, 0.00f, 0.06f, 0.46f, 0.20f, 0.72f, 0.44f, 0.90f,
            0.66f, 1.00f, 0.86f, 0.82f, 0.95f, 0.52f, 1.00f, 0.00f
        };

        /// <summary>
        /// Global size multiplier for drawn stems. Trees are read against the width of a road,
        /// and at the geometric size the lattice alone produces they came out too small next
        /// to one; the reference look wants a mature crown to be a sizeable share of a tile.
        /// </summary>
        private const float StemSizeBoost = 1.45f;

        /// <summary>Upper bound on the stems one tile can carry; sizes the stack buffer.</summary>
        private const int MaximumStemsPerTile = 16;
        private readonly List<TreeInstance> chunkStems = new List<TreeInstance>();
        private readonly List<(Tile Tile, int Offset, int Count)> chunkStands = new List<(Tile, int, int)>();

        /// <summary>
        /// Expands one tile's stand into the stems that are actually drawn. The tile is split
        /// into a fixed lattice and every cell is given a deterministic rank; a cell carries a
        /// stem while its rank falls under the tile's stocking. Because the ranks never change,
        /// a growing stand keeps the stems it already has and fills in new ones between them
        /// instead of reshuffling the whole tile every month.
        /// </summary>
        private int BuildStems(ForestStand stand, float crowding, Tile tile, Span<TreeInstance> stems, ForestLod lod)
        {
            if (stand.IsEmpty || stems.IsEmpty) return 0;

            TreeModel model = TreeModel.For(stand.Species);
            // A young stand is a handful of saplings; a stocked mature one closes the canopy.
            float stocking = Math.Clamp(stand.Biomass / MathF.Max(
                ForestSpeciesProfile.For(stand.Species).MaximumBiomass, 0.0001f), 0f, 1f);
            // Tiles with open neighbours carry a thinner stand, which is what gives a block of
            // forest a ragged edge instead of a wall of trees ending at a tile boundary.
            float edge = 0.72f + 0.28f * crowding;

            float target = (1f + (model.MatureStems - 1f) * stand.Maturity)
                * (0.40f + 0.60f * stocking) * edge;
            int capacity = Math.Min(model.MatureStems, MaximumStemsPerTile);
            float fill = Math.Clamp(target / capacity, 0f, 1f);

            int lattice = (int)MathF.Ceiling(MathF.Sqrt(capacity));
            // Stems share the tile, so each one has to shrink or the canopy turns into a
            // handful of overlapping giants. This is deliberately a function of the species'
            // full stocking rather than of the current stem count: sizing off the live count
            // would make a lone sapling as large as a mature tree. The exponent is tuned so
            // that a fully stocked tile's crowns overlap into a closed canopy.
            float density = MathF.Pow(1f / capacity, 0.28f);
            float standScale = TreeVisualScale(stand) * density * StemSizeBoost;

            // Crowded stems are drawn up: narrower crown, carried higher on the stem.
            float crownWidth = 1.12f - 0.32f * crowding;
            float crownRise = 0.92f + 0.24f * crowding;

            int count = 0;
            int limit = Math.Min(capacity, stems.Length);
            // A planted tile must always show something, even before its stocking lets the
            // first lattice cell through; the lowest-ranked cell is the fallback stem.
            int firstCell = 0;
            float firstRank = float.MaxValue;

            for (int cell = 0; cell < lattice * lattice; cell++)
            {
                float rank = UnitFloat(TreeHash(tile.Id, (uint)cell * 977u + 101u));
                if (rank < firstRank)
                {
                    firstRank = rank;
                    firstCell = cell;
                }

                if (rank >= fill || count >= limit) continue;
                stems[count++] = MakeStem(tile, stand, model, cell, lattice, rank, fill,
                    standScale, crownWidth, crownRise, lod);
            }

            if (count == 0)
                stems[count++] = MakeStem(tile, stand, model, firstCell, lattice, 0f, 1f,
                    standScale, crownWidth, crownRise, lod);

            return count;
        }

        /// <summary>
        /// Low shrubs scattered between the stems. Without them a stand is a set of trees
        /// standing on open lawn; the reference look needs the forest floor to be occupied.
        /// Only stands dense enough to read as forest, and close enough to see, get them.
        /// </summary>
        private static void DrawUndergrowth(Tile tile, in TreeInstance reference, int stemCount)
        {
            if (reference.Detail < 2 || stemCount < 3) return;

            TreeModel model = TreeModel.For(reference.Stand.Species);
            Color shrub = Shade(Weather(model.CrownColor, reference.Stand.Health), 0.72f);
            int shrubCount = Math.Min(4, stemCount / 3);

            for (int shrub_ = 0; shrub_ < shrubCount; shrub_++)
            {
                uint seed = TreeHash(tile.Id, (uint)shrub_ * 613u + 331u);
                float u = 0.15f + UnitFloat(seed) * 0.70f;
                float v = 0.15f + TreeRandom(seed, 5u) * 0.70f;
                float radius = reference.Scale * (0.42f + TreeRandom(seed, 11u) * 0.26f);

                SurfacePoint(tile, u, v, out float x, out float y, out float z);
                DrawCrownLobe(reference, Shade(shrub, 0.92f + TreeRandom(seed, 17u) * 0.18f),
                    x, y, z, radius, radius * 1.15f, LobeOutline, 5);
            }
        }

        /// <summary>Places one stem inside its lattice cell and resolves its drawn size.</summary>
        private static TreeInstance MakeStem(Tile tile, in ForestStand stand, in TreeModel model,
            int cell, int lattice, float rank, float fill,
            float standScale, float crownWidth, float crownRise, ForestLod lod)
        {
            uint seed = TreeHash(tile.Id, (uint)cell * 131u + 7u);
            // Cell centre plus a jitter that stays inside the cell, so stems spread evenly
            // over the tile without the lattice ever becoming visible.
            float u = (cell % lattice + 0.5f + (UnitFloat(seed) - 0.5f) * 0.8f) / lattice;
            float v = (cell / lattice + 0.5f + (TreeRandom(seed, 3u) - 0.5f) * 0.8f) / lattice;

            // The stems that appeared first are the oldest, and stand above the rest.
            float tier = rank < fill * 0.22f ? 1.18f : rank > fill * 0.72f ? 0.66f : 1f;
            float sizeVariation = 0.88f + TreeRandom(seed, 7u) * 0.24f;
            float scale = standScale * tier * sizeVariation;

            SurfacePoint(tile, u, v, out float x, out float y, out float z);
            return new TreeInstance(
                stand, unchecked(tile.Id * 61 + cell), x, y, z, scale,
                TreeRandom(seed, 11u) * MathF.Tau,
                TreeRandom(seed, 17u) - 0.5f,
                crownWidth, crownRise,
                (byte)Math.Min((int)lod, DetailLevel(model.CrownRadius * scale * crownWidth)));
        }

        /// <summary>Point on the actual terrain triangles (forest tiles do not have road diagonal overrides).</summary>
        internal static void SurfacePoint(Tile tile, float u, float v,
            out float x, out float y, out float z)
        {
            float wW = (1f - u) * (1f - v);
            float wS = u * (1f - v);
            float wE = u * v;
            float wN = (1f - u) * v;

            x = tile.W.xPos * wW + tile.S.xPos * wS + tile.E.xPos * wE + tile.N.xPos * wN;
            y = tile.W.yPos * wW + tile.S.yPos * wS + tile.E.yPos * wE + tile.N.yPos * wN;
            bool diagonalWE = Math.Abs(tile.W.zPos - tile.E.zPos) <= Math.Abs(tile.N.zPos - tile.S.zPos);
            z = diagonalWE
                ? (u >= v ? tile.W.zPos * (1 - u) + tile.S.zPos * (u - v) + tile.E.zPos * v
                          : tile.W.zPos * (1 - v) + tile.E.zPos * u + tile.N.zPos * (v - u))
                : (u + v <= 1 ? tile.W.zPos * (1 - u - v) + tile.S.zPos * u + tile.N.zPos * v
                              : tile.S.zPos * (1 - v) + tile.E.zPos * (u + v - 1) + tile.N.zPos * (1 - u));
        }

        /// <summary>Detail band from the stem's crown radius in world units, not from its scale.</summary>
        private static byte DetailLevel(float crownRadius) =>
            crownRadius >= 0.85f ? (byte)2 : crownRadius >= 0.45f ? (byte)1 : (byte)0;

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
            float crownHeight = model.CrownHeight * tree.Scale * tree.CrownRise;
            float crownBase = boleTopZ - crownHeight * model.CrownDrop;
            float crownRadius = model.CrownRadius * tree.Scale * tree.CrownWidth;
            // Fork below the crown so the limbs are visible before they disappear into foliage.
            float forkZ = crownBase - (crownBase - tree.BaseZ) * 0.55f;

            for (int branch = 0; branch < model.BranchCount; branch++)
            {
                int seed = tree.Seed * 31 + branch;
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

        /// <summary>Sweeps one crown outline around a vertical axis.</summary>
        private static void DrawCrownLobe(in TreeInstance tree, Color color,
            float centerX, float centerY, float baseZ, float radius, float height,
            float[] outline, int sides, float yawOffset = 0f)
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
                    float a0 = tree.Yaw + yawOffset + MathF.Tau * side / sides;
                    float a1 = tree.Yaw + yawOffset + MathF.Tau * (side + 1) / sides;
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

        // Independent full-width samples: shifting first collapses the random range.
        internal static float TreeRandom(uint seed, uint channel) =>
            UnitFloat(TreeHash(unchecked((int)seed), channel));

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

        // Growth has to be visible: a fresh planting is a small stem, a mature stand is full
        // size. The floor keeps a newly planted sapling readable at normal isometric zoom.
        internal static float TreeVisualScale(ForestStand stand) => 0.28f + stand.Maturity * 0.72f;
    }
}
