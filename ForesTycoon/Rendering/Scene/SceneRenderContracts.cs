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
        ForestWeatherMetrics Metrics => default;
        void BeginFrame(GraphicsSettings settings) { }
        void Draw(Terrain terrain, ForestSystem forest, WeatherVisualState weather, GraphicsSettings settings,
            RenderContext context, EnvironmentSystem environment);
    }
    internal readonly record struct ForestWeatherMetrics(int FogParticles, int LightningSegments, long CpuPayloadBytes,
        long GpuPayloadBytes, double CpuMilliseconds, bool DepthFallback);
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
        private sealed class FactoryState { internal ISceneRenderBackendFactory Current = new OpenGl.OpenGlSceneBackendFactory(); }
        private static readonly object factoryKey = new();
        private static FactoryState State => RenderDevice.GetState(factoryKey, () => new FactoryState());
        internal static ISceneRenderBackendFactory Current { get => State.Current; set => State.Current = value ?? throw new ArgumentNullException(nameof(value)); }
    }
}
