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
        public double DecayProgress { get; init; }
    }

    internal readonly record struct ForestDeadTree(ForestTree Tree, double DeathYear);

    internal readonly record struct ForestBoundaryGeometry(ForestTreeDimensions Dimensions, float VolumeIncrement);

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

    }
}
