using System;
namespace ForesTycoon
{
    internal sealed class ForestWeatherRenderer : IDisposable
    {
        private readonly IForestWeatherBackend backend;
        private bool disposed;
        internal ForestWeatherMetrics Metrics { get; private set; }
        internal void BeginFrame(GraphicsSettings settings) {
            backend.BeginFrame(settings);
            Metrics = backend.Metrics with { FogParticles = 0, LightningSegments = 0, CpuMilliseconds = 0, DepthFallback = false };
        }
        internal ForestWeatherRenderer(IForestWeatherBackend backend = null) => this.backend = backend ?? SceneRenderBackends.Current.CreateForestWeather();
        internal void Draw(Terrain terrain, ForestSystem forest, WeatherVisualState weather, GraphicsSettings settings, RenderContext context, EnvironmentSystem environment = null)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { backend.Draw(terrain, forest, weather, settings, context, environment); }
            finally { Metrics = backend.Metrics with { CpuMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds }; }
        }
        public void Dispose() { if (disposed) return; backend.Dispose(); disposed = true; Metrics = default; }
    }
}
