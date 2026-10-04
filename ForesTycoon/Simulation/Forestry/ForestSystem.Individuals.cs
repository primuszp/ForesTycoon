using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ForesTycoon
{
    sealed partial class ForestSystem
    {
        internal ForestTreeStore IndividualTrees { get; } = new();
        private ForestCompetition competition = new();
        private readonly ForestMonthlyPreparation monthlyPreparation = new();
        internal int LastMonthlyPreparedTrees { get; private set; }
        internal bool ProfileMonthlyWork { get; set; }
        internal ForestMonthProfile LastMonthProfile { get; private set; }
        internal int LastPreparationSnapshotPatches => monthlyPreparation.LastStepSnapshotPatches;
        internal int LastPreparationResourcePatches => monthlyPreparation.LastStepResourcePatches;
        private void CompleteLocalEditRevision()
        {
            ulong before = Revision;
            Revision++; EditRevision++;
            monthlyPreparation.AcceptLocalRevision(before, Revision);
        }
        internal void PrepareNextMonthStep()
        {
            if (Environment != null)
                monthlyPreparation.Advance(habitat, IndividualTrees, Revision, (month + 1) / 12.0, SecondsUntilMonth);
        }
        private readonly HashSet<int> changedResourceTiles = new();
        internal double ForestYear => month / 12.0 + accumulatedSeconds / secondsPerYear;
        internal int IndividualTreeCount => IndividualTrees.TreeCount;
        private double currentYearGrowth, lastYearGrowth;
        internal float LastAnnualGrowthCubicMetres => (float)lastYearGrowth;
        // Diagnostic fixtures only. Normal edits go through planting/harvest operations.
        internal void NotifyIndividualVisualEdit() {
            foreach (var entry in IndividualTrees.Patches) IndividualTrees.NotifyTopologyChanged(entry.Value);
            Revision++; EditRevision++;
        }

        private void InitializeIndividuals()
        {
            monthlyPreparation.Clear();
            currentYearGrowth = lastYearGrowth = 0;
            IndividualTrees.Clear();
            ClearPlantations(); competition.Clear(); changedResourceTiles.Clear();
            for (int id = 0; id < stands.Length; id++)
                if (!stands[id].IsEmpty) IndividualTrees.Create(id, stands[id], ForestYear, habitat.Seed);
            for (int id = 0; id < stands.Length; id++) stands[id] = IndividualStand(id, ForestYear);
            competition.Snapshot(habitat, IndividualTrees, ForestYear);
            foreach (var entry in IndividualTrees.Patches) UpdateIndividualRates(entry.Key, entry.Value, ForestYear);
        }

        private void CreateIndividuals(int id, ForestStand stand, double year, bool planted = false)
        {
            IndividualTrees.Create(id, stand, year, habitat.Seed, planted);
            IndividualTrees.TryGet(id, out var patch);
            if (!planted) UpdateIndividualRates(id, patch, year, updateHealth: false);
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

        private void UpdateIndividualRates(int id, ForestTreeStore.Patch patch, double year, bool updateHealth = true, bool currentConditions = false)
        {
            float radiation = Environment != null ? (float)(currentConditions ? Environment.Radiation : Environment.PeriodRadiation) : 1;
            ForestSpecies siteSpecies = ForestSpecies.None;
            float water = 0, fitness = 0;
            float season = ForestTreeGrowth.Season(year);
            for (int i = 0; i < patch.Count; i++)
            {
                ForestTree tree = patch.Trees[i];
                if (siteSpecies != tree.Species)
                {
                    siteSpecies = tree.Species;
                    water = Environment != null
                        ? (float)(currentConditions ? Environment.CurrentWaterFactor(id, tree.Species) : Environment.GrowthFactor(id, tree.Species))
                        : Math.Clamp(habitat.GetMoisture(id) / ForestSpeciesProfile.For(tree.Species).PreferredMoisture, 0, 1);
                    fitness = Suitability(tree.Species, id);
                }
                ForestResources resources = default;
                ForestBoundaryGeometry geometry = default;
                bool cached = updateHealth && !currentConditions && Environment != null
                    && monthlyPreparation.TryGet(Revision, year, id, i, tree.Id, water, radiation, out resources, out geometry);
                if (cached)
                {
                    LastMonthlyPreparedTrees++;
                    currentYearGrowth += geometry.VolumeIncrement;
                    tree = tree with { Dimensions = geometry.Dimensions, AnchorYear = year };
                }
                else
                {
                    tree = SettleIndividual(tree, year);
                    resources = competition.Evaluate(habitat, tree, year, water, radiation, Environment != null);
                    geometry = new(tree.Dimensions, 0, ForestTreeGrowth.Shape(tree.Species, tree.At(year)),
                        MathF.Sqrt(Math.Clamp(resources.Space, 0, 1)));
                }
                float lightResponse = resources.LightResponse(tree.Species);
                float limit = Math.Min(resources.Space, Math.Min(resources.Water, lightResponse));
                float target = Math.Clamp(fitness * (0.15f + 0.85f * limit), 0, 1);
                if (updateHealth) tree = tree with { Health = MoveTowards(tree.Health, target, 0.035f) };
                patch.Trees[i] = tree with { Resources = resources,
                    AnnualGrowth = geometry.Growth.Apply(ForestTreeGrowth.Factor(fitness, lightResponse,
                        resources.Water, geometry.SpaceResponse, tree.Health, season)) };
            }
            patch.Revision++;
        }

        internal void RefreshEnvironmentRates()
        {
            competition.Snapshot(habitat, IndividualTrees, ForestYear);
            foreach (var entry in IndividualTrees.Patches)
                UpdateIndividualRates(entry.Key, entry.Value, ForestYear, updateHealth: false, currentConditions: true);
            Revision++;
        }

        private void MarkResourceArea(int tileId)
        {
            changedResourceTiles.Add(tileId);
            Span<int> first = stackalloc int[4], second = stackalloc int[4];
            int count = habitat.GetAdjacentTileIds(tileId, first);
            for (int i = 0; i < count; i++)
            {
                changedResourceTiles.Add(first[i]);
                int neighbours = habitat.GetAdjacentTileIds(first[i], second);
                for (int j = 0; j < neighbours; j++) changedResourceTiles.Add(second[j]);
            }
        }

        private void RefreshChangedResourceRates(double year, bool currentConditions)
        {
            if (changedResourceTiles.Count == 0) return;
            ulong before = Revision;
            competition.SnapshotAffected(habitat, IndividualTrees, year, changedResourceTiles);
            foreach (int id in changedResourceTiles)
                if (IndividualTrees.TryGet(id, out var patch))
                {
                    UpdateIndividualRates(id, patch, year, updateHealth: false, currentConditions: currentConditions);
                    stands[id] = IndividualStand(id, year);
                }
            RecalculateStatistics();
            Revision++;
            monthlyPreparation.InvalidateLocal(habitat, before, Revision, (month + 1) / 12.0, changedResourceTiles);
            changedResourceTiles.Clear();
        }
        private void StepIndividualMonth()
        {
            long started = ProfileMonthlyWork ? Stopwatch.GetTimestamp() : 0;
            LastMonthlyPreparedTrees = 0;
            double year = month / 12.0;
            // Close all growth intervals first. Competition reads a common immutable tile snapshot.
            foreach (var entry in IndividualTrees.Patches)
                stands[entry.Key] = monthlyPreparation.TryGetStand(Revision, year, entry.Key, out var preparedStand)
                    ? preparedStand : IndividualStand(entry.Key, year);
            long closed = ProfileMonthlyWork ? Stopwatch.GetTimestamp() : 0;
            if (!monthlyPreparation.PublishSnapshot(Revision, year, ref competition))
                competition.Snapshot(habitat, IndividualTrees, year);
            ClearSeedCandidates();
            for (int id = 0; id < stands.Length; id++)
                if (IsSeedSource(stands[id])) MarkSeedCandidates(id);
            long seeded = ProfileMonthlyWork ? Stopwatch.GetTimestamp() : 0;
            foreach (var entry in IndividualTrees.Patches)
            {
                var patch = entry.Value;
                UpdateIndividualRates(entry.Key, patch, year);
                for (int i = patch.Count - 1; i >= 0; i--)
                {
                    var tree = patch.Trees[i];
                    float shadeTolerance = ForestSpeciesProfile.For(tree.Species).ShadeTolerance;
                    // Pioneers need more sustained light; shade-tolerant species can
                    // persist with fewer resources. Thresholds are gameplay parameters.
                    float stressThreshold = 0.25f + 0.25f * (1 - shadeTolerance);
                    bool suppressed = tree.Resources.Limitation(tree.Species) < stressThreshold || tree.Health < 0.18f;
                    float stress = suppressed ? tree.StressYears + YearsPerStep : Math.Max(0, tree.StressYears - YearsPerStep * 0.5f);
                    tree = tree with { StressYears = stress };
                    patch.Trees[i] = tree;
                    if ((stress >= 4 && tree.Health < 0.4f + 0.25f * (1 - shadeTolerance))
                        || tree.Age(year) > ForestSpeciesProfile.For(tree.Species).MaximumAgeYears)
                    {
                        (patch.DeadTrees ??= new()).Add(new(tree with { AnnualGrowth = default, Health = 0 }, year));
                        IndividualTrees.RemoveLiving(patch, i);
                        MarkResourceArea(entry.Key);
                    }
                }
                if (patch.DeadTrees != null)
                    for (int i = patch.DeadTrees.Count - 1; i >= 0; i--)
                        if (year - patch.DeadTrees[i].DeathYear >= 8) {
                            patch.DeadTrees.RemoveAt(i); IndividualTrees.NotifyTopologyChanged(patch);
                        }
                if (patch.Stumps == null) continue;
                for (int i = patch.Stumps.Count - 1; i >= 0; i--)
                    if (patch.Stumps[i].Decay(year) >= 1) {
                        patch.Stumps.RemoveAt(i); IndividualTrees.NotifyTopologyChanged(patch);
                    }
            }
            long grown = ProfileMonthlyWork ? Stopwatch.GetTimestamp() : 0;
            for (int i = 0; i < seedCandidateCount; i++)
            {
                int id = seedCandidateTiles[i];
                if (!stands[id].IsEmpty) continue;
                ForestStand seedling = TryRegenerate(id);
                if (!seedling.IsEmpty) { CreateIndividuals(id, seedling, year); MarkResourceArea(id); }
            }
            RefreshChangedResourceRates(year, currentConditions: false);
            foreach (var entry in IndividualTrees.Patches) stands[entry.Key] = IndividualStand(entry.Key, year);
            RecalculateStatistics();
            if (month % 12 == 0)
            {
                lastYearGrowth = currentYearGrowth;
                currentYearGrowth = 0;
            }
            Revision++;
            if (ProfileMonthlyWork)
                LastMonthProfile = new(Stopwatch.GetElapsedTime(started, closed).TotalMilliseconds,
                    Stopwatch.GetElapsedTime(closed, seeded).TotalMilliseconds,
                    Stopwatch.GetElapsedTime(seeded, grown).TotalMilliseconds,
                    Stopwatch.GetElapsedTime(grown).TotalMilliseconds);
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
                MarkResourceArea(id); RefreshChangedResourceRates(ForestYear, currentConditions: true);
                CompleteLocalEditRevision();
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
            MarkResourceArea(id); RefreshChangedResourceRates(ForestYear, currentConditions: true);
            CompleteLocalEditRevision();
            return ForestryActionResult.Harvested;
        }
    }
}
