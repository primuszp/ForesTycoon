namespace ForesTycoon.Tests;

public class FogDepthPlanTests
{
    [Fact]
    public void DepthPayloadHonorsBudgetAndDeviceLimitWithoutIntegerOverflow()
    {
        Assert.Equal(4096, FogDepthPlan.Create(32, 32, 4096, 4096).PayloadBytes);
        Assert.Equal(0, FogDepthPlan.Create(32, 32, 4095, 4096).PayloadBytes);
        Assert.Equal(0, FogDepthPlan.Create(4097, 1, 64 * 1024 * 1024, 4096).PayloadBytes);
        Assert.Equal(0, FogDepthPlan.Create(int.MaxValue, int.MaxValue, long.MaxValue, int.MaxValue).PayloadBytes);
        Assert.Equal(0, FogDepthPlan.Create(0, 32, 4096, 4096).PayloadBytes);
    }
}
