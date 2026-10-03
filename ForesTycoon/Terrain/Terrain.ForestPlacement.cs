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

        /// <summary>
        /// Global size multiplier for drawn stems. Trees are read against the width of a road,
        /// and at the geometric size the lattice alone produces they came out too small next
        /// to one; the reference look wants a mature crown to be a sizeable share of a tile.
        /// </summary>
        private const float StemSizeBoost = 1.45f;

        /// <summary>Upper bound on the stems one tile can carry; sizes the stack buffer.</summary>
        internal const int MaximumStemsPerTile = 16;
        private readonly List<TreeInstance> chunkStems = new List<TreeInstance>();
        private readonly List<(Tile Tile, int Offset, int Count)> chunkStands = new List<(Tile, int, int)>();

        /// <summary>
        /// Expands one tile's stand into the stems that are actually drawn. The tile is split
        /// into a fixed lattice and every cell is given a deterministic rank; a cell carries a
        /// stem while its rank falls under the tile's stocking. Because the ranks never change,
        /// a growing stand keeps the stems it already has and fills in new ones between them
        /// instead of reshuffling the whole tile every month.
        /// </summary>
        internal static int BuildStems(ForestStand stand, float crowding, Tile tile, Span<TreeInstance> stems, ForestLod lod)
        {
            if (stand.IsEmpty || stems.IsEmpty) return 0;
            StemLayout layout = LayoutStems(stand, crowding);
            TreeModel model = layout.Model;

            int count = 0;
            int limit = Math.Min(layout.Capacity, stems.Length);
            for (int cell = 0; cell < layout.Lattice * layout.Lattice; cell++)
            {
                float rank = CellRank(tile, cell);
                if (rank >= layout.Fill || count >= limit) continue;
                stems[count++] = MakeStem(tile, stand, model, cell, layout.Lattice, rank, layout.Fill,
                    layout.StandScale, layout.CrownWidth, layout.CrownRise, lod);
            }

            // A planted tile must always show something, even before its stocking lets the
            // first lattice cell through; the lowest-ranked cell is the fallback stem.
            if (count == 0)
                stems[count++] = MakeStem(tile, stand, model, layout.FirstCell(tile), layout.Lattice, 0f, 1f,
                    layout.StandScale, layout.CrownWidth, layout.CrownRise, lod);

            return count;
        }

        /// <summary>
        /// Reuses the felled stand's actual stems, including its fallback. Living stems
        /// cover old stumps once large enough; a different species can have a different lattice.
        /// </summary>
        internal static int BuildStumps(in ForestStumpVisualState stump, in ForestVisualState living, Tile tile, Span<TreeInstance> stumps)
        {
            if (stump.IsEmpty || stumps.IsEmpty) return 0;
            Span<TreeInstance> felled = stackalloc TreeInstance[MaximumStemsPerTile];
            Span<TreeInstance> current = stackalloc TreeInstance[MaximumStemsPerTile];
            int felledCount = BuildStems(stump.Felled.Stand, stump.Felled.CanopyPressure, tile, felled, ForestLod.Near);
            int liveCount = BuildStems(living.Stand, living.CanopyPressure, tile, current, ForestLod.Near);
            int count = 0;
            for (int i = 0; i < felledCount && count < stumps.Length; i++)
            {
                bool occupied = false;
                for (int j = 0; j < liveCount; j++)
                {
                    // A different species has a different lattice. Compare actual positions,
                    // and let small replanted saplings coexist with the old cut stems.
                    float dx = felled[i].X - current[j].X, dy = felled[i].Y - current[j].Y;
                    float liveRadius = TreeModel.For(current[j].Stand.Species).TrunkRadius * current[j].Scale;
                    if (dx * dx + dy * dy < liveRadius * liveRadius && current[j].Scale >= felled[i].Scale * 0.55f)
                    {
                        occupied = true;
                        break;
                    }
                }
                if (!occupied) stumps[count++] = felled[i];
            }
            return count;
        }

        private static float CellRank(Tile tile, int cell) => UnitFloat(TreeHash(tile.Id, (uint)cell * 977u + 101u));

        private readonly record struct StemLayout(TreeModel Model, int Capacity, int Lattice, float Fill,
            float StandScale, float CrownWidth, float CrownRise)
        {
            internal int FirstCell(Tile tile)
            {
                int first = 0;
                float firstRank = float.MaxValue;
                for (int cell = 0; cell < Lattice * Lattice; cell++)
                {
                    float rank = CellRank(tile, cell);
                    if (rank < firstRank) { firstRank = rank; first = cell; }
                }
                return first;
            }
        }

        private static StemLayout LayoutStems(ForestStand stand, float crowding)
        {
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

            return new StemLayout(model, capacity, lattice, fill, standScale, crownWidth, crownRise);
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

    }
}
