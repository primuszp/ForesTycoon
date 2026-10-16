using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon.Ecology
{
    internal sealed record CompetitionCellCheckpoint(int Tile, ForestCompetition.Sample[] Trees);
    internal sealed record NeighboursCheckpoint(int Tile, int[] Neighbours);
    internal sealed record CompetitionCheckpoint(CompetitionCellCheckpoint[] Cells, NeighboursCheckpoint[] Neighbours);
    internal sealed record PreparedPatchCheckpoint(int Tile, ForestCompetitionLoads[] Loads, ulong[] Ids,
        ForestBoundaryGeometry[] Geometry, ForestStand Stand);
    internal sealed record PreparationCheckpoint(CompetitionCheckpoint Competition, int[] Patches, PreparedPatchCheckpoint[] Prepared,
        ulong Revision, double Year, int Cursor, bool Published, int[] SnapshotRepairs, int[] ResourceRepairs);

    internal sealed partial class ForestCompetition
    {
        internal CompetitionCheckpoint Capture() => new(cells.Where(p => p.Value.Count > 0).OrderBy(p => p.Key)
            .Select(p => new CompetitionCellCheckpoint(p.Key, p.Value.Trees.Take(p.Value.Count).ToArray())).ToArray(),
            neighbours.OrderBy(p => p.Key).Select(p => new NeighboursCheckpoint(p.Key, (int[])p.Value.Clone())).ToArray());
        internal void Restore(CompetitionCheckpoint s, int tileCount) => SimulationWork.Complete(RestoreWork(s, tileCount));

        internal IEnumerable<int> RestoreWork(CompetitionCheckpoint s, int tileCount)
        {
            CheckpointGuard.Require(s != null && s.Cells != null && s.Neighbours != null, "competition snapshot");
            Clear();
            foreach (var c in s.Cells)
            {
                yield return 1;
                CheckpointGuard.Require(c != null && (uint)c.Tile < (uint)tileCount && c.Trees != null &&
                    c.Trees.Length > 0 && !cells.ContainsKey(c.Tile), "competition cell");
                var cell = new Cell
                {
                    Trees = (Sample[])c.Trees.Clone(),
                    Count = c.Trees.Length
                };
                foreach (var tree in cell.Trees)
                {
                    yield return 1;
                    CheckpointGuard.Require(tree.Id > 0 && float.IsFinite(tree.X) && float.IsFinite(tree.Y), "competition sample");
                    CheckpointGuard.Unit(tree.Health, "competition health");
                    CheckpointGuard.Require(Enum.IsDefined(tree.Species), "competition species");
                    CheckpointGuard.NonNegative(tree.Size.Diameter, "competition diameter");
                    CheckpointGuard.NonNegative(tree.Size.Height, "competition height"); CheckpointGuard.NonNegative(tree.Size.CrownRadius, "competition crown");

                }
                cells.Add(c.Tile, cell);
            }
            foreach (var n in s.Neighbours)
            {
                yield return 1;
                CheckpointGuard.Require(n != null && (uint)n.Tile < (uint)tileCount && n.Neighbours != null &&
                    n.Neighbours.Length > 0 && n.Neighbours.All(id => (uint)id < (uint)tileCount) &&
                    neighbours.TryAdd(n.Tile, (int[])n.Neighbours.Clone()), "competition neighbours");
            }
        }
    }

    internal sealed partial class ForestMonthlyPreparation
    {
        internal PreparationCheckpoint Capture() => new(competition.Capture(), patches.Select(p => p.Key).ToArray(),
            prepared.OrderBy(p => p.Key).Select(p => new PreparedPatchCheckpoint(p.Key,
                p.Value.Loads.Take(p.Value.Count).ToArray(), p.Value.Ids.Take(p.Value.Count).ToArray(),
                p.Value.Geometry.Take(p.Value.Count).ToArray(), p.Value.Stand)).ToArray(), revision, year, cursor, published,
            snapshotRepairs.ToArray(), resourceRepairs.ToArray());
        internal void Restore(PreparationCheckpoint s, ForestTreeStore store, int tileCount) => SimulationWork.Complete(RestoreWork(s, store, tileCount));

        internal IEnumerable<int> RestoreWork(PreparationCheckpoint s, ForestTreeStore store, int tileCount)
        {
            CheckpointGuard.Require(s != null && s.Patches != null && s.Prepared != null && s.SnapshotRepairs != null &&
                s.ResourceRepairs != null && s.Cursor >= 0 && s.Cursor <= (long)s.Patches.Length * 2, "preparation cursor");
            CheckpointGuard.NonNegative(s.Year, "preparation calendar");
            Clear(); foreach (int unit in competition.RestoreWork(s.Competition, tileCount)) yield return unit;
            var unique = new HashSet<int>();
            foreach (int id in s.Patches)
            {
                yield return 1;
                CheckpointGuard.Require((uint)id < (uint)tileCount && unique.Add(id), "preparation patch order");
                store.TryGet(id, out var patch); patches.Add(new(id, patch));
            }
            foreach (var p in s.Prepared)
            {
                yield return 1;
                CheckpointGuard.Require(p != null && (uint)p.Tile < (uint)tileCount && p.Ids != null && p.Loads != null &&
                    p.Geometry != null && p.Ids.Length == p.Loads.Length && p.Ids.Length == p.Geometry.Length &&
                    !prepared.ContainsKey(p.Tile), "prepared geometry");
                foreach (var r in p.Loads)
                {
                    yield return 1; CheckpointGuard.NonNegative(r.Shade, "prepared shade load");
                    CheckpointGuard.NonNegative(r.CrownLoad, "prepared crown load");
                }
                foreach (var g in p.Geometry)
                {
                    yield return 1;
                    CheckpointGuard.NonNegative(g.VolumeIncrement, "prepared volume");
                    foreach (double v in new[] { g.Dimensions.Diameter, g.Dimensions.Height, g.Dimensions.CrownRadius })
                    { yield return 1; CheckpointGuard.NonNegative(v, "prepared dimensions"); }
                }
                prepared.Add(p.Tile, new PreparedPatch
                {
                    Loads = (ForestCompetitionLoads[])p.Loads.Clone(),
                    Ids = (ulong[])p.Ids.Clone(),
                    Geometry = (ForestBoundaryGeometry[])p.Geometry.Clone(),
                    Stand = p.Stand,
                    Count = p.Ids.Length
                });
            }
            foreach (int id in s.SnapshotRepairs) { yield return 1; CheckpointGuard.Require((uint)id < (uint)tileCount && snapshotPending.Add(id), "snapshot repair"); snapshotRepairs.Enqueue(id); }
            foreach (int id in s.ResourceRepairs) { yield return 1; CheckpointGuard.Require((uint)id < (uint)tileCount && resourcePending.Add(id), "resource repair"); resourceRepairs.Enqueue(id); }
            revision = s.Revision; year = s.Year; cursor = s.Cursor; published = s.Published;
        }
    }
}
