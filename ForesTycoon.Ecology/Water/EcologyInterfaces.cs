namespace ForesTycoon.Ecology
{
    /// <summary>Shared tempo of the ecosystem: how long a forest year lasts and how finely water is stepped.</summary>
    internal static class EcologyTime
    {
        /// <summary>Reference tempo: weather intensities are defined for a year of this many simulated seconds.</summary>
        internal const double SecondsPerForestYear = 1200;
        /// <summary>
        /// Default tempo: at normal (1×) speed a year lasts an hour and a season a quarter of an hour, so a day is about
        /// ten seconds — the scale of Transport Tycoon (a 74-tick day of ~2 s at its 1×, a year of ~13 minutes) at 4×.
        /// The speed buttons multiply it (up to 256×: a year in 14 s).
        /// </summary>
        internal const double DefaultGameSecondsPerYear = 3600;
        /// <summary>Fastest tempo saved games may use (the earlier default).</summary>
        internal const double MinGameSecondsPerYear = 120;
        /// <summary>Environment seconds per hour of simulated weather.</summary>
        internal const double HoursPerSecond = 1.0 / 60;
        /// <summary>Granularity of water and weather stepping.</summary>
        internal const double StepSeconds = 0.5;
        internal static bool IsValidForestYearSeconds(double seconds) => double.IsFinite(seconds)
            && seconds >= MinGameSecondsPerYear && seconds <= DefaultGameSecondsPerYear;
    }

    /// <summary>What the water balance needs to know about the vegetation of the world.</summary>
    internal interface IForestCanopy
    {
        /// <summary>Changes whenever the vegetation (and so interception and uptake demand) changes.</summary>
        ulong Revision { get; }
        ForestHydrologyInputs HydrologyInputs(int tileId);
        /// <summary>Species of the stand on the tile, if any, for species-specific drought response.</summary>
        bool TryGetSpecies(int tileId, out ForestSpecies species);
    }

    /// <summary>What forest growth needs from the weather and water model.</summary>
    internal interface IForestEnvironment
    {
        /// <summary>Relative shortwave radiation right now (0..1).</summary>
        double Radiation { get; }
        /// <summary>Mean radiation of the running month.</summary>
        double PeriodRadiation { get; }
        /// <summary>Water-limited growth factor of the finished month.</summary>
        double GrowthFactor(int tileId, ForestSpecies species);
        /// <summary>Water response under today's soil water.</summary>
        double CurrentWaterFactor(int tileId, ForestSpecies species);
    }
}
