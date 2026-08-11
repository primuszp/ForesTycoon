using System;

namespace ForesTycoon
{
    readonly record struct SimulationFrameResult(int Commands, int Ticks);

    /// <summary>Owns frame-to-fixed-tick scheduling independently from any window toolkit.</summary>
    sealed class SimulationFrameRunner
    {
        public SimulationFrameRunner(double ticksPerSecond = 30.0, int maximumTicksPerFrame = 8)
        {
            Clock = new FixedStepClock(ticksPerSecond, maximumTicksPerFrame);
        }

        public FixedStepClock Clock { get; }

        public SimulationFrameResult Advance(double elapsedSeconds, Func<int> executeCommands, Action<double> update)
        {
            if (executeCommands == null) throw new ArgumentNullException(nameof(executeCommands));
            if (update == null) throw new ArgumentNullException(nameof(update));

            int commands = executeCommands();
            int ticks = Clock.Advance(elapsedSeconds, update);
            return new SimulationFrameResult(commands, ticks);
        }
    }
}
