using System;

namespace ForesTycoon.Effects
{
    internal interface IWeatherRenderBackend : IDisposable
    {
        void Draw(IWeatherSurface surface, WeatherVisualState weather, RenderContext context, IWeatherSettings settings);
    }
    internal interface ICloudRenderBackend : IDisposable
    {
        void Draw(IWeatherSurface surface, WeatherVisualState weather, IWeatherSettings settings);
    }
    internal interface IEffectRenderBackendFactory
    {
        IWeatherRenderBackend CreateWeather();
        ICloudRenderBackend CreateClouds();
    }
    internal static class EffectRenderBackends
    {
        internal static IEffectRenderBackendFactory Current { get; set; } = new OpenGl.OpenGlEffectBackendFactory();
    }
}
