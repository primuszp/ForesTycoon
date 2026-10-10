using ForesTycoon.Rendering.OpenGl;
using ForesTycoon.Models.OpenGl;
using ForesTycoon.Effects.OpenGl;

namespace ForesTycoon.OpenGl
{
    internal static class OpenGlRenderBackendBundle
    {
        internal static RenderBackendBundle Create() => new(new OpenGlGraphicsBackend(),
            model => new OpenGlModelRenderer(model), new OpenGlEffectBackendFactory(),
            new OpenGlSceneBackendFactory(), new OpenGlWindowPlatform(), Create);
    }
}
