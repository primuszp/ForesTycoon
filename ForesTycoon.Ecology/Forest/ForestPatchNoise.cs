namespace ForesTycoon.Ecology
{
    /// <summary>
    /// A smooth, seeded noise over the tile grid for laying out the initial forest in woods and clearings: about eight-tile
    /// patches with ragged three-tile edges, 0–1. The natural and the patterned generators both read it as noise.patch.
    /// </summary>
    internal static class ForestPatchNoise
    {
        internal static float At(int seed, int column, int row) =>
            .75f * Value(seed, column / 8f, row / 8f) + .25f * Value(seed + 7919, column / 3f, row / 3f);

        private static float Value(int seed, float x, float y)
        {
            int ix = (int)System.MathF.Floor(x), iy = (int)System.MathF.Floor(y);
            float tx = x - ix, ty = y - iy;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float a = Corner(seed, ix, iy), b = Corner(seed, ix + 1, iy), c = Corner(seed, ix, iy + 1), d = Corner(seed, ix + 1, iy + 1);
            return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
        }

        private static float Corner(int seed, int x, int y)
        {
            uint h = unchecked((uint)seed * 0x9e3779b9u ^ (uint)x * 0x85ebca6bu ^ (uint)y * 0xc2b2ae35u ^ 0x77616c64u);
            h ^= h >> 16; h = unchecked(h * 0x7feb352du); h ^= h >> 15; h = unchecked(h * 0x846ca68bu); h ^= h >> 16;
            return h / (float)uint.MaxValue;
        }
    }
}
