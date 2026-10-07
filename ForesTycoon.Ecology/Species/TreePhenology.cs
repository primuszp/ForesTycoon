using System;
using OpenTK.Mathematics;

namespace ForesTycoon.Ecology
{
    /// <summary>
    /// Leaf phenology as a pure function of the forest year fraction and the tree seed, so that it
    /// replays exactly. The year starts in spring: 0.25 is mid-summer, 0.75 the coldest point
    /// (matching <see cref="WeatherSystem"/>'s temperature curve).
    /// </summary>
    internal static class TreePhenology
    {
        internal static bool Evergreen(ForestSpecies species) => ForestSpeciesTraits.For(species).Evergreen;

        // Segment ends within the cycle that starts at bud burst: budding, full, autumn colour, leaf fall.
        private readonly record struct Calendar(float Start, float Budding, float Full, float Autumn, float Falling);
        private static Calendar For(ForestSpecies species) => species switch
        {
            ForestSpecies.Birch => new(0.95f, 0.07f, 0.49f, 0.60f, 0.69f),
            ForestSpecies.Oak or ForestSpecies.SessileOak => new(0.97f, 0.07f, 0.49f, 0.59f, 0.67f),
            ForestSpecies.TurkeyOak => new(0.99f, 0.07f, 0.53f, 0.64f, 0.72f),
            ForestSpecies.Maple => new(0.96f, 0.07f, 0.50f, 0.60f, 0.68f),
            // Ash breaks bud late and drops its leaves early, often still green.
            ForestSpecies.Ash => new(0.04f, 0.08f, 0.46f, 0.54f, 0.62f),
            ForestSpecies.Larch => new(0.95f, 0.08f, 0.52f, 0.64f, 0.72f),
            ForestSpecies.Hazel => new(0.93f, 0.07f, 0.50f, 0.60f, 0.68f),
            ForestSpecies.Hawthorn or ForestSpecies.Blackthorn => new(0.95f, 0.07f, 0.52f, 0.62f, 0.69f),
            ForestSpecies.Elder => new(0.92f, 0.07f, 0.52f, 0.62f, 0.69f),
            _ => new(0.98f, 0.07f, 0.52f, 0.62f, 0.69f)
        };

        // Individuals differ by a few days; the offset is part of the tree, not of the weather.
        private static float Offset(uint seed) => (ForestTreeStore.Unit(ForestTreeStore.Random(seed + 4027)) - 0.5f) * 0.03f;

        private static float Phase(ForestSpecies species, uint seed, double year)
        {
            var calendar = For(species);
            double t = year - calendar.Start - Offset(seed);
            return (float)(t - Math.Floor(t));
        }

        internal static LeafState At(ForestSpecies species, uint seed, double year)
        {
            if (Evergreen(species)) return LeafState.Full;
            var c = For(species);
            float t = Phase(species, seed, year);
            return t < c.Budding ? LeafState.Budding : t < c.Full ? LeafState.Full
                : t < c.Autumn ? LeafState.Autumn : t < c.Falling ? LeafState.Falling : LeafState.Bare;
        }

        /// <summary>First forest year after <paramref name="year"/> at which the leaf state differs.</summary>
        internal static double NextChange(ForestSpecies species, uint seed, double year)
        {
            if (Evergreen(species)) return double.PositiveInfinity;
            var c = For(species);
            float t = Phase(species, seed, year);
            float next = t < c.Budding ? c.Budding : t < c.Full ? c.Full : t < c.Autumn ? c.Autumn
                : t < c.Falling ? c.Falling : 1f;
            return year + (next - t) + 1e-4;
        }
    }
}
