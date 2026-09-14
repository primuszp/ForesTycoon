using System;

namespace ForesTycoon
{
    internal static class ForestVisualFixture
    {
        internal const int Size = 16;
        internal static int Height(int u, int v) => 2 + (u > 10 && v > 7 ? 1 : 0);
        internal static ForestStand[] CreateStands()
        {
            var result = new ForestStand[Size * Size];
            for (int u = 1; u < Size - 1; u++)
                for (int v = 1; v < Size - 1; v++)
                {
                    uint seed = (uint)(u * Size + v + 20260913);
                    float x = (u - 8.2f) / 3.3f, y = (v - 7.2f) / 4.7f;
                    float edge = x * x + y * y + 0.18f * MathF.Sin(u * 1.3f + v * 0.8f);
                    float random = Terrain.TreeRandom(seed, 37);
                    if (edge < 0.85f || (edge < 1.25f && random < 0.48f)) continue;
                    ForestSpecies species = (u < 8, v < 8) switch
                    {
                        (true, true) => ForestSpecies.Spruce,
                        (true, false) => ForestSpecies.Birch,
                        (false, true) => ForestSpecies.Oak,
                        _ => ForestSpecies.Beech
                    };
                    // Small mixed groups break quadrant boundaries while keeping the four
                    // dominant species regions readable in the reference scene.
                    if (random < 0.18f) species = ForestSpecies.Birch;
                    if (random > 0.91f) species = ForestSpecies.Spruce;
                    var profile = ForestSpeciesProfile.For(species);
                    float maturity = edge < 1.35f ? 0.30f + random * 0.45f : 0.82f + random * 0.30f;
                    result[u * Size + v] = new ForestStand(species, maturity * profile.MatureAgeYears,
                        profile.MaximumBiomass * Math.Min(1, maturity) * (0.83f + random * 0.17f), 0.97f);
                }
            return result;
        }
    }
}
