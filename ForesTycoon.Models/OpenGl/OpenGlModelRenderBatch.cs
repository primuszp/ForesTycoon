using System;
using ForesTycoon.Rendering.OpenGl;

namespace ForesTycoon.Models.OpenGl
{
    internal sealed class OpenGlModelRenderBatch : OpenGlRenderStateScope, IModelRenderBatch
    {
        internal OpenGlModelRenderBatch()
        {
            Enable(RenderCapability.DepthTest);
            Disable(RenderCapability.CullFace);
        }

        internal void ThrowIfDisposed() => VerifyAccess();
    }
}
