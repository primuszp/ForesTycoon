using System;

namespace ForesTycoon
{
    /// <summary>Accumulates render-frame time and emits deterministic simulation ticks.</summary>
    public sealed class FixedStepClock
    {
        private double accumulatorSeconds;

        public FixedStepClock(double ticksPerSecond = 30.0, int maxTicksPerFrame = 8)
        {
            if (!double.IsFinite(ticksPerSecond) || ticksPerSecond <= 0.0 || !double.IsFinite(1.0 / ticksPerSecond))
                throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
            if (maxTicksPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(maxTicksPerFrame));

            StepSeconds = 1.0 / ticksPerSecond;
            MaxTicksPerFrame = maxTicksPerFrame;
        }

        public double StepSeconds { get; }
        public int MaxTicksPerFrame { get; }
        public double Speed { get; set; } = 1.0;
        public bool IsPaused { get; set; }
        public ulong Tick { get; private set; }
        public double SimulationTimeSeconds => Tick * StepSeconds;
        public float InterpolationAlpha => (float)(accumulatorSeconds / StepSeconds);

        public int Advance(double elapsedSeconds, Action<double> update)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (!double.IsFinite(Speed) || Speed < 0.0) throw new InvalidOperationException("Simulation speed must be finite and nonnegative.");
            if (IsPaused || Speed == 0.0) return 0;

            // Prevent a debugger break or a stalled render frame from causing a spiral of death.
            accumulatorSeconds = Math.Min(
                accumulatorSeconds + elapsedSeconds * Speed,
                StepSeconds * MaxTicksPerFrame);

            int executed = 0;
            while (accumulatorSeconds >= StepSeconds && executed < MaxTicksPerFrame)
            {
                update(StepSeconds);
                accumulatorSeconds -= StepSeconds;
                Tick++;
                executed++;
            }

            return executed;
        }

        public void Reset(ulong tick = 0)
        {
            accumulatorSeconds = 0.0;
            Tick = tick;
        }
    }
}
