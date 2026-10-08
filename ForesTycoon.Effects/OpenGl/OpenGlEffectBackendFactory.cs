namespace ForesTycoon.Effects.OpenGl
{
    internal sealed class OpenGlEffectBackendFactory : IEffectRenderBackendFactory
    {
        public IWeatherRenderBackend CreateWeather() => new OpenGlWeatherRenderer();
        public ICloudRenderBackend CreateClouds() => new OpenGlCloudRenderer();
    }
}
