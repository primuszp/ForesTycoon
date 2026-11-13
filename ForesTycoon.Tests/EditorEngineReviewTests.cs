using ForesTycoon.Audio;
using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public sealed class EditorEngineReviewTests
{
    [Theory]
    [InlineData(1)] [InlineData(10)] [InlineData(180)]
    public void PausedExplicitTimeCompletesEveryRequestedTick(int seconds)
    {
        var clock = new FixedStepClock(30, 8) { IsPaused = true };
        clock.RequestTime(seconds);
        for (int frame = 0; frame < seconds * 30; frame++) clock.AdvanceWork(0, _ => true);
        Assert.Equal((ulong)seconds * 30, clock.Tick);
        Assert.InRange(clock.PendingSimulationSeconds, 0, 1e-8);
        Assert.Equal(0, clock.AdvanceWork(0, _ => throw new Exception("Unexpected tick")));
    }

    [Fact]
    public void RequestDuringPartialTickAddsWorkAfterThatTick()
    {
        var clock = new FixedStepClock(10, 8);
        clock.AdvanceWork(.1, _ => false, () => false);
        Assert.True(clock.WorkInProgress);
        clock.IsPaused = true;
        clock.RequestTick();
        Assert.Equal(2, clock.AdvanceWork(0, _ => true));
        Assert.Equal(2UL, clock.Tick);
        Assert.Equal(0, clock.PendingSimulationSeconds);
    }

    [Fact]
    public void RejectedExplicitTimeRequestsLeaveTheQueueUnchanged()
    {
        var clock = new FixedStepClock(1) { IsPaused = true };
        clock.RequestTime(int.MaxValue);
        double before = clock.PendingSimulationSeconds;
        Assert.Throws<ArgumentOutOfRangeException>(clock.RequestTick);
        Assert.Equal(before, clock.PendingSimulationSeconds);
        var slow = new FixedStepClock(1e-308);
        slow.RequestTick();
        Assert.Throws<ArgumentOutOfRangeException>(slow.RequestTick);
        Assert.True(double.IsFinite(slow.PendingSimulationSeconds));
    }

    [Fact]
    public void CubicRotationRejectsZeroQuaternionInsteadOfPublishingNan()
    {
        var channel = new AnimatedGlbModel.Channel {
            Path = AnimatedGlbModel.AnimationPath.Rotation, Arity = 4,
            Interpolation = AnimatedGlbModel.AnimationInterpolation.CubicSpline,
            Times = new float[] { 0, 1 },
            Values = new float[] { 0,0,0,0, 0,0,0,1, 0,0,0,0, 0,0,0,0, 0,0,0,-1, 0,0,0,0 }
        };
        Assert.Throws<InvalidDataException>(() => channel.Sample(.5f));
        Assert.Equal(Vector4.UnitW, channel.Sample(0));
        Assert.Equal(-Vector4.UnitW, channel.Sample(1));
    }

    [Fact]
    public void EditorAudioTuningRoundTripsIntoTheRuntimeMixer()
    {
        var document = new EditorDocument();
        document.Load(SimulationWorld.DefaultRuleProject());
        // The same tuning model backs the editor's native parameter table and project serialization.
        document.Scene.SetTuning(nameof(Tune.AudioRainGain), .2);
        document.Scene.SetTuning(nameof(Tune.AudioRainReference), 4);
        Assert.NotNull(document.History.Undo());
        Assert.Equal(12, document.Tuning.Get(nameof(Tune.AudioRainReference)));
        Assert.NotNull(document.History.Redo());
        var project = document.Capture();
        var loaded = SimulationWorld.NormalizeRuleProject(RuleProject.FromJson(project.ToJson()));
        var runtime = GameTuning.FromOverrides(loaded.Tuning);
        Span<float> targets = stackalloc float[8];
        AmbientSoundscape.Targets(new(0, 4, false, 0, false), runtime, targets);
        Assert.Equal(.2f, targets[(int)AmbientCue.Rain]);
        Assert.DoesNotContain(WorldBehaviorPolicy.Hooks, h => h.Id == "tuning.AudioRainGain");
        var catalog = CurrentGameRules.Build(tuning: runtime);
        Assert.Contains(catalog.Rules, r => r.Id == "audio.ambience" && r.Parameters.Any(p => p.Key == "AudioRainGain" && p.Value == .2));
    }

    [Fact]
    public void TenSecondRainTrialUsesAValidWeatherEvent()
    {
        var project = SimulationWorld.DefaultRuleProject();
        project.World = SimulationWorld.DefaultScenario(42, 16); project.Algorithms = null;
        var result = TrialService.Run(project, project, 25, 10);
        Assert.True(result.Fits(16, 16));
        Assert.Equal(result.Baseline, result.Edited);
        Assert.All(result.Water, water => Assert.True(double.IsFinite(water) && water >= 0));
    }

    [Theory]
    [InlineData(-1, 1)] [InlineData(41, 1)] [InlineData(0, -1)] [InlineData(0, int.MaxValue)]
    public void RainTrialRejectsInvalidRequestsBeforeStartingWork(int intensity, int seconds)
    {
        using var trial = new TrialService();
        Assert.Throws<ArgumentOutOfRangeException>(() => trial.Start(null!, null!, intensity, seconds, "test"));
        Assert.False(trial.IsBusy);
        Assert.Throws<ArgumentOutOfRangeException>(() => TrialService.Run(null!, null!, intensity, seconds));
    }
}
