using System;

namespace ForesTycoon.Engine
{
    /// <summary>Accumulates render-frame time and emits deterministic simulation ticks.</summary>
    public sealed class FixedStepClock
    {
        private double accumulatorSeconds;
        private bool workInProgress;
        private int requestedTicks;
        public bool WorkInProgress => workInProgress;
        public double PendingSimulationSeconds => accumulatorSeconds;
        public void RequestTick()
        {
            accumulatorSeconds += StepSeconds;
            requestedTicks++;
        }
        public void RequestTime(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0 || seconds / StepSeconds > int.MaxValue - requestedTicks)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            int ticks = (int)Math.Ceiling(seconds / StepSeconds);
            accumulatorSeconds += ticks * StepSeconds;
            requestedTicks += ticks;
        }

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
        /// <summary>Same tick interval as pose interpolation, advancing between completed ticks too.</summary>
        public double PresentationTimeSeconds => Math.Max(0, SimulationTimeSeconds - StepSeconds + Math.Min(accumulatorSeconds, StepSeconds * 2));
        public float InterpolationAlpha => (float)Math.Clamp(accumulatorSeconds / StepSeconds, 0, 1);
        /// <summary>Vehicle presentation may predict at most one tick while cooperative work finishes.</summary>
        public float MotionInterpolationAlpha => (float)Math.Clamp(accumulatorSeconds / StepSeconds, 0, 2);
        /// <summary>Simulated time discarded by the catch-up cap or host work budget.</summary>
        public double DroppedSimulationSeconds { get; private set; }
        public double LastDroppedSimulationSeconds { get; private set; }

        public int Advance(double elapsedSeconds, Action<double> update, Func<bool> canContinue = null)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (!double.IsFinite(Speed) || Speed < 0.0) throw new InvalidOperationException("Simulation speed must be finite and nonnegative.");
            double requested = accumulatorSeconds + elapsedSeconds * Speed;
            if (!double.IsFinite(requested)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Scaled frame time must be finite.");
            LastDroppedSimulationSeconds = 0;
            if (IsPaused || Speed == 0.0) return 0;

            // Prevent a debugger break or a stalled render frame from causing a spiral of death.
            accumulatorSeconds = Math.Min(requested, StepSeconds * MaxTicksPerFrame);
            LastDroppedSimulationSeconds = requested - accumulatorSeconds;

            int executed = 0;
            while (accumulatorSeconds >= StepSeconds && executed < MaxTicksPerFrame)
            {
                // The host budget is checked between complete deterministic ticks.
                if (executed > 0 && canContinue != null && !canContinue()) break;
                update(StepSeconds);
                accumulatorSeconds -= StepSeconds;
                Tick++;
                executed++;
            }

            // Slow down under load instead of retaining whole-tick debt after fast-forward.
            if (accumulatorSeconds >= StepSeconds)
            {
                double remainder = accumulatorSeconds % StepSeconds;
                LastDroppedSimulationSeconds += accumulatorSeconds - remainder;
                accumulatorSeconds = remainder;
            }
            DroppedSimulationSeconds = Math.Min(double.MaxValue, DroppedSimulationSeconds + LastDroppedSimulationSeconds);
            return executed;
        }

        /// <summary>Interactive scheduling: retain debt and resume a partial tick without skipping simulation time.</summary>
        public int AdvanceWork(double elapsedSeconds, Func<double, bool> continueTick, Func<bool> canContinue = null)
        {
            ArgumentNullException.ThrowIfNull(continueTick);
            if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0 || !double.IsFinite(Speed) || Speed < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (!IsPaused) {
                double next = accumulatorSeconds + elapsedSeconds * Speed;
                if (!double.IsFinite(next)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
                accumulatorSeconds = next;
            }
            LastDroppedSimulationSeconds = 0;
            int ticks = 0, slices = 0;
            // Bound even an accidental host without a wall-clock budget. Decisions within a tick never depend on time.
            while ((workInProgress || (!IsPaused && Speed > 0 || requestedTicks > 0) && accumulatorSeconds >= StepSeconds) && ticks < MaxTicksPerFrame && slices < 4096) {
                if (slices > 0 && canContinue != null && !canContinue()) break;
                workInProgress = true;
                bool complete = continueTick(StepSeconds);
                slices++;
                if (!complete) continue;
                workInProgress = false;
                if (requestedTicks > 0) requestedTicks--;
                accumulatorSeconds = Math.Max(0, accumulatorSeconds - StepSeconds);
                Tick++; ticks++;
            }
            return ticks;
        }

        public void Reset(ulong tick = 0)
        {
            accumulatorSeconds = 0.0;
            workInProgress = false;
            requestedTicks = 0;
            Tick = tick;
            DroppedSimulationSeconds = LastDroppedSimulationSeconds = 0;
        }
    }
}
