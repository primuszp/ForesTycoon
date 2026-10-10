using System;

namespace ForesTycoon.Models
{
    internal static class ModelRenderBackends
    {
        private sealed class FactoryState { internal Func<AnimatedGlbModel, IModelRenderBackend> Create = model => new OpenGl.OpenGlModelRenderer(model); }
        private static readonly object factoryKey = new();
        private static FactoryState State => RenderDevice.GetState(factoryKey, () => new FactoryState());
        internal static Func<AnimatedGlbModel, IModelRenderBackend> Create { get => State.Create; set => State.Create = value ?? throw new ArgumentNullException(nameof(value)); }
    }
}
