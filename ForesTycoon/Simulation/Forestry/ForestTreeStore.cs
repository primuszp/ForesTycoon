using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    /// <summary>Sparse per-tile arrays; no managed object per tree. Growth reuses occupied arrays.</summary>
    internal sealed class ForestTreeStore
    {
        internal const int PlantingRows = 6;
        internal const int PlantedTreesPerTile = PlantingRows * PlantingRows;
        internal sealed class Patch
        {
            internal ForestTree[] Trees;
            internal int Count;
            internal List<ForestTreeStump> Stumps;
            internal List<ForestDeadTree> DeadTrees;
            internal float Depot;
            internal ulong Revision;
            internal ulong TopologyRevision;
            internal Patch(int capacity) => Trees = new ForestTree[capacity];
        }

        private readonly Dictionary<int, Patch> patches = new();
        internal IEnumerable<KeyValuePair<int, Patch>> Patches => patches;
        private ulong nextId = 1;
        private ulong topologyRevision;
        internal ulong Generation { get; private set; }
        internal int TreeCount { get; private set; }
        internal bool TryGet(int tileId, out Patch patch) => patches.TryGetValue(tileId, out patch);

        internal void Clear()
        {
            patches.Clear();
            Generation++;
            nextId = 1;
            TreeCount = 0;
        }

        internal void Create(int tileId, ForestStand stand, double year, int worldSeed, bool planted = false)
        {
            if (stand.IsEmpty) return;
            if (!patches.TryGetValue(tileId, out Patch patch))
                patches.Add(tileId, patch = new Patch(16));
            if (patch.Count != 0) throw new InvalidOperationException("Tile already has living trees.");
            if (planted && patch.Trees.Length < PlantedTreesPerTile) Array.Resize(ref patch.Trees, PlantedTreesPerTile);
            int capacity = ForestTreeGrowth.Capacity(stand.Species);
            float stock = Math.Clamp(stand.Biomass / ForestSpeciesProfile.For(stand.Species).MaximumBiomass, 0, 1);
            int count = Math.Clamp((int)MathF.Round((1 + (capacity - 1) * stand.Maturity) * (0.4f + 0.6f * stock)), 1, capacity);
            int lattice = (int)MathF.Ceiling(MathF.Sqrt(capacity));
            if (planted) { count = PlantedTreesPerTile; lattice = PlantingRows; }
            Span<int> cells = stackalloc int[lattice * lattice];
            for (int i = 0; i < cells.Length; i++) cells[i] = i;
            // Select scattered cells rather than filling every tile row from the same corner.
            for (int i = 1; i < cells.Length; i++)
            {
                int cell = cells[i], previous = i - 1;
                uint rank = CellRank(cell);
                while (previous >= 0 && CellRank(cells[previous]) > rank)
                {
                    cells[previous + 1] = cells[previous];
                    previous--;
                }
                cells[previous + 1] = cell;
            }
            for (int i = 0; i < count; i++)
            {
                uint seed = Random(unchecked((uint)worldSeed ^ (uint)tileId * 977u ^ (uint)nextId * 131u));
                int cell = planted ? i : cells[i];
                float u = (cell % lattice + 0.5f + (planted ? 0 : (Unit(seed) - 0.5f) * 0.7f)) / lattice;
                float v = (cell / lattice + 0.5f + (planted ? 0 : (Unit(Random(seed)) - 0.5f) * 0.7f)) / lattice;
                float age = planted ? stand.AgeYears : stand.AgeYears * (0.85f + Unit(Random(seed + 7)) * 0.3f);
                var tree = new ForestTree(nextId++, tileId, stand.Species, u, v, seed, year - age, year,
                    ForestTreeGrowth.Initial(stand.Species, age, 0.88f + Unit(Random(seed + 17)) * 0.24f), default, stand.Health);
                patch.Trees[patch.Count++] = tree;
                TreeCount++;
            }
            patch.Revision++;
            NotifyTopologyChanged(patch);
            uint CellRank(int cell) => Random(unchecked((uint)worldSeed ^ (uint)tileId * 977u ^ (uint)cell * 313u));
        }

        internal void RemoveLiving(Patch patch, int index)
        {
            // Preserve ordering so harvest selection and floating-point sums stay deterministic.
            Array.Copy(patch.Trees, index + 1, patch.Trees, index, patch.Count - index - 1);
            patch.Trees[--patch.Count] = default;
            patch.Revision++;
            NotifyTopologyChanged(patch);
            TreeCount--;
        }

        internal void NotifyTopologyChanged(Patch patch) => patch.TopologyRevision = ++topologyRevision;

        internal void RemoveTile(int id)
        {
            if (!patches.Remove(id, out var patch)) return;
            TreeCount -= patch.Count;
        }

        internal static uint Random(uint value)
        {
            value ^= value >> 16; value *= 0x7FEB352Du;
            value ^= value >> 15; value *= 0x846CA68Bu;
            return value ^ (value >> 16);
        }
        internal static float Unit(uint value) => (value >> 8) * (1f / 16777216f);
    }
}
