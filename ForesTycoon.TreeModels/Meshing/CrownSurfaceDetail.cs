using System;
using OpenTK.Mathematics;

namespace ForesTycoon.TreeModels
{
    /// <summary>A continuous tree-local detail field, independent of vertex order and mesh resolution.</summary>
    internal static class CrownSurfaceDetail
    {
        internal static float Factor(Vector3 direction, int seed, float amount)
        {
            // Two wavelengths: groups of shoots and smaller foliage pads. Both are evaluated in
            // direction space, so detail turns with the tree and persists between LODs.
            float detail = .65f * Noise(direction * 3.2f, seed)
                + .35f * Noise(direction * 7.1f + new Vector3(17.3f), seed);
            return 1 + .6f * amount * detail;
        }

        private static float Noise(Vector3 p, int seed)
        {
            int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y), z = (int)MathF.Floor(p.Z);
            var t = p - new Vector3(x, y, z);
            // Cubic interpolation makes adjacent cells share both value and first derivative.
            t = t * t * (new Vector3(3) - 2 * t);
            float Lerp(float a, float b, float w) => a + (b - a) * w;
            return Lerp(Lerp(Lerp(Hash(x, y, z, seed), Hash(x + 1, y, z, seed), t.X),
                    Lerp(Hash(x, y + 1, z, seed), Hash(x + 1, y + 1, z, seed), t.X), t.Y),
                Lerp(Lerp(Hash(x, y, z + 1, seed), Hash(x + 1, y, z + 1, seed), t.X),
                    Lerp(Hash(x, y + 1, z + 1, seed), Hash(x + 1, y + 1, z + 1, seed), t.X), t.Y), t.Z);
        }

        private static float Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)seed ^ (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)z * 0xcb1ab31fu;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return (h & 0xffffff) * (2f / 0xffffff) - 1;
            }
        }
    }
}
