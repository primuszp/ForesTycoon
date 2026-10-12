using System;
using System.Drawing;

namespace ForesTycoon
{
    partial class Terrain
    {
        /// <summary>Surface water shallower than this (mm) is a wet film, not drawn as water.</summary>
        internal const double MinimumFloodDepth = 3;
        /// <summary>Height of the flood sheet above the ground it covers (m), so it does not flicker into the terrain.</summary>
        private const float FloodLift = 0.03f;
        internal int FloodTilesDrawn { get; private set; }

        /// <summary>
        /// Water surface heights at the four corners of a flooded tile. The level is the tile's mean ground height plus the
        /// water depth (that is the level the flow model works with). In a hollow the sheet lies flat at that level; on a
        /// slope a thin film is draped over the ground instead of floating above the lower corners: a corner rises at most
        /// twice the depth above its own ground.
        /// </summary>
        internal static void FloodCorners(ReadOnlySpan<float> ground, double depthMillimetres, Span<float> surface)
        {
            if (ground.Length != surface.Length) throw new ArgumentException("Corner count mismatch.");
            float mean = 0;
            foreach (float z in ground) mean += z;
            mean /= ground.Length;
            float depth = (float)(Math.Max(0, depthMillimetres) / 1000), level = mean + depth;
            for (int i = 0; i < ground.Length; i++)
                surface[i] = ground[i] + FloodLift + Math.Clamp(level - ground[i], 0, 2 * depth);
        }

        /// <summary>Colour of flood water: a light, slightly muddy blue when shallow, deep blue when deep.</summary>
        internal static Color FloodColor(double depthMillimetres)
        {
            double share = Math.Sqrt(Math.Clamp((depthMillimetres - MinimumFloodDepth) / 300, 0, 1));
            int Mix(int shallow, int deep) => (int)Math.Round(shallow + (deep - shallow) * share);
            return Color.FromArgb(Mix(150, 220), Mix(75, 30), Mix(135, 95), Mix(185, 170));
        }

        /// <summary>Draws the moving surface water of the environment model (rain, springs, overflowing rivers) where it
        /// stands deeper than <see cref="MinimumFloodDepth"/>. Lakes and rivers the map already draws are skipped.</summary>
        internal void DrawFloodWater(RenderContext context, Func<int, double> depthMillimetres)
        {
            FloodTilesDrawn = 0;
            if (depthMillimetres == null) return;
            float t = (float)(context.TotalTimeSeconds % 628.318);
            using (RenderDevice.CreateStateScope().AlphaBlend())
            {
                DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
                {
                    Span<float> g = stackalloc float[4], s = stackalloc float[4];
                    foreach (Tile tile in visibleTiles)
                    {
                        if (HasDynamicWater(tile) || CanRenderFallbackRiver(tile)) continue;
                        double depth = depthMillimetres(tile.Id);
                        if (!(depth >= MinimumFloodDepth)) continue;
                        g[0] = tile.W.zPos; g[1] = tile.S.zPos; g[2] = tile.E.zPos; g[3] = tile.N.zPos;
                        FloodCorners(g, depth, s);
                        Color color = FloodColor(depth);
                        // A slight shimmer keeps the sheet readable as water without lifting it off thin films.
                        int shimmer = (int)(8 * MathF.Sin(t * 1.3f + tile.Id * 0.7f));
                        DynamicPrimitiveBatch.Color4(Color.FromArgb(color.A, Math.Clamp(color.R + shimmer, 0, 255),
                            Math.Clamp(color.G + shimmer, 0, 255), Math.Clamp(color.B + shimmer / 2, 0, 255)));
                        DynamicPrimitiveBatch.Vertex3(tile.W.xPos, tile.W.yPos, s[0]);
                        DynamicPrimitiveBatch.Vertex3(tile.S.xPos, tile.S.yPos, s[1]);
                        DynamicPrimitiveBatch.Vertex3(tile.E.xPos, tile.E.yPos, s[2]);
                        DynamicPrimitiveBatch.Vertex3(tile.N.xPos, tile.N.yPos, s[3]);
                        FloodTilesDrawn++;
                    }
                });
            }
        }
    }
}
