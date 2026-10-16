using System;
using System.Diagnostics;

namespace ForesTycoon.Engine
{
    readonly record struct SimulationFrameResult(int Commands, int Ticks);

    /// <summary>Owns frame-to-fixed-tick scheduling independently from any window toolkit.</summary>
    sealed class SimulationFrameRunner
    {
        private readonly double maximumWorkMilliseconds;
        private long workStarted;
        private readonly Func<bool> canContinue;

        public SimulationFrameRunner(double ticksPerSecond = 30.0, int maximumTicksPerFrame = 8,
            double maximumWorkMilliseconds = double.PositiveInfinity)
        {
            if (double.IsNaN(maximumWorkMilliseconds) || maximumWorkMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumWorkMilliseconds));
            Clock = new FixedStepClock(ticksPerSecond, maximumTicksPerFrame);
            this.maximumWorkMilliseconds = maximumWorkMilliseconds;
            if (double.IsFinite(maximumWorkMilliseconds))
                canContinue = () => Stopwatch.GetElapsedTime(workStarted).TotalMilliseconds < this.maximumWorkMilliseconds;
        }

        public FixedStepClock Clock { get; }

        public SimulationFrameResult AdvanceWork(double elapsedSeconds, Func<int> executeCommands, Func<double, bool> continueTick, Func<bool> foregroundPending = null)
        {
            ArgumentNullException.ThrowIfNull(executeCommands);
            ArgumentNullException.ThrowIfNull(continueTick);
            workStarted = canContinue != null ? Stopwatch.GetTimestamp() : 0;
            int commands = executeCommands(), slices = 0;
            while (foregroundPending?.Invoke() == true && slices++ < 4096 && (canContinue == null || canContinue()))
                commands += executeCommands();
            bool foregroundBlocked = foregroundPending?.Invoke() == true;
            int ticks = Clock.AdvanceWork(elapsedSeconds, dt => {
                if (foregroundBlocked) return false;
                commands += executeCommands();
                if (foregroundPending?.Invoke() == true) return false;
                return continueTick(dt);
            }, canContinue);
            // Paused worlds still accept input when there is no partial tick.
            if (foregroundPending == null && !Clock.WorkInProgress && ticks == 0) commands += executeCommands();
            return new(commands, ticks);
        }

        public SimulationFrameResult Advance(double elapsedSeconds, Func<int> executeCommands, Action<double> update)
        {
            if (executeCommands == null) throw new ArgumentNullException(nameof(executeCommands));
            if (update == null) throw new ArgumentNullException(nameof(update));

            int commands = executeCommands();
            workStarted = canContinue != null ? Stopwatch.GetTimestamp() : 0;
            int ticks = Clock.Advance(elapsedSeconds, update, canContinue);
            return new SimulationFrameResult(commands, ticks);
        }
    }
}
