using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace ForesTycoon.Rendering
{
    internal static class DynamicPrimitiveBatch
    {
        private sealed class BatchState
        {
            internal readonly List<ColoredVertex> Source = new(4096), Expanded = new(6144);
            internal uint Color = 0xffffffff;
            internal bool Drawing;
        }
        private static readonly object stateKey = new();
        private static BatchState State => RenderDevice.GetState(stateKey, () => new BatchState());
        private static List<ColoredVertex> source => State.Source;
        private static List<ColoredVertex> expanded => State.Expanded;
        private static uint currentColor { get => State.Color; set => State.Color = value; }
        private static bool drawing { get => State.Drawing; set => State.Drawing = value; }

        // Capture the existing procedural emitters without a GL context or a draw call.
        internal static Vertex[] BuildGeometry(PrimitiveTopology primitiveType, Action draw)
        {
            var owner = RenderDevice.Environment;
            var state = State;
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (drawing) throw new InvalidOperationException("Primitive batches cannot be nested.");
            drawing = true;
            source.Clear();
            expanded.Clear();
            currentColor = 0xffffffff;
            try
            {
                draw();
                if (!ReferenceEquals(RenderDevice.Environment, owner)) throw new InvalidOperationException("A primitive emitter cannot change render environments.");
                Expand(primitiveType);
                var result = new Vertex[expanded.Count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = new Vertex(expanded[i].Position, Vector3.UnitZ, expanded[i].Color);
                return result;
            }
            finally
            {
                state.Drawing = false;
            }
        }

        public static void Draw(PrimitiveTopology primitiveType, Action draw)
        {
            var owner = RenderDevice.Environment;
            var state = State;
            if (draw == null) throw new ArgumentNullException(nameof(draw));
            if (drawing) throw new InvalidOperationException("DynamicPrimitiveBatch.Draw cannot be nested.");

            drawing = true;
            source.Clear();
            expanded.Clear();
            currentColor = 0xffffffff;
            try
            {
                draw();
                owner.VerifyAccess();
                Expand(primitiveType);
                if (expanded.Count == 0) return;

                var topology = primitiveType == PrimitiveTopology.Lines || primitiveType == PrimitiveTopology.LineLoop
                    ? PrimitiveTopology.Lines : PrimitiveTopology.Triangles;
                RenderDevice.Backend.DrawPrimitives(topology, CollectionsMarshal.AsSpan(expanded));
            }
            finally
            {
                state.Drawing = false;
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
