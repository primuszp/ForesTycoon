using System;
using ImGuiNET;

namespace ForesTycoon
{
    internal interface ISurfaceRenderBackend : ISurfaceVisuals, IDisposable
    {
        void BeginFrame();
        void RenderShadows(Terrain terrain, Action draw);
    }
    internal interface IForestMaterialBackend : IDisposable { void Use(float outlineWorldWidth); }
    internal interface IForestWeatherBackend : IDisposable
    {
        void Draw(Terrain terrain, ForestSystem forest, WeatherVisualState weather, GraphicsSettings settings,
            RenderContext context, EnvironmentSystem environment);
    }
    // DrawData and font texture IDs are ImGui's opaque API types; the controller does not interpret them.
    internal interface IUiRenderBackend : IDisposable
    {
        void Initialize(ImGuiIOPtr io);
        void Draw(ImDrawDataPtr data);
    }
    internal interface ISceneRenderBackendFactory
    {
        ISurfaceRenderBackend CreateSurface(GraphicsSettings settings, WeatherVisualState weather,
            EnvironmentSystem environment, Terrain terrain);
        IForestMaterialBackend CreateForestMaterial();
        IForestWeatherBackend CreateForestWeather();
        IUiRenderBackend CreateUi();
    }
    internal static class SceneRenderBackends
    {
        internal static ISceneRenderBackendFactory Current { get; set; } = new OpenGl.OpenGlSceneBackendFactory();
    }
}
