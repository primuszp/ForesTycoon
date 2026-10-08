using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace ForesTycoon.Rendering
{
    public enum PrimitiveTopology { Triangles, Quads, Lines, LineLoop }
    public enum GeometryBufferUsage { Static, Dynamic, Stream }
    internal enum RenderCapability { DepthTest, Blend, CullFace, PolygonOffsetFill }
    internal enum RenderCullFace { Front, Back }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct ColoredVertex
    {
        internal const int Stride = 4 * sizeof(float);
        internal readonly Vector3 Position;
        internal readonly uint Color;
        internal ColoredVertex(Vector3 position, uint color) { Position = position; Color = color; }
    }

    internal interface IForestStateBuffer : IDisposable
    {
        void SetData(Vector4[] data, int count);
    }

    internal interface IGeometryBufferBackend : IDisposable
    {
        ReadOnlySpan<Vertex> CpuVertices { get; }
        float ForestElapsedYears { get; set; }
        float ForestCurrentYear { get; set; }
        IForestStateBuffer ForestState { get; set; }
        void SetData(Vertex[] data, bool retainCpuCopy);
        void SetElements(uint[] data);
        void SetForestGrowth(ForestVertexGrowth[] data);
        IEnumerable<bool> UploadForestPages(List<Vertex> data, List<ForestVertexGrowth> growth);
        void DrawArray(bool useGeometryShader);
        void DrawElements();
        void ReadVertices(Vertex[] destination);
    }

    /// <summary>Core GPU operations grouped by resource and pass ownership, never individual native calls.</summary>
    internal interface IGraphicsBackend : IDisposable
    {
        void Initialize();
        void UseGeometryShader();
        void UseScreenLineShader(float widthPixels);
        IGeometryBufferBackend CreateGeometryBuffer(PrimitiveTopology topology, GeometryBufferUsage usage);
        IForestStateBuffer CreateForestStateBuffer();
        RenderStateScope CreateStateScope();
        IPostProcessBackend CreatePostProcess();
        // Borrowed vertices must be consumed or copied before this call returns.
        void DrawPrimitives(PrimitiveTopology topology, ReadOnlySpan<ColoredVertex> vertices);
        void InitializeFrameState();
        void SetViewport(int width, int height);
        void Clear(Vector4 color);
        // Packed RGBA8, rows starting at the framebuffer's bottom left. Backends adapt native conventions.
        void ReadPixels(int x, int y, int width, int height, byte[] rgba);
        void CheckErrors(string operation);
    }

    internal interface IPostProcessBackend : IDisposable
    {
        bool Active { get; }
        bool Begin(IPostProcessSettings settings, int width, int height);
        void DrawBackdrop(IPostProcessSettings settings, Vector3 clearColor);
        void End(IPostProcessSettings settings, float pixelsPerWorldUnit, float dpiScale, float time);
    }
}
