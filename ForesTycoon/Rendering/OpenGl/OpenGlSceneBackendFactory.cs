namespace ForesTycoon.OpenGl
{
    internal sealed class OpenGlSceneBackendFactory : ISceneRenderBackendFactory
    {
        public ISurfaceRenderBackend CreateSurface(GraphicsSettings settings, WeatherVisualState weather, EnvironmentSystem environment, Terrain terrain)
            => new OpenGlSurfaceVisualRenderer(settings, weather, environment, terrain);
        public IForestMaterialBackend CreateForestMaterial() => new OpenGlForestMaterial();
        public IForestWeatherBackend CreateForestWeather() => new OpenGlForestWeatherRenderer();
        public IUiRenderBackend CreateUi() => new OpenGlImGuiRenderer();
    }
}
