using System;

namespace ForesTycoon.Ecology
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

    // Geometry-only growth terms. Apply preserves the original floating-point operation order.
    internal readonly record struct ForestGrowthShape(float DiameterNumerator, float DiameterDenominator,
        float HeightMultiplier, float CrownRatio, float CrownSpread)
    {
        internal ForestTreeDimensions Apply(float factor)
        {
            float diameter = DiameterNumerator * factor / DiameterDenominator;
            float height = HeightMultiplier * factor;
            return new(diameter, height, Math.Max(height * CrownRatio, diameter * CrownSpread));
        }
    }
    internal readonly record struct ForestBoundaryGeometry(ForestTreeDimensions Dimensions,
        float VolumeIncrement, ForestGrowthShape Growth, float SpaceResponse);

    /// <summary>Species-specific gameplay growth curves in physical metres.</summary>
    internal static class ForestTreeGrowth
    {
        internal static int Capacity(ForestSpecies species) =>
            species == ForestSpecies.None ? 0 : ForestSpeciesTraits.For(species).Capacity;

        internal static ForestTreeDimensions Initial(ForestSpecies species, float age, float variation)
        {
            if (species == ForestSpecies.None) throw new ArgumentOutOfRangeException(nameof(species));
            var traits = ForestSpeciesTraits.For(species);
            float mature = ForestSpeciesProfile.For(species).MatureAgeYears;
            float growth = 1 - MathF.Exp(-Math.Max(0, age) / mature * 1.8f);
            float startHeight = Math.Min(0.8f, 0.15f * traits.MatureHeight);
            float startDiameter = Math.Min(0.012f, 0.1f * traits.MatureDiameter);
            float h = (startHeight + (traits.MatureHeight - startHeight) * growth) * variation;
            float d = (startDiameter + traits.MatureDiameter * growth) * variation;
            return new(d, h, Math.Max(0.15f, h * traits.CrownRatio));
        }

        internal static ForestTreeDimensions Rates(in ForestTree tree, double year, float fitness, float crowding, float water)
        {
            float light = Math.Clamp(1 - crowding * (1 - ForestSpeciesProfile.For(tree.Species).ShadeTolerance), 0.05f, 1);
            return RatesWithResources(tree, year, fitness, new(light, water, 1));
        }

        internal static ForestTreeDimensions RatesWithResources(in ForestTree tree, double year, float fitness, ForestResources resources)
        {
            return Shape(tree.Species, tree.At(year)).Apply(Factor(fitness, resources.LightResponse(tree.Species),
                resources.Water, MathF.Sqrt(Math.Clamp(resources.Space, 0, 1)), tree.Health, Season(year)));
        }

        internal static float Season(double year) =>
            Math.Clamp(0.65f + 0.8f * MathF.Cos(MathF.Tau * ((float)(year % 1) - 0.25f)), 0, 1.45f);

        internal static float Factor(float fitness, float lightResponse, float water, float spaceResponse, float health, float season) =>
            Math.Clamp(fitness, 0, 1) * lightResponse * Math.Clamp(water, 0, 1) * spaceResponse * health * season;

        internal static ForestGrowthShape Shape(ForestSpecies species, ForestTreeDimensions size)
        {
            var traits = ForestSpeciesTraits.For(species);
            // Mature trees continue thickening; height approaches a species-dependent envelope.
            // Young crowns expand with the leader; older crowns still spread as
            // the trunk thickens. A diameter-only rate kept saplings artificially narrow.
            return new(traits.RadialRate * 2, 1 + size.Diameter * 0.8f,
                traits.HeightRate * Math.Max(0, 1 - size.Height / traits.MaxHeight), traits.CrownRatio, traits.CrownSpread);
        }
    }
}
