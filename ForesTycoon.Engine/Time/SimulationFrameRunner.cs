using System;
using System.Diagnostics;

namespace ForesTycoon.Engine
{
    internal interface ISimulationFrameTarget
    {
        int ContinueFrameCommands();
        bool ContinueFrameTick(double seconds);
        bool ForegroundPending { get; }
    }

    readonly record struct SimulationFrameResult(int Commands, int Ticks);

    /// <summary>Owns frame-to-fixed-tick scheduling independently from any window toolkit.</summary>
    sealed class SimulationFrameRunner
    {
        private readonly double maximumWorkMilliseconds;
        private readonly double catchUpWorkMilliseconds;
        private double frameWorkMilliseconds;
        private long workStarted;
        private readonly Func<bool> canContinue;
        private readonly Func<double, bool> tickCallback;
        private readonly Func<int> targetCommands;
        private readonly Func<double, bool> targetTick;
        private readonly Func<bool> targetPending;
        private ISimulationFrameTarget activeTarget;
        private Func<int> executingCommands;
        private Func<double, bool> continuingTick;
        private Func<bool> foregroundProbe;
        private int commandsExecuted;
        private bool foregroundBlocked, advancing;

        public SimulationFrameRunner(double ticksPerSecond = 30.0, int maximumTicksPerFrame = 8,
            double maximumWorkMilliseconds = double.PositiveInfinity, double? catchUpWorkMilliseconds = null)
        {
            if (double.IsNaN(maximumWorkMilliseconds) || maximumWorkMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumWorkMilliseconds));
            Clock = new FixedStepClock(ticksPerSecond, maximumTicksPerFrame);
            tickCallback = ContinueScheduledTick;
            targetCommands = () => activeTarget.ContinueFrameCommands();
            targetTick = seconds => activeTarget.ContinueFrameTick(seconds);
            targetPending = () => activeTarget.ForegroundPending;
            this.maximumWorkMilliseconds = maximumWorkMilliseconds;
            this.catchUpWorkMilliseconds = catchUpWorkMilliseconds ?? maximumWorkMilliseconds;
            if (double.IsNaN(this.catchUpWorkMilliseconds) || this.catchUpWorkMilliseconds < maximumWorkMilliseconds
                || double.IsFinite(maximumWorkMilliseconds) && !double.IsFinite(this.catchUpWorkMilliseconds))
                throw new ArgumentOutOfRangeException(nameof(catchUpWorkMilliseconds));
            frameWorkMilliseconds = maximumWorkMilliseconds;
            if (double.IsFinite(maximumWorkMilliseconds))
                canContinue = () => Stopwatch.GetElapsedTime(workStarted).TotalMilliseconds < frameWorkMilliseconds;
        }

        // Both interactive hosts use the same bounded overload policy. No logical tick is skipped.
        internal static SimulationFrameRunner Interactive() => new(maximumTicksPerFrame: 2048,
            maximumWorkMilliseconds: 4, catchUpWorkMilliseconds: 12);
        internal double FrameWorkMilliseconds => frameWorkMilliseconds;
        private void BeginFrame()
        {
            double pressure = Math.Clamp((Clock.PendingSimulationSeconds - Clock.StepSeconds * 2) / .1, 0, 1);
            frameWorkMilliseconds = double.IsFinite(maximumWorkMilliseconds)
                ? maximumWorkMilliseconds + (catchUpWorkMilliseconds - maximumWorkMilliseconds) * pressure : maximumWorkMilliseconds;
            workStarted = canContinue != null ? Stopwatch.GetTimestamp() : 0;
        }

        public FixedStepClock Clock { get; }

        internal SimulationFrameResult AdvanceWork(double elapsedSeconds, ISimulationFrameTarget target)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (advancing) throw new InvalidOperationException("A simulation frame cannot be advanced recursively.");
            activeTarget = target;
            try { return AdvanceWork(elapsedSeconds, targetCommands, targetTick, targetPending); }
            finally { activeTarget = null; }
        }

        public SimulationFrameResult AdvanceWork(double elapsedSeconds, Func<int> executeCommands, Func<double, bool> continueTick, Func<bool> foregroundPending = null)
        {
            ArgumentNullException.ThrowIfNull(executeCommands); ArgumentNullException.ThrowIfNull(continueTick);
            if (advancing) throw new InvalidOperationException("A simulation frame cannot be advanced recursively.");
            Clock.ValidateFrameInput(elapsedSeconds);
            advancing = true; executingCommands = executeCommands; continuingTick = continueTick; foregroundProbe = foregroundPending;
            try {
                BeginFrame();
                commandsExecuted = executeCommands(); int slices = 0;
                while (foregroundProbe?.Invoke() == true && slices++ < 4096 && (canContinue == null || canContinue()))
                    commandsExecuted += executeCommands();
                foregroundBlocked = foregroundProbe?.Invoke() == true;
                int ticks = Clock.AdvanceWork(elapsedSeconds, tickCallback, canContinue);
                if (foregroundProbe == null && !Clock.WorkInProgress && ticks == 0) commandsExecuted += executeCommands();
                return new(commandsExecuted, ticks);
            } finally {
                advancing = false; executingCommands = null; continuingTick = null; foregroundProbe = null;
            }
        }

        private bool ContinueScheduledTick(double seconds)
        {
            if (foregroundBlocked) return false;
            commandsExecuted += executingCommands();
            if (foregroundProbe?.Invoke() == true) return false;
            return continuingTick(seconds);
        }

        public SimulationFrameResult Advance(double elapsedSeconds, Func<int> executeCommands, Action<double> update)
        {
            if (executeCommands == null) throw new ArgumentNullException(nameof(executeCommands));
            if (update == null) throw new ArgumentNullException(nameof(update));

            if (advancing) throw new InvalidOperationException("A simulation frame cannot be advanced recursively.");
            Clock.ValidateSynchronousFrame(elapsedSeconds);
            advancing = true;
            try {
                BeginFrame();
                int commands = executeCommands();
                int ticks = Clock.Advance(elapsedSeconds, update, canContinue);
                return new SimulationFrameResult(commands, ticks);
            } finally { advancing = false; }
        }
    }
}
