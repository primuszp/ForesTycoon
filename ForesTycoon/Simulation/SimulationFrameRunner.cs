using System;
using System.Diagnostics;

namespace ForesTycoon
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
