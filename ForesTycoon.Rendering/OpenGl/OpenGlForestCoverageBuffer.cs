using System;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon.Rendering.OpenGl
{
    /// <summary>Shared indexed templates, positioned and shaded from individual tree data.</summary>
    internal sealed class OpenGlForestCoverageBuffer : IForestCoverageBuffer
    {
        private static readonly object indicesKey = new();
        private static readonly byte[] indices = CreateIndices();
        private sealed class SharedIndices
        {
            internal readonly int Buffer;

            internal SharedIndices()
            {
                Buffer = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.CopyWriteBuffer, Buffer);
                GL.BufferData(BufferTarget.CopyWriteBuffer, indices.Length, indices, BufferUsageHint.StaticDraw);
                GL.BindBuffer(BufferTarget.CopyWriteBuffer, 0);
                RenderDevice.Disposing += () => GL.DeleteBuffer(Buffer);
            }
        }
        private readonly RenderResourceOwner owner = new();
        private int vao, instances;
        private int firstInstance = -1;
        private bool disposed;
        public int Count { get; private set; }

        public void SetData(ForestCoverageInstance[] data)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            owner.Check();
            Count = data.Length;
            if (data.Length == 0 && vao == 0) return;
            if (vao == 0) {
                vao = GL.GenVertexArray();
                instances = GL.GenBuffer();
                var elements = RenderDevice.GetState(indicesKey, () => new SharedIndices());
                GL.BindVertexArray(vao);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, elements.Buffer);
                for (int attribute = 6; attribute <= 12; attribute++) {
                    GL.EnableVertexAttribArray(attribute);
                    GL.VertexAttribDivisor(attribute, 1);
                }
                GL.BindVertexArray(0);
            }
            GL.BindBuffer(BufferTarget.ArrayBuffer, instances);
            GL.BufferData(BufferTarget.ArrayBuffer, data.Length * ForestCoverageInstance.Stride, data, BufferUsageHint.StaticDraw);
        }

        public void Draw(int first, int count, ForestCoveragePart part, float elapsed, float year)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            owner.Check();
            if (first < 0 || count < 0 || first > Count - count) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0) return;
            if (part == ForestCoveragePart.Bole) RenderDevice.UseGeometryShader();
            GlProgram.ApplyForestState(elapsed, year, coveragePart: (int)part);
            GL.BindVertexArray(vao);
            // OpenGL 3.3 has no base-instance draw. Offset instance attributes for road-cleared ranges.
            if (firstInstance != first) {
                GL.BindBuffer(BufferTarget.ArrayBuffer, instances);
                for (int attribute = 0; attribute < 7; attribute++)
                    GL.VertexAttribPointer(attribute + 6, 4, VertexAttribPointerType.Float, false,
                        ForestCoverageInstance.Stride, first * ForestCoverageInstance.Stride + attribute * 16);
                firstInstance = first;
            }
            int indexCount = ForestCoverageInstance.IndicesPerPart;
            int indexOffset = part == ForestCoveragePart.WhorledCrown ? indexCount : 0;
            GL.DrawElementsInstanced(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedByte, (IntPtr)indexOffset, count);
            RenderMetrics.RecordDraw(count * indexCount);
            GL.BindVertexArray(0);
            GlProgram.ApplyForestState();
        }

        public void Dispose()
        {
            if (disposed) return;
            owner.CheckIfBound();
            disposed = true;
            if (vao != 0) GL.DeleteVertexArray(vao);
            if (instances != 0) GL.DeleteBuffer(instances);
            Count = 0;
        }

        private static byte[] CreateIndices()
        {
            var result = new byte[ForestCoverageInstance.IndicesPerPart * 2];
            int offset = 0;
            AppendShell(ForestCoverageInstance.StandardSides, ForestCoverageInstance.StandardRings);
            AppendShell(ForestCoverageInstance.WhorledSides, ForestCoverageInstance.WhorledRings);
            return result;

            void AppendShell(int sides, int rings)
            {
                int tip = 1 + sides * rings;
                for (int i = 0; i < sides; i++) {
                    int a = 1 + i, b = 1 + (i + 1) % sides;
                    Triangle(0, b, a);
                    for (int ring = 1; ring < rings; ring++) {
                        Triangle(a, b, a + sides);
                        Triangle(b, b + sides, a + sides);
                        a += sides;
                        b += sides;
                    }
                    Triangle(a, b, tip);
                }
            }

            void Triangle(int a, int b, int c) {
                result[offset++] = (byte)a;
                result[offset++] = (byte)b;
                result[offset++] = (byte)c;
            }
        }
    }
}
