using System;

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
}

internal sealed class AmbientSoundscape : IDisposable
{
    private readonly IAmbientOutput output;
    private readonly Random random = new(7817);
    private readonly float[] gains = new float[8];
    private double thunderWait = 8, deerWait = 18;
    private bool disposed;
    internal float MasterVolume = .7f, AmbienceVolume = .7f, WildlifeVolume = .6f;
    internal bool Muted;
    internal AmbientSoundscape(IAmbientOutput output) => this.output = output;
    internal static float Unit(double value) => double.IsFinite(value) ? (float)Math.Clamp(value, 0, 1) : 0;

    internal static float[] Targets(AmbientState state)
    {
        float wind = Unit(state.WindMetresPerSecond / 15);
        float rain = Unit(state.RainMillimetresPerHour / 12);
        var result = new float[8];
        result[0] = .45f * wind; result[1] = .55f * rain;
        // The game's year starts in spring. Equal-power overlap across each season boundary.
        double year = double.IsFinite(state.YearFraction) ? state.YearFraction - Math.Floor(state.YearFraction) : 0;
        double season = year * 4;
        int current = (int)season;
        double blend = Math.Clamp((season - current - .8) / .2, 0, 1);
        float calm = (1 - .7f * rain) * (state.Storm ? .35f : 1);
        result[2 + current] = calm * .3f * (float)Math.Cos(blend * Math.PI / 2);
        result[2 + (current + 1) % 4] += calm * .3f * (float)Math.Sin(blend * Math.PI / 2);
        result[6] = state.Storm ? .65f : 0;
        result[7] = state.DeerPresent && !state.Storm ? .35f * (1 - rain) : 0;
        return result;
    }

    internal void Update(AmbientState state, double realSeconds, bool focused = true)
    {
        if (disposed) return;
        // No catch-up bursts after a blocked frame or debugger stop; no simulation-speed multiplier.
        double dt = double.IsFinite(realSeconds) ? Math.Clamp(realSeconds, 0, .25) : 0;
        float alpha = (float)(1 - Math.Exp(-dt / 1.5));
        float master = Muted || !focused ? 0 : Unit(MasterVolume);
        float[] targets = Targets(state);
        float total = 0; foreach (float target in targets) total += target;
        float headroom = .9f / Math.Max(.9f, total);
        for (int i = 0; i < gains.Length; i++) {
            // Mute/focus is immediate, including already playing events. Weather and sliders fade.
            float bus = i == (int)AmbientCue.Deer ? Unit(WildlifeVolume) : Unit(AmbienceVolume);
            gains[i] += (targets[i] * bus * headroom - gains[i]) * alpha;
            if (i < 6) output.SetLoop((AmbientCue)i, master * gains[i]);
            else output.SetEventGain((AmbientCue)i, master * gains[i]);
        }
        if (!focused || Muted || state.Paused || master == 0) return;
        Event(AmbientCue.Thunder, state.Storm && targets[6] > 0 && Unit(AmbienceVolume) > 0, ref thunderWait, dt, 8, 18);
        Event(AmbientCue.Deer, targets[7] > 0 && Unit(WildlifeVolume) > 0, ref deerWait, dt, 20, 45);
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
