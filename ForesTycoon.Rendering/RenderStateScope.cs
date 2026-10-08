using System;
namespace ForesTycoon.Rendering
{
    internal abstract class RenderStateScope : IDisposable
    {
        internal abstract bool IsDisposed { get; }
        public abstract RenderStateScope Enable(RenderCapability capability);
        public abstract RenderStateScope Disable(RenderCapability capability);
        public abstract RenderStateScope AlphaBlend();
        public abstract RenderStateScope DepthWrite(bool enabled);
        public abstract RenderStateScope ThinLines();
        public abstract RenderStateScope PolygonOffset(float factor, float units);
        public abstract RenderStateScope Cull(RenderCullFace face);
        public abstract void Dispose();
    }
}
