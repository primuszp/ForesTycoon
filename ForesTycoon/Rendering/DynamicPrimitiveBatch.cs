using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    internal static class DynamicPrimitiveBatch
    {
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct BatchVertex
        {
            public const int Stride = 4 * sizeof(float);
            public readonly Vector3 Position;
            public readonly uint Color;

            public BatchVertex(Vector3 position, uint color)
            {
                Position = position;
                Color = color;
            }
        }

        private static readonly List<BatchVertex> source = new List<BatchVertex>(4096);
        private static readonly List<BatchVertex> expanded = new List<BatchVertex>(6144);
        private static uint currentColor = 0xffffffff;
        private static BatchVertex[] uploadBuffer = Array.Empty<BatchVertex>();
        private static int vao;
        private static int vbo;
        private static bool drawing;

        // Capture the existing procedural emitters without a GL context or a draw call.
        internal static Vertex[] BuildGeometry(PrimitiveType primitiveType, Action draw)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (drawing) throw new InvalidOperationException("Primitive batches cannot be nested.");
            drawing = true;
            source.Clear();
            expanded.Clear();
            currentColor = 0xffffffff;
            try
            {
                draw();
                Expand(primitiveType);
                var result = new Vertex[expanded.Count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = new Vertex(expanded[i].Position, Vector3.UnitZ, expanded[i].Color);
                return result;
            }
            finally
            {
                drawing = false;
            }
        }

        public static void Draw(PrimitiveType primitiveType, Action draw)
        {
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (drawing) throw new InvalidOperationException("DynamicPrimitiveBatch.Draw cannot be nested.");

            drawing = true;
            source.Clear();
            expanded.Clear();
            currentColor = 0xffffffff;
            try
            {
                draw();
                Expand(primitiveType);
                if (expanded.Count == 0) return;

                EnsureDeviceResources();
                RenderDevice.UseGeometryShader();
                GL.BindVertexArray(vao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                EnsureUploadCapacity(expanded.Count);
                expanded.CopyTo(uploadBuffer, 0);
                GL.BufferData(BufferTarget.ArrayBuffer,
                    expanded.Count * BatchVertex.Stride, uploadBuffer, BufferUsageHint.StreamDraw);
                PrimitiveType corePrimitive = primitiveType == PrimitiveType.Lines || primitiveType == PrimitiveType.LineLoop
                    ? PrimitiveType.Lines
                    : PrimitiveType.Triangles;
                GL.DrawArrays(corePrimitive, 0, expanded.Count);
                RenderMetrics.RecordDraw(expanded.Count);
            }
            finally
            {
                GL.BindVertexArray(0);
                drawing = false;
            }
        }

        public static void Color3(Color color) => currentColor = Pack(Color.FromArgb(255, color.R, color.G, color.B));
        public static void Color4(Color color) => currentColor = Pack(color);
        internal static void ColorPacked(uint color) => currentColor = color;
        public static void Vertex3(float x, float y, float z) => Vertex3(new Vector3(x, y, z));
        public static void Vertex3(Vector3 position)
        {
            if (!drawing) throw new InvalidOperationException("Vertices can only be submitted inside Draw.");
            source.Add(new BatchVertex(position, currentColor));
        }

        internal static void DisposeDeviceResources()
        {
            if (vbo != 0) GL.DeleteBuffer(vbo);
            if (vao != 0) GL.DeleteVertexArray(vao);
            vbo = vao = 0;
            uploadBuffer = Array.Empty<BatchVertex>();
        }

        private static void Expand(PrimitiveType primitiveType)
        {
            if (primitiveType == PrimitiveType.Quads)
            {
                if (source.Count % 4 != 0) throw new InvalidOperationException("Quad batches require four vertices per quad.");
                for (int i = 0; i < source.Count; i += 4)
                {
                    expanded.Add(source[i]); expanded.Add(source[i + 1]); expanded.Add(source[i + 2]);
                    expanded.Add(source[i]); expanded.Add(source[i + 2]); expanded.Add(source[i + 3]);
                }
                return;
            }

            if (primitiveType == PrimitiveType.LineLoop)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    expanded.Add(source[i]);
                    expanded.Add(source[(i + 1) % source.Count]);
                }
                return;
            }

            if (primitiveType != PrimitiveType.Triangles && primitiveType != PrimitiveType.Lines)
                throw new NotSupportedException($"Primitive type {primitiveType} is not supported by the core batcher.");
            expanded.AddRange(source);
        }

        private static void EnsureDeviceResources()
        {
            if (vao != 0) return;
            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, BatchVertex.Stride, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, BatchVertex.Stride, 3 * sizeof(float));
            GL.BindVertexArray(0);
        }

        private static void EnsureUploadCapacity(int required)
        {
            if (uploadBuffer.Length >= required) return;
            int capacity = Math.Max(256, uploadBuffer.Length);
            while (capacity < required) capacity *= 2;
            uploadBuffer = new BatchVertex[capacity];
        }

        private static uint Pack(Color color) =>
            ((uint)color.A << 24) | ((uint)color.B << 16) | ((uint)color.G << 8) | color.R;
    }
}
