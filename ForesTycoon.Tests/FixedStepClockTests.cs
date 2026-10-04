namespace ForesTycoon.Tests;

public class FixedStepClockTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(32)]
    [InlineData(128)]
    [InlineData(256)]
    public void FastForwardExecutesFixedTicksBeyondTheOldFrameCap(int speed)
    {
        var clock = new FixedStepClock(30, 2048) { Speed = speed };
        int updates = 0;
        for (int frame = 0; frame < 60; frame++)
            clock.Advance(1.0 / 60, dt => { Assert.Equal(1.0 / 30, dt); updates++; });
        Assert.InRange(updates, 30 * speed - 1, 30 * speed);
        Assert.Equal((ulong)updates, clock.Tick);
        Assert.InRange(clock.InterpolationAlpha, 0, 1);
    }

    [Fact]
    public void BudgetStopsBetweenTicksAndDropsDebtBeforeSlowingDown()
    {
        var clock = new FixedStepClock(10, 2048) { Speed = 256 };
        int updates = 0;
        Assert.Equal(3, clock.Advance(.1, _ => updates++, () => updates < 3));
        Assert.Equal((ulong)3, clock.Tick);
        Assert.InRange(clock.InterpolationAlpha, 0, 1);
        clock.Speed = 1;
        Assert.Equal(0, clock.Advance(0, _ => updates++));
        Assert.Equal(3, updates);
    }

    [Fact]
    public void ExhaustedBudgetStillAllowsOneCompleteTick()
    {
        var clock = new FixedStepClock(10, 2048) { Speed = 256 };
        Assert.Equal(1, clock.Advance(.1, dt => Assert.Equal(.1, dt), () => false));
        Assert.Equal((ulong)1, clock.Tick);
    }

    [Fact]
    public void Advance_ProducesSameTicksForDifferentRenderFrameSizes()
    {
        FixedStepClock a = new FixedStepClock(20);
        FixedStepClock b = new FixedStepClock(20);

        for (int i = 0; i < 10; i++) a.Advance(0.01, _ => { });
        b.Advance(0.1, _ => { });

        Assert.Equal(b.Tick, a.Tick);
        Assert.Equal((ulong)2, a.Tick);
    }

    [Fact]
    public void Advance_CapsCatchUpWork()
    {
        FixedStepClock clock = new FixedStepClock(30, maxTicksPerFrame: 4);

        int ticks = clock.Advance(10.0, _ => { });

        Assert.Equal(4, ticks);
        Assert.Equal((ulong)4, clock.Tick);
    }

    [Fact]
    public void Pause_DoesNotAccumulateBacklog()
    {
        FixedStepClock clock = new FixedStepClock(20) { IsPaused = true };
        clock.Advance(1.0, _ => { });
        clock.IsPaused = false;

        Assert.Equal(0, clock.Advance(0.01, _ => { }));
        Assert.Equal((ulong)0, clock.Tick);
    }

    [Fact]
    public void InterpolationAlpha_RepresentsRemainderBetweenTicks()
    {
        FixedStepClock clock = new FixedStepClock(10);

        clock.Advance(0.15, _ => { });

        Assert.Equal((ulong)1, clock.Tick);
        Assert.Equal(0.5f, clock.InterpolationAlpha, 3);
    }
}
