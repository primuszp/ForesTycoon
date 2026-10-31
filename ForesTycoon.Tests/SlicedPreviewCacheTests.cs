namespace ForesTycoon.Tests;

public sealed class SlicedPreviewCacheTests
{
    private static IEnumerable<int> Query(WorkResult<int> output, int units, Action step, int value = 42)
    {
        for (int i = 0; i < units; i++) { step(); yield return 1; }
        output.Value = value;
    }

    [Fact]
    public void ExpensivePreviewResumesWithinASharedBudgetAndReusesItsResult()
    {
        using var cache = new SlicedPreviewCache<int, int>(refreshFrames: 1000);
        int calls = 0, steps = 0;
        IEnumerable<int> Factory(WorkResult<int> r) { calls++; foreach (int unit in Query(r, 100, () => steps++)) yield return unit; }
        bool ready = false; int result = 0;
        for (int frame = 0; frame < 40 && !ready; frame++) {
            cache.BeginFrame(1);
            ready = cache.Request(7, Factory, out result);
            int before = steps; cache.Advance(3);
            Assert.InRange(steps - before, 0, 3); Assert.InRange(cache.LastWorkUnits, 0, 3);
        }
        Assert.True(ready); Assert.Equal(42, result); Assert.Equal(100, steps); Assert.Equal(1, calls);
        Assert.True(cache.Request(7, Factory, out result)); cache.Advance(3);
        Assert.Equal(100, steps);
    }

    [Fact]
    public void TopologyOrRuleRevisionDisposesUnfinishedWorkAndDropsCachedResults()
    {
        using var cache = new SlicedPreviewCache<int, int>();
        bool disposed = false;
        IEnumerable<int> Slow(WorkResult<int> r) { try { for (int i = 0; i < 100; i++) yield return 1; } finally { disposed = true; } }
        cache.BeginFrame(("map", 1)); cache.Request(1, Slow, out _); cache.Advance(1);
        Assert.False(disposed);
        cache.BeginFrame(("map", 2)); Assert.True(disposed); Assert.Equal(0, cache.Count);
        Assert.False(cache.Request(1, r => Query(r, 1, () => { }, 99), out _)); cache.Advance(2);
        Assert.True(cache.Request(1, Slow, out int value)); Assert.Equal(99, value);
    }

    [Fact]
    public void RapidHoverChangesDoNotAccumulateUnboundedWorkOrDelayTheCurrentTarget()
    {
        using var cache = new SlicedPreviewCache<int, int>(capacity: 3);
        for (int frame = 0; frame < 30; frame++) {
            cache.BeginFrame(1);
            cache.Request(frame, r => Query(r, 1000, () => { }), out _);
            cache.Advance(2);
            Assert.InRange(cache.Count, 1, 3);
        }
        cache.BeginFrame(1); cache.Request(100, r => Query(r, 0, () => { }, 5), out _); cache.Advance(1);
        Assert.True(cache.Request(100, r => Query(r, 0, () => { }), out int result)); Assert.Equal(5, result);
    }

    [Fact]
    public void DynamicRulesAreRefreshedWithoutDiscardingAnUnfinishedSearchEveryFrame()
    {
        using var cache = new SlicedPreviewCache<int, int>(refreshFrames: 2);
        int value = 1;
        IEnumerable<int> Factory(WorkResult<int> r) => Query(r, 1, () => { }, value);
        cache.BeginFrame(1); cache.Request(1, Factory, out _); cache.Advance(2);
        value = 9;
        cache.BeginFrame(1); Assert.True(cache.Request(1, Factory, out int old)); Assert.Equal(1, old);
        cache.BeginFrame(1); Assert.False(cache.Request(1, Factory, out _)); cache.Advance(2);
        Assert.True(cache.Request(1, Factory, out int current)); Assert.Equal(9, current);
    }
}
