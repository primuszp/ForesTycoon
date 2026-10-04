namespace ForesTycoon
{
    internal readonly record struct ForestMonthProfile(double CloseStandsMs, double SnapshotAndSeedsMs,
        double GrowthAndMortalityMs, double RegenerationAndPublishMs);
}
