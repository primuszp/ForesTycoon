using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon.Rendering.OpenGl
{
    internal sealed class OpenGlForestStateBuffer : IForestStateBuffer
    {
        private int buffer;
        internal int Texture { get; private set; }
        private bool disposed;
        public void SetData(Vector4[] data, int count)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (count < 0 || count > data.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (buffer == 0) buffer = GL.GenBuffer();
            if (Texture == 0) Texture = GL.GenTexture();
            GL.BindBuffer(BufferTarget.TextureBuffer, buffer);
            GL.BufferData(BufferTarget.TextureBuffer, count * 4 * sizeof(float), data, BufferUsageHint.DynamicDraw);
            GL.ActiveTexture(TextureUnit.Texture7);
            GL.BindTexture(TextureTarget.TextureBuffer, Texture);
            GL.TexBuffer(TextureBufferTarget.TextureBuffer, SizedInternalFormat.Rgba32f, buffer);
            GL.ActiveTexture(TextureUnit.Texture0);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (Texture != 0) GL.DeleteTexture(Texture);
            if (buffer != 0) GL.DeleteBuffer(buffer);
            Texture = buffer = 0;
        }
    }
}
