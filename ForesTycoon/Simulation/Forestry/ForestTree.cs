using System;

namespace ForesTycoon
{
    // Dimensions are physical metres. Rendering maps these to the diorama independently.
    internal readonly record struct ForestTreeDimensions(float Diameter, float Height, float CrownRadius);
    internal readonly record struct ForestTree(
        ulong Id, int TileId, ForestSpecies Species, float U, float V, uint Seed,
        double BirthYear, double AnchorYear, ForestTreeDimensions Dimensions,
        ForestTreeDimensions AnnualGrowth, float Health, ForestResources Resources = default, float StressYears = 0)
    {
        internal ForestTreeDimensions At(double year)
        {
            float elapsed = (float)Math.Max(0, year - AnchorYear);
            return new(Dimensions.Diameter + AnnualGrowth.Diameter * elapsed,
                Dimensions.Height + AnnualGrowth.Height * elapsed,
                Dimensions.CrownRadius + AnnualGrowth.CrownRadius * elapsed);
        }

        internal ForestTree Settle(double year) => this with { Dimensions = At(year), AnchorYear = year };
        internal float Age(double year) => (float)Math.Max(0, year - BirthYear);
        internal static float Volume(ForestTreeDimensions size) =>
            MathF.PI * size.Diameter * size.Diameter * 0.25f * size.Height * 0.45f;
    }

    internal readonly record struct ForestTreeStump(ForestTree Felled, double FelledYear)
    {
        internal const float LifetimeYears = 6f;
        internal float Decay(double year) => (float)Math.Clamp((year - FelledYear) / LifetimeYears, 0, 1);
    }

    internal readonly record struct ForestDeadTree(ForestTree Tree, double DeathYear);

    /// <summary>Species-specific gameplay growth curves in physical metres.</summary>
    internal static class ForestTreeGrowth
    {
        internal static int Capacity(ForestSpecies species) => species switch
        {
            ForestSpecies.Spruce => 14, ForestSpecies.Birch => 12,
            ForestSpecies.Oak => 4, ForestSpecies.Beech => 7, _ => 0
        };

        internal static ForestTreeDimensions Initial(ForestSpecies species, float age, float variation)
        {
            var (height, diameter) = species switch
            {
                ForestSpecies.Spruce => (34f, 0.52f), ForestSpecies.Birch => (24f, 0.30f),
                ForestSpecies.Oak => (27f, 0.70f), ForestSpecies.Beech => (31f, 0.56f),
                _ => throw new ArgumentOutOfRangeException(nameof(species))
            };
            float mature = ForestSpeciesProfile.For(species).MatureAgeYears;
            float growth = 1 - MathF.Exp(-Math.Max(0, age) / mature * 1.8f);
            float h = (0.8f + (height - 0.8f) * growth) * variation;
            float d = (0.012f + diameter * growth) * variation;
            return new(d, h, Math.Max(0.15f, h * CrownRatio(species)));
        }

        private static float CrownRatio(ForestSpecies species) => species switch
        {
            ForestSpecies.Oak => 0.28f, ForestSpecies.Spruce => 0.26f,
            ForestSpecies.Birch => 0.16f, _ => 0.22f
        };

        internal static ForestTreeDimensions Rates(in ForestTree tree, double year, float fitness, float crowding, float water)
        {
            float light = Math.Clamp(1 - crowding * (1 - ForestSpeciesProfile.For(tree.Species).ShadeTolerance), 0.05f, 1);
            return RatesWithResources(tree, year, fitness, new(light, water, 1));
        }

        internal static ForestTreeDimensions RatesWithResources(in ForestTree tree, double year, float fitness, ForestResources resources)
        {
            ForestTreeDimensions size = tree.At(year);
            float season = Math.Clamp(0.65f + 0.8f * MathF.Cos(MathF.Tau * ((float)(year % 1) - 0.25f)), 0, 1.45f);
            float factor = Math.Clamp(fitness, 0, 1) * resources.LightResponse(tree.Species)
                * Math.Clamp(resources.Water, 0, 1) * MathF.Sqrt(Math.Clamp(resources.Space, 0, 1)) * tree.Health * season;
            float maxHeight = tree.Species switch { ForestSpecies.Spruce => 40, ForestSpecies.Birch => 28, ForestSpecies.Oak => 35, _ => 38 };
            float radial = tree.Species == ForestSpecies.Birch ? 0.0055f : tree.Species == ForestSpecies.Oak ? 0.0045f : 0.005f;
            // Mature trees continue thickening; height approaches a species-dependent envelope.
            float diameter = radial * 2 * factor / (1 + size.Diameter * 0.8f);
            float height = 0.9f * Math.Max(0, 1 - size.Height / maxHeight) * factor;
            // Young crowns expand with the leader; older crowns still spread as
            // the trunk thickens. A diameter-only rate kept saplings artificially narrow.
            float crown = Math.Max(height * CrownRatio(tree.Species), diameter * (tree.Species == ForestSpecies.Oak ? 9 : 6));
            return new(diameter, height, crown);
        }
    }
}
