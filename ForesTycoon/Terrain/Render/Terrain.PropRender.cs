using System;
using System.Drawing;
using System.Collections.Generic;
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
        /// Species code carried in the vertex alpha of bark and crown geometry. The surface
        /// shader decodes it (alpha − 246) to pick the species' bark and foliage pattern;
        /// 247 marks fresh-cut end grain. Plain geometry keeps alpha 255.
        /// </summary>
        internal static int SurfaceSpeciesCode(ForestSpecies species) => TreeScale.SurfaceSpeciesCode(species);
        internal const int CutWoodCode = 247;

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
