using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon.Ecology
{
    internal sealed partial class ForestTreeStore
    {
        internal TreeStoreCheckpoint Capture() => new(patches.Select(p => new TreePatchCheckpoint(p.Key,
            p.Value.Trees.Take(p.Value.Count).ToArray(), p.Value.Stumps?.ToArray() ?? Array.Empty<ForestTreeStump>(),
            p.Value.DeadTrees?.ToArray() ?? Array.Empty<ForestDeadTree>(), p.Value.Depot, p.Value.Revision,
            p.Value.TopologyRevision)).ToArray(), nextId, topologyRevision, Generation, slots.ToArray(), freeSlots.ToArray());

        internal void Restore(TreeStoreCheckpoint s, int tileCount, double year)
        {
            ArgumentNullException.ThrowIfNull(s);
            CheckpointGuard.Require(s.Patches != null && s.Slots != null && s.FreeSlots != null && s.NextId > 0, "tree store");
            var replacement = new Dictionary<int, Patch>(); var ids = new HashSet<ulong>();
            foreach (var p in s.Patches)
            {
                CheckpointGuard.Require(p != null && (uint)p.TileId < (uint)tileCount && p.Trees != null &&
                    p.Stumps != null && p.DeadTrees != null && !replacement.ContainsKey(p.TileId), "tree patch");
                CheckpointGuard.NonNegative(p.Depot, "timber depot");
                CheckpointGuard.Require(p.TopologyRevision <= s.TopologyRevision, "tree topology revision");
                foreach (var tree in p.Trees) ValidateTree(tree, p.TileId, year, s.NextId, ids);
                foreach (var stump in p.Stumps) {
                    ValidateTree(stump.Felled, p.TileId, year, s.NextId, ids);
                    CheckpointGuard.Require(double.IsFinite(stump.FelledYear) && stump.FelledYear >= 0 && stump.FelledYear <= year, "stump time");
                }
                foreach (var dead in p.DeadTrees) {
                    ValidateTree(dead.Tree, p.TileId, year, s.NextId, ids);
                    CheckpointGuard.Require(double.IsFinite(dead.DeathYear) && dead.DeathYear >= 0 && dead.DeathYear <= year, "dead tree time");
                }
                replacement.Add(p.TileId, new Patch(Math.Max(16, p.Trees.Length)) {
                    Count = p.Trees.Length, Depot = p.Depot, Revision = p.Revision, TopologyRevision = p.TopologyRevision,
                    Stumps = new List<ForestTreeStump>(p.Stumps), DeadTrees = new List<ForestDeadTree>(p.DeadTrees)
                });
                p.Trees.CopyTo(replacement[p.TileId].Trees, 0);
            }
            var live = new HashSet<int>(); var holes = new HashSet<int>();
            for (int i = 0; i < s.Slots.Length; i++)
                if (s.Slots[i] == -1) holes.Add(i);
                else CheckpointGuard.Require(live.Add(s.Slots[i]) && replacement.ContainsKey(s.Slots[i]), "tree slot");
            CheckpointGuard.Require(live.Count == replacement.Count && holes.SetEquals(s.FreeSlots) &&
                holes.Count == s.FreeSlots.Length, "tree free slots");
            Clear();
            // Dummy keys reconstruct holes without serializing runtime internals or using reflection.
            for (int i = 0; i < s.Slots.Length; i++)
            {
                int id = s.Slots[i];
                patches.Add(id == -1 ? -i - 1 : id, id == -1 ? new Patch(0) : replacement[id]);
                slots.Add(id); if (id >= 0) slotOf.Add(id, i);
            }
            for (int i = s.FreeSlots.Length - 1; i >= 0; i--) {
                int slot = s.FreeSlots[i]; patches.Remove(-slot - 1); freeSlots.Push(slot);
            }
            TreeCount = replacement.Values.Sum(p => p.Count);
            nextId = s.NextId; topologyRevision = s.TopologyRevision; Generation = s.Generation;
        }

        private static void ValidateTree(ForestTree t, int tile, double year, ulong next, HashSet<ulong> ids)
        {
            CheckpointGuard.Require(t.Id > 0 && t.Id < next && ids.Add(t.Id) && t.TileId == tile &&
                Enum.IsDefined(t.Species) && t.Species != ForestSpecies.None, "tree identity/species");
            CheckpointGuard.Unit(t.U, "tree U"); CheckpointGuard.Unit(t.V, "tree V"); CheckpointGuard.Unit(t.Health, "tree health");
            CheckpointGuard.Require(double.IsFinite(t.BirthYear) && double.IsFinite(t.AnchorYear) && t.BirthYear <= year &&
                t.AnchorYear >= 0 && t.AnchorYear <= year + 1e-10, "tree calendar");
            foreach (double v in new[] { t.Dimensions.Diameter, t.Dimensions.Height, t.Dimensions.CrownRadius,
                t.AnnualGrowth.Diameter, t.AnnualGrowth.Height, t.AnnualGrowth.CrownRadius, t.StressYears })
                CheckpointGuard.NonNegative(v, "tree dimension/growth/stress");
            CheckpointGuard.Unit(t.Resources.Light, "tree light"); CheckpointGuard.Unit(t.Resources.Water, "tree water");
            CheckpointGuard.Unit(t.Resources.Space, "tree space");
        }
    }

    sealed partial class ForestSystem
    {
        internal ForestCheckpoint Capture() => new(month, accumulatedSeconds, Revision, EditRevision, currentYearGrowth,
            lastYearGrowth, nextPlantationId, PlantationRevision, (ForestStand[])stands.Clone(), IndividualTrees.Capture(),
            plantations.OrderBy(p => p.Key).Select(p => new PlantationCheckpoint(p.Key, p.Value)).ToArray(), competition.Capture(), monthlyPreparation.Capture());

        internal void Restore(ForestCheckpoint s)
        {
            ArgumentNullException.ThrowIfNull(s);
            CheckpointGuard.Length(s.Stands, stands.Length, "forest stands");
            CheckpointGuard.Require(double.IsFinite(s.AccumulatedSeconds) && s.AccumulatedSeconds >= 0 &&
                s.AccumulatedSeconds < secondsPerYear / 12 && s.NextPlantationId > 0 && s.Plantations != null, "forest calendar");
            CheckpointGuard.NonNegative(s.CurrentYearGrowth, "annual growth"); CheckpointGuard.NonNegative(s.LastYearGrowth, "last annual growth");
            foreach (var stand in s.Stands) {
                CheckpointGuard.Require(Enum.IsDefined(stand.Species), "stand species");
                CheckpointGuard.NonNegative(stand.AgeYears, "stand age"); CheckpointGuard.NonNegative(stand.Biomass, "stand biomass");
                CheckpointGuard.Unit(stand.Health, "stand health");
            }
            double year = s.Month / 12.0 + s.AccumulatedSeconds / secondsPerYear;
            var planted = new Dictionary<int, ForestPlantation>();
            foreach (var p in s.Plantations) {
                CheckpointGuard.Require(p != null && (uint)p.TileId < (uint)stands.Length && p.Plantation.AreaId > 0 &&
                    p.Plantation.AreaId < s.NextPlantationId && Enum.IsDefined(p.Plantation.Species) && p.Plantation.Species != ForestSpecies.None &&
                    double.IsFinite(p.Plantation.PlantedYear) && p.Plantation.PlantedYear >= 0 && p.Plantation.PlantedYear <= year &&
                    p.Plantation.InitialTrees > 0 && planted.TryAdd(p.TileId, p.Plantation), "plantation");
            }
            IndividualTrees.Restore(s.Trees, stands.Length, year);
            s.Stands.CopyTo(stands, 0); month = s.Month; accumulatedSeconds = s.AccumulatedSeconds;
            Revision = s.Revision; EditRevision = s.EditRevision; currentYearGrowth = s.CurrentYearGrowth; lastYearGrowth = s.LastYearGrowth;
            nextPlantationId = s.NextPlantationId; PlantationRevision = s.PlantationRevision;
            plantations.Clear(); foreach (var p in planted) plantations.Add(p.Key, p.Value);
            competition.Restore(s.Competition, stands.Length);
            monthlyPreparation.Restore(s.Preparation, IndividualTrees, stands.Length); changedResourceTiles.Clear();
            Array.Clear(seedCandidate); seedCandidateCount = 0; RecalculateStatistics();
        }
    }
}
