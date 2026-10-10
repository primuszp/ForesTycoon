using System;

namespace ForesTycoon.Effects
{
    internal interface IWeatherRenderBackend : IDisposable
    {
        EffectMetrics Metrics => default;
        void Draw(IWeatherSurface surface, WeatherVisualState weather, RenderContext context, IWeatherSettings settings);
    }
    internal interface ICloudRenderBackend : IDisposable
    {
        EffectMetrics Metrics => default;
        void Draw(IWeatherSurface surface, WeatherVisualState weather, IWeatherSettings settings);
    }
    internal interface IEffectRenderBackendFactory
    {
        IWeatherRenderBackend CreateWeather();
        ICloudRenderBackend CreateClouds();
    }
    internal static class EffectRenderBackends
    {
        private sealed class FactoryState { internal IEffectRenderBackendFactory Current = new OpenGl.OpenGlEffectBackendFactory(); }
        private static readonly object factoryKey = new();
        private static FactoryState State => RenderDevice.GetState(factoryKey, () => new FactoryState());
        internal static IEffectRenderBackendFactory Current { get => State.Current; set => State.Current = value ?? throw new ArgumentNullException(nameof(value)); }
    }
}
