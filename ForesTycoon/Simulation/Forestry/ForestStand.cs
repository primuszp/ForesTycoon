namespace ForesTycoon
{
    /// <summary>Compact simulation state for the representative stand on one terrain tile.</summary>
    readonly record struct ForestStand(
        ForestSpecies Species,
        float AgeYears,
        float Biomass,
        float Health)
    {
        public bool IsEmpty => Species == ForestSpecies.None;

        public float Maturity
        {
            get
            {
                if (IsEmpty) return 0f;
                float matureAge = ForestSpeciesProfile.For(Species).MatureAgeYears;
                return System.Math.Clamp(AgeYears / matureAge, 0f, 1f);
            }
        }
    }

    readonly record struct ForestStatistics(
        int StandCount,
        int MatureStandCount,
        float TotalBiomass,
        float AverageHealth);

    readonly record struct ForestHarvest(
        ForestSpecies Species,
        float AgeYears,
        float TimberVolume);
}
