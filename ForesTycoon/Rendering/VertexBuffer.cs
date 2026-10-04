using System;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    public sealed class VertexBuffer : IDisposable
    {
        private int vboId;
        private int eboId;
        private int vaoId;
        private int growthVbo;
        internal float ForestElapsedYears { get; set; }
        internal int ForestStateTexture { get; set; }
        private bool disposed;
        private uint[] indices;
        private Vertex[] vertices;
        private int vertexCount;

        private PrimitiveType mode = PrimitiveType.Triangles;
        private readonly BufferUsageHint usageHint;
        internal ReadOnlySpan<Vertex> CpuVertices => vertices;

        public int VboId
        {
            get
            {
                ThrowIfDisposed();

                // Create an id on first use.
                if (vboId == 0)
                {
                    GL.GenBuffers(1, out vboId);
                    if (vboId == 0) throw new Exception("Could not create VBO!");
                }
                return vboId;
            }
        }

        public int EboId
        {
            get
            {
                ThrowIfDisposed();

                // Create an id on first use.
                if (eboId == 0)
                {
                    GL.GenBuffers(1, out eboId);
                    if (eboId == 0) throw new Exception("Could not create VBO!");
                }
                return eboId;
            }
        }

        private int VaoId
        {
            get
            {
                ThrowIfDisposed();
                if (vaoId == 0) vaoId = GL.GenVertexArray();
                return vaoId;
            }
        }

        public VertexBuffer(PrimitiveType mode, BufferUsageHint usageHint = BufferUsageHint.StaticDraw)
        {
            this.mode = mode;
            this.usageHint = usageHint;
        }

        public void SetData(Vertex[] data, bool retainCpuCopy = true)
        {
            ThrowIfDisposed();
            int size;

            if (data == null) throw new ArgumentNullException(nameof(data));
            else
            {
                this.vertices = retainCpuCopy ? data : null;
                vertexCount = data.Length;
                GL.BindVertexArray(VaoId);
                GL.BindBuffer(BufferTarget.ArrayBuffer, VboId);
                GL.BufferData(BufferTarget.ArrayBuffer, new IntPtr(data.Length * Vertex.Stride), data, usageHint);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Vertex.Stride, 0);
                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, Vertex.Stride, 6 * sizeof(float));
                GL.EnableVertexAttribArray(2);
                GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, Vertex.Stride, 3 * sizeof(float));
                GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out size);
                if (data.Length * Vertex.Stride != size)
                    throw new ApplicationException("Vertex data not uploaded correctly");
                GL.BindVertexArray(0);
            }
        }

        public void SetElements(uint[] data)
        {
            ThrowIfDisposed();
            int size;

            if (data == null) throw new ArgumentNullException(nameof(data));
            else
            {
                this.indices = data;
                GL.BindVertexArray(VaoId);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, EboId);
                GL.BufferData(BufferTarget.ElementArrayBuffer, new IntPtr(indices.Length * sizeof(uint)), indices, BufferUsageHint.StaticDraw);
                GL.GetBufferParameter(BufferTarget.ElementArrayBuffer, BufferParameterName.BufferSize, out size);
                if (indices.Length * sizeof(uint) != size)
                    throw new ApplicationException("Element data not uploaded correctly");
                GL.BindVertexArray(0);
            }
        }

        internal void SetForestGrowth(ForestVertexGrowth[] data)
        {
            ThrowIfDisposed();
            if (data.Length != vertexCount) throw new ArgumentException("Growth metadata must match the vertex count.", nameof(data));
            if (growthVbo == 0) growthVbo = GL.GenBuffer();
            GL.BindVertexArray(VaoId);
            GL.BindBuffer(BufferTarget.ArrayBuffer, growthVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, new IntPtr(data.Length * ForestVertexGrowth.Stride), data, usageHint);
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, ForestVertexGrowth.Stride, 0);
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, ForestVertexGrowth.Stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(5);
            GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, ForestVertexGrowth.Stride, 6 * sizeof(float));
            GL.BindVertexArray(0);
        }

        public void DrawArray() => DrawArray(true);

        internal void DrawArray(bool useGeometryShader)
        {
            ThrowIfDisposed();
            if (vertexCount == 0) return;

            if (useGeometryShader) RenderDevice.UseGeometryShader();
            ApplyForestState();
            GL.BindVertexArray(VaoId);
            GL.DrawArrays(mode, 0, vertexCount);
            RenderMetrics.RecordDraw(vertexCount);
            GL.BindVertexArray(0);
        }

        public void DrawElements()
        {
            ThrowIfDisposed();
            if (vertexCount == 0 || indices == null || indices.Length == 0) return;

            RenderDevice.UseGeometryShader();
            ApplyForestState();
            GL.BindVertexArray(VaoId);
            GL.DrawElements(mode, indices.Length, DrawElementsType.UnsignedInt, IntPtr.Zero);
            RenderMetrics.RecordDraw(indices.Length);
            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (vboId != 0) { GL.DeleteBuffers(1, ref vboId); vboId = 0; }
            if (eboId != 0) { GL.DeleteBuffers(1, ref eboId); eboId = 0; }
            if (vaoId != 0) { GL.DeleteVertexArray(vaoId); vaoId = 0; }
            if (growthVbo != 0) { GL.DeleteBuffers(1, ref growthVbo); growthVbo = 0; }
        }

        private void ApplyForestState()
        {
            GL.GetInteger(GetPName.CurrentProgram, out int currentProgram);
            GL.Uniform1(GlProgram.Uniform(currentProgram, "forest_elapsed"), growthVbo != 0 ? ForestElapsedYears : 0);
            GL.Uniform1(GlProgram.Uniform(currentProgram, "forest_dynamic"), ForestStateTexture != 0 ? 1 : 0);
            if (ForestStateTexture == 0) return;
            GL.ActiveTexture(TextureUnit.Texture7);
            GL.BindTexture(TextureTarget.TextureBuffer, ForestStateTexture);
            GL.Uniform1(GlProgram.Uniform(currentProgram, "forest_state"), 7);
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(VertexBuffer));
        }
    }
}
