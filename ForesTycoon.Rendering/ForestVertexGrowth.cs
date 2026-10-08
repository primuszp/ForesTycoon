using System;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace ForesTycoon.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct ForestVertexGrowth(Vector3 Origin, Vector3 AnnualScale, float TreeIndex = -1)
    {
        internal const int Stride = 7 * sizeof(float);
        // Dead wood reuses the metadata layout: origin=root, rate=(-2,death year,
        // ground clearance), tree-index=yaw. It never samples living-tree texture slots.
        internal static ForestVertexGrowth DeadWood(Vector3 root, double deathYear, float clearance, float yaw)
            => new(root, new Vector3(-2, (float)deathYear, clearance), yaw);
        internal Vector3 At(Vector3 point, float elapsed) => Origin + (point - Origin) * (Vector3.One + AnnualScale * Math.Max(0, elapsed));

    }
}
