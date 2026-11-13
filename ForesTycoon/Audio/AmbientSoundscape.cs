using System;
using ForesTycoon.Rules;

namespace ForesTycoon.Audio;

internal enum AmbientCue { Wind, Rain, Spring, Summer, Autumn, Winter, Thunder, Deer }
internal readonly record struct AmbientState(double WindMetresPerSecond, double RainMillimetresPerHour,
    bool Storm, double YearFraction, bool DeerPresent, bool Paused = false);

// Device-independent presentation: never advances or consumes randomness from the simulation.
internal interface IAmbientOutput : IDisposable
{
    void SetLoop(AmbientCue cue, float gain);
    void SetEventGain(AmbientCue cue, float gain);
    void PlayEvent(AmbientCue cue);
    void SetGains(ReadOnlySpan<float> gains)
    {
        for (int i = 0; i < gains.Length; i++)
            if (i < (int)AmbientCue.Thunder) SetLoop((AmbientCue)i, gains[i]);
            else SetEventGain((AmbientCue)i, gains[i]);
    }
}

internal sealed class AmbientSoundscape : IDisposable
{
    private readonly IAmbientOutput output;
    private readonly Random random = new(7817);
    private readonly float[] gains = new float[8];
    private double thunderWait, deerWait;
    private readonly PresentationProfile profile;
    private static readonly string[] CueKeys = Enum.GetNames<AmbientCue>();
    private bool disposed;
    internal float MasterVolume = .7f, AmbienceVolume = .7f, WildlifeVolume = .6f;
    internal bool Muted;
    internal AmbientSoundscape(IAmbientOutput output, PresentationProfile profile = null)
    {
        this.output = output; this.profile = profile ?? PresentationProfile.Default;
        thunderWait = this.profile.Sounds["Thunder"].MinimumDelay;
        deerWait = this.profile.Sounds["Deer"].MinimumDelay;
    }
    internal void Audition(AmbientCue cue)
    {
        if (disposed) return;
        var binding = profile.Sounds[CueKeys[(int)cue]];
        if (!binding.Enabled) return;
        float gain = Unit(binding.Gain * MasterVolume);
        if (cue < AmbientCue.Thunder) output.SetLoop(cue, gain);
        else { output.SetEventGain(cue, gain); output.PlayEvent(cue); }
    }
    internal static float Unit(double value) => double.IsFinite(value) ? (float)Math.Clamp(value, 0, 1) : 0;

    internal static float[] Targets(AmbientState state)
    {
        var result = new float[8];
        Targets(state, GameTuning.Default, result);
        return result;
    }

    internal static void Targets(AmbientState state, GameTuning tuning, Span<float> result)
    {
        if (result.Length < 8) throw new ArgumentException("Eight audio targets are required.", nameof(result));
        result.Clear();
        float wind = Unit(state.WindMetresPerSecond / tuning.BaseValue(Tune.AudioWindReference));
        float rain = Unit(state.RainMillimetresPerHour / tuning.BaseValue(Tune.AudioRainReference));
        result[0] = (float)tuning.BaseValue(Tune.AudioWindGain) * wind;
        result[1] = (float)tuning.BaseValue(Tune.AudioRainGain) * rain;
        // The game's year starts in spring. Equal-power overlap across each season boundary.
        double year = double.IsFinite(state.YearFraction) ? state.YearFraction - Math.Floor(state.YearFraction) : 0;
        double season = year * 4;
        int current = (int)season;
        double blend = Math.Clamp((season - current - .8) / .2, 0, 1);
        float calm = (1 - .7f * rain) * (state.Storm ? .35f : 1);
        result[2 + current] = calm * (float)tuning.BaseValue(Tune.AudioSeasonGain) * (float)Math.Cos(blend * Math.PI / 2);
        result[2 + (current + 1) % 4] += calm * (float)tuning.BaseValue(Tune.AudioSeasonGain) * (float)Math.Sin(blend * Math.PI / 2);
        result[6] = state.Storm ? (float)tuning.BaseValue(Tune.AudioThunderGain) : 0;
        result[7] = state.DeerPresent && !state.Storm ? (float)tuning.BaseValue(Tune.AudioDeerGain) * (1 - rain) : 0;
    }

    internal void Update(AmbientState state, double realSeconds, bool focused = true, GameTuning tuning = null)
    {
        if (disposed) return;
        tuning ??= GameTuning.Default;
        // No catch-up bursts after a blocked frame or debugger stop; no simulation-speed multiplier.
        double dt = double.IsFinite(realSeconds) ? Math.Clamp(realSeconds, 0, .25) : 0;
        float alpha = (float)(1 - Math.Exp(-dt / tuning.BaseValue(Tune.AudioFadeSeconds)));
        float master = Muted || !focused ? 0 : Unit(MasterVolume);
        Span<float> targets = stackalloc float[8];
        Targets(state, tuning, targets);
        for (int i = 0; i < targets.Length; i++) {
            var binding = profile.Sounds[CueKeys[i]];
            targets[i] *= binding.Enabled ? (float)binding.Gain : 0;
        }
        float total = 0; foreach (float target in targets) total += target;
        float headroom = .9f / Math.Max(.9f, total);
        Span<float> outputGains = stackalloc float[8];
        for (int i = 0; i < gains.Length; i++) {
            // Mute/focus is immediate, including already playing events. Weather and sliders fade.
            float bus = i == (int)AmbientCue.Deer ? Unit(WildlifeVolume) : Unit(AmbienceVolume);
            gains[i] += (targets[i] * bus * headroom - gains[i]) * alpha;
            outputGains[i] = master * gains[i];
        }
        output.SetGains(outputGains);
        if (!focused || Muted || state.Paused || master == 0) return;
        Event(AmbientCue.Thunder, state.Storm && targets[6] > 0 && Unit(AmbienceVolume) > 0, ref thunderWait, dt, profile.Sounds["Thunder"].MinimumDelay, profile.Sounds["Thunder"].DelayRange);
        Event(AmbientCue.Deer, targets[7] > 0 && Unit(WildlifeVolume) > 0, ref deerWait, dt, profile.Sounds["Deer"].MinimumDelay, profile.Sounds["Deer"].DelayRange);
    }

    private void Event(AmbientCue cue, bool eligible, ref double wait, double dt, double min, double range)
    {
        if (!eligible) { wait = min; return; }
        wait -= dt;
        if (wait > 0) return;
        output.PlayEvent(cue); wait = min + random.NextDouble() * range;
    }

    public void Dispose() { if (disposed) return; disposed = true; output.Dispose(); }
}
