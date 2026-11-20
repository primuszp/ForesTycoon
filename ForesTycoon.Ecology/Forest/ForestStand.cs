namespace ForesTycoon.Ecology
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

    readonly record struct ThinningPrescription(int TargetPercent, int SurveyedTrees, int MarkedTrees,
        double StandingBasalArea, double MarkedBasalArea)
    {
        public double MarkedPercent => StandingBasalArea > 0 ? 100 * MarkedBasalArea / StandingBasalArea : 0;
        internal string Description => $"Körlapösszeg: {MarkedPercent:F1}% (cél: {TargetPercent}%). Kijelölve {MarkedTrees}/{SurveyedTrees} törzs.";
    }

    /// <summary>Outcome of one area planting or felling gesture.</summary>
    readonly record struct ForestryAreaSummary(
        int TileCount,
        int Applied,
        float TimberVolume)
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public ThinningPrescription? Thinning { get; init; }
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
        Designated,
        ThinningDesignated
    }
}
