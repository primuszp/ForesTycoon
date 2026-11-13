using System;

namespace ForesTycoon.Engine
{
    /// <summary>Accumulates render-frame time and emits deterministic simulation ticks.</summary>
    public sealed class FixedStepClock
    {
        private double accumulatorSeconds;
        private bool workInProgress;
        private bool workConsumesRequest;
        private int requestedTicks;
        public bool WorkInProgress => workInProgress;
        public double PendingSimulationSeconds => accumulatorSeconds;
        public void RequestTick()
        {
            RequestTime(StepSeconds);
        }
        public void RequestTime(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0 || seconds / StepSeconds > int.MaxValue - requestedTicks)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            int ticks = (int)Math.Ceiling(seconds / StepSeconds);
            double next = accumulatorSeconds + ticks * StepSeconds;
            if (!double.IsFinite(next)) throw new ArgumentOutOfRangeException(nameof(seconds));
            accumulatorSeconds = next;
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
            ValidateSynchronousFrame(elapsedSeconds);
            double requested = accumulatorSeconds + elapsedSeconds * Speed;
            LastDroppedSimulationSeconds = 0;
            if (IsPaused || Speed == 0.0) return 0;

            // Prevent a debugger break or a stalled render frame from causing a spiral of death.
            accumulatorSeconds = Math.Min(requested, StepSeconds * MaxTicksPerFrame);
            LastDroppedSimulationSeconds = requested - accumulatorSeconds;

            int executed = RunTicks(dt => { update(dt); return true; }, canContinue, MaxTicksPerFrame);

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
            ValidateFrameInput(elapsedSeconds);
            if (!IsPaused) accumulatorSeconds += elapsedSeconds * Speed;
            LastDroppedSimulationSeconds = 0;
            return RunTicks(continueTick, canContinue);
        }

        internal void ValidateSynchronousFrame(double elapsedSeconds)
        {
            ValidateFrameInput(elapsedSeconds);
            if (workInProgress || requestedTicks > 0)
                throw new InvalidOperationException("Complete cooperative work and manual tick requests through AdvanceWork before using the capped adapter.");
        }

        internal void ValidateFrameInput(double elapsedSeconds)
        {
            if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (!double.IsFinite(Speed) || Speed < 0) throw new InvalidOperationException("Simulation speed must be finite and nonnegative.");
            if (!double.IsFinite(accumulatorSeconds + elapsedSeconds * Speed)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Scaled frame time must be finite.");
        }

        private int RunTicks(Func<double, bool> continueTick, Func<bool> canContinue, int maximumSlices = 4096)
        {
            int ticks = 0, slices = 0;
            // Bound even an accidental host without a wall-clock budget. Decisions within a tick never depend on time.
            while ((workInProgress || requestedTicks > 0 || !IsPaused && Speed > 0 && accumulatorSeconds >= StepSeconds) && ticks < MaxTicksPerFrame && slices < maximumSlices) {
                if (slices > 0 && canContinue != null && !canContinue()) break;
                if (Tick == ulong.MaxValue) throw new InvalidOperationException("Simulation tick counter exhausted.");
                if (!workInProgress) workConsumesRequest = requestedTicks > 0;
                workInProgress = true;
                bool complete = continueTick(StepSeconds);
                slices++;
                if (!complete) continue;
                workInProgress = false;
                if (workConsumesRequest) requestedTicks--;
                workConsumesRequest = false;
                accumulatorSeconds = Math.Max(0, accumulatorSeconds - StepSeconds);
                Tick++; ticks++;
            }
            return ticks;
        }

        public void Reset(ulong tick = 0)
        {
            accumulatorSeconds = 0.0;
            workInProgress = false;
            workConsumesRequest = false;
            requestedTicks = 0;
            Tick = tick;
            DroppedSimulationSeconds = LastDroppedSimulationSeconds = 0;
        }
    }
}
