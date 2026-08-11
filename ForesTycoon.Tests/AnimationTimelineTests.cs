namespace ForesTycoon.Tests;

public class AnimationTimelineTests
{
    [Fact]
    public void Once_ClampsAndCompletes()
    {
        AnimationTimeline timeline = new AnimationTimeline(1.0).Advance(1.5);

        Assert.True(timeline.IsComplete);
        Assert.Equal(1.0, timeline.SampleTime(1f), 6);
        Assert.Equal(1f, timeline.SampleProgress(1f), 6);
    }

    [Fact]
    public void Loop_WrapsAtDuration()
    {
        AnimationTimeline timeline = new AnimationTimeline(2.0, AnimationPlayback.Loop).Advance(2.5);

        Assert.False(timeline.IsComplete);
        Assert.Equal(0.5, timeline.SampleTime(1f), 6);
    }

    [Fact]
    public void PingPong_ReversesAfterDuration()
    {
        AnimationTimeline timeline = new AnimationTimeline(2.0, AnimationPlayback.PingPong).Advance(2.5);

        Assert.Equal(1.5, timeline.SampleTime(1f), 6);
        Assert.Equal(0.75f, timeline.SampleProgress(1f), 6);
    }

    [Fact]
    public void Sample_InterpolatesBetweenFixedSimulationStates()
    {
        AnimationTimeline timeline = new AnimationTimeline(4.0).Advance(1.0).Advance(1.0);

        Assert.Equal(1.25, timeline.SampleTime(0.25f), 6);
    }
}
