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

        public void Reset()
        {
            stopwatch.Restart();
            lastTimeSeconds = 0.0;
            TotalTimeSeconds = 0.0;
            DeltaTimeSeconds = 0.0f;
        }

        public void Tick()
        {
            double now = stopwatch.Elapsed.TotalSeconds;
            double delta = Math.Max(0.0, now - lastTimeSeconds);
            lastTimeSeconds = now;

            TotalTimeSeconds = now;
            DeltaTimeSeconds = (float)Math.Min(delta, 0.25);
        }
    }
}
