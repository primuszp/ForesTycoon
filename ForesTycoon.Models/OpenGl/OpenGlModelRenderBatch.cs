using System;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon.Models.OpenGl
{
    internal sealed class OpenGlModelRenderBatch : RenderStateScope, IModelRenderBatch
    {
        internal OpenGlModelRenderBatch()
        {
            Enable(EnableCap.DepthTest);
            Disable(EnableCap.CullFace);
        }

        internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);
    }
}
