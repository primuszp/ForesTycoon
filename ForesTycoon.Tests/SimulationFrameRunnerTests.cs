namespace ForesTycoon.Tests;

public class SimulationFrameRunnerTests
{
    [Fact]
    public void InteractiveBudgetRisesWithinItsCapAndFallsAfterRecoveryWithoutDroppingTime()
    {
        var runner = SimulationFrameRunner.Interactive();
        runner.AdvanceWork(0, () => 0, _ => true);
        Assert.Equal(4, runner.FrameWorkMilliseconds);
        runner.Clock.RequestTime(1);
        runner.AdvanceWork(0, () => 0, _ => true);
        Assert.Equal(12, runner.FrameWorkMilliseconds);
        while (runner.Clock.PendingSimulationSeconds >= runner.Clock.StepSeconds)
            runner.AdvanceWork(0, () => 0, _ => true);
        runner.AdvanceWork(0, () => 0, _ => true);
        Assert.Equal(4, runner.FrameWorkMilliseconds);
        Assert.Equal(0, runner.Clock.DroppedSimulationSeconds);
        Assert.Equal(30UL, runner.Clock.Tick);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    public void RejectsInvalidCatchUpCap(double cap) => Assert.Throws<ArgumentOutOfRangeException>(
        () => new SimulationFrameRunner(maximumWorkMilliseconds: 4, catchUpWorkMilliseconds: cap));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void RejectsInvalidWorkBudget(double budget)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationFrameRunner(maximumWorkMilliseconds: budget));

    [Fact]
    public void Advance_AppliesCommandsBeforeFixedTicks()
    {
        SimulationFrameRunner runner = new SimulationFrameRunner(10);
        List<string> events = new();

        SimulationFrameResult result = runner.Advance(0.1,
            () => { events.Add("commands"); return 2; },
            _ => events.Add("tick"));

        Assert.Equal(new[] { "commands", "tick" }, events);
        Assert.Equal(2, result.Commands);
        Assert.Equal(1, result.Ticks);
    }
}
