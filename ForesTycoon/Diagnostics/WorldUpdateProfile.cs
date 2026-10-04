namespace ForesTycoon
{
    internal readonly record struct WorldUpdateProfile(double EnvironmentForestMs, double LogisticsMs,
        double WildlifeMs, double OtherSystemsMs);
}
