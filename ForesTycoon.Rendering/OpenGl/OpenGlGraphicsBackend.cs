using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon.Rendering.OpenGl
{
    internal sealed class OpenGlGraphicsBackend : IGraphicsBackend
    {
        private readonly RenderResourceOwner owner = new();
        private readonly OpenGlGeometryProgram geometry = new();
        private readonly OpenGlScreenLineProgram screenLines = new();
        private ColoredVertex[] upload = Array.Empty<ColoredVertex>();
        private int vao, buffer;

        public void Initialize() { owner.Check(); geometry.Initialize(); }
        public void UseGeometryShader() { owner.Check(); geometry.Use(); }
        public void UseScreenLineShader(float widthPixels) { owner.Check(); screenLines.Use(widthPixels); }
        public IGeometryBufferBackend CreateGeometryBuffer(PrimitiveTopology topology, GeometryBufferUsage usage)
            { owner.Check(); return new OpenGlVertexBuffer(topology, usage); }
        public IForestStateBuffer CreateForestStateBuffer() { owner.Check(); return new OpenGlForestStateBuffer(); }
        public RenderStateScope CreateStateScope() { owner.Check(); return new OpenGlRenderStateScope(); }
        public IPostProcessBackend CreatePostProcess() { owner.Check(); return new OpenGlPostProcess(); }
        public void InitializeFrameState()
        {
            owner.Check();
            GL.Enable(EnableCap.DepthTest); GL.Disable(EnableCap.CullFace); GL.LineWidth(1);
        }
        public void SetViewport(int width, int height) { owner.Check(); GL.Viewport(0, 0, width, height); }
        public void Clear(Vector4 color)
        {
            owner.Check();
            GL.ClearColor(color.X, color.Y, color.Z, color.W);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }
        public void ReadPixels(int x, int y, int width, int height, byte[] rgba)
            { owner.Check(); GL.ReadPixels(x, y, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, rgba); }
        public void CheckErrors(string operation)
        {
            owner.Check();
            ErrorCode error = GL.GetError();
            if (error != ErrorCode.NoError) throw new InvalidOperationException($"{operation}: {error}");
        }
        public void DrawPrimitives(PrimitiveTopology topology, ReadOnlySpan<ColoredVertex> vertices)
        {
            owner.Check();
            if (vertices.IsEmpty) return;
            if (vao == 0)
            {
                vao = GL.GenVertexArray(); buffer = GL.GenBuffer();
                GL.BindVertexArray(vao); GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, ColoredVertex.Stride, 0);
                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, ColoredVertex.Stride, 3 * sizeof(float));
            }
            geometry.Use();
            GL.GetInteger(GetPName.CurrentProgram, out int program);
            GL.Uniform1(GlProgram.Uniform(program, "forest_dynamic"), 0);
            GL.Uniform1(GlProgram.Uniform(program, "forest_elapsed"), 0f);
            if (upload.Length < vertices.Length) upload = new ColoredVertex[Math.Max(vertices.Length, Math.Max(256, upload.Length * 2))];
            vertices.CopyTo(upload);
            GL.BindVertexArray(vao); GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * ColoredVertex.Stride, upload, BufferUsageHint.StreamDraw);
            GL.DrawArrays(OpenGlConversions.Topology(topology), 0, vertices.Length);
            RenderMetrics.RecordDraw(vertices.Length);
            GL.BindVertexArray(0);
        }
        public void Dispose()
        {
            owner.CheckIfBound();
            if (buffer != 0) GL.DeleteBuffer(buffer);
            if (vao != 0) GL.DeleteVertexArray(vao);
            vao = buffer = 0; upload = Array.Empty<ColoredVertex>();
            screenLines.Dispose();
            geometry.Dispose();
        }
    }
}
