using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon.Rendering
{
    // Seamless, fixed-seed material detail. No downloads or asset licence dependencies.
    internal static class ProceduralSurfaceTextures
    {
        internal const int Size = 256;
        // Six terrain materials, sixteen species foliage materials, five more ground patterns (sand, rock, moss, gravel, snow).
        internal const int Layers = 27;
        /// <summary>The texture layer of a ground pattern, in the order of <c>GroundMaterialDefinition.Patterns</c>.</summary>
        internal static int GroundLayer(int pattern) => pattern switch { 0 => 0, 1 => 1, _ => 20 + pattern };

        private static readonly Lazy<Task<byte[]>> pixels = new(() => Task.Run(Build));

        internal static byte[] Build()
        {
            byte[] data = new byte[Size * Size * Layers * 4];
            for (int layer = 0; layer < Layers; layer++)
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        float fine = Noise(x, y, layer);
                        float coarse = Smooth(x / 8f, y / 8f, layer, 32);
                        float detail = layer >= 22 ? GroundDetail(x, y, layer, coarse, fine) : layer >= 6 ? LeafDetail(x, y, layer - 4) : layer switch
                        {
                            0 => 0.75f + 0.28f * coarse + 0.18f * fine,
                            1 => 0.72f + 0.3f * coarse + 0.18f * fine,
                            2 => 0.65f + 0.25f * coarse + 0.35f * Smooth(x / 4f, y / 4f, layer, 64),
                            3 => 0.65f + 0.45f * Smooth(x / 4f, y / 32f, layer, 64, 8) + 0.1f * fine,
                            5 => 0.84f + 0.035f * coarse + 0.075f * fine,
                            _ => 0.90f + 0.08f * coarse + 0.02f * fine
                        };
                        int at = ((layer * Size + y) * Size + x) * 4;
                        byte value = (byte)Math.Clamp((int)(detail * 220), 0, 255);
                        data[at] = data[at + 1] = data[at + 2] = value;
                        data[at + 3] = 255;
                    }
            return data;
        }

        /// <summary>The extra ground patterns; every term wraps at the texture edge.</summary>
        private static float GroundDetail(int x, int y, int layer, float coarse, float fine)
        {
            switch (layer)
            {
                case 22: // sand: wind ripples bent by a slow noise
                {
                    float bend = Smooth(x / 32f, y / 32f, layer, 8) * 6.28f;
                    float ripple = 0.5f + 0.5f * MathF.Sin(MathF.Tau * (x + y * 0.35f) / 16f + bend);
                    return 0.82f + 0.10f * ripple + 0.06f * fine;
                }
                case 23: // rock: broad plates, dark cracks where a noise crosses its middle
                {
                    float plates = Smooth(x / 16f, y / 16f, layer, 16);
                    float crack = MathF.Abs(Smooth(x / 8f, y / 8f, layer + 7, 32) - 0.5f);
                    return 0.66f + 0.30f * plates + 0.06f * fine - (crack < 0.035f ? 0.22f * (1 - crack / 0.035f) : 0);
                }
                case 24: // moss: soft, dense clumps
                    return 0.70f + 0.24f * Smooth(x / 4f, y / 4f, layer, 64) + 0.12f * fine;
                case 25: // gravel: pebbles in a jittered 16-px grid, each with its own tone
                {
                    float best = 1e9f, tone = 0.5f;
                    int cx = x / 16, cy = y / 16;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int sx = ((cx + dx) % 16 + 16) % 16, sy = ((cy + dy) % 16 + 16) % 16;
                            float px = (cx + dx) * 16 + 4 + 8 * Noise(sx, sy, layer), py = (cy + dy) * 16 + 4 + 8 * Noise(sx, sy, layer + 1);
                            float d = (x - px) * (x - px) + (y - py) * (y - py);
                            if (d < best) { best = d; tone = Noise(sx, sy, layer + 2); }
                        }
                    float pebble = Math.Clamp(1.4f - MathF.Sqrt(best) / 6f, 0, 1);
                    return 0.62f + pebble * (0.22f + 0.18f * tone) + 0.05f * fine;
                }
                default: // snow: smooth, faintly drifted, rare sparkles
                    return 0.95f + 0.04f * coarse + (fine > 0.985f ? 0.08f : 0);
            }
        }

        private static float LeafDetail(int x, int y, int species)
        {
            // Periodic 8x8 clusters. All coordinates wrap, including the neighbouring cells,
            // so repeat sampling and mipmaps have no seam at the texture boundary.
            const int cells = 8;
            float px = x / 32f, py = y / 32f;
            int cx = (int)px, cy = (int)py;
            bool needle = species is 2 or 8 or 9 or 10 or 17;
            float leaves = 0, vein = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int sx = (cx + dx + cells) % cells, sy = (cy + dy + cells) % cells;
                    float seed = Noise(sx, sy, species);
                    float angle = seed * MathF.Tau;
                    float u = px - (cx + dx + 0.5f + (Noise(sx, sy, species + 31) - .5f) * .45f);
                    float v = py - (cy + dy + 0.5f + (Noise(sx, sy, species + 57) - .5f) * .45f);
                    float a = u * MathF.Cos(angle) + v * MathF.Sin(angle);
                    float b = -u * MathF.Sin(angle) + v * MathF.Cos(angle);
                    float width = needle ? species == 8 ? .12f : .18f : species is 3 or 14 or 15 ? .30f : .40f;
                    float length = needle ? .72f : species is 7 or 16 ? .62f : .48f;
                    float lobes = species is 4 or 11 or 12 or 6
                        ? 1 + .15f * MathF.Cos(a / length * 12 + species) : 1;
                    float distance = a * a / (length * length) + b * b / (width * width * lobes * lobes);
                    float leaf = Math.Clamp((1.08f - distance) * 6, 0, 1);
                    leaves = Math.Max(leaves, leaf * (.72f + seed * .28f));
                    float rib = MathF.Exp(-MathF.Abs(b) * (needle ? 38 : 55));
                    vein = Math.Max(vein, leaf * rib);
                }
            float clusters = Smooth(x / 32f, y / 32f, species + 101, cells);
            return .63f + .22f * leaves + .075f * vein + .08f * clusters;
        }

        private static float Noise(int x, int y, int seed)
        {
            uint h = unchecked((uint)(x * 374761393 + y * 668265263 + seed * 144269));
            h = unchecked((h ^ (h >> 13)) * 1274126177);
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }

        private static float Smooth(float x, float y, int seed, int period, int periodY = 0)
        {
            if (periodY == 0) periodY = period;
            int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
            float tx = x - ix, ty = y - iy;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float a = Noise(ix % period, iy % periodY, seed), b = Noise((ix + 1) % period, iy % periodY, seed);
            float c = Noise(ix % period, (iy + 1) % periodY, seed), d = Noise((ix + 1) % period, (iy + 1) % periodY, seed);
            return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
        }

        internal static IEnumerable<int> UploadWork(WorkResult<int> result)
        {
            var generating = pixels.Value;
            while (!generating.IsCompleted) yield return 64;
            byte[] data = generating.GetAwaiter().GetResult();
            int texture = GL.GenTexture(); result.Value = texture;
            yield return 64;
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2DArray, texture);
            GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.Rgba8, Size, Size, Layers, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            yield return 64;
            var layerPixels = new byte[Size * Size * 4];
            for (int layer = 0; layer < Layers; layer++)
            {
                System.Buffer.BlockCopy(data, layer * layerPixels.Length, layerPixels, 0, layerPixels.Length);
                GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2DArray, texture);
                GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, layer, Size, Size, 1, PixelFormat.Rgba, PixelType.UnsignedByte, layerPixels);
                yield return 64;
            }
            GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2DArray, texture);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2DArray);
            yield return 64;
        }
    }
}
