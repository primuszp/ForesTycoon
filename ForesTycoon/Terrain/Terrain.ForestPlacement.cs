using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    partial class Terrain
    {
        /// <summary>Resolved placement and dimensions of one drawn stem.</summary>
        internal readonly struct TreeInstance
        {
            public readonly ForestStand Stand;
            // Per-stem seed; stems in one tile must not share their branch and lobe layout.
            public readonly int Seed;
            public readonly float X;
            public readonly float Y;
            public readonly float BaseZ;
            public readonly float Scale;
            public readonly float TrunkScale;
            public readonly float TrunkHeightScale;
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
                float scale, float yaw, float tint, float crownWidth, float crownRise, byte detail,
                float? trunkScale = null, float? trunkHeightScale = null)
            {
                Stand = stand;
                Seed = seed;
                X = x;
                Y = y;
                BaseZ = baseZ;
                Scale = scale;
                TrunkScale = trunkScale ?? scale;
                TrunkHeightScale = trunkHeightScale ?? scale;
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

            public float TrunkTop(in TreeModel model) => BaseZ + model.TrunkHeight * TrunkHeightScale;
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

    }
}
