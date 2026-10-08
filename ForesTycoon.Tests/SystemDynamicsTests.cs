namespace ForesTycoon.Tests;

public class SystemDynamicsTests
{
    private sealed class Process(string id, EcologicalPhase phase, Action<EcologicalStep>? action = null,
        string[]? reads = null, string[]? writes = null) : IEcologicalProcess
    {
        public EcologicalProcessDescriptor Descriptor { get; } = new(id, phase, reads!, writes!);
        public void Advance(in EcologicalStep step) => action?.Invoke(step);
    }

    private sealed class Calendar : IEcologicalProcess
    {
        public EcologicalProcessDescriptor Descriptor { get; } = new("calendar", EcologicalPhase.Vegetation);
        public EcologicalBoundary Boundary => EcologicalBoundary.ForestMonth;
        public double SecondsUntilBoundary { get; private set; } = .75;
        public void Advance(in EcologicalStep step) => SecondsUntilBoundary = step.EndsAt(Boundary)
            ? .75 : SecondsUntilBoundary - step.DeltaSeconds;
    }

    [Fact]
    public void PhasesAreStableAndCalendarBudgetsFinishAtTheExactBoundary()
    {
        var calls = new List<(string Id, double Dt, EcologicalBoundary Flags)>();
        var runtime = new EcologicalProcessRuntime(.5, new IEcologicalProcess[] {
            new Process("finish", EcologicalPhase.Finalization, s => calls.Add(("finish", s.DeltaSeconds, s.Boundaries))),
            new Calendar(),
            new Process("water", EcologicalPhase.Environment, s => calls.Add(("water", s.DeltaSeconds, s.Boundaries))),
            new Process("prepare", EcologicalPhase.Preparation, s => calls.Add(("prepare", s.DeltaSeconds, s.Boundaries)))
        });
        runtime.Update(.2); runtime.Update(.3); runtime.Update(.5);
        Assert.Equal(new[] { "prepare", "water", "finish", "prepare", "water", "finish", "prepare", "water", "finish" },
            calls.Select(c => c.Id));
        Assert.Equal(new[] { .5, .25, .25 }, calls.Where(c => c.Id == "finish").Select(c => c.Dt));
        Assert.Equal(EcologicalBoundary.ForestMonth, calls[5].Flags);
        Assert.Equal(EcologicalBoundary.EnvironmentStep, calls[8].Flags);
        Assert.Equal(2UL, runtime.CompletedSteps);
        Assert.Equal(1, runtime.TimeSeconds);
    }

    [Fact]
    public void InvalidDependenciesAndConflictingWritersAreRejectedBeforeRunning()
    {
        Assert.Throws<ArgumentException>(() => new EcologicalProcessRuntime(.5, new IEcologicalProcess[] {
            new Process("consumer", EcologicalPhase.Environment, reads: new[] { "missing" }) }));
        Assert.Throws<ArgumentException>(() => new EcologicalProcessRuntime(.5, new IEcologicalProcess[] {
            new Process("a", EcologicalPhase.Environment, writes: new[] { "water" }),
            new Process("b", EcologicalPhase.Environment, writes: new[] { "water" }) }));
        Assert.Throws<ArgumentException>(() => new EcologicalProcessRuntime(.5, new IEcologicalProcess[] {
            new Process("a", EcologicalPhase.Environment), new Process("a", EcologicalPhase.Vegetation) }));
        var valid = new EcologicalProcessRuntime(.5, new IEcologicalProcess[] {
            new Process("water", EcologicalPhase.Environment, writes: new[] { "soil" }),
            new Process("growth", EcologicalPhase.Vegetation, reads: new[] { "soil" }) });
        valid.Update(.5);
        Assert.Equal(1UL, valid.CompletedSteps);
    }

    [Fact]
    public void PartialProcessFailureCannotBeRetriedOnAMutatedWorld()
    {
        int mutations = 0;
        var runtime = new EcologicalProcessRuntime(.5, new IEcologicalProcess[] {
            new Process("mutate", EcologicalPhase.Environment, _ => mutations++),
            new Process("fail", EcologicalPhase.Vegetation, _ => throw new InvalidOperationException()) });
        Assert.Throws<InvalidOperationException>(() => runtime.Update(.5));
        Assert.True(runtime.IsFaulted);
        Assert.Throws<InvalidOperationException>(() => runtime.Update(.5));
        Assert.Equal(1, mutations);
        Assert.Equal(0UL, runtime.CompletedSteps);
    }

    [Fact]
    public void CompetingFlowsShareSupplyAndCannotSpendIncomingStock()
    {
        var n = new StockFlowNetwork("mm");
        var a = n.AddStock("soil", 100); var b = n.AddStock("roots", 0); var c = n.AddStock("drainage", 0);
        n.BeginStep(1); n.Transfer(a, b, 80); n.Transfer(a, c, 80); n.Transfer(b, c, 50);
        var balance = n.Commit();
        Assert.Equal(0, n.Quantity(a)); Assert.Equal(50, n.Quantity(b)); Assert.Equal(50, n.Quantity(c));
        Assert.Equal(0, balance.Error);
    }

    [Fact]
    public void CapacityRejectionKeepsWaterAtItsSourceAndExternalFlowsAreAccounted()
    {
        var n = new StockFlowNetwork("mm");
        var a = n.AddStock("source", 100); var b = n.AddStock("bounded", 0, 10);
        var c = n.AddStock("other", 0);
        n.BeginStep(1); n.Transfer(a, b, 80); n.Transfer(a, c, 80); n.Inflow(c, 7);
        var balance = n.Commit();
        Assert.Equal(40, n.Quantity(a), 10); Assert.Equal(10, n.Quantity(b), 10); Assert.Equal(57, n.Quantity(c), 10);
        Assert.Equal(7, balance.Inflow); Assert.InRange(Math.Abs(balance.Error), 0, 1e-12);
        n.BeginStep(1); n.Outflow(c, 100); balance = n.Commit();
        Assert.Equal(57, balance.Outflow, 10); Assert.InRange(Math.Abs(balance.Error), 0, 1e-12);
    }

    [Fact]
    public void FailedCommitDoesNotPublishAndForeignStockHandlesAreRejected()
    {
        var n = new StockFlowNetwork("kg"); var a = n.AddStock("a", 10);
        var other = new StockFlowNetwork("kg"); var foreign = other.AddStock("a", 10);
        n.BeginStep(1);
        Assert.Throws<ArgumentException>(() => n.Transfer(a, foreign, 1));
        n.Outflow(a, double.MaxValue); n.Outflow(a, double.MaxValue);
        Assert.Throws<InvalidOperationException>(() => n.Commit());
        Assert.Equal(10, n.Quantity(a)); n.CancelStep();
        n.BeginStep(1); n.Outflow(a, 2); n.Commit(); Assert.Equal(8, n.Quantity(a));
    }

    private sealed class Reservoir : IEcologicalProcess
    {
        internal readonly StockFlowNetwork Network = new("mm");
        internal readonly StockHandle Water;
        internal double BalanceError;
        public EcologicalProcessDescriptor Descriptor { get; } = new("reservoir", EcologicalPhase.Environment);
        internal Reservoir() => Water = Network.AddStock("water", 0);
        public void Advance(in EcologicalStep step)
        {
            double water = Network.Quantity(Water);
            Network.BeginStep(step.DeltaSeconds);
            Network.Inflow(Water, 10);
            Network.Outflow(Water, .5 * water);
            BalanceError += Math.Abs(Network.Commit().Error);
        }
    }

    [Fact]
    public void FeedbackReservoirConvergesToTheAnalyticalSolutionAsStepShrinks()
    {
        double exact = 20 * (1 - Math.Exp(-5));
        double Run(double dt)
        {
            var process = new Reservoir();
            var runtime = new EcologicalProcessRuntime(dt, new IEcologicalProcess[] { process });
            runtime.Update(10);
            Assert.InRange(process.BalanceError, 0, 1e-10);
            return Math.Abs(process.Network.Quantity(process.Water) - exact);
        }
        Assert.True(Run(.05) < Run(.1));
        Assert.InRange(Run(.05), 0, .01);
    }

    [Fact]
    public void WarmRuntimeAndNetworkDoNotAllocatePerStep()
    {
        var process = new Reservoir();
        var runtime = new EcologicalProcessRuntime(.5, new IEcologicalProcess[] { process });
        for (int i = 0; i < 100; i++) runtime.Update(.5);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) runtime.Update(.5);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void RandomCompetingFlowsPreserveMassAndBoundsAcrossManySteps()
    {
        var random = new Random(1729); var n = new StockFlowNetwork("mm");
        var handles = Enumerable.Range(0, 20).Select(i => n.AddStock($"cell{i}", 50, 100)).ToArray();
        for (int step = 0; step < 100; step++)
        {
            n.BeginStep(.5);
            for (int j = 0; j < 150; j++)
            {
                int a = random.Next(handles.Length), b = random.Next(handles.Length);
                if (a != b) n.Transfer(handles[a], handles[b], random.NextDouble() * 200);
            }
            n.Inflow(handles[0], 10); n.Outflow(handles[1], 10);
            var balance = n.Commit();
            Assert.InRange(Math.Abs(balance.Error), 0, 1e-10);
            foreach (double quantity in n.Stocks) Assert.InRange(quantity, 0, 100);
        }
    }

    private sealed class Habitat : IForestHabitat
    {
        public int TileCount => 4;
        public int Seed => 42;
        public bool CanSupportForest(int id) => true;
        public float GetMoisture(int id) => .65f;
        public float GetNormalizedElevation(int id) => .45f;
        public ForestTileGeometry GetForestTileGeometry(int id) => new(id * 16, 0, 16, 16);
        public int GetAdjacentTileIds(int id, Span<int> target) => 0;
    }

    [Theory]
    [InlineData(13, true)]
    [InlineData(13, false)]
    [InlineData(1200, true)]
    public void ProcessRuntimeMatchesThePreviousCoordinatorAcrossMonthsAndTerrainEdits(double year, bool prepare)
    {
        var habitat = new Habitat();
        var actualForest = new ForestSystem(habitat, year);
        var referenceForest = new ForestSystem(habitat, year);
        var actualWater = new EnvironmentSystem(habitat, actualForest);
        var referenceWater = new EnvironmentSystem(habitat, referenceForest);
        var runtime = new ForestEnvironmentCoordinator(actualForest, actualWater, prepare);
        referenceForest.Environment = referenceWater;
        referenceForest.RefreshEnvironmentRates();
        actualWater.ForceWeather(WeatherPreset.Storm, 32, 90);
        referenceWater.ForceWeather(WeatherPreset.Storm, 32, 90);
        double remainder = 0;
        void Advance(double seconds)
        {
            runtime.Update(seconds);
            // Independent copy of the established pre-runtime integration order.
            remainder += seconds;
            while (remainder + 1e-10 >= EnvironmentSystem.StepSeconds)
            {
                remainder -= EnvironmentSystem.StepSeconds;
                double remaining = EnvironmentSystem.StepSeconds;
                while (remaining > 1e-10)
                {
                    double until = referenceForest.SecondsUntilMonth;
                    double dt = Math.Min(remaining, until);
                    if (prepare) referenceForest.PrepareNextMonthStep();
                    referenceWater.AdvanceStep(dt); referenceForest.Update(dt);
                    if (dt == until) referenceWater.FinishForestMonth();
                    remaining -= dt;
                }
            }
        }
        for (int i = 0; i < 100; i++) Advance(.13);
        actualForest.ClearTerrainTiles(new[] { 0 }); referenceForest.ClearTerrainTiles(new[] { 0 });
        actualWater.RefreshRouting(new[] { 0 }); referenceWater.RefreshRouting(new[] { 0 });
        Advance(220.7);
        Assert.Equal(referenceForest.ForestYear, actualForest.ForestYear);
        Assert.Equal(referenceForest.Statistics, actualForest.Statistics);
        Assert.Equal(referenceWater.Time, actualWater.Time);
        Assert.Equal(referenceWater.StoredWater, actualWater.StoredWater);
        Assert.Equal(referenceWater.Transpired, actualWater.Transpired);
        for (int id = 0; id < habitat.TileCount; id++)
        {
            Assert.Equal(referenceWater.Cell(id), actualWater.Cell(id));
            Assert.Equal(referenceForest.IndividualTrees.TryGet(id, out var expected), actualForest.IndividualTrees.TryGet(id, out var actual));
            if (expected != null) Assert.Equal(expected.Trees, actual!.Trees);
        }
    }

    [Fact]
    public void AggregateOverflowCannotPublishAnInvalidBalance()
    {
        var n = new StockFlowNetwork("kg");
        var a = n.AddStock("a", double.MaxValue); n.AddStock("b", double.MaxValue);
        n.BeginStep(1); n.Outflow(a, 1);
        Assert.Throws<InvalidOperationException>(() => n.Commit());
        Assert.Equal(double.MaxValue, n.Quantity(a));
    }

    [Fact]
    public void InvalidInputDoesNotFaultTheRuntimeOrMutateStocks()
    {
        var process = new Reservoir();
        var runtime = new EcologicalProcessRuntime(.5, new IEcologicalProcess[] { process });
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Update(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Update(-1));
        Assert.False(runtime.IsFaulted);
        runtime.Update(0); Assert.Equal(0UL, runtime.CompletedSteps);
        double source = 3, destination = 2;
        Assert.Throws<ArgumentOutOfRangeException>(() => StockFlows.Transfer(ref source, ref destination, double.PositiveInfinity));
        Assert.Equal(3, source); Assert.Equal(2, destination);
        Assert.Equal(3, StockFlows.Transfer(ref source, ref destination, 20));
        Assert.Equal(0, source); Assert.Equal(5, destination);
    }
}
