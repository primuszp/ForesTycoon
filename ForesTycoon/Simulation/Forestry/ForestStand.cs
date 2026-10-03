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

    /// <summary>
    /// What a felled stand leaves behind: the stand as it stood before the first cut (so the
    /// renderer can put a stump where every removed stem was), and how long ago it was cut.
    /// Purely a by-product of harvesting; stumps never block planting or affect growth.
    /// </summary>
    readonly record struct ForestStump(ForestStand Felled, float Crowding, float YearsSinceFelled)
    {
        /// <summary>Stumps rot away and vanish after this many forest years.</summary>
        internal const float LifetimeYears = 6f;
        public bool IsEmpty => Felled.IsEmpty;
        public float Decay => System.Math.Clamp(YearsSinceFelled / LifetimeYears, 0f, 1f);
    }

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
