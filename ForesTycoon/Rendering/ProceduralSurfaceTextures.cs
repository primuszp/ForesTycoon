using System;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    // Seamless, fixed-seed material detail. No downloads or asset licence dependencies.
    internal static class ProceduralSurfaceTextures
    {
        internal const int Size = 256;
        internal const int Layers = 5; // grass, soil, gravel, bark, snow

        internal static byte[] Build()
        {
            byte[] data = new byte[Size * Size * Layers * 4];
            for (int layer = 0; layer < Layers; layer++)
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float fine = Noise(x, y, layer);
                float coarse = Smooth(x / 8f, y / 8f, layer, 32);
                float detail = layer switch
                {
                    0 => 0.75f + 0.28f * coarse + 0.18f * fine,
                    1 => 0.72f + 0.3f * coarse + 0.18f * fine,
                    2 => 0.65f + 0.25f * coarse + 0.35f * Smooth(x / 4f, y / 4f, layer, 64),
                    3 => 0.65f + 0.45f * Smooth(x / 4f, y / 32f, layer, 64, 8) + 0.1f * fine,
                    _ => 0.90f + 0.08f * coarse + 0.02f * fine
                };
                int at = ((layer * Size + y) * Size + x) * 4;
                byte value = (byte)Math.Clamp((int)(detail * 220), 0, 255);
                data[at] = data[at + 1] = data[at + 2] = value;
                data[at + 3] = 255;
            }
            return data;
        }

        private static float Noise(int x, int y, int seed)
        {
            uint h = unchecked((uint)(x * 374761393 + y * 668265263 + seed * 144269));
            h = unchecked((h ^ (h >> 13)) * 1274126177);
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }

        private static float Smooth(float x, float y, int seed, int period, int periodY = 0)
        {
            if(periodY == 0) periodY = period;
            int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
            float tx = x - ix, ty = y - iy;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float a = Noise(ix % period, iy % periodY, seed), b = Noise((ix + 1) % period, iy % periodY, seed);
            float c = Noise(ix % period, (iy + 1) % periodY, seed), d = Noise((ix + 1) % period, (iy + 1) % periodY, seed);
            return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
        }

        internal static int Upload()
        {
            int texture = GL.GenTexture();
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2DArray, texture);
            GL.TexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.Rgba8, Size, Size, Layers, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, Build());
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2DArray);
            return texture;
        }
    }
}
