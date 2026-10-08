using System;
namespace ForesTycoon.Effects
{
    internal sealed class CloudRenderer : IDisposable
    {
        private readonly ICloudRenderBackend backend;
        internal CloudRenderer(ICloudRenderBackend backend = null) => this.backend = backend ?? EffectRenderBackends.Current.CreateClouds();
        internal void Draw(IWeatherSurface surface, WeatherVisualState weather, IWeatherSettings settings) => backend.Draw(surface, weather, settings);
        public void Dispose() => backend.Dispose();
    }
}
