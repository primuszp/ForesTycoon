using System;

namespace ForesTycoon
{
    // Explicit multi-rate ordering: water/radiation interval, forest month, then clear interval budgets.
    // Environment never advances forestry, and forestry never clears environmental integrals.
    internal sealed class ForestEnvironmentCoordinator
    {
        private readonly ForestSystem forest;
        internal EnvironmentSystem Environment { get; }
        private double remainder;
        private readonly bool prepareMonthlyGrowth;

        internal ForestEnvironmentCoordinator(ForestSystem forest, EnvironmentSystem environment, bool prepareMonthlyGrowth = true)
        {
            this.forest = forest ?? throw new ArgumentNullException(nameof(forest));
            this.prepareMonthlyGrowth = prepareMonthlyGrowth;
            Environment = environment ?? throw new ArgumentNullException(nameof(environment));
            forest.Environment = environment;
            forest.RefreshEnvironmentRates();
        }

        internal void Update(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            remainder += seconds;
            while (remainder + 1e-10 >= EnvironmentSystem.StepSeconds)
            {
                remainder -= EnvironmentSystem.StepSeconds;
                double remaining = EnvironmentSystem.StepSeconds;
                while (remaining > 1e-10)
                {
                    double untilMonth = forest.SecondsUntilMonth;
                    double dt = Math.Min(remaining, untilMonth);
                    if (prepareMonthlyGrowth) forest.PrepareNextMonthStep();
                    Environment.AdvanceStep(dt);
                    forest.Update(dt);
                    if (dt == untilMonth) Environment.FinishForestMonth();
                    remaining -= dt;
                }
            }
        }
    }
}
