namespace ForesTycoon
{
    enum ForestSpecies : byte
    {
        None = 0,
        Spruce = 2,
        Birch = 3,
        Oak = 4,
        Beech = 5
    }

    /// <summary>Silhouette family used by the tree renderer to build a species-specific model.</summary>
    enum TreeCrownShape : byte
    {
        /// <summary>Narrow, many-layered spire (spruce).</summary>
        Spire,
        /// <summary>Small, light, roughly spherical broadleaf crown (birch).</summary>
        Rounded,
        /// <summary>Wide, flattened, heavy broadleaf crown (oak).</summary>
        Broad,
        /// <summary>Tall egg-shaped broadleaf crown (beech).</summary>
        Ovoid
    }

    readonly record struct ForestSpeciesProfile(
        float MaximumAgeYears,
        float MatureAgeYears,
        float MaximumBiomass,
        float AnnualGrowthRate,
        float PreferredMoisture,
        float MoistureTolerance,
        float PreferredElevation,
        float ElevationTolerance,
        // 0 = light demanding pioneer, 1 = grows and regenerates happily under a closed canopy.
        float ShadeTolerance,
        // Recoverable roundwood per biomass unit, relative to spruce pulpwood.
        float WoodDensity,
        TreeCrownShape CrownShape)
    {
        /// <summary>Biomass a fully stocked neighbouring tile is assumed to carry; used to normalise crowding.</summary>
        public const float ReferenceCanopyBiomass = 1.2f;

        public static ForestSpeciesProfile For(ForestSpecies species) => species switch
        {
            ForestSpecies.Spruce => new ForestSpeciesProfile(
                190f, 30f, 1.15f, 0.065f, 0.76f, 0.25f, 0.72f, 0.38f, 0.70f, 0.92f, TreeCrownShape.Spire),
            ForestSpecies.Birch => new ForestSpeciesProfile(
                90f, 15f, 0.72f, 0.105f, 0.64f, 0.30f, 0.45f, 0.50f, 0.10f, 1.12f, TreeCrownShape.Rounded),
            ForestSpecies.Oak => new ForestSpeciesProfile(
                320f, 45f, 1.35f, 0.045f, 0.55f, 0.30f, 0.32f, 0.40f, 0.45f, 1.45f, TreeCrownShape.Broad),
            ForestSpecies.Beech => new ForestSpeciesProfile(
                240f, 40f, 1.30f, 0.052f, 0.70f, 0.26f, 0.55f, 0.36f, 0.90f, 1.32f, TreeCrownShape.Ovoid),
            _ => default
        };
    }
}
