using System;

namespace ForesTycoon.Ecology
{
    // Explicit multi-rate ordering: water/radiation interval, forest month, then clear interval budgets.
    // Environment never advances forestry, and forestry never clears environmental integrals.
    internal sealed class ForestEnvironmentCoordinator
    {
        internal EnvironmentSystem Environment { get; }
        internal EcologicalProcessRuntime Runtime { get; }

        internal ForestEnvironmentCoordinator(ForestSystem forest, EnvironmentSystem environment, bool prepareMonthlyGrowth = true)
        {
            ArgumentNullException.ThrowIfNull(forest);
            Environment = environment ?? throw new ArgumentNullException(nameof(environment));
            Runtime = new EcologicalProcessRuntime(EnvironmentSystem.StepSeconds,
                new IEcologicalProcess[] {
                    new Preparation(forest, prepareMonthlyGrowth), new Water(environment),
                    new Vegetation(forest), new Finalization(environment)
                }, "forest.state", "environment.state", "environment.monthBudget");
            forest.Environment = environment;
            forest.RefreshEnvironmentRates();
        }

        internal void Update(double seconds) => Runtime.Update(seconds);

        private sealed class Preparation(ForestSystem forest, bool enabled) : IEcologicalProcess
        {
            private static readonly EcologicalProcessDescriptor description = new("forest.prepare", EcologicalPhase.Preparation,
                new[] { "forest.state", "environment.state" }, new[] { "forest.prediction" });
            public EcologicalProcessDescriptor Descriptor => description;
            public void Advance(in EcologicalStep step) { if (enabled) forest.PrepareNextMonthStep(); }
        }

        private sealed class Water(EnvironmentSystem environment) : IEcologicalProcess
        {
            private static readonly EcologicalProcessDescriptor description = new("water.advance", EcologicalPhase.Environment,
                new[] { "forest.state", "environment.state" }, new[] { "environment.state", "environment.monthBudget" });
            public EcologicalProcessDescriptor Descriptor => description;
            public void Advance(in EcologicalStep step) => environment.AdvanceStep(step.DeltaSeconds);
        }

        private sealed class Vegetation(ForestSystem forest) : IEcologicalProcess
        {
            private static readonly EcologicalProcessDescriptor description = new("forest.advance", EcologicalPhase.Vegetation,
                new[] { "forest.prediction", "environment.state", "environment.monthBudget" }, new[] { "forest.state" });
            public EcologicalProcessDescriptor Descriptor => description;
            public EcologicalBoundary Boundary => EcologicalBoundary.ForestMonth;
            public double SecondsUntilBoundary => forest.SecondsUntilMonth;
            public void Advance(in EcologicalStep step) => forest.Update(step.DeltaSeconds);
        }

        private sealed class Finalization(EnvironmentSystem environment) : IEcologicalProcess
        {
            private static readonly EcologicalProcessDescriptor description = new("water.finishMonth", EcologicalPhase.Finalization,
                new[] { "environment.monthBudget", "forest.state" }, new[] { "environment.monthBudget" });
            public EcologicalProcessDescriptor Descriptor => description;
            public void Advance(in EcologicalStep step)
            { if (step.EndsAt(EcologicalBoundary.ForestMonth)) environment.FinishForestMonth(); }
        }
    }
}
