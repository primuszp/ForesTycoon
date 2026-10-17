namespace ForesTycoon.Tests;

public class SlicedQueryBatchTests
{
    [Fact]
    public void InterruptedLaunchRetainsAndJoinsReadersBeforeSlotReuse()
    {
        var batch = new SlicedQueryBatch(); int calls = 0;
        using (var work = batch.RunWork(3, _ => Interlocked.Increment(ref calls)).GetEnumerator())
            Assert.True(work.MoveNext());
        Assert.True(batch.Pending);
        Assert.Throws<InvalidOperationException>(() => batch.Start(0, () => { }));
        SimulationWork.Complete(batch.JoinWork());
        Assert.Equal(1, calls); Assert.False(batch.Pending);
        SimulationWork.Complete(batch.RunWork(2, _ => Interlocked.Increment(ref calls)));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void FailureIsObservedOnlyAfterEveryReaderHasRetired()
    {
        var batch = new SlicedQueryBatch(); int calls = 0;
        batch.Start(0, () => throw new ArithmeticException("query failed"));
        batch.Start(1, () => Interlocked.Increment(ref calls));
        var error = Assert.Throws<ArithmeticException>(() => SimulationWork.Complete(batch.JoinWork()));
        Assert.Equal("query failed", error.Message); Assert.Equal(1, calls); Assert.False(batch.Pending);
        SimulationWork.Complete(batch.RunWork(1, _ => Interlocked.Increment(ref calls)));
        Assert.Equal(2, calls);
    }
}
