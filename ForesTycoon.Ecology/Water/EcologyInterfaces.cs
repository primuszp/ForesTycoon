namespace ForesTycoon.Ecology
{
    /// <summary>Shared tempo of the ecosystem: how long a forest year lasts and how finely water is stepped.</summary>
    internal static class EcologyTime
    {
        /// <summary>Simulated seconds in one forest year at the slowest (reference) tempo.</summary>
        internal const double SecondsPerForestYear = 1200;
        /// <summary>Fastest allowed tempo, the default game speed.</summary>
        internal const double DefaultGameSecondsPerYear = 120;
        /// <summary>Environment seconds per hour of simulated weather.</summary>
        internal const double HoursPerSecond = 1.0 / 60;
        /// <summary>Granularity of water and weather stepping.</summary>
        internal const double StepSeconds = 0.5;
        internal static bool IsValidForestYearSeconds(double seconds) => double.IsFinite(seconds)
            && seconds >= DefaultGameSecondsPerYear && seconds <= SecondsPerForestYear;
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
