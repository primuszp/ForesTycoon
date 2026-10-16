using System;
using System.Collections.Generic;

namespace ForesTycoon.Ecology
{
    internal readonly record struct ForestTileGeometry(float X, float Y, float Width, float Height);
    internal readonly record struct ForestResources(float Light, float Water, float Space)
    {
        internal float LightResponse(ForestSpecies species, ForesTycoon.Rules.EcologyResponseProgram rules = null) =>
            (float)(rules ?? ForesTycoon.Rules.EcologyResponseProgram.Default).LightResponse(Light, Water, Space, ForestSpeciesProfile.For(species).ShadeTolerance);
        internal float Limitation(ForestSpecies species, ForesTycoon.Rules.EcologyResponseProgram rules = null) =>
            (float)(rules ?? ForesTycoon.Rules.EcologyResponseProgram.Default).ResourceLimit(Light, Water, Space, ForestSpeciesProfile.For(species).ShadeTolerance);
    }
    internal readonly record struct ForestCompetitionLoads(double Shade, double CrownLoad);

    // Snapshot all trees before updating any of them. Geometry and ordered reduction are
    // mechanisms; the project graphs own every pair contribution and resource response.
    internal sealed partial class ForestCompetition
    {
        internal readonly record struct Sample(ulong Id, float X, float Y, ForestTreeDimensions Size, float Health, ForestSpecies Species = ForestSpecies.None);
        private sealed class Cell
        {
            internal Sample[] Trees = Array.Empty<Sample>();
            internal int Count;
            internal float MinX, MaxX, MinY, MaxY;
            internal readonly ForesTycoon.Rules.GraphInputRange[] Bounds = new ForesTycoon.Rules.GraphInputRange[5];
            internal void ResetBounds()
            {
                MinX = MinY = float.PositiveInfinity; MaxX = MaxY = float.NegativeInfinity;
                for (int i = 0; i < 4; i++) Bounds[i] = new(double.PositiveInfinity, double.NegativeInfinity);
                // Species profiles can change independently of a geometry snapshot; unknown is always safe.
                Bounds[4] = ForesTycoon.Rules.GraphInputRange.Unknown;
            }
            internal void Include(in Sample tree)
            {
                MinX = Math.Min(MinX, tree.X); MaxX = Math.Max(MaxX, tree.X);
                MinY = Math.Min(MinY, tree.Y); MaxY = Math.Max(MaxY, tree.Y);
                Add(0, tree.Size.Height); Add(1, tree.Size.CrownRadius); Add(2, tree.Size.Diameter); Add(3, tree.Health);
            }
            private void Add(int i, double value) => Bounds[i] = new(Math.Min(Bounds[i].Min, value), Math.Max(Bounds[i].Max, value));
            internal (double Min, double Max) DistanceSquared(float x, float y)
            {
                // Keep the same float subtraction, multiplication and addition as the actual pair query.
                float dx = x < MinX ? MinX - x : x > MaxX ? MaxX - x : 0;
                float dy = y < MinY ? MinY - y : y > MaxY ? MaxY - y : 0;
                float farX = Math.Max(Math.Abs(MinX - x), Math.Abs(MaxX - x));
                float farY = Math.Max(Math.Abs(MinY - y), Math.Abs(MaxY - y));
                return (Math.Max(0, MathF.BitDecrement(dx * dx + dy * dy)), MathF.BitIncrement(farX * farX + farY * farY));
            }
        }
        private readonly Dictionary<int, Cell> cells = new();
        private readonly Dictionary<int, int[]> neighbours = new();
        private readonly HashSet<int> snapshotTiles = new();
        internal void Clear() { cells.Clear(); neighbours.Clear(); snapshotTiles.Clear(); }
        internal void BeginSnapshot() => SimulationWork.Complete(BeginSnapshotWork());
        internal IEnumerable<int> BeginSnapshotWork()
        {
            foreach (var cell in cells.Values) { cell.Count = 0; yield return 1; }
        }
        internal void SnapshotPatch(IForestHabitat habitat, ForestTreeStore store, int id, double year)
            => SimulationWork.Complete(SnapshotPatchWork(habitat, store, id, year));
        internal IEnumerable<int> SnapshotPatchWork(IForestHabitat habitat, ForestTreeStore store, int id, double year)
        {
            foreach (int unit in SnapshotTileWork(habitat, store, id, year)) yield return unit;
            Neighbours(habitat, id);
            yield return 1;
        }
        internal void Snapshot(IForestHabitat habitat, ForestTreeStore store, double year)
            => SimulationWork.Complete(SnapshotWork(habitat, store, year));
        internal IEnumerable<int> SnapshotWork(IForestHabitat habitat, ForestTreeStore store, double year)
        {
            foreach (int unit in BeginSnapshotWork()) yield return unit;
            foreach (var entry in store.Patches)
            {
                foreach (int unit in SnapshotPatchWork(habitat, store, entry.Key, year)) yield return unit;
            }
        }

        // Local edits only need the samples read by the affected trees. Capture all of those
        // dependencies before changing rates, including removed patches and second-ring neighbours.
        internal void SnapshotAffected(IForestHabitat habitat, ForestTreeStore store, double year, HashSet<int> affected)
            => SimulationWork.Complete(SnapshotAffectedWork(habitat, store, year, affected));
        internal IEnumerable<int> SnapshotAffectedWork(IForestHabitat habitat, ForestTreeStore store, double year, HashSet<int> affected)
        {
            snapshotTiles.Clear();
            foreach (int id in affected)
            {
                yield return 1;
                if (!store.TryGet(id, out var patch) || patch.Count == 0) continue;
                foreach (int dependency in Neighbours(habitat, id)) snapshotTiles.Add(dependency);
            }
            foreach (int id in snapshotTiles)
                foreach (int unit in SnapshotTileWork(habitat, store, id, year)) yield return unit;
        }

        private IEnumerable<int> SnapshotTileWork(IForestHabitat habitat, ForestTreeStore store, int id, double year)
        {
            if (!store.TryGet(id, out var patch) || patch.Count == 0)
            {
                yield return 1; // Removed/empty dependencies must also consume a bounded work unit.
                if (cells.TryGetValue(id, out var old)) old.Count = 0;
                yield break;
            }
            if (!cells.TryGetValue(id, out var cell)) cells.Add(id, cell = new());
            if (cell.Trees.Length < patch.Trees.Length) cell.Trees = new Sample[patch.Trees.Length];
            var geometry = habitat.GetForestTileGeometry(id);
            cell.Count = patch.Count;
            cell.ResetBounds();
            for (int i = 0; i < cell.Count; i++)
            {
                yield return 1;
                var tree = patch.Trees[i];
                var sample = new Sample(tree.Id, geometry.X + tree.U * geometry.Width,
                    geometry.Y + tree.V * geometry.Height, tree.At(year), tree.Health, tree.Species);
                cell.Trees[i] = sample;
                cell.Include(sample);

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

        // Only snapshot geometry, candidate enumeration and ordered reduction remain in the host.
        // No crown-based pruning is allowed: an authored graph may intentionally influence a distant candidate.
        internal IEnumerable<int> EvaluateLoadsWork(IForestHabitat habitat, ForestTree tree, double year,
            WorkResult<ForestCompetitionLoads> result, ForesTycoon.Rules.EcologyResponseProgram rules = null)
        {
            rules ??= ForesTycoon.Rules.EcologyResponseProgram.Default;
            var ids = Neighbours(habitat, tree.TileId);
            var geometry = habitat.GetForestTileGeometry(tree.TileId);
            float x = geometry.X + tree.U * geometry.Width, y = geometry.Y + tree.V * geometry.Height;
            var size = tree.At(year);
            double shade = 0, crownLoad = 0;
            double tolerance = ForestSpeciesProfile.For(tree.Species).ShadeTolerance;
            foreach (int id in ids) {
                yield return 1;
                if (!cells.TryGetValue(id, out var cell)) continue;
                if (cell.Count > 0 && rules.CanBoundPairs) {
                    var distance = cell.DistanceSquared(x, y);
                    if (!rules.PairMayContribute(size.Height, size.CrownRadius, size.Diameter, tree.Health, tolerance,
                        cell.Bounds, distance.Min, distance.Max)) continue;
                }
                for (int i = 0; i < cell.Count; i++) {
                    yield return 1;
                    var other = cell.Trees[i];
                    if (other.Id == tree.Id) continue;
                    float dx = other.X - x, dy = other.Y - y;
                    var effects = rules.Pair(size.Height, size.CrownRadius, size.Diameter, tree.Health, tolerance,
                        other.Size.Height, other.Size.CrownRadius, other.Size.Diameter, other.Health,
                        ForestSpeciesProfile.For(other.Species).ShadeTolerance, dx * dx + dy * dy);
                    shade += effects.Shade; crownLoad += effects.CrownLoad;
                }
            }
            result.Value = new(shade, crownLoad);
        }
        internal static ForestResources Resolve(ForestCompetitionLoads loads, float water, float radiation,
            ForesTycoon.Rules.EcologyResponseProgram rules = null)
        {
            var response = (rules ?? ForesTycoon.Rules.EcologyResponseProgram.Default).Competition(loads.Shade, loads.CrownLoad, radiation, water);
            return new((float)response.Light, Math.Clamp(water, 0, 1), (float)response.Space);
        }
        // Synchronous diagnostics use exactly the same sliced implementation.
        internal ForestResources Evaluate(IForestHabitat habitat, in ForestTree tree, double year, float siteWater, float radiation = 1,
            ForesTycoon.Rules.EcologyResponseProgram rules = null)
        {
            var result = new WorkResult<ForestCompetitionLoads>();
            SimulationWork.Complete(EvaluateLoadsWork(habitat, tree, year, result, rules));
            return Resolve(result.Value, siteWater, radiation, rules);
        }
    }
}
