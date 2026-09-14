namespace ForesTycoon.Tests;

public class EngineRegressionTests
{
    [Theory]
    [InlineData(3u)]
    [InlineData(5u)]
    [InlineData(7u)]
    [InlineData(11u)]
    [InlineData(17u)]
    public void TreeVariation_UsesFullRangeAndIsDeterministic(uint channel)
    {
        float min = 1, max = 0;
        for (uint seed = 0; seed < 1000; seed++)
        {
            float value = Terrain.TreeRandom(seed, channel);
            Assert.Equal(value, Terrain.TreeRandom(seed, channel));
            Assert.InRange(value, 0f, 1f);
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }
        Assert.True(min < 0.05f && max > 0.95f);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Clock_RejectsNonfiniteInputWithoutPoisoningState(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepClock(value));
        var clock = new FixedStepClock(10);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(value, _ => { }));
        clock.Speed = value;
        Assert.Throws<InvalidOperationException>(() => clock.Advance(0.1, _ => { }));
        clock.Speed = 1;
        Assert.Equal(1, clock.Advance(0.1, _ => { }));
    }

    [Fact]
    public void Effects_CompactMixedLifetimesInOriginalOrder()
    {
        var effects = new WorldEffectSystem();
        for (int i = 0; i < 10000; i++)
            effects.Spawn(WorldEffectKind.TreePlanted, new OpenTK.Mathematics.Vector3(i, 0, 0), i % 2 == 0 ? 0.1 : 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => effects.Update(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => effects.Update(double.PositiveInfinity));
        effects.Update(0.2);
        Assert.Equal(5000, effects.Count);
        for (int i = 0; i < effects.Count; i++)
        {
            Assert.Equal(i * 2 + 1, effects.Active[i].Position.X);
            Assert.Equal(0.2, effects.Active[i].AgeSeconds);
        }
    }

    [Fact]
    public async Task Jobs_DisposeCancelsQueuedWorkAndDrainsPendingCount()
    {
        var jobs = new BackgroundJobScheduler(1);
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int published = 0;
        jobs.Schedule(_ => { started.SetResult(); release.Wait(); return 1; }, _ => published++);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (int i = 0; i < 100; i++) jobs.Schedule(_ => 2, _ => published++);
            jobs.Dispose();
            Assert.Throws<ObjectDisposedException>(() => jobs.Schedule(_ => 3, _ => { }));
        }
        finally
        {
            release.Set();
            jobs.Dispose();
        }
        await WaitUntilIdle(jobs);
        Assert.Equal(0, jobs.PublishCompleted());
        Assert.Equal(0, published);
    }

    [Fact]
    public async Task Jobs_ReportFailureAtFrameBoundaryAndKeepRunning()
    {
        using var jobs = new BackgroundJobScheduler(1);
        jobs.Schedule<int>(_ => throw new ArgumentException("test failure"), _ => { });
        await WaitUntilIdle(jobs);
        var error = Assert.Throws<InvalidOperationException>(() => jobs.PublishCompleted());
        Assert.IsType<ArgumentException>(error.InnerException);
        int result = 0;
        jobs.Schedule(_ => 42, value => result = value);
        await WaitUntilIdle(jobs);
        Assert.Equal(1, jobs.PublishCompleted());
        Assert.Equal(42, result);
    }

    private static async Task WaitUntilIdle(BackgroundJobScheduler jobs)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (jobs.PendingCount != 0) await Task.Delay(1, timeout.Token);
    }
}
