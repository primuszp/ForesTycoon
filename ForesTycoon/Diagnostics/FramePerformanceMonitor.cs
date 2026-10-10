using System;
using System.Diagnostics;

namespace ForesTycoon
{
    sealed class FramePerformanceMonitor
    {
        private const double Smoothing = 0.08;
        private long frameStart;
        private long simulationStart;
        private long renderStart;
        private long allocatedAtFrameStart;

        public double FrameMilliseconds { get; private set; }
        public double SimulationMilliseconds { get; private set; }
        public double RenderMilliseconds { get; private set; }
        public long AllocatedBytes { get; private set; }
        public int SimulationTicks { get; private set; }
        public int Commands { get; private set; }
        public double DroppedSimulationSeconds { get; private set; }
        public int DrawCalls => RenderMetrics.DrawCalls;
        public int SubmittedVertices => RenderMetrics.SubmittedVertices;

        public void BeginFrame()
        {
            frameStart = Stopwatch.GetTimestamp();
            allocatedAtFrameStart = GC.GetAllocatedBytesForCurrentThread();
            RenderMetrics.BeginFrame();
        }

        public void BeginSimulation() => simulationStart = Stopwatch.GetTimestamp();

        public void EndSimulation(int commands, int ticks, double droppedSimulationSeconds = 0)
        {
            SimulationMilliseconds = Smooth(SimulationMilliseconds, ElapsedMilliseconds(simulationStart));
            Commands = commands;
            SimulationTicks = ticks;
            DroppedSimulationSeconds = droppedSimulationSeconds;
        }

        public void BeginRender() => renderStart = Stopwatch.GetTimestamp();

        public void EndRender()
        {
            RenderMilliseconds = Smooth(RenderMilliseconds, ElapsedMilliseconds(renderStart));
            AllocatedBytes = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocatedAtFrameStart);
            FrameMilliseconds = Smooth(FrameMilliseconds, ElapsedMilliseconds(frameStart));
        }

        private static double ElapsedMilliseconds(long start) =>
            (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

        private static double Smooth(double current, double sample) =>
            current <= 0.0 ? sample : current + (sample - current) * Smoothing;
    }
}
