namespace ForesTycoon.Tests;

public sealed class BackgroundWaitTests
{
    [Fact]
    public void PollingDoesNotObserveFailureBeforeTheOwnerJoinsOtherReaders()
    {
        var completion = new TaskCompletionSource();
        Assert.True(SimulationWork.TaskPending(completion.Task));
        completion.SetException(new ArithmeticException("worker failure"));
        Assert.False(SimulationWork.TaskPending(completion.Task));
        Assert.Throws<ArithmeticException>(() => completion.Task.GetAwaiter().GetResult());
        Assert.False(SimulationWork.TaskPending(Task.FromCanceled(new CancellationToken(true))));
    }

    [Fact]
    public void SuspendedQueryJoinStaysBoundedAndRetainsTheResult()
    {
        using var release = new ManualResetEventSlim();
        var batch = new SlicedQueryBatch(); int result = 0;
        batch.Start(0, () => { release.Wait(); result = 42; });
        using var work = batch.JoinWork().GetEnumerator();
        try {
            for (int i = 0; i < 10; i++) {
                Assert.True(SimulationWork.MoveNext(work, 3));
                Assert.InRange(work.Current, 1, 3);
                Assert.Equal(0, result);
            }
        } finally { release.Set(); }
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (SimulationWork.MoveNext(work, 3)) Assert.True(timeout.Elapsed.TotalSeconds < 10);
        Assert.Equal(42, result); Assert.False(batch.Pending);
    }
}
