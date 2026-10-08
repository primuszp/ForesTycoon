using System;
namespace ForesTycoon
{
    internal sealed class ForestWeatherRenderer : IDisposable
    {
        private readonly IForestWeatherBackend backend;
        internal ForestWeatherRenderer(IForestWeatherBackend backend = null) => this.backend = backend ?? SceneRenderBackends.Current.CreateForestWeather();
        internal void Draw(Terrain terrain, ForestSystem forest, WeatherVisualState weather, GraphicsSettings settings, RenderContext context, EnvironmentSystem environment = null)
            => backend.Draw(terrain, forest, weather, settings, context, environment);
        public void Dispose() => backend.Dispose();
    }
}
