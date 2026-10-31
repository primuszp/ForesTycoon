using System;
using System.IO;
using System.Text;

namespace ForesTycoon.Audio;

internal sealed record PcmWave(short[] Samples, int Channels, int SampleRate)
{
    // Bounds keep optional local content from exhausting memory. This MVP preloads short loops.
    internal const int MaxFileBytes = 32 * 1024 * 1024;
    internal static PcmWave Read(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 12 || stream.Length > MaxFileBytes)
            throw new InvalidDataException("Expected a seekable WAV of at most 32 MiB.");
        using var r = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        string Tag() => Encoding.ASCII.GetString(r.ReadBytes(4));
        if (Tag() != "RIFF") throw new InvalidDataException("Expected RIFF WAV.");
        long end = r.ReadUInt32() + 8L;
        if (Tag() != "WAVE" || end > stream.Length || end < 12) throw new InvalidDataException("Invalid WAV header.");
        int channels = 0, rate = 0, bits = 0, align = 0;
        byte[] data = null;
        bool formatFound = false;
        while (stream.Position + 8 <= end) {
            string tag = Tag(); uint size = r.ReadUInt32();
            long next = stream.Position + size + (size & 1);
            if (next > end) throw new InvalidDataException("Truncated WAV chunk.");
            if (tag == "fmt ") {
                if (formatFound || size < 16) throw new InvalidDataException("Invalid WAV format chunk.");
                formatFound = true;
                int format = r.ReadUInt16(); channels = r.ReadUInt16(); rate = r.ReadInt32();
                r.ReadUInt32(); align = r.ReadUInt16(); bits = r.ReadUInt16();
                if (format == 0xfffe) {
                    if (size < 40 || r.ReadUInt16() < 22) throw new InvalidDataException("Invalid extensible WAV.");
                    int validBits = r.ReadUInt16(); r.ReadUInt32();
                    var subformat = new Guid(r.ReadBytes(16));
                    if (validBits != bits || subformat != new Guid("00000001-0000-0010-8000-00aa00389b71"))
                        throw new InvalidDataException("Only integer PCM WAV is supported.");
                    format = 1;
                }
                if (format != 1 || channels is < 1 or > 2 || bits is not (16 or 24) ||
                    rate is < 8000 or > 192000 || align != channels * (bits / 8))
                    throw new InvalidDataException("Use mono/stereo 16- or 24-bit PCM WAV, 8–192 kHz.");
            }
            else if (tag == "data") {
                if (data != null) throw new InvalidDataException("Multiple data chunks are unsupported.");
                data = r.ReadBytes(checked((int)size));
            }
            stream.Position = next;
        }
        if (!formatFound || data == null || data.Length == 0 || data.Length % align != 0)
            throw new InvalidDataException("Missing or incomplete PCM frames.");
        int bytes = bits / 8;
        var samples = new short[data.Length / bytes];
        for (int i = 0, p = 0; i < samples.Length; i++, p += bytes) {
            // Signed little-endian 24-bit -> 16-bit, retaining the most significant bits.
            samples[i] = bits == 16 ? (short)(data[p] | data[p + 1] << 8)
                : (short)(data[p + 1] | data[p + 2] << 8);
        }
        return new(samples, channels, rate);
    }
}
