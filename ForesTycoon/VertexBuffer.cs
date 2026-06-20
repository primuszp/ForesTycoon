using System;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    sealed public class VertexBuffer : IDisposable
    {
        private int vboId;
        private int eboId;
        private bool disposed;
        private uint[] indices;
        private Vertex[] vertices;

        private PrimitiveType mode = PrimitiveType.Triangles;

        public int VboId
        {
            get
            {
                // Create an id on first use.
                if (vboId == 0)
                {
                    OpenTK.Graphics.GraphicsContext.Assert();

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
                // Create an id on first use.
                if (eboId == 0)
                {
                    OpenTK.Graphics.GraphicsContext.Assert();

                    GL.GenBuffers(1, out eboId);
                    if (eboId == 0) throw new Exception("Could not create VBO!");
                }
                return eboId;
            }
        }

        public VertexBuffer(PrimitiveType mode)
        {
            this.mode = mode;
        }

        public void SetData(Vertex[] data)
        {
            int size;

            if (data == null)
            {
                throw new ArgumentNullException("The data is null!");
            }
            else
            {
                this.vertices = data;
                GL.BindBuffer(BufferTarget.ArrayBuffer, VboId);
                GL.BufferData(BufferTarget.ArrayBuffer, new IntPtr(data.Length * Vertex.Stride), data, BufferUsageHint.StaticDraw);
                GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out size);
                if (vertices.Length * BlittableValueType.StrideOf(vertices) != size)
                    throw new ApplicationException("Vertex data not uploaded correctly");
            }
        }

        public void SetElements(uint[] data)
        {
            int size;

            if (data == null)
            {
                throw new ArgumentNullException("The element data is null!");
            }
            else
            {
                this.indices = data;
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, EboId);
                GL.BufferData(BufferTarget.ElementArrayBuffer, new IntPtr(indices.Length * sizeof(uint)), indices, BufferUsageHint.StaticDraw);
                GL.GetBufferParameter(BufferTarget.ElementArrayBuffer, BufferParameterName.BufferSize, out size);
                if (indices.Length * sizeof(uint) != size)
                    throw new ApplicationException("Element data not uploaded correctly");
            }
        }

        public void DrawArray()
        {
            GL.EnableClientState(ArrayCap.VertexArray);
            GL.EnableClientState(ArrayCap.NormalArray);
            GL.EnableClientState(ArrayCap.ColorArray);

            GL.BindBuffer(BufferTarget.ArrayBuffer, VboId);
            {
                GL.VertexPointer(3, VertexPointerType.Float, Vertex.Stride, new IntPtr(0));
                GL.NormalPointer(NormalPointerType.Float, Vertex.Stride, new IntPtr(3 * sizeof(float)));
                GL.ColorPointer(4, ColorPointerType.UnsignedByte, Vertex.Stride, new IntPtr(6 * sizeof(float)));
                GL.DrawArrays(mode, 0, vertices.Length);
            }
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

            GL.DisableClientState(ArrayCap.VertexArray);
            GL.DisableClientState(ArrayCap.NormalArray);
            GL.DisableClientState(ArrayCap.ColorArray);
        }

        public void DrawElements()
        {
            GL.EnableClientState(ArrayCap.VertexArray);
            GL.EnableClientState(ArrayCap.NormalArray);
            GL.EnableClientState(ArrayCap.ColorArray);

            GL.BindBuffer(BufferTarget.ArrayBuffer, VboId);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, EboId);
            {
                GL.VertexPointer(3, VertexPointerType.Float, Vertex.Stride, new IntPtr(0));
                GL.NormalPointer(NormalPointerType.Float, Vertex.Stride, new IntPtr(3 * sizeof(float)));
                GL.ColorPointer(4, ColorPointerType.UnsignedByte, Vertex.Stride, new IntPtr(6 * sizeof(float)));
                GL.DrawElements(mode, indices.Length, DrawElementsType.UnsignedInt, IntPtr.Zero);
            }
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);

            GL.DisableClientState(ArrayCap.VertexArray);
            GL.DisableClientState(ArrayCap.NormalArray);
            GL.DisableClientState(ArrayCap.ColorArray);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (vboId != 0) { GL.DeleteBuffers(1, ref vboId); vboId = 0; }
            if (eboId != 0) { GL.DeleteBuffers(1, ref eboId); eboId = 0; }
        }
    }
}
