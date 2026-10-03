using System;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    internal sealed class RenderStateScope : IDisposable
    {
        private readonly bool depthTest;
        private readonly bool blend;
        private readonly bool cullFace;
        private readonly bool polygonOffsetFill;
        private readonly bool depthMask;
        private readonly float lineWidth;
        private bool disposed;

        public RenderStateScope()
        {
            depthTest = GL.IsEnabled(EnableCap.DepthTest);
            blend = GL.IsEnabled(EnableCap.Blend);
            cullFace = GL.IsEnabled(EnableCap.CullFace);
            polygonOffsetFill = GL.IsEnabled(EnableCap.PolygonOffsetFill);
            GL.GetBoolean(GetPName.DepthWritemask, out depthMask);
            GL.GetFloat(GetPName.LineWidth, out lineWidth);
        }

        public RenderStateScope Enable(EnableCap cap)
        {
            GL.Enable(cap);
            return this;
        }

        public RenderStateScope Disable(EnableCap cap)
        {
            GL.Disable(cap);
            return this;
        }

        public RenderStateScope AlphaBlend()
        {
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            return this;
        }

        public RenderStateScope DepthWrite(bool enabled)
        {
            GL.DepthMask(enabled);
            return this;
        }

        public RenderStateScope LineWidth(float width)
        {
            // Apple core profiles commonly expose only 1px hardware lines.
            // Wide outlines must be represented as geometry, not driver state.
            GL.LineWidth(1.0f);
            return this;
        }

        public RenderStateScope PolygonOffset(float factor, float units)
        {
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(factor, units);
            return this;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            Restore(EnableCap.DepthTest, depthTest);
            Restore(EnableCap.Blend, blend);
            Restore(EnableCap.CullFace, cullFace);
            Restore(EnableCap.PolygonOffsetFill, polygonOffsetFill);
            GL.DepthMask(depthMask);
            GL.LineWidth(lineWidth);
            GL.PolygonOffset(0.0f, 0.0f);
        }

        private static void Restore(EnableCap cap, bool enabled)
        {
            if (enabled) GL.Enable(cap);
            else GL.Disable(cap);
        }
    }
}
