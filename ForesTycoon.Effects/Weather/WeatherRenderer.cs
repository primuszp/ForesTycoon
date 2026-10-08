using System;
namespace ForesTycoon.Effects
{
    internal sealed class WeatherRenderer : IDisposable
    {
        private readonly IWeatherRenderBackend backend;
        internal WeatherRenderer(IWeatherRenderBackend backend = null) => this.backend = backend ?? EffectRenderBackends.Current.CreateWeather();
        internal void Draw(IWeatherSurface surface, WeatherVisualState weather, RenderContext context, IWeatherSettings settings) => backend.Draw(surface, weather, context, settings);
        public void Dispose() => backend.Dispose();
    }
}
