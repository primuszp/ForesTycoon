using System;
using System.Diagnostics;

namespace ForesTycoon
{
    sealed class FrameClock
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private double lastTimeSeconds;

        public double TotalTimeSeconds { get; private set; }
        public float DeltaTimeSeconds { get; private set; }
        public double SimulationDeltaSeconds { get; private set; }

        public void Reset()
        {
            stopwatch.Restart();
            lastTimeSeconds = 0.0;
            TotalTimeSeconds = 0.0;
            DeltaTimeSeconds = 0.0f;
            SimulationDeltaSeconds = 0.0;
        }

        public void Tick() => RecordTime(stopwatch.Elapsed.TotalSeconds);

        internal void RecordTime(double now)
        {
            double delta = Math.Max(0.0, now - lastTimeSeconds);
            lastTimeSeconds = now;

            TotalTimeSeconds = now;
            SimulationDeltaSeconds = delta;
            DeltaTimeSeconds = (float)Math.Min(delta, 0.25);
        }
    }
}
