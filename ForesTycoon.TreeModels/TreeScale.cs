using System;

namespace ForesTycoon.TreeModels
{
    /// <summary>Conventions shared by tree models and everything that places or draws them.</summary>
    internal static class TreeScale
    {
        /// <summary>A physical metre is intentionally compressed for the existing diorama proportions.</summary>
        internal const float MetresToWorld = WorldScale.MetresToWorld;

        /// <summary>
        /// Alpha byte of a vertex colour: 246 + material family. The surface shader decodes it to pick the
        /// bark and foliage pattern, so every species borrows one of the five families it knows.
        /// </summary>
        internal static int SurfaceSpeciesCode(ForestSpecies species) => 246 + ForestSpeciesTraits.For(species).Pattern;
        // Dedicated crown material IDs preserve the species rather than only its bark family.
        internal static int CrownSpeciesCode(ForestSpecies species) => 224 + (byte)species;
    }
}
