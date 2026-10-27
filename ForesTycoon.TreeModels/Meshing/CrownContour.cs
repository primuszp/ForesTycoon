using System;
using System.Linq;
using OpenTK.Mathematics;

namespace ForesTycoon.TreeModels
{
    /// <summary>Preserves the sampled horizontal footprint when filling a sparse, flat canopy's shoulders.</summary>
    internal static class CrownContour
    {
        internal static Vector2[] Hull(Vector3[] samples)
        {
            var points = samples.Select(p => p.Xy).Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
            if (points.Length < 3) return points;
            var hull = new Vector2[2 * points.Length]; int count = 0;
            foreach (var p in points)
            {
                while (count >= 2 && Cross(hull[count - 1] - hull[count - 2], p - hull[count - 1]) <= 0) count--;
                hull[count++] = p;
            }
            int lower = count;
            for (int i = points.Length - 2; i >= 0; i--)
            {
                var p = points[i];
                while (count > lower && Cross(hull[count - 1] - hull[count - 2], p - hull[count - 1]) <= 0) count--;
                hull[count++] = p;
            }
            return hull.AsSpan(0, count - 1).ToArray();
        }

        internal static float Limit(Vector2[] hull, Vector2 point, float requested)
        {
            float limit = requested;
            for (int i = 0; i < hull.Length; i++)
            {
                var a = hull[i]; var edge = hull[(i + 1) % hull.Length] - a;
                float slope = Cross(edge, point);
                if (slope < -1e-9f) limit = Math.Min(limit, Cross(edge, a) / slope);
            }
            return Math.Max(1, limit);
        }

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    }
}
