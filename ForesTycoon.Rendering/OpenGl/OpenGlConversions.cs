using System;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon.Rendering.OpenGl
{
    internal static class OpenGlConversions
    {
        internal static PrimitiveType Topology(PrimitiveTopology value) => value switch {
            PrimitiveTopology.Triangles => PrimitiveType.Triangles,
            PrimitiveTopology.Lines => PrimitiveType.Lines,
            _ => throw new NotSupportedException("Persistent geometry requires triangles or lines.") };
        internal static BufferUsageHint Usage(GeometryBufferUsage value) => value switch {
            GeometryBufferUsage.Static => BufferUsageHint.StaticDraw,
            GeometryBufferUsage.Dynamic => BufferUsageHint.DynamicDraw,
            GeometryBufferUsage.Stream => BufferUsageHint.StreamDraw,
            _ => throw new ArgumentOutOfRangeException(nameof(value)) };
        internal static EnableCap Capability(RenderCapability value) => value switch {
            RenderCapability.DepthTest => EnableCap.DepthTest, RenderCapability.Blend => EnableCap.Blend,
            RenderCapability.CullFace => EnableCap.CullFace, RenderCapability.PolygonOffsetFill => EnableCap.PolygonOffsetFill,
            _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    }
}
