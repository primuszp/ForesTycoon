using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon.Ecology
{
    internal sealed record CompetitionCellCheckpoint(int Tile, ForestCompetition.Sample[] Trees);
    internal sealed record NeighboursCheckpoint(int Tile, int[] Neighbours);
    internal sealed record CompetitionCheckpoint(CompetitionCellCheckpoint[] Cells, NeighboursCheckpoint[] Neighbours);
    internal sealed record PreparedPatchCheckpoint(int Tile, ForestResources[] Resources, ulong[] Ids,
        ForestBoundaryGeometry[] Geometry, ForestStand Stand);
    internal sealed record PreparationCheckpoint(CompetitionCheckpoint Competition, int[] Patches, PreparedPatchCheckpoint[] Prepared,
        ulong Revision, double Year, int Cursor, bool Published, int[] SnapshotRepairs, int[] ResourceRepairs);

    internal sealed partial class ForestCompetition
    {
        internal CompetitionCheckpoint Capture() => new(cells.Where(p => p.Value.Count > 0).OrderBy(p => p.Key)
            .Select(p => new CompetitionCellCheckpoint(p.Key, p.Value.Trees.Take(p.Value.Count).ToArray())).ToArray(),
            neighbours.OrderBy(p => p.Key).Select(p => new NeighboursCheckpoint(p.Key, (int[])p.Value.Clone())).ToArray());
        internal void Restore(CompetitionCheckpoint s, int tileCount)
        {
            CheckpointGuard.Require(s != null && s.Cells != null && s.Neighbours != null, "competition snapshot");
            Clear();
            foreach (var c in s.Cells) {
                CheckpointGuard.Require(c != null && (uint)c.Tile < (uint)tileCount && c.Trees != null &&
                    c.Trees.Length > 0 && !cells.ContainsKey(c.Tile), "competition cell");
                var cell = new Cell { Trees = (Sample[])c.Trees.Clone(), Count = c.Trees.Length,
                    MinX = float.PositiveInfinity, MinY = float.PositiveInfinity, MaxX = float.NegativeInfinity, MaxY = float.NegativeInfinity };
                foreach (var tree in cell.Trees) {
                    CheckpointGuard.Require(tree.Id > 0 && float.IsFinite(tree.X) && float.IsFinite(tree.Y), "competition sample");
                    CheckpointGuard.Unit(tree.Health, "competition health");
                    CheckpointGuard.NonNegative(tree.Size.Diameter, "competition diameter");
                    CheckpointGuard.NonNegative(tree.Size.Height, "competition height"); CheckpointGuard.NonNegative(tree.Size.CrownRadius, "competition crown");
                    cell.MinX = Math.Min(cell.MinX, tree.X); cell.MaxX = Math.Max(cell.MaxX, tree.X);
                    cell.MinY = Math.Min(cell.MinY, tree.Y); cell.MaxY = Math.Max(cell.MaxY, tree.Y);
                    cell.MaxRadius = Math.Max(cell.MaxRadius, tree.Size.CrownRadius);
                }
                cells.Add(c.Tile, cell);
            }
            foreach (var n in s.Neighbours) {
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
                p.Value.Resources.Take(p.Value.Count).ToArray(), p.Value.Ids.Take(p.Value.Count).ToArray(),
                p.Value.Geometry.Take(p.Value.Count).ToArray(), p.Value.Stand)).ToArray(), revision, year, cursor, published,
            snapshotRepairs.ToArray(), resourceRepairs.ToArray());
        internal void Restore(PreparationCheckpoint s, ForestTreeStore store, int tileCount)
        {
            CheckpointGuard.Require(s != null && s.Patches != null && s.Prepared != null && s.SnapshotRepairs != null &&
                s.ResourceRepairs != null && s.Cursor >= 0 && s.Cursor <= (long)s.Patches.Length * 2, "preparation cursor");
            CheckpointGuard.NonNegative(s.Year, "preparation calendar");
            Clear(); competition.Restore(s.Competition, tileCount);
            var unique = new HashSet<int>();
            foreach (int id in s.Patches) {
                CheckpointGuard.Require((uint)id < (uint)tileCount && unique.Add(id), "preparation patch order");
                store.TryGet(id, out var patch); patches.Add(new(id, patch));
            }
            foreach (var p in s.Prepared) {
                CheckpointGuard.Require(p != null && (uint)p.Tile < (uint)tileCount && p.Ids != null && p.Resources != null &&
                    p.Geometry != null && p.Ids.Length == p.Resources.Length && p.Ids.Length == p.Geometry.Length &&
                    !prepared.ContainsKey(p.Tile), "prepared geometry");
                foreach (var r in p.Resources) { CheckpointGuard.Unit(r.Light, "prepared light");
                    CheckpointGuard.Unit(r.Water, "prepared water"); CheckpointGuard.Unit(r.Space, "prepared space"); }
                foreach (var g in p.Geometry) {
                    CheckpointGuard.NonNegative(g.VolumeIncrement, "prepared volume"); CheckpointGuard.Unit(g.SpaceResponse, "prepared space response");
                    foreach (double v in new[] { g.Dimensions.Diameter, g.Dimensions.Height, g.Dimensions.CrownRadius,
                        g.Growth.DiameterNumerator, g.Growth.HeightMultiplier, g.Growth.CrownRatio, g.Growth.CrownSpread })
                        CheckpointGuard.NonNegative(v, "prepared growth");
                    CheckpointGuard.Require(float.IsFinite(g.Growth.DiameterDenominator) && g.Growth.DiameterDenominator > 0, "prepared denominator");
                }
                prepared.Add(p.Tile, new PreparedPatch { Resources = (ForestResources[])p.Resources.Clone(), Ids = (ulong[])p.Ids.Clone(),
                    Geometry = (ForestBoundaryGeometry[])p.Geometry.Clone(), Stand = p.Stand, Count = p.Ids.Length });
            }
            foreach (int id in s.SnapshotRepairs) { CheckpointGuard.Require((uint)id < (uint)tileCount && snapshotPending.Add(id), "snapshot repair"); snapshotRepairs.Enqueue(id); }
            foreach (int id in s.ResourceRepairs) { CheckpointGuard.Require((uint)id < (uint)tileCount && resourcePending.Add(id), "resource repair"); resourceRepairs.Enqueue(id); }
            revision = s.Revision; year = s.Year; cursor = s.Cursor; published = s.Published;
        }
    }
}
