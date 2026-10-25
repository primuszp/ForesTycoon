using System;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>The baked light of terrain faces: one fixed sun from above-front, never darker than 55%.</summary>
    internal static class TileShading
    {
        private static readonly Vector3 Light = Vector3.Normalize(new Vector3(0.4f, 0.6f, 1.5f));

        /// <summary>The colour of a face through a, b, c; either winding, and a degenerate face counts as flat.</summary>
        internal static Color Shade(Color color, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 cross = Vector3.Cross(b - a, c - a);
            Vector3 normal = cross.LengthSquared > 0.0001f ? Vector3.Normalize(cross) : Vector3.UnitZ;
            float shade = Math.Clamp(Math.Abs(Vector3.Dot(normal, Light)), 0.55f, 1.0f);
            return Color.FromArgb(color.A, (int)(color.R * shade), (int)(color.G * shade), (int)(color.B * shade));
        }
    }
}
