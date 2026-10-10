using System;
namespace ForesTycoon.Effects
{
    internal sealed class CloudRenderer : IDisposable
    {
        private readonly ICloudRenderBackend backend;
        private bool disposed;
        internal EffectMetrics Metrics { get; private set; }
        internal void BeginFrame() => Metrics = backend.Metrics with { Particles = 0, CloudSteps = 0, CpuMilliseconds = 0 };
        internal CloudRenderer(ICloudRenderBackend backend = null) => this.backend = backend ?? EffectRenderBackends.Current.CreateClouds();
        internal void Draw(IWeatherSurface surface, WeatherVisualState weather, IWeatherSettings settings) {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentNullException.ThrowIfNull(settings);
            double budget = settings.EffectCpuBudgetMilliseconds;
            if (!double.IsFinite(budget) || budget <= 0) throw new ArgumentOutOfRangeException(nameof(settings));
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { backend.Draw(surface, weather, settings); }
            finally { Metrics = backend.Metrics with { CpuMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, CpuBudgetMilliseconds = budget }; }
        }
        public void Dispose() { if (disposed) return; backend.Dispose(); disposed = true; Metrics = default; }
    }
}
