namespace ForesTycoon.Tests;

public class SimulationFrameRunnerTests
{
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
