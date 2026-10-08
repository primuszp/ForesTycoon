using System;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon.Rendering
{
    internal sealed class RenderStateScope : IDisposable
    {
        private readonly bool depthTest;
        private readonly bool blend;
        private readonly bool cullFace;
        private readonly bool polygonOffsetFill;
        private readonly bool depthMask;
        private float lineWidth;
        private bool lineWidthCaptured;
        private int blendSourceRgb,blendDestinationRgb,blendSourceAlpha,blendDestinationAlpha,blendEquationRgb,blendEquationAlpha;
        private bool blendFunctionCaptured;
        private bool polygonOffsetCaptured;
        private float polygonOffsetFactor, polygonOffsetUnits;
        private bool disposed;

        public RenderStateScope()
        {
            depthTest = GL.IsEnabled(EnableCap.DepthTest);
            blend = GL.IsEnabled(EnableCap.Blend);
            cullFace = GL.IsEnabled(EnableCap.CullFace);
            polygonOffsetFill = GL.IsEnabled(EnableCap.PolygonOffsetFill);
            GL.GetBoolean(GetPName.DepthWritemask, out depthMask);
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
            // Solid-only scopes need no additional driver state queries.
            if(!blendFunctionCaptured) {
                GL.GetInteger(GetPName.BlendSrcRgb,out blendSourceRgb);
                GL.GetInteger(GetPName.BlendDstRgb,out blendDestinationRgb);
                GL.GetInteger(GetPName.BlendSrcAlpha,out blendSourceAlpha);
                GL.GetInteger(GetPName.BlendDstAlpha,out blendDestinationAlpha);
                GL.GetInteger(GetPName.BlendEquationRgb,out blendEquationRgb);
                GL.GetInteger(GetPName.BlendEquationAlpha,out blendEquationAlpha);
                blendFunctionCaptured=true;
            }
            GL.Enable(EnableCap.Blend);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
                BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
            return this;
        }

        public RenderStateScope DepthWrite(bool enabled)
        {
            GL.DepthMask(enabled);
            return this;
        }

        public RenderStateScope ThinLines()
        {
            // Apple core profiles commonly expose only 1px hardware lines.
            // Wide outlines must be represented as geometry, not driver state.
            if (!lineWidthCaptured)
            {
                GL.GetFloat(GetPName.LineWidth, out lineWidth);
                lineWidthCaptured = true;
            }
            GL.LineWidth(1.0f);
            return this;
        }

        public RenderStateScope PolygonOffset(float factor, float units)
        {
            if (!polygonOffsetCaptured)
            {
                GL.GetFloat(GetPName.PolygonOffsetFactor, out polygonOffsetFactor);
                GL.GetFloat(GetPName.PolygonOffsetUnits, out polygonOffsetUnits);
                polygonOffsetCaptured = true;
            }
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
            if(blendFunctionCaptured) {
                GL.BlendFuncSeparate((BlendingFactorSrc)blendSourceRgb,(BlendingFactorDest)blendDestinationRgb,
                    (BlendingFactorSrc)blendSourceAlpha,(BlendingFactorDest)blendDestinationAlpha);
                GL.BlendEquationSeparate((BlendEquationMode)blendEquationRgb,(BlendEquationMode)blendEquationAlpha);
            }
            if (lineWidthCaptured) GL.LineWidth(lineWidth);
            if (polygonOffsetCaptured) GL.PolygonOffset(polygonOffsetFactor, polygonOffsetUnits);
        }

        private static void Restore(EnableCap cap, bool enabled)
        {
            if (enabled) GL.Enable(cap);
            else GL.Disable(cap);
        }
    }
}
