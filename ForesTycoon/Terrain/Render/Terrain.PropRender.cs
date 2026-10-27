using System;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    // Shared dimensions and colour helpers for stumps, ground contact and weather sources.
    // Living tree geometry is owned by TreeModels/DendroTreeGenerator.
    partial class Terrain
    {
        // Same sun the terrain is lit by, so trees and ground agree on where the light is.
        private static readonly Vector3 TreeLight = Vector3.Normalize(new Vector3(0.45f, 0.65f, 1.05f));
        private const float TreeAmbient = 0.42f;

        /// <summary>Reference dimensions used to place ground decals, weather sources and stumps.</summary>
        internal readonly record struct TreeModel(
            float TrunkHeight,
            float TrunkRadius,
            float CrownRadius,
            float CrownHeight,
            // How far the crown is pulled down over the bole, as a fraction of its height.
            float CrownDrop,
            Color TrunkColor)
        {
            public float TotalHeight => TrunkHeight + CrownHeight;

            /// <summary>Share of the tree's height that is bare stem, before the crown starts.</summary>
            public float BareStemFraction => TotalHeight <= 0f ? 0f : TrunkHeight / TotalHeight;

            public static TreeModel For(ForestSpecies species) => species switch
            {
                // Narrow whorled spire branching almost from the ground; barely any bare bole.
                ForestSpecies.Spruce => new TreeModel(
                    0.55f, 0.26f, 1.77f, 8.8f, 0.02f,
                    Color.FromArgb(74, 54, 38)),

                // Slender white stem, light airy crown carried on a few fine limbs.
                ForestSpecies.Birch => new TreeModel(
                    2.2f, 0.16f, 1.75f, 5.3f, 0.05f,
                    Color.FromArgb(208, 208, 196)),

                // Short heavy bole forking into thick limbs under a broad spreading dome.
                ForestSpecies.Oak => new TreeModel(
                    1.45f, 0.38f, 2.35f, 4.65f, 0.07f,
                    Color.FromArgb(110, 84, 54)),

                // Tall smooth grey column under a high egg-shaped crown.
                ForestSpecies.Beech => new TreeModel(
                    1.80f, 0.25f, 2.10f, 5.50f, 0.06f,
                    Color.FromArgb(146, 134, 116)),

                _ => new TreeModel(
                    2.0f, 0.30f, 1.70f, 3.6f, 0.10f,
                    Color.FromArgb(115, 72, 32))
            };
        }

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
    }
}
