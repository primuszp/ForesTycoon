using System;
using System.Collections.Generic;
using ForesTycoon.Rules;

namespace ForesTycoon.Ecology;

sealed partial class ForestSystem
{
    // Four adjacent cells are the spatial contract, not an authored biological restriction.
    // Every occupied source is offered to the graphs, regardless of maturity or health.
    private IEnumerable<int> RegenerateWork(ForestStand[] sources, double year)
    {
        int[] neighbors = new int[MaximumNeighbours];
        for (int tile = 0; tile < sources.Length; tile++)
        {
            yield return 1;
            if (!stands[tile].IsEmpty || !habitat.CanSupportForest(tile)) continue;
            int count = habitat.GetAdjacentTileIds(tile, neighbors);
            float biomass = 0;
            for (int n = 0; n < count; n++) { biomass += sources[neighbors[n]].Biomass; yield return 1; }
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
            for (int n = 0; n < count; n++)
            {
                yield return 1;
                var source = sources[neighbors[n]];
                if (source.IsEmpty) continue;
                var profile = ForestSpeciesProfile.For(source.Species);
                var from = habitat.GetForestTileGeometry(neighbors[n]); var to = habitat.GetForestTileGeometry(tile);
                double dx = from.X + from.Width / 2 - to.X - to.Width / 2, dy = from.Y + from.Height / 2 - to.Y - to.Height / 2;
                var candidate = query with {
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
            IndividualTrees.Create(tile, new(selected, age, 0, health), year, habitat.Seed, authoredCount: initialCount);
            IndividualTrees.TryGet(tile, out var patch);
            foreach (int unit in UpdateIndividualRatesWork(tile, patch, year)) yield return unit;
            MarkResourceArea(tile);
            yield return 1;
        }
    }
}
