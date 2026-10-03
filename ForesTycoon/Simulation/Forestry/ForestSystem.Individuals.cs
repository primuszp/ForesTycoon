using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    sealed partial class ForestSystem
    {
        internal ForestTreeStore IndividualTrees { get; } = new();
        internal double ForestYear => month / 12.0 + accumulatedSeconds / secondsPerYear;
        internal int IndividualTreeCount => IndividualTrees.TreeCount;
        private double currentYearGrowth, lastYearGrowth;
        internal float LastAnnualGrowthCubicMetres => (float)lastYearGrowth;
        // Diagnostic fixtures only. Normal edits go through planting/harvest operations.
        internal void NotifyIndividualVisualEdit() { Revision++; EditRevision++; }

        private void InitializeIndividuals()
        {
            currentYearGrowth = lastYearGrowth = 0;
            IndividualTrees.Clear();
            for (int id = 0; id < stands.Length; id++)
                if (!stands[id].IsEmpty) IndividualTrees.Create(id, stands[id], ForestYear, habitat.Seed);
            for (int id = 0; id < stands.Length; id++) stands[id] = IndividualStand(id, ForestYear);
            foreach (var entry in IndividualTrees.Patches) UpdateIndividualRates(entry.Key, entry.Value, ForestYear);
        }

        private void CreateIndividuals(int id, ForestStand stand, double year)
        {
            IndividualTrees.Create(id, stand, year, habitat.Seed);
            IndividualTrees.TryGet(id, out var patch);
            UpdateIndividualRates(id, patch, year);
        }

        private ForestStand IndividualStand(int id, double year)
        {
            if (!IndividualTrees.TryGet(id, out var patch) || patch.Count == 0) return default;
            float age = 0, health = 0, volume = 0;
            for (int i = 0; i < patch.Count; i++)
            {
                var tree = patch.Trees[i];
                age += tree.Age(year); health += tree.Health; volume += ForestTree.Volume(tree.At(year));
            }
            return new(patch.Trees[0].Species, age / patch.Count, volume / 100, health / patch.Count);
        }

        internal float AvailableTimber(int id)
        {
            if (!IndividualTrees.TryGet(id, out var patch)) return 0;
            float volume = patch.Depot;
            for (int i = 0; i < patch.Count; i++) volume += ForestTree.Volume(patch.Trees[i].At(ForestYear));
            return volume;
        }

        private void UpdateIndividualRates(int id, ForestTreeStore.Patch patch, double year)
        {
            float crowding = GetCrowding(id);
            for (int i = 0; i < patch.Count; i++)
            {
                ForestTree tree = SettleIndividual(patch.Trees[i], year);
                float water = (float)(Environment?.GrowthFactor(id, tree.Species) ?? 1);
                float fitness = Suitability(tree.Species, id);
                float light = Math.Clamp(1 - crowding * (1 - ForestSpeciesProfile.For(tree.Species).ShadeTolerance), 0.05f, 1);
                float target = Math.Clamp((0.25f + fitness * 0.75f) * (0.55f + light * 0.45f) * water, 0, 1);
                tree = tree with { Health = MoveTowards(tree.Health, target, 0.035f) };
                patch.Trees[i] = tree with { AnnualGrowth = ForestTreeGrowth.Rates(tree, year, fitness, crowding, water) };
            }
            patch.Revision++;
        }

        private void StepIndividualMonth()
        {
            double year = month / 12.0;
            // Close all growth intervals first. Competition reads a common immutable tile snapshot.
            foreach (var entry in IndividualTrees.Patches)
                stands[entry.Key] = IndividualStand(entry.Key, year);
            ClearSeedCandidates();
            for (int id = 0; id < stands.Length; id++)
                if (IsSeedSource(stands[id])) MarkSeedCandidates(id);
            foreach (var entry in IndividualTrees.Patches)
            {
                var patch = entry.Value;
                UpdateIndividualRates(entry.Key, patch, year);
                if (patch.Stumps == null) continue;
                for (int i = patch.Stumps.Count - 1; i >= 0; i--)
                    if (patch.Stumps[i].Decay(year) >= 1) patch.Stumps.RemoveAt(i);
            }
            for (int i = 0; i < seedCandidateCount; i++)
            {
                int id = seedCandidateTiles[i];
                if (!stands[id].IsEmpty) continue;
                ForestStand seedling = TryRegenerate(id);
                if (!seedling.IsEmpty) CreateIndividuals(id, seedling, year);
            }
            foreach (var entry in IndividualTrees.Patches) stands[entry.Key] = IndividualStand(entry.Key, year);
            RecalculateStatistics();
            Environment?.FinishForestMonth();
            if (month % 12 == 0)
            {
                lastYearGrowth = currentYearGrowth;
                currentYearGrowth = 0;
            }
            Revision++;
        }

        private ForestTree SettleIndividual(ForestTree tree, double year)
        {
            ForestTree settled = tree.Settle(year);
            currentYearGrowth += Math.Max(0, ForestTree.Volume(settled.Dimensions) - ForestTree.Volume(tree.Dimensions));
            return settled;
        }

        private void FellIndividual(ForestTreeStore.Patch patch, int index)
        {
            ForestTree tree = SettleIndividual(patch.Trees[index], ForestYear) with { AnnualGrowth = default };
            patch.Depot += ForestTree.Volume(tree.Dimensions);
            (patch.Stumps ??= new List<ForestTreeStump>()).Add(new(tree, ForestYear));
            IndividualTrees.RemoveLiving(patch, index);
        }

        private float ExtractIndividualTimber(int id, float requested)
        {
            if (requested == 0 || !IndividualTrees.TryGet(id, out var patch)) return 0;
            // Fell whole trees into a depot; a partial truck load never shrinks surviving trees.
            bool felled = false;
            while (patch.Depot < requested && patch.Count > 0)
            {
                FellIndividual(patch, 0);
                felled = true;
            }
            float loaded = Math.Min(requested, patch.Depot);
            patch.Depot = Math.Max(0, patch.Depot - loaded);
            if (felled)
            {
                stands[id] = IndividualStand(id, ForestYear);
                RecalculateStatistics();
                Revision++; EditRevision++;
            }
            return loaded;
        }

        private ForestryActionResult HarvestIndividuals(int id, out ForestHarvest harvest)
        {
            harvest = default;
            if ((uint)id >= (uint)stands.Length) return ForestryActionResult.InvalidTile;
            if (!IndividualTrees.TryGet(id, out var patch) || patch.Count == 0) return ForestryActionResult.NoForest;
            ForestStand stand = IndividualStand(id, ForestYear);
            float volume = AvailableTimber(id);
            while (patch.Count > 0) FellIndividual(patch, 0);
            // Immediate harvest transfers the depot directly to its caller.
            patch.Depot = 0;
            harvest = new(stand.Species, stand.AgeYears, volume);
            stands[id] = default;
            RecalculateStatistics();
            Revision++; EditRevision++;
            return ForestryActionResult.Harvested;
        }
    }
}
