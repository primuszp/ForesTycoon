using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using OpenTK.Graphics.OpenGL;

namespace ForesTycoon
{
    internal static class FramebufferCapture
    {
        internal static void SavePng(string path, int width, int height)
        {
            var pixels = new byte[checked(width * height * 4)];
            GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            // The review image is opaque, even where scene effects blended into framebuffer alpha.
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            using var output = File.Create(path);
            output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            byte[] header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
            header[8] = 8; header[9] = 6;
            Chunk(output, "IHDR", header);
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
                for (int y = height - 1; y >= 0; y--)
                {
                    zlib.WriteByte(0);
                    zlib.Write(pixels, y * width * 4, width * 4);
                }
            Chunk(output, "IDAT", compressed.ToArray());
            Chunk(output, "IEND", Array.Empty<byte>());
        }
        private static void Chunk(Stream output, string type, byte[] data)
        {
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, data.Length); output.Write(number);
            byte[] tag = Encoding.ASCII.GetBytes(type); output.Write(tag); output.Write(data);
            uint crc = 0xffffffff;
            foreach (byte value in tag) Accumulate(value);
            foreach (byte value in data) Accumulate(value);
            BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xffffffff); output.Write(number);
            void Accumulate(byte value)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
        }
    }
}
