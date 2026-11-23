using System;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace ForesTycoon.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct ForestCoverageInstance(Vector4 RootRadius, Vector4 Form, Vector4 Growth,
        Vector4 Crown, Vector4 Wood, Vector4 SeasonTint, Vector4 SeasonBounds)
    {
        internal const int Stride = 7 * 4 * sizeof(float);
        internal const int StandardSides = 6, StandardRings = 2;
        internal const int WhorledSides = 4, WhorledRings = 3;
        internal const int IndicesPerPart = StandardSides * StandardRings * 6;
    }

    internal enum ForestCoveragePart { Crown = 1, Bole, WhorledCrown }

    internal interface IForestCoverageBuffer : IDisposable
    {
        int Count { get; }
        void SetData(ForestCoverageInstance[] instances);
        void Draw(int first, int count, ForestCoveragePart part, float elapsed, float year);
    }
}
