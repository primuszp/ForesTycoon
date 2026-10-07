using System;
using System.Collections.Generic;

namespace ForesTycoon.Ecology
{
    internal readonly record struct ForestTileGeometry(float X, float Y, float Width, float Height);
    internal readonly record struct ForestResources(float Light, float Water, float Space)
    {
        internal float LightResponse(ForestSpecies species) => MathF.Pow(Math.Clamp(Light, 0, 1),
            1 - ForestSpeciesProfile.For(species).ShadeTolerance * 0.65f);
        internal float Limitation(ForestSpecies species) => Math.Min(Space, Math.Min(Water, LightResponse(species)));
    }

    // FORMIND-inspired mechanisms, with local crown/root overlap rather than a
    // calibrated carbon balance. Snapshot all trees before updating any of them.
    internal sealed class ForestCompetition
    {
        private readonly record struct Sample(ulong Id, float X, float Y, ForestTreeDimensions Size, float Health);
        private sealed class Cell
        {
            internal Sample[] Trees = Array.Empty<Sample>();
            internal int Count;
            internal float MinX, MinY, MaxX, MaxY, MaxRadius;
        }
        private readonly Dictionary<int, Cell> cells = new();
        private readonly Dictionary<int, int[]> neighbours = new();
        private readonly HashSet<int> snapshotTiles = new();
        internal void Clear() { cells.Clear(); neighbours.Clear(); snapshotTiles.Clear(); }
        internal void BeginSnapshot()
        {
            foreach (var cell in cells.Values) cell.Count = 0;
        }
        internal void SnapshotPatch(IForestHabitat habitat, ForestTreeStore store, int id, double year)
        {
            SnapshotTile(habitat, store, id, year);
            Neighbours(habitat, id);
        }
        internal void Snapshot(IForestHabitat habitat, ForestTreeStore store, double year)
        {
            BeginSnapshot();
            foreach (var entry in store.Patches)
            {
                SnapshotPatch(habitat, store, entry.Key, year);
            }
        }

        // Local edits only need the samples read by the affected trees. Capture all of those
        // dependencies before changing rates, including removed patches and second-ring neighbours.
        internal void SnapshotAffected(IForestHabitat habitat, ForestTreeStore store, double year, HashSet<int> affected)
        {
            snapshotTiles.Clear();
            foreach (int id in affected)
            {
                if (!store.TryGet(id, out var patch) || patch.Count == 0) continue;
                foreach (int dependency in Neighbours(habitat, id)) snapshotTiles.Add(dependency);
            }
            foreach (int id in snapshotTiles) SnapshotTile(habitat, store, id, year);
        }

        private void SnapshotTile(IForestHabitat habitat, ForestTreeStore store, int id, double year)
        {
            if (!store.TryGet(id, out var patch) || patch.Count == 0)
            {
                if (cells.TryGetValue(id, out var old)) old.Count = 0;
                return;
            }
            if (!cells.TryGetValue(id, out var cell)) cells.Add(id, cell = new());
            if (cell.Trees.Length < patch.Trees.Length) cell.Trees = new Sample[patch.Trees.Length];
            var geometry = habitat.GetForestTileGeometry(id);
            cell.MinX = cell.MinY = float.PositiveInfinity;
            cell.MaxX = cell.MaxY = float.NegativeInfinity;
            cell.MaxRadius = 0;
            cell.Count = patch.Count;
            for (int i = 0; i < cell.Count; i++)
            {
                var tree = patch.Trees[i];
                var sample = new Sample(tree.Id, geometry.X + tree.U * geometry.Width,
                    geometry.Y + tree.V * geometry.Height, tree.At(year), tree.Health);
                cell.Trees[i] = sample;
                cell.MinX = Math.Min(cell.MinX, sample.X); cell.MaxX = Math.Max(cell.MaxX, sample.X);
                cell.MinY = Math.Min(cell.MinY, sample.Y); cell.MaxY = Math.Max(cell.MaxY, sample.Y);
                cell.MaxRadius = Math.Max(cell.MaxRadius, sample.Size.CrownRadius);
            }
        }

        private int[] Neighbours(IForestHabitat habitat, int tileId)
        {
            if (neighbours.TryGetValue(tileId, out var cached)) return cached;
            Span<int> adjacent = stackalloc int[16];
            var ids = new List<int> { tileId };
            int firstRing = habitat.GetAdjacentTileIds(tileId, adjacent);
            for (int i = 0; i < firstRing; i++) if (!ids.Contains(adjacent[i])) ids.Add(adjacent[i]);
            int end = ids.Count;
            for (int n = 1; n < end; n++)
            {
                int count = habitat.GetAdjacentTileIds(ids[n], adjacent);
                for (int i = 0; i < count; i++) if (!ids.Contains(adjacent[i])) ids.Add(adjacent[i]);
            }
            var result = ids.ToArray();
            neighbours.Add(tileId, result);
            return result;
        }
        internal ReadOnlySpan<int> NeighbourTiles(IForestHabitat habitat, int tileId) => Neighbours(habitat, tileId);

        internal ForestResources Evaluate(IForestHabitat habitat, in ForestTree tree, double year, float siteWater, float radiation = 1, bool waterAllocated = false)
        {
            if (!neighbours.TryGetValue(tree.TileId, out var ids)) return new(Math.Clamp(radiation, 0, 1), siteWater, 1);
            var geometry = habitat.GetForestTileGeometry(tree.TileId);
            float x = geometry.X + tree.U * geometry.Width, y = geometry.Y + tree.V * geometry.Height;
            var size = tree.At(year);
            float shade = 0, crownLoad = 0, rootLoad = 0;
            foreach (int id in ids)
            {
                if (!cells.TryGetValue(id, out var cell) || cell.Count == 0) continue;
                // Conservative distance to all sampled stems in the cell. The maximum crown
                // radius also covers the wider legacy root response; no contributing pair is culled.
                float gapX = Math.Max(0, Math.Max(cell.MinX - x, x - cell.MaxX));
                float gapY = Math.Max(0, Math.Max(cell.MinY - y, y - cell.MaxY));
                float cellReach = size.CrownRadius + cell.MaxRadius;
                if (!waterAllocated) cellReach = Math.Max(1, cellReach * 1.25f);
                if (gapX * gapX + gapY * gapY > Math.Max(0.01f, cellReach * cellReach)) continue;
                for (int i = 0; i < cell.Count; i++)
                {
                    var other = cell.Trees[i];
                    if (other.Id == tree.Id) continue;
                    float dx = other.X - x, dy = other.Y - y, distance2 = dx * dx + dy * dy;
                    float reach = size.CrownRadius + other.Size.CrownRadius;
                    float overlap = Overlap(distance2, reach);
                    float rootOverlap = waterAllocated ? 0 : Overlap(distance2, Math.Max(1, reach * 1.25f));
                    // Most sampled neighbours cannot compete. Preserve the accumulation order of
                    // overlapping pairs while avoiding their unnecessary ratios and height response.
                    if (overlap == 0 && rootOverlap == 0) continue;
                    float demandRatio = Math.Clamp(other.Size.CrownRadius * other.Size.CrownRadius
                        / Math.Max(0.0225f, size.CrownRadius * size.CrownRadius), 0.001f, 4);
                    if (overlap > 0)
                    {
                        crownLoad += overlap * demandRatio;
                        // Height-asymmetric interception: taller neighbours shade more strongly.
                        float dominance = Math.Clamp(0.5f + (other.Size.Height - size.Height) / Math.Max(0.8f, size.Height) * 2, 0, 1);
                        shade += overlap * 2 * dominance * other.Health;
                    }
                    if (rootOverlap > 0) rootLoad += rootOverlap * MathF.Sqrt(demandRatio);
                }
            }
            return new(Math.Clamp(radiation, 0, 1) * MathF.Exp(-0.65f * shade),
                Math.Clamp(siteWater, 0, 1) / (1 + rootLoad * 0.22f),
                1 / (1 + crownLoad * 0.55f));
        }

        private static float Overlap(float distance2, float reach)
        {
            float t = Math.Max(0, 1 - distance2 / Math.Max(0.01f, reach * reach));
            return t * t;
        }
    }
}
