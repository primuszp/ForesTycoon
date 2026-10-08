using System;

namespace ForesTycoon.Ecology
{
    /// <summary>The resource that holds a stand's growth back the most.</summary>
    internal enum ForestLimit : byte { None, Light, Water, Space }

    /// <summary>What the management view recommends for a tile, most urgent first (docs/management-view-design.md).</summary>
    internal enum ManagementIssue : byte { None, Dieback, Waterlogging, Drought, Overstocked, Harvestable, Regenerate }

    /// <summary>Monthly, read-only per-tile summary for the management lenses. Never advances the simulation.</summary>
    internal readonly record struct ForestTileSurvey(
        ForestSpecies Species, float AgeYears, float Maturity, float Health, int Trees, float Stocking,
        float VolumeCubicMetres, float DeadShare, float StressedShare, float CrowdedShare, ForestLimit Limit,
        float WaterAvailability, float Drought, float Waterlogging, float Fertility, bool Forestable, bool Cleared,
        ManagementIssue Issue, byte Severity)
    {
        internal bool IsForest => Species != ForestSpecies.None;
    }

    /// <summary>Diagnosis thresholds. Kept as data so they can be tuned and tested in isolation.</summary>
    internal sealed record ManagementRules(
        float DroughtAvailability = 0.25f, float DroughtStress = 0.35f, float Waterlogging = 0.5f,
        float OverstockedShare = 0.4f, float OverstockedSpace = 0.6f, float DiebackDeadShare = 0.3f,
        float DiebackHealth = 0.3f, float HarvestMaturity = 1f, float RegenerateFertility = 0.45f)
    {
        internal static ManagementRules Default { get; } = new();
    }

    internal static class ForestManagementSurvey
    {
        /// <summary>Summarises every tile from the published forest, water and soil state.</summary>
        internal static ForestTileSurvey[] Build(ForestSystem forest, EnvironmentSystem water, SoilLandscape soils,
            ManagementRules rules = null)
        {
            rules ??= ManagementRules.Default;
            var habitat = forest.Habitat;
            int count = habitat.TileCount;
            var result = new ForestTileSurvey[count];
            double year = forest.ForestYear;
            for (int id = 0; id < count; id++)
            {
                var soil = soils?.Profile(id).Properties ?? habitat.GetSoilProperties(id);
                float availability = 1, drought = 0, waterlogging = 0;
                if (water != null && id < water.CellCount)
                {
                    var cell = water.Cell(id);
                    availability = (float)soil.Availability(cell.Soil);
                    drought = (float)cell.Drought; waterlogging = (float)cell.Waterlogging;
                }
                var survey = new ForestTileSurvey(ForestSpecies.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, ForestLimit.None,
                    availability, drought, waterlogging, soil.Fertility, habitat.CanSupportForest(id), false, ManagementIssue.None, 0);
                if (forest.IndividualTrees.TryGet(id, out var patch))
                    survey = patch.Count > 0 ? Stand(survey, patch, year, rules)
                        // Felled or died out: stumps or standing dead wood without living trees.
                        : survey with { Cleared = patch.Stumps?.Count > 0 || patch.DeadTrees?.Count > 0 };
                result[id] = survey;
            }
            for (int id = 0; id < count; id++)
            {
                var (issue, severity) = Diagnose(result[id], rules);
                result[id] = result[id] with { Issue = issue, Severity = severity };
            }
            return result;
        }

        private static ForestTileSurvey Stand(ForestTileSurvey site, ForestTreeStore.Patch patch, double year, ManagementRules rules)
        {
            var species = patch.Trees[0].Species;
            float age = 0, health = 0, volume = 0;
            int stressed = 0, crowded = 0;
            Span<int> limits = stackalloc int[4];
            for (int i = 0; i < patch.Count; i++)
            {
                var tree = patch.Trees[i];
                age += tree.Age(year); health += tree.Health; volume += ForestTree.Volume(tree.At(year));
                if (tree.StressYears > 0) stressed++;
                var r = tree.Resources;
                if (r == default) continue;
                float light = r.LightResponse(tree.Species), limitation = r.Limitation(tree.Species);
                if (r.Space < rules.OverstockedSpace && r.Space <= Math.Min(light, r.Water)) crowded++;
                // Only count a limit that actually binds.
                if (limitation < 0.85f)
                    limits[limitation == r.Space ? (int)ForestLimit.Space : limitation == r.Water ? (int)ForestLimit.Water : (int)ForestLimit.Light]++;
            }
            int dead = patch.DeadTrees?.Count ?? 0;
            var limit = ForestLimit.None;
            for (int l = 1; l < limits.Length; l++) if (limits[l] > limits[(int)limit]) limit = (ForestLimit)l;
            float meanAge = age / patch.Count;
            int capacity = Math.Max(1, ForestTreeGrowth.Capacity(species));
            return site with
            {
                Species = species, AgeYears = meanAge, Health = health / patch.Count, Trees = patch.Count,
                Maturity = Math.Clamp(meanAge / ForestSpeciesProfile.For(species).MatureAgeYears, 0, 2),
                Stocking = Math.Clamp((float)patch.Count / capacity, 0, 1.5f), VolumeCubicMetres = volume,
                DeadShare = (float)dead / (dead + patch.Count), StressedShare = (float)stressed / patch.Count,
                CrowdedShare = (float)crowded / patch.Count, Limit = limit
            };
        }

        /// <summary>The most urgent recommendation for one tile and its severity (1 = watch, 3 = act now).</summary>
        internal static (ManagementIssue Issue, byte Severity) Diagnose(in ForestTileSurvey s, ManagementRules rules)
        {
            // Open land is not a problem; a clearing left by felling or dieback is.
            if (!s.IsForest)
                return s.Forestable && s.Cleared && s.Fertility >= rules.RegenerateFertility
                    ? (ManagementIssue.Regenerate, (byte)1) : (ManagementIssue.None, (byte)0);
            if (s.DeadShare > rules.DiebackDeadShare || s.Health < rules.DiebackHealth)
                return (ManagementIssue.Dieback, Grade(Math.Max(s.DeadShare / rules.DiebackDeadShare, rules.DiebackHealth / Math.Max(0.05f, s.Health))));
            if (s.Waterlogging > rules.Waterlogging)
                return (ManagementIssue.Waterlogging, Grade(s.Waterlogging / rules.Waterlogging));
            if (s.WaterAvailability < rules.DroughtAvailability && (s.Limit == ForestLimit.Water || s.Drought > rules.DroughtStress))
                return (ManagementIssue.Drought, Grade(rules.DroughtAvailability / Math.Max(0.02f, s.WaterAvailability)));
            if (s.CrowdedShare > rules.OverstockedShare)
                return (ManagementIssue.Overstocked, Grade(s.CrowdedShare / rules.OverstockedShare));
            if (s.Maturity >= rules.HarvestMaturity)
                return (ManagementIssue.Harvestable, Grade(s.Maturity / rules.HarvestMaturity));
            return (ManagementIssue.None, 0);
        }

        // 1 just past the threshold, 2 at 1.5x, 3 at 2x and beyond.
        private static byte Grade(float ratio) => ratio >= 2 ? (byte)3 : ratio >= 1.5f ? (byte)2 : (byte)1;

        /// <summary>Playable trees ranked by how well they suit a tile's site (0..1).</summary>
        internal static (ForestSpecies Species, float Suitability)[] Recommend(IForestHabitat habitat, int tileId, int top = 3)
        {
            var ranked = new System.Collections.Generic.List<(ForestSpecies, float)>();
            float fertility = habitat.GetSoilProperties(tileId).Fertility;
            foreach (var species in ForestSpeciesTraits.Playable)
            {
                if (ForestSpeciesTraits.For(species).Shrub) continue;
                float fit = ForestSystem.Fitness(species, habitat.GetMoisture(tileId), habitat.GetNormalizedElevation(tileId));
                ranked.Add((species, fit * (0.6f + 0.4f * fertility)));
            }
            ranked.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return ranked.GetRange(0, Math.Min(top, ranked.Count)).ToArray();
        }
    }
}
