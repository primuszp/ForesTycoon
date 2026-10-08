namespace ForesTycoon.Tests;

public class SurfaceWaterFluxTests
{
    [Fact]
    public void IncomingWaterWaitsUntilTheNextStepRegardlessOfTraversalOrder()
    {
        double[] forward = { 100, 0, 0 }, reverse = (double[])forward.Clone();
        foreach (var water in new[] { forward, reverse })
        {
            var flux = new SurfaceWaterFlux(3); flux.BeginStep();
            int[] order = ReferenceEquals(water, forward) ? new[] { 0, 1, 2 } : new[] { 2, 1, 0 };
            foreach (int id in order) flux.Schedule(id, id < 2 ? id + 1 : -1, false, water[id], SurfaceRunoffLaw.Legacy.Prepare(1));
            flux.Commit(water);
        }
        Assert.Equal(forward, reverse); Assert.Equal(0, forward[2]);
        Assert.True(forward[1] > 97); Assert.Equal(100, forward.Sum(), 12);
    }

    [Fact]
    public void ConvergingFlowsPreserveWaterAndOnlyExplicitOutletsExportIt()
    {
        double[] water = { 80, 70, 50, 40 };
        var flux = new SurfaceWaterFlux(4); flux.BeginStep();
        Assert.Equal(0, flux.Schedule(0, 2, false, water[0], SurfaceRunoffLaw.Legacy.Prepare(.2)));
        Assert.Equal(0, flux.Schedule(1, 2, false, water[1], SurfaceRunoffLaw.Legacy.Prepare(.2)));
        Assert.Equal(0, flux.Schedule(2, -1, false, water[2], SurfaceRunoffLaw.Legacy.Prepare(.2)));
        double exported = flux.Schedule(3, -1, true, water[3], SurfaceRunoffLaw.Legacy.Prepare(.2));
        flux.Commit(water);
        Assert.True(water[2] > 50); Assert.All(water, depth => Assert.True(depth >= 0));
        Assert.Equal(240, water.Sum() + exported, 12);
    }

    [Fact]
    public void LegacyLawAndOrderedCommitMatchThePreviousRasterAlgorithmExactly()
    {
        var random = new Random(42);
        double[] water = Enumerable.Range(0, 256).Select(_ => random.NextDouble() * 200).ToArray();
        double[] expected = (double[])water.Clone(), delta = new double[water.Length];
        var flux = new SurfaceWaterFlux(water.Length); flux.BeginStep();
        double oldExported = 0, exported = 0, hours = .5 / 60;
        for (int id = 0; id < water.Length; id++)
        {
            int to = id % 4 == 0 ? -1 : id - 1;
            bool outlet = id % 8 == 0;
            double amount = Math.Min(water[id], Math.Max(0, water[id] - 2) * (1 - Math.Exp(-hours * 6)));
            if (to >= 0) { delta[id] -= amount; delta[to] += amount; }
            else if (outlet) { delta[id] -= amount; oldExported += amount; }
            exported += flux.Schedule(id, to, outlet, water[id], SurfaceRunoffLaw.Legacy.Prepare(hours));
        }
        for (int id = 0; id < expected.Length; id++) expected[id] += delta[id];
        flux.Commit(water);
        Assert.Equal(expected, water); Assert.Equal(oldExported, exported);
    }

    [Fact]
    public void InvalidTopologyDuplicateSourcesAndOverdrawCannotPartiallyCommit()
    {
        var flux = new SurfaceWaterFlux(2);
        Assert.Throws<InvalidOperationException>(() => flux.Commit(new double[2]));
        flux.BeginStep();
        Assert.Throws<ArgumentOutOfRangeException>(() => flux.Schedule(0, 0, false, 100, SurfaceRunoffLaw.Legacy.Prepare(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => flux.Schedule(0, 2, false, 100, SurfaceRunoffLaw.Legacy.Prepare(1)));
        flux.Schedule(1, 0, false, 100, SurfaceRunoffLaw.Legacy.Prepare(1));
        Assert.Throws<InvalidOperationException>(() => flux.Schedule(1, 0, false, 100, SurfaceRunoffLaw.Legacy.Prepare(1)));
        double[] water = { 0, 1 };
        Assert.Throws<InvalidOperationException>(() => flux.Commit(water));
        Assert.Equal(new double[] { 0, 1 }, water);
        flux.Cancel(); flux.BeginStep(); flux.Commit(water);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(100, 0)]
    public void RetainedWaterAndZeroTimeHaveNoRunoff(double water, double hours) =>
        Assert.Equal(0, SurfaceRunoffLaw.Legacy.Prepare(hours).Amount(water));

    [Fact]
    public void RunoffParametersAndInputsMustBeFiniteAndNonNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SurfaceRunoffLaw(-1, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SurfaceRunoffLaw(2, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceRunoffLaw.Legacy.Prepare(1).Amount(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceRunoffLaw.Legacy.Prepare(-1));
        Assert.Equal(0, new SurfaceRunoffLaw(2, 0).Prepare(1).Amount(100));
    }
}
