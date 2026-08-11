namespace ForesTycoon.Tests;

public class BackgroundJobSchedulerTests
{
    [Fact]
    public async Task Result_IsPublishedOnlyAtFrameBoundary()
    {
        using BackgroundJobScheduler jobs = new BackgroundJobScheduler(1);
        TaskCompletionSource computed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int liveValue = 0;

        jobs.Schedule(_ =>
        {
            computed.SetResult();
            return 42;
        }, value => liveValue = value);

        await computed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, liveValue);

        for (int i = 0; i < 100 && jobs.PublishCompleted() == 0; i++)
            await Task.Delay(1);

        Assert.Equal(42, liveValue);
    }
}
