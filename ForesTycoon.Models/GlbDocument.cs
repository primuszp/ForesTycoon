using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ForesTycoon.Models
{
    /// <summary>Validated GLB container and accessor layout shared by all model importers.</summary>
    internal sealed partial class GlbDocument : IDisposable
    {
        private readonly JsonDocument json;
        private readonly Accessor[] accessors;
        internal byte[] Bytes { get; }
        internal int BinaryOffset { get; }
        internal JsonElement Root => json.RootElement;
        internal readonly record struct Accessor(int Offset, int Count, int Stride, int Type, int Arity, bool Normalized);

        internal static GlbDocument Load(string path)
        {
            if (new FileInfo(path).Length > MaxAssetBytes) throw new NotSupportedException("GLB exceeds the 128 MiB asset budget.");
            try { return new GlbDocument(File.ReadAllBytes(path)); }
            catch (Exception error) when (error is JsonException or EndOfStreamException or KeyNotFoundException or InvalidOperationException
                or ArgumentException or IndexOutOfRangeException or OverflowException)
            { throw new InvalidDataException("Malformed GLB structure or values.", error); }
        }

        private GlbDocument(byte[] bytes)
        {
            Bytes = bytes;
            if (bytes.Length < 28 || BitConverter.ToUInt32(bytes, 0) != 0x46546c67 || BitConverter.ToUInt32(bytes, 4) != 2
                || BitConverter.ToUInt32(bytes, 8) != bytes.Length) throw new InvalidDataException("Invalid GLB.");
            int jsonSize = checked((int)BitConverter.ToUInt32(bytes, 12));
            if (jsonSize > bytes.Length - 28 || jsonSize % 4 != 0 || BitConverter.ToUInt32(bytes, 16) != 0x4e4f534a)
                throw new InvalidDataException("Invalid JSON chunk.");
            BinaryOffset = 28 + jsonSize;
            int binarySize = checked((int)BitConverter.ToUInt32(bytes, 20 + jsonSize));
            if (binarySize != bytes.Length - BinaryOffset || binarySize % 4 != 0 || BitConverter.ToUInt32(bytes, 24 + jsonSize) != 0x004e4942)
                throw new InvalidDataException("Invalid BIN chunk.");
            json = JsonDocument.Parse(bytes.AsMemory(20, jsonSize));
            try
            {
                ValidateDocument(Root, binarySize);
                var definitions = Root.GetProperty("accessors");
                var views = Root.GetProperty("bufferViews");
                accessors = new Accessor[definitions.GetArrayLength()];
                for (int i = 0; i < accessors.Length; i++)
                {
                    var definition = definitions[i];
                    var view = views[definition.GetProperty("bufferView").GetInt32()];
                    int type = definition.GetProperty("componentType").GetInt32();
                    int arity = AccessorArity(definition.GetProperty("type").GetString());
                    int offset = (view.TryGetProperty("byteOffset", out var value) ? value.GetInt32() : 0)
                        + (definition.TryGetProperty("byteOffset", out value) ? value.GetInt32() : 0);
                    int stride = view.TryGetProperty("byteStride", out value) ? value.GetInt32() : arity * ComponentSize(type);
                    accessors[i] = new(BinaryOffset + offset, definition.GetProperty("count").GetInt32(), stride, type, arity,
                        definition.TryGetProperty("normalized", out value) && value.GetBoolean());
                }
            }
            catch { json.Dispose(); throw; }
        }

        internal Accessor AccessorAt(int id, int minimumArity = 0)
        {
            var accessor = accessors[id];
            if (accessor.Arity < minimumArity) throw new InvalidDataException("Invalid accessor arity.");
            return accessor;
        }

        private static int ComponentSize(int type) => type switch
        {
            5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4,
            _ => throw new NotSupportedException("Component type.")
        };
        private static int AccessorArity(string type) => type switch
        {
            "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16,
            _ => throw new NotSupportedException("Accessor type.")
        };
        public void Dispose() => json.Dispose();
    }
}
