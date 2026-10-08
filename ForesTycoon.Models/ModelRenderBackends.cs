using System;

namespace ForesTycoon.Models
{
    internal static class ModelRenderBackends
    {
        internal static Func<AnimatedGlbModel, IModelRenderBackend> Create { get; set; }
            = model => new OpenGl.OpenGlModelRenderer(model);
    }
}
