using System;
using OpenTK.Mathematics;
namespace ForesTycoon
{
    internal sealed class SurfaceVisualRenderer : IDisposable, ISurfaceVisuals
    {
        private readonly ISurfaceRenderBackend backend;
        internal SurfaceVisualRenderer(GraphicsSettings settings, WeatherVisualState weather,
            EnvironmentSystem environment = null, Terrain terrain = null, ISurfaceRenderBackend backend = null)
            => this.backend = backend ?? SceneRenderBackends.Current.CreateSurface(settings, weather, environment, terrain);
        public SurfaceKind Kind { get => backend.Kind; set => backend.Kind = value; }
        public bool ShadowPass => backend.ShadowPass;
        public Matrix4 ShadowCamera => backend.ShadowCamera;
        public Vector4 Atmosphere => backend.Atmosphere;
        public bool ShadowsReady => backend.ShadowsReady;
        public bool Active => backend.Active;
        internal void BeginFrame() => backend.BeginFrame();
        internal void RenderShadows(Terrain terrain, Action draw) => backend.RenderShadows(terrain, draw);
        public void Use(float outlineWidth = 0) => backend.Use(outlineWidth);
        public void Dispose() => backend.Dispose();
    }
}
