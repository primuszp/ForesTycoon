using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace ForesTycoon.Rendering
{
    internal static class DynamicPrimitiveBatch
    {
        private static readonly List<ColoredVertex> source = new List<ColoredVertex>(4096);
        private static readonly List<ColoredVertex> expanded = new List<ColoredVertex>(6144);
        private static uint currentColor = 0xffffffff;
        private static bool drawing;

        // Capture the existing procedural emitters without a GL context or a draw call.
        internal static Vertex[] BuildGeometry(PrimitiveTopology primitiveType, Action draw)
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

        public static void Draw(PrimitiveTopology primitiveType, Action draw)
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

                var topology = primitiveType == PrimitiveTopology.Lines || primitiveType == PrimitiveTopology.LineLoop
                    ? PrimitiveTopology.Lines : PrimitiveTopology.Triangles;
                RenderDevice.Backend.DrawPrimitives(topology, CollectionsMarshal.AsSpan(expanded));
            }
            finally
            {
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
            source.Add(new ColoredVertex(position, currentColor));
        }

        private static void Expand(PrimitiveTopology primitiveType)
        {
            if (primitiveType == PrimitiveTopology.Quads)
            {
                if (source.Count % 4 != 0) throw new InvalidOperationException("Quad batches require four vertices per quad.");
                for (int i = 0; i < source.Count; i += 4)
                {
                    expanded.Add(source[i]); expanded.Add(source[i + 1]); expanded.Add(source[i + 2]);
                    expanded.Add(source[i]); expanded.Add(source[i + 2]); expanded.Add(source[i + 3]);
                }
                return;
            }

            if (primitiveType == PrimitiveTopology.LineLoop)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    expanded.Add(source[i]);
                    expanded.Add(source[(i + 1) % source.Count]);
                }
                return;
            }

            if (primitiveType != PrimitiveTopology.Triangles && primitiveType != PrimitiveTopology.Lines)
                throw new NotSupportedException($"Primitive type {primitiveType} is not supported by the core batcher.");
            expanded.AddRange(source);
        }

        private static uint Pack(Color color) =>
            ((uint)color.A << 24) | ((uint)color.B << 16) | ((uint)color.G << 8) | color.R;
    }
}
