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

        /// <summary>Detail band from the stem's crown radius in world units, not from its scale.</summary>
        private static byte DetailLevel(float crownRadius) =>
            crownRadius >= 0.85f ? (byte)2 : crownRadius >= 0.45f ? (byte)1 : (byte)0;

    }
}
