namespace ForesTycoon
{
    enum ForestSpecies : byte
    {
        None = 0,
        Pine = 1,
        Spruce = 2,
        Birch = 3
    }

    readonly record struct ForestSpeciesProfile(
        float MaximumAgeYears,
        float MatureAgeYears,
        float MaximumBiomass,
        float AnnualGrowthRate,
        float PreferredMoisture,
        float MoistureTolerance)
    {
        public static ForestSpeciesProfile For(ForestSpecies species) => species switch
        {
            ForestSpecies.Pine => new ForestSpeciesProfile(160f, 24f, 1.00f, 0.075f, 0.52f, 0.34f),
            ForestSpecies.Spruce => new ForestSpeciesProfile(190f, 30f, 1.15f, 0.065f, 0.76f, 0.25f),
            ForestSpecies.Birch => new ForestSpeciesProfile(90f, 15f, 0.72f, 0.105f, 0.64f, 0.30f),
            _ => default
        };
    }
}
