using System;
using OpenTK.Mathematics;
namespace ForesTycoon.Rendering
{
    internal sealed class DioramaPostProcess : IDisposable
    {
        private readonly IPostProcessBackend backend;
        internal DioramaPostProcess(IPostProcessBackend backend = null) => this.backend = backend ?? RenderDevice.Backend.CreatePostProcess();
        internal bool Active => backend.Active;
        internal static bool Enabled(IPostProcessSettings settings) => settings.Enhanced && settings.Diorama;
        internal bool Begin(IPostProcessSettings settings, int width, int height) => backend.Begin(settings, width, height);
        internal void DrawBackdrop(IPostProcessSettings settings, Vector3 color) => backend.DrawBackdrop(settings, color);
        internal void End(IPostProcessSettings settings, float pixelsPerWorldUnit, float dpiScale, float time)
            => backend.End(settings, pixelsPerWorldUnit, dpiScale, time);
        public void Dispose() => backend.Dispose();
    }
}
