using System;
using System.Collections.Generic;
using ForesTycoon.Rules;

namespace ForesTycoon.Ecology;

sealed partial class ForestSystem
{
    private readonly record struct SeedSource(ForestStand Stand, int MatureCount, float BestMatureHealth);

    // Probe one actual opening each month. Its position is stable across months, so a new
    // seedling occupies the opening even before its crown becomes large enough to shade it.
    private bool TryFindGap(int tile, ForestTreeStore.Patch patch, double year, out float u, out float v)
    {
        u = v = 0;
        int stumps = patch.Stumps?.Count ?? 0, gaps = stumps + (patch.DeadTrees?.Count ?? 0);
        if (gaps == 0 || patch.Count >= 128) return false;
        int index = (int)((month + (uint)tile) % (ulong)gaps);
        var former = index < stumps ? patch.Stumps[index].Felled : patch.DeadTrees[index - stumps].Tree;
        var cell = habitat.GetForestTileGeometry(tile);
        // Establish beside the former stem. Use metric distances on rectangular tiles.
        u = Math.Clamp(former.U + (ForestTreeStore.Unit(former.Seed) - .5f) / cell.Width, 0, 1);
        v = Math.Clamp(former.V + (ForestTreeStore.Unit(ForestTreeStore.Random(former.Seed)) - .5f) / cell.Height, 0, 1);
        for (int i = 0; i < patch.Count; i++) {
            var tree = patch.Trees[i];
            float dx = (u - tree.U) * cell.Width, dy = (v - tree.V) * cell.Height;
            float radius = Math.Max(.5f, tree.At(year).CrownRadius);
            if (dx * dx + dy * dy <= radius * radius) return false;
        }
        return true;
    }

    private IEnumerable<int> RegenerateWork(SeedSource[] sources, double year)
    {
        int[] neighbors = new int[MaximumNeighbours + 1];
        for (int tile = 0; tile < sources.Length; tile++)
        {
            yield return 1;
            IndividualTrees.TryGet(tile, out var patch);
            bool occupied = patch != null && patch.Count > 0;
            float gapU = 0, gapV = 0;
            if (occupied && !TryFindGap(tile, patch, year, out gapU, out gapV)) continue;
            if (!occupied && !stands[tile].IsEmpty) continue;
            if (!habitat.CanSupportForest(tile)) continue;
            int count = habitat.GetAdjacentTileIds(tile, neighbors.AsSpan(0, MaximumNeighbours));
            float biomass = 0;
            for (int n = 0; n < count; n++) { biomass += sources[neighbors[n]].Stand.Biomass; yield return 1; }
            var query = new ForestRegenerationInputs {
                NeighborBiomass = count == 0 ? 0 : biomass / count, Moisture = habitat.GetMoisture(tile),
                Fertility = habitat.GetSoilProperties(tile).Fertility, Elevation = habitat.GetNormalizedElevation(tile),
                Radiation = Environment?.PeriodRadiation ?? 1, Years = YearsPerStep,
                Random = UnitFloat(Hash(habitat.Seed, tile, month))
            };
            query = query with { Crowding = Responses.Regeneration("forest.seedCrowding", query) };
            double supply = 0, best = 0;
            ForestSpecies selected = ForestSpecies.None;
            ForestRegenerationInputs selectedQuery = default;
            // A retained overstorey supplies its own gaps. Keep one species per tile, as assumed by
            // the timber price, the management survey and the stand-level seed-source contract.
            if (occupied) neighbors[count++] = tile;
            for (int n = 0; n < count; n++)
            {
                yield return 1;
                var snapshot = sources[neighbors[n]];
                var source = snapshot.Stand;
                if (source.IsEmpty || occupied && source.Species != patch.Trees[0].Species) continue;
                var profile = ForestSpeciesProfile.For(source.Species);
                var from = habitat.GetForestTileGeometry(neighbors[n]); var to = habitat.GetForestTileGeometry(tile);
                double dx = from.X + from.Width / 2 - to.X - to.Width / 2, dy = from.Y + from.Height / 2 - to.Y - to.Height / 2;
                var candidate = query with {
                    MatureCount = snapshot.MatureCount, BestMatureHealth = snapshot.BestMatureHealth,
                    Maturity = source.Maturity, Age = source.AgeYears, Health = source.Health, Biomass = source.Biomass,
                    ShadeTolerance = profile.ShadeTolerance, MaximumBiomass = profile.MaximumBiomass, MatureAge = profile.MatureAgeYears,
                    Distance = Math.Sqrt(dx * dx + dy * dy), Suitability = Suitability(source.Species, tile),
                    Water = Environment?.GrowthFactor(tile, source.Species) ?? 1
                };
                double seeds = Responses.Regeneration("forest.seedSupply", candidate);
                supply += seeds;
                if (seeds <= 0) continue;
                candidate = candidate with { Supply = seeds };
                double score = Responses.Regeneration("forest.seedSelection", candidate);
                // Stable adjacency order breaks equal scores deterministically.
                if (score <= best) continue;
                best = score; selected = source.Species; selectedQuery = candidate;
            }
            if (selected == ForestSpecies.None || supply <= 0) continue;
            selectedQuery = selectedQuery with { Supply = supply };
            yield return 1;
            if (Responses.Regeneration("forest.germination", selectedQuery) <= 0) continue;
            int initialCount = (int)Math.Round(Responses.Regeneration("forest.seedlingCount", selectedQuery));
            float health = (float)Responses.Regeneration("forest.seedlingHealth", selectedQuery);
            if (initialCount <= 0 || health <= 0) continue;
            float age = (float)Responses.Regeneration("forest.seedlingAge", selectedQuery);
            if (occupied)
                IndividualTrees.RecruitIntoGap(patch, tile, selected, gapU, gapV, age, year, health, habitat.Seed, Responses);
            else {
                foreach (int unit in IndividualTrees.CreateWork(tile, new(selected, age, 0, health), year, habitat.Seed, authoredCount: initialCount, responses: Responses)) yield return unit;
                IndividualTrees.TryGet(tile, out patch);
                foreach (int unit in UpdateIndividualRatesWork(tile, patch, year)) yield return unit;
            }
            if (SeedlingEstablished != null) {
                int first = occupied ? patch.Count - 1 : 0;
                for (int i = first; i < patch.Count; i++) {
                    SeedlingEstablished.Invoke(month, patch.Trees[i], occupied);
                    yield return 1;
                }
            }
            MarkResourceArea(tile);
            yield return 1;
        }
    }
}
