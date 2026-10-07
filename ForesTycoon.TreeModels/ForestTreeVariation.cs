using System;

namespace ForesTycoon.TreeModels
{
    // Independent hash channels keep morphology stable across growth, reloads and LODs.
    // Adding a trait never shifts the random sequence of existing traits.
    internal static class ForestTreeVariation
    {
        internal static float Unit(int seed, int channel) => ForestTreeStore.Unit(
            ForestTreeStore.Random(unchecked((uint)seed ^ (uint)channel * 0x9E3779B9u)));

        internal static float Range(int seed, int channel, float min, float max) =>
            min + (max - min) * Unit(seed, channel);
    }
}
