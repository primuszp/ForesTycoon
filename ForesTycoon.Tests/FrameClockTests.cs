namespace ForesTycoon.Tests;

public class FrameClockTests
{
    [Fact]
    public void LongFrameRetainsElapsedSimulationTimeWhileLimitingPresentationDelta()
    {
        var clock = new FrameClock();
        clock.RecordTime(1.5);
        Assert.Equal(1.5, clock.SimulationDeltaSeconds);
        Assert.Equal(.25f, clock.DeltaTimeSeconds);
        clock.RecordTime(1.6);
        Assert.Equal(.1, clock.SimulationDeltaSeconds, 10);
    }
}
