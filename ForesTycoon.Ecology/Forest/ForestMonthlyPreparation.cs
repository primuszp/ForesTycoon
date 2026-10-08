using System;
using System.Collections.Generic;

namespace ForesTycoon.Ecology
{
    // Predict only geometry at the next monthly boundary. Water and radiation are applied
    // at that boundary from the completed environmental integrals, never predicted here.
    // Work is partitioned by simulation steps, not wall time, preserving deterministic replay.
    internal sealed partial class ForestMonthlyPreparation
    {
        private sealed class PreparedPatch
        {
            internal ForestResources[] Resources = Array.Empty<ForestResources>();
            internal ulong[] Ids = Array.Empty<ulong>();
            internal ForestBoundaryGeometry[] Geometry = Array.Empty<ForestBoundaryGeometry>();
            internal ForestStand Stand;
            internal int Count;
        }
        private ForestCompetition competition = new();
        private readonly List<KeyValuePair<int, ForestTreeStore.Patch>> patches = new();
        private readonly Dictionary<int, PreparedPatch> prepared = new();
        private ulong revision = ulong.MaxValue;
        private double year;
        private int cursor;
        private bool published;
        private readonly Queue<int> snapshotRepairs = new(), resourceRepairs = new();
        private readonly HashSet<int> snapshotPending = new(), resourcePending = new();
        internal int LastStepSnapshotPatches { get; private set; }
        internal int LastStepResourcePatches { get; private set; }
        internal bool Ready => cursor == patches.Count * 2 && snapshotRepairs.Count == 0 && resourceRepairs.Count == 0;

        internal void Clear()
        {
            competition.Clear(); patches.Clear(); prepared.Clear();
            revision = ulong.MaxValue; cursor = 0;
            ClearRepairs(); published = false;
        }

        private void ClearRepairs()
        {
            snapshotRepairs.Clear(); resourceRepairs.Clear(); snapshotPending.Clear(); resourcePending.Clear();
        }

        internal void AcceptLocalRevision(ulong before, ulong after)
        {
            // Only accept the final bookkeeping increment after a known local repair notification.
            // An intervening untracked/global revision must still force a full restart.
            if (!published && revision == before) revision = after;
        }

        internal void InvalidateLocal(IForestHabitat habitat, ulong before, ulong after, double targetYear, IEnumerable<int> changed)
        {
            if (published || revision != before || year != targetYear) return;
            foreach (int id in changed) InvalidateTile(habitat, id);
            revision = after;
        }

        internal void InvalidateLocalTile(IForestHabitat habitat, ulong before, ulong after, double targetYear, int id)
        {
            if (published || revision != before || year != targetYear) return;
            InvalidateTile(habitat, id);
            revision = after;
        }

        private void InvalidateTile(IForestHabitat habitat, int id)
        {
            if (snapshotPending.Add(id)) snapshotRepairs.Enqueue(id);
            // A changed growth rate changes the predicted crown at the boundary. Repair every
            // two-ring observer, not just the directly felled/planted tile.
            foreach (int observer in competition.NeighbourTiles(habitat, id))
                if (resourcePending.Add(observer)) resourceRepairs.Enqueue(observer);
        }

        internal void Advance(IForestHabitat habitat, ForestTreeStore store, ulong currentRevision,
            double targetYear, double secondsUntilMonth)
        {
            if (revision != currentRevision || year != targetYear)
            {
                revision = currentRevision; year = targetYear; cursor = 0;
                patches.Clear();
                foreach (var entry in store.Patches) patches.Add(entry);
                competition.BeginSnapshot();
                ClearRepairs(); published = false;
            }
            LastStepSnapshotPatches = LastStepResourcePatches = 0;
            int remaining = patches.Count * 2 - cursor + snapshotRepairs.Count + resourceRepairs.Count;
            int steps = Math.Max(1, (int)Math.Ceiling(secondsUntilMonth / EcologyTime.StepSeconds));
            int work = (int)Math.Ceiling((double)remaining / steps);
            for (int i = 0; i < work; i++)
            {
                if (cursor < patches.Count)
                {
                    competition.SnapshotPatch(habitat, store, patches[cursor++].Key, year);
                    LastStepSnapshotPatches++;
                    continue;
                }
                if (snapshotRepairs.TryDequeue(out int snapshotId))
                {
                    snapshotPending.Remove(snapshotId);
                    competition.SnapshotPatch(habitat, store, snapshotId, year);
                    LastStepSnapshotPatches++;
                    continue;
                }
                int id;
                if (cursor < patches.Count * 2) id = patches[cursor++ - patches.Count].Key;
                else if (resourceRepairs.TryDequeue(out id)) resourcePending.Remove(id);
                else break;
                LastStepResourcePatches++;
                if (!store.TryGet(id, out var patch) || patch.Count == 0)
                {
                    if (prepared.TryGetValue(id, out var removed)) removed.Count = 0;
                    continue;
                }
                if (!prepared.TryGetValue(id, out var result))
                    prepared.Add(id, result = new());
                if (result.Resources.Length < patch.Count)
                {
                    result.Resources = new ForestResources[patch.Trees.Length];
                    result.Ids = new ulong[patch.Trees.Length];
                    result.Geometry = new ForestBoundaryGeometry[patch.Trees.Length];
                }
                result.Count = patch.Count;
                float age = 0, health = 0, volume = 0;
                for (int treeIndex = 0; treeIndex < patch.Count; treeIndex++)
                {
                    var source = patch.Trees[treeIndex];
                    var tree = source.Settle(year);
                    var resources = competition.Evaluate(habitat, tree, year, 1, 1, waterAllocated: true);
                    result.Resources[treeIndex] = resources;
                    result.Ids[treeIndex] = tree.Id;
                    float treeVolume = ForestTree.Volume(tree.Dimensions);
                    result.Geometry[treeIndex] = new(tree.Dimensions, Math.Max(0, treeVolume - ForestTree.Volume(source.Dimensions)),
                        ForestTreeGrowth.Shape(tree.Species, tree.Dimensions), MathF.Sqrt(Math.Clamp(resources.Space, 0, 1)));
                    age += tree.Age(year); health += tree.Health; volume += treeVolume;
                }
                result.Stand = new(patch.Trees[0].Species, age / patch.Count, volume / 100, health / patch.Count);
            }
        }

        internal bool TryGet(ulong currentRevision, double targetYear, int tileId, int index, ulong treeId,
            float water, float radiation, out ForestResources resources, out ForestBoundaryGeometry geometry)
        {
            resources = default;
            geometry = default;
            if (!Ready || revision != currentRevision || year != targetYear
                || !prepared.TryGetValue(tileId, out var patch) || index >= patch.Count || patch.Ids[index] != treeId)
                return false;
            var rawResources = patch.Resources[index];
            resources = new(Math.Clamp(radiation, 0, 1) * rawResources.Light, Math.Clamp(water, 0, 1), rawResources.Space);
            geometry = patch.Geometry[index];
            return true;
        }

        internal bool TryGetStand(ulong currentRevision, double targetYear, int tileId, out ForestStand stand)
        {
            stand = default;
            if (!Ready || revision != currentRevision || year != targetYear || !prepared.TryGetValue(tileId, out var patch)) return false;
            if (patch.Count > 0) stand = patch.Stand;
            return true;
        }

        internal bool PublishSnapshot(ulong currentRevision, double targetYear, ref ForestCompetition destination)
        {
            if (!Ready || revision != currentRevision || year != targetYear) return false;
            // Reuse both buffers. New seedlings still evaluate against the same pre-update
            // snapshot as existing trees; next month's preparation cannot mutate that snapshot.
            (destination, competition) = (competition, destination);
            published = true;
            return true;
        }
    }
}
