namespace ForesTycoon
{
    /// <summary>Aggregated view of the living individuals on one terrain tile.</summary>
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

    /// <summary>Outcome of one area planting or felling gesture.</summary>
    readonly record struct ForestryAreaSummary(
        int TileCount,
        int Applied,
        float TimberVolume)
    {
        public int Skipped => TileCount - Applied;
        public bool IsEmpty => TileCount == 0;
    }

    enum ForestryActionResult : byte
    {
        None,
        Planted,
        Harvested,
        InvalidTile,
        TileOccupied,
        UnsuitableTerrain,
        NoForest,
        Designated
    }
}
