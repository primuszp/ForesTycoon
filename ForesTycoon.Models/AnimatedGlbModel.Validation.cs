using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ForesTycoon.Models
{
    internal sealed partial class AnimatedGlbModel
    {
        internal const long MaxAssetBytes = 128L * 1024 * 1024;
        internal const long MaxDecodedAssetBytes = 256L * 1024 * 1024;

        internal static void ValidateDocument(JsonElement root, int binarySize)
        {
            void Require(bool valid, string field) { if (!valid) throw new InvalidDataException("Invalid GLB " + field + "."); }
            JsonElement At(JsonElement array, int index, string field)
            { Require(index >= 0 && index < array.GetArrayLength(), field); return array[index]; }
            int OptionalInt(JsonElement item, string name, int fallback = 0) => item.TryGetProperty(name, out var value) ? value.GetInt32() : fallback;
            void FiniteArray(JsonElement value, int length, string field)
            {
                Require(value.GetArrayLength() == length, field);
                foreach (var number in value.EnumerateArray()) Require(float.IsFinite(number.GetSingle()), field);
            }
            Require(root.GetProperty("asset").GetProperty("version").GetString() == "2.0", "asset version");
            var buffers = root.GetProperty("buffers");
            if (buffers.GetArrayLength() != 1 || buffers[0].TryGetProperty("uri", out _))
                throw new NotSupportedException("One embedded GLB buffer is required.");
            int declared = buffers[0].GetProperty("byteLength").GetInt32();
            Require(declared > 0 && declared <= binarySize && binarySize - declared <= 3, "buffer length");
            var views = root.GetProperty("bufferViews");
            foreach (var view in views.EnumerateArray())
            {
                int offset = OptionalInt(view, "byteOffset"), length = view.GetProperty("byteLength").GetInt32();
                Require(view.GetProperty("buffer").GetInt32() == 0 && offset >= 0 && length > 0 && (long)offset + length <= declared, "buffer view");
                if (view.TryGetProperty("byteStride", out var stride)) Require(stride.GetInt32() is >= 4 and <= 252 && stride.GetInt32() % 4 == 0, "byte stride");
            }
            var accessors = root.GetProperty("accessors");
            void Accessor(int id, string shape, int count, string field, params int[] components)
            {
                var accessor = At(accessors, id, field);
                Require(accessor.GetProperty("type").GetString() == shape && accessor.GetProperty("count").GetInt32() == count, field + " shape/count");
                Require(Array.IndexOf(components, accessor.GetProperty("componentType").GetInt32()) >= 0, field + " component type");
            }
            void NormalizedInteger(int id, string field)
            {
                var accessor = accessors[id];
                if (accessor.GetProperty("componentType").GetInt32() != 5126)
                    Require(accessor.TryGetProperty("normalized", out var normalized) && normalized.GetBoolean(), field);
            }
            foreach (var accessor in accessors.EnumerateArray())
            {
                if (accessor.TryGetProperty("sparse", out _)) throw new NotSupportedException("Sparse accessor.");
                var view = At(views, accessor.GetProperty("bufferView").GetInt32(), "accessor view");
                int type = accessor.GetProperty("componentType").GetInt32();
                int size = type switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => throw new NotSupportedException("Component type.") };
                int arity = accessor.GetProperty("type").GetString() switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => throw new NotSupportedException("Accessor type.") };
                int count = accessor.GetProperty("count").GetInt32(), offset = OptionalInt(accessor, "byteOffset"), stride = OptionalInt(view, "byteStride", arity * size);
                Require(count > 0 && offset >= 0 && offset % size == 0 && (OptionalInt(view, "byteOffset") + (long)offset) % size == 0
                    && stride >= arity * size && stride % size == 0 && (long)offset + (long)(count - 1) * stride + arity * size <= view.GetProperty("byteLength").GetInt32(), "accessor bounds/alignment");
                if (accessor.TryGetProperty("normalized", out var normalized) && normalized.GetBoolean()) Require(type is 5120 or 5121 or 5122 or 5123, "normalized component type");
            }
            var nodes = root.GetProperty("nodes");
            Require(nodes.GetArrayLength() is > 0 and <= 16384, "node count");
            foreach (var node in nodes.EnumerateArray())
            {
                bool matrix = node.TryGetProperty("matrix", out var value);
                if (matrix) { FiniteArray(value, 16, "node matrix"); Require(!node.TryGetProperty("translation", out _) && !node.TryGetProperty("rotation", out _) && !node.TryGetProperty("scale", out _), "matrix/TRS exclusion"); }
                if (node.TryGetProperty("translation", out value)) FiniteArray(value, 3, "translation");
                if (node.TryGetProperty("scale", out value)) FiniteArray(value, 3, "scale");
                if (node.TryGetProperty("rotation", out value))
                {
                    FiniteArray(value, 4, "rotation"); double norm = 0;
                    foreach (var component in value.EnumerateArray()) norm += (double)component.GetSingle() * component.GetSingle();
                    Require(Math.Abs(norm - 1) <= .001, "rotation norm");
                }
            }
            int skinCount = root.TryGetProperty("skins", out var skins) ? skins.GetArrayLength() : 0;
            if (skinCount > 0) foreach (var skin in skins.EnumerateArray())
            {
                var joints = skin.GetProperty("joints");
                Require(joints.GetArrayLength() > 0, "skin joints");
                if (joints.GetArrayLength() > 64) throw new NotSupportedException("Maximum 64 joints per skin.");
                var unique = new HashSet<int>();
                foreach (var joint in joints.EnumerateArray()) { At(nodes, joint.GetInt32(), "joint node"); Require(unique.Add(joint.GetInt32()), "duplicate joint"); }
                if (skin.TryGetProperty("inverseBindMatrices", out var bind)) Accessor(bind.GetInt32(), "MAT4", joints.GetArrayLength(), "inverse bind matrices", 5126);
            }
            foreach (var node in nodes.EnumerateArray()) if (node.TryGetProperty("mesh", out var meshId))
            {
                int skin = OptionalInt(node, "skin", -1);
                Require(!node.TryGetProperty("skin", out _) || skin >= 0 && skin < skinCount, "mesh skin");
                var mesh = At(root.GetProperty("meshes"), meshId.GetInt32(), "mesh index");
                foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
                {
                    if (primitive.TryGetProperty("targets", out _)) throw new NotSupportedException("Morph targets are not supported.");
                    var attributes = primitive.GetProperty("attributes");
                    int position = attributes.GetProperty("POSITION").GetInt32();
                    int count = At(accessors, position, "position accessor").GetProperty("count").GetInt32();
                    Accessor(position, "VEC3", count, "positions", 5126);
                    Accessor(attributes.GetProperty("NORMAL").GetInt32(), "VEC3", count, "normals", 5126);
                    if (attributes.TryGetProperty("TEXCOORD_0", out var uv)) { Accessor(uv.GetInt32(), "VEC2", count, "texture coordinates", 5126, 5121, 5123); NormalizedInteger(uv.GetInt32(), "texture coordinate normalization"); }
                    if (attributes.TryGetProperty("JOINTS_1", out _) || attributes.TryGetProperty("WEIGHTS_1", out _)) throw new NotSupportedException("Maximum four skin influences per vertex.");
                    if (skin >= 0)
                    {
                        int joints = attributes.GetProperty("JOINTS_0").GetInt32(), weights = attributes.GetProperty("WEIGHTS_0").GetInt32();
                        Accessor(joints, "VEC4", count, "joint indices", 5121, 5123);
                        Require(!accessors[joints].TryGetProperty("normalized", out var normalized) || !normalized.GetBoolean(), "normalized joints");
                        Accessor(weights, "VEC4", count, "skin weights", 5126, 5121, 5123);
                        NormalizedInteger(weights, "skin weight normalization");
                    }
                    int indices = primitive.GetProperty("indices").GetInt32(); var index = At(accessors, indices, "index accessor");
                    Accessor(indices, "SCALAR", index.GetProperty("count").GetInt32(), "indices", 5121, 5123, 5125);
                    Require(!index.TryGetProperty("normalized", out var indexNormalized) || !indexNormalized.GetBoolean(), "normalized indices");
                }
            }
        }
    }
}
