using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    // Predict only geometry at the next monthly boundary. Water and radiation are applied
    // at that boundary from the completed environmental integrals, never predicted here.
    // Work is partitioned by simulation steps, not wall time, preserving deterministic replay.
    internal sealed class ForestMonthlyPreparation
    {
        private sealed class PreparedPatch
        {
            internal ForestResources[] Resources = Array.Empty<ForestResources>();
            internal ulong[] Ids = Array.Empty<ulong>();
            internal int Count;
        }
        private ForestCompetition competition = new();
        private readonly List<KeyValuePair<int, ForestTreeStore.Patch>> patches = new();
        private readonly Dictionary<int, PreparedPatch> prepared = new();
        private ulong revision = ulong.MaxValue;
        private double year;
        private int cursor;
        internal bool Ready => cursor == patches.Count * 2;

        internal void Clear()
        {
            competition.Clear(); patches.Clear(); prepared.Clear();
            revision = ulong.MaxValue; cursor = 0;
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
            }
            int remaining = patches.Count * 2 - cursor;
            int steps = Math.Max(1, (int)Math.Ceiling(secondsUntilMonth / EnvironmentSystem.StepSeconds));
            int work = (int)Math.Ceiling((double)remaining / steps);
            for (int i = 0; i < work; i++, cursor++)
            {
                if (cursor < patches.Count)
                {
                    competition.SnapshotPatch(habitat, store, patches[cursor].Key, year);
                    continue;
                }
                var entry = patches[cursor - patches.Count];
                var patch = entry.Value;
                if (!prepared.TryGetValue(entry.Key, out var result))
                    prepared.Add(entry.Key, result = new());
                if (result.Resources.Length < patch.Count)
                {
                    result.Resources = new ForestResources[patch.Trees.Length];
                    result.Ids = new ulong[patch.Trees.Length];
                }
                result.Count = patch.Count;
                for (int treeIndex = 0; treeIndex < patch.Count; treeIndex++)
                {
                    var tree = patch.Trees[treeIndex].Settle(year);
                    result.Resources[treeIndex] = competition.Evaluate(habitat, tree, year, 1, 1, waterAllocated: true);
                    result.Ids[treeIndex] = tree.Id;
                }
            }
        }

        internal bool TryGet(ulong currentRevision, double targetYear, int tileId, int index, ulong treeId,
            float water, float radiation, out ForestResources resources)
        {
            resources = default;
            if (!Ready || revision != currentRevision || year != targetYear
                || !prepared.TryGetValue(tileId, out var patch) || index >= patch.Count || patch.Ids[index] != treeId)
                return false;
            var geometry = patch.Resources[index];
            resources = new(Math.Clamp(radiation, 0, 1) * geometry.Light, Math.Clamp(water, 0, 1), geometry.Space);
            return true;
        }

        internal bool PublishSnapshot(ulong currentRevision, double targetYear, ref ForestCompetition destination)
        {
            if (!Ready || revision != currentRevision || year != targetYear) return false;
            // Reuse both buffers. New seedlings still evaluate against the same pre-update
            // snapshot as existing trees; next month's preparation cannot mutate that snapshot.
            (destination, competition) = (competition, destination);
            return true;
        }
    }
}
