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
            internal ForestCompetitionLoads[] Loads = Array.Empty<ForestCompetitionLoads>();
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

        internal IEnumerable<int> ReserveLikeWork(ForestCompetition source) => competition.ReserveLikeWork(source);

        internal IEnumerable<int> FinishQueriesWork() => competition.FinishQueriesWork();

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
            => SimulationWork.Complete(InvalidateLocalWork(habitat, before, after, targetYear, changed));
        internal IEnumerable<int> InvalidateLocalWork(IForestHabitat habitat, ulong before, ulong after, double targetYear, IEnumerable<int> changed)
        {
            if (published || revision != before || year != targetYear) yield break;
            foreach (int id in changed) { InvalidateTile(habitat, id); yield return 1; }
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
            double targetYear, double secondsUntilMonth, ForesTycoon.Rules.EcologyResponseProgram rules = null)
            => SimulationWork.Complete(AdvanceWork(habitat, store, currentRevision, targetYear, secondsUntilMonth, rules));

        internal IEnumerable<int> AdvanceWork(IForestHabitat habitat, ForestTreeStore store, ulong currentRevision,
            double targetYear, double secondsUntilMonth, ForesTycoon.Rules.EcologyResponseProgram rules = null)
        {
            if (revision != currentRevision || year != targetYear)
            {
                revision = currentRevision; year = targetYear; cursor = 0;
                patches.Clear();
                foreach (var entry in store.Patches) { patches.Add(entry); yield return 1; }
                foreach (int unit in competition.BeginSnapshotWork()) yield return unit;
                ClearRepairs(); published = false;
            }
            LastStepSnapshotPatches = LastStepResourcePatches = 0;
            // A snapshot copies trees; a resource patch scans neighbouring candidates and runs authored graphs.
            // Equal patch counts delayed the costly phase until halfway through the month, causing a sustained burst.
            // Fixed scheduling weights are deterministic and affect only preparation timing, never the query result.
            const int resourceWeight = 32;
            int snapshots = Math.Max(0, patches.Count - cursor) + snapshotRepairs.Count;
            int resources = patches.Count * 2 - Math.Max(cursor, patches.Count) + resourceRepairs.Count;
            long remaining = snapshots + (long)resources * resourceWeight;
            int steps = Math.Max(1, (int)Math.Ceiling(secondsUntilMonth / EcologyTime.StepSeconds));
            int work = (int)Math.Ceiling((double)remaining / steps);
            for (int i = 0; i < work; i++)
            {
                yield return 1;
                if (cursor < patches.Count)
                {
                    foreach (int unit in competition.SnapshotPatchWork(habitat, store, patches[cursor++].Key, year)) yield return unit;
                    LastStepSnapshotPatches++;
                    continue;
                }
                if (snapshotRepairs.TryDequeue(out int snapshotId))
                {
                    snapshotPending.Remove(snapshotId);
                    foreach (int unit in competition.SnapshotPatchWork(habitat, store, snapshotId, year)) yield return unit;
                    LastStepSnapshotPatches++;
                    continue;
                }
                var queries = new List<ForestCompetition.Query>();
                var outputs = new List<(PreparedPatch Patch, int Index, WorkResult<ForestCompetitionLoads> Loads)>();
                int batchSteps = 0;
                for (; batchSteps < Math.Min(32, Math.Max(1, (work - i + resourceWeight - 1) / resourceWeight)); batchSteps++) {
                    int id;
                    if (cursor < patches.Count * 2) id = patches[cursor++ - patches.Count].Key;
                    else if (resourceRepairs.TryDequeue(out id)) resourcePending.Remove(id);
                    else break;
                    LastStepResourcePatches++;
                    yield return 1;
                    if (!store.TryGet(id, out var patch) || patch.Count == 0) {
                        if (prepared.TryGetValue(id, out var removed)) removed.Count = 0;
                        continue;
                    }
                    if (!prepared.TryGetValue(id, out var result)) prepared.Add(id, result = new());
                    if (result.Loads.Length < patch.Count) {
                        result.Loads = new ForestCompetitionLoads[patch.Trees.Length];
                        result.Ids = new ulong[patch.Trees.Length];
                        result.Geometry = new ForestBoundaryGeometry[patch.Trees.Length];
                    }
                    result.Count = patch.Count;
                    float age = 0, health = 0, volume = 0;
                    for (int treeIndex = 0; treeIndex < patch.Count; treeIndex++) {
                        yield return 1;
                        var source = patch.Trees[treeIndex]; var tree = source.Settle(year);
                        var loads = new WorkResult<ForestCompetitionLoads>();
                        queries.Add(new(tree, year, loads)); outputs.Add((result, treeIndex, loads));
                        result.Ids[treeIndex] = tree.Id;
                        float treeVolume = ForestTree.Volume(tree.Dimensions);
                        result.Geometry[treeIndex] = new(tree.Dimensions, Math.Max(0, treeVolume - ForestTree.Volume(source.Dimensions)));
                        age += tree.Age(year); health += tree.Health; volume += treeVolume;
                    }
                    result.Stand = new(patch.Trees[0].Species, age / patch.Count, volume / 100, health / patch.Count);
                }
                if (batchSteps == 0) break;
                i += batchSteps * resourceWeight - 1;
                foreach (int unit in competition.EvaluateBatchWork(habitat, queries, rules ?? ForesTycoon.Rules.EcologyResponseProgram.Default)) yield return unit;
                foreach (var output in outputs) { output.Patch.Loads[output.Index] = output.Loads.Value; yield return 1; }

            }
        }

        internal bool TryGet(ulong currentRevision, double targetYear, int tileId, int index, ulong treeId,
            float water, float radiation, out ForestResources resources, out ForestBoundaryGeometry geometry, ForesTycoon.Rules.EcologyResponseProgram rules = null)
        {
            resources = default;
            geometry = default;
            if (!Ready || revision != currentRevision || year != targetYear
                || !prepared.TryGetValue(tileId, out var patch) || index >= patch.Count || patch.Ids[index] != treeId)
                return false;
            resources = ForestCompetition.Resolve(patch.Loads[index], water, radiation, rules);
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
