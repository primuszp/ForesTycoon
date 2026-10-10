using System.Text;
using System.Text.Json.Nodes;

namespace ForesTycoon.Tests;

public class GlbValidationTests
{
    public static IEnumerable<object[]> InstalledAssets() => Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Assets"), "*.glb", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal).Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(InstalledAssets))]
    public void EveryInstalledAssetProducesFinitePoseAndConsistentVertexLayout(string path)
    {
        var model = AnimatedGlbModel.Load(path);
        Assert.NotEmpty(model.Meshes);
        var pose = model.CreatePose(); pose.Evaluate(null, 0);
        Assert.All(pose.World, matrix => { for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) Assert.True(float.IsFinite(matrix[r, c])); });
        Assert.All(model.Meshes, mesh => { Assert.Equal(0, mesh.Vertices.Length % 16); Assert.All(mesh.Indices, index => Assert.True(index < mesh.Vertices.Length / 16)); });
    }

    private static JsonNode Document() => JsonNode.Parse("""
        {"asset":{"version":"2.0"},"buffers":[{"byteLength":96}],"nodes":[{"mesh":0}],
         "meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1},"indices":2}]}],
         "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":36},
                        {"buffer":0,"byteOffset":72,"byteLength":12},{"buffer":0,"byteOffset":84,"byteLength":12}],
         "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
                      {"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},
                      {"bufferView":2,"componentType":5125,"count":3,"type":"SCALAR"},
                      {"bufferView":3,"componentType":5126,"count":3,"type":"SCALAR"}]}
        """)!;

    private static AnimatedGlbModel Load(JsonNode document, Action<byte[]>? mutateBinary = null, Action<byte[]>? mutateFile = null)
    {
        using var binary = new MemoryStream();
        using (var writer = new BinaryWriter(binary, Encoding.UTF8, true)) {
            foreach (float value in new[] { -1f,-1,0, 1,-1,0, 0,1,0, 0,0,1, 0,0,1, 0,0,1 }) writer.Write(value);
            foreach (uint index in new uint[] { 0, 1, 2 }) writer.Write(index);
            foreach (float time in new[] { 0f, 1, 2 }) writer.Write(time);
        }
        byte[] payload = binary.ToArray(); mutateBinary?.Invoke(payload);
        byte[] json = Encoding.UTF8.GetBytes(document.ToJsonString());
        int padded = (json.Length + 3) & ~3;
        using var file = new MemoryStream();
        using (var writer = new BinaryWriter(file, Encoding.UTF8, true)) {
            writer.Write(0x46546c67u); writer.Write(2u); writer.Write(28 + padded + payload.Length);
            writer.Write(padded); writer.Write(0x4e4f534au); writer.Write(json);
            for (int i = json.Length; i < padded; i++) writer.Write((byte)32);
            writer.Write(payload.Length); writer.Write(0x004e4942u); writer.Write(payload);
        }
        byte[] bytes = file.ToArray(); mutateFile?.Invoke(bytes);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".glb");
        try { File.WriteAllBytes(path, bytes); return AnimatedGlbModel.Load(path); }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("normal-count")] [InlineData("position-shape")] [InlineData("foreign-buffer")]
    [InlineData("negative-view")] [InlineData("short-stride")] [InlineData("unaligned-accessor")]
    [InlineData("zero-count")] [InlineData("overflow-count")] [InlineData("float-indices")]
    [InlineData("normalized-indices")] [InlineData("zero-rotation")] [InlineData("matrix-and-trs")]
    [InlineData("self-cycle")] [InlineData("negative-skin")] [InlineData("short-bind-matrices")]
    [InlineData("material-range")] [InlineData("animation-input-shape")] [InlineData("duplicate-channel")]
    public void RejectsMalformedAssetBeforeItCanReachRendering(string defect)
    {
        var root = Document(); var accessors = root["accessors"]!;
        var node = root["nodes"]![0]!; var view = root["bufferViews"]![0]!;
        switch (defect) {
            case "normal-count": accessors[1]!["count"] = 2; break;
            case "position-shape": accessors[0]!["type"] = "VEC2"; break;
            case "foreign-buffer": view["buffer"] = 1; break;
            case "negative-view": view["byteOffset"] = -4; break;
            case "short-stride": view["byteStride"] = 4; break;
            case "unaligned-accessor": accessors[0]!["byteOffset"] = 1; break;
            case "zero-count": accessors[0]!["count"] = 0; break;
            case "overflow-count": accessors[0]!["count"] = int.MaxValue; break;
            case "float-indices": accessors[2]!["componentType"] = 5126; break;
            case "normalized-indices": accessors[2]!["normalized"] = true; break;
            case "zero-rotation": node["rotation"] = JsonNode.Parse("[0,0,0,0]"); break;
            case "matrix-and-trs": node["matrix"] = JsonNode.Parse("[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]"); node["translation"] = JsonNode.Parse("[0,0,0]"); break;
            case "self-cycle": node["children"] = JsonNode.Parse("[0]"); break;
            case "negative-skin": node["skin"] = -1; break;
            case "short-bind-matrices": root["skins"] = JsonNode.Parse("[{\"joints\":[0],\"inverseBindMatrices\":0}]"); break;
            case "material-range": root["materials"] = JsonNode.Parse("[{\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,1,1,2]}}]"); root["meshes"]![0]!["primitives"]![0]!["material"] = 0; break;
            case "animation-input-shape": root["animations"] = Animation(); accessors[3]!["type"] = "VEC3"; break;
            case "duplicate-channel": root["animations"] = Animation(); root["animations"]![0]!["channels"]!.AsArray().Add(root["animations"]![0]!["channels"]![0]!.DeepClone()); break;
        }
        Assert.Throws<InvalidDataException>(() => Load(root));
    }

    private static JsonNode Animation() => JsonNode.Parse("""
        [{"name":"move","samplers":[{"input":3,"output":0}],"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]
        """)!;

    [Fact]
    public void ValidFixtureProducesFiniteDeterministicPose()
    {
        var root = Document(); root["animations"] = Animation();
        var model = Load(root); var pose = model.CreatePose(); pose.Evaluate("move", .5);
        Assert.Equal(3, Assert.Single(model.Meshes).Indices.Length);
        Assert.Equal(-1, pose.World[0].M42);
        Assert.True(float.IsFinite(pose.World[0].M41));
    }

    [Fact]
    public void NonFiniteBinaryAndMalformedChunkHaveConsistentDataErrors()
    {
        Assert.Throws<InvalidDataException>(() => Load(Document(), bytes => BitConverter.GetBytes(float.NaN).CopyTo(bytes, 0)));
        Assert.Throws<InvalidDataException>(() => Load(Document(), mutateFile: bytes => BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, 12)));
    }

    [Fact]
    public void OnlySelectedSceneMeshesAreImported()
    {
        var root = Document(); root["nodes"]!.AsArray().Add(JsonNode.Parse("{\"mesh\":0,\"translation\":[7,0,0]}"));
        root["scenes"] = JsonNode.Parse("[{\"nodes\":[0]},{\"nodes\":[1]}]");
        Assert.Equal(0, Assert.Single(Load(root).Meshes).Node);
        root["scene"] = 1; var model = Load(root); Assert.Equal(1, Assert.Single(model.Meshes).Node);
        var pose = model.CreatePose(); pose.Evaluate(null, 0); Assert.Equal(7, pose.World[1].M41);
        root["scene"] = 2; Assert.Throws<InvalidDataException>(() => Load(root));
    }

    [Fact]
    public void OversizedFileIsRejectedBeforeReadingItsPayload()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".glb");
        try {
            using (var file = File.Create(path)) file.SetLength(AnimatedGlbModel.MaxAssetBytes + 1);
            Assert.Throws<NotSupportedException>(() => AnimatedGlbModel.Load(path));
        } finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void ImportedSceneRejectsInvalidScaleBeforeReadingAssets(float width)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new ImportedSceneAsset("missing.glb", width));

    [Fact]
    public void DeepHierarchyLoadsWithoutRecursiveTraversal()
    {
        var root = Document(); var nodes = root["nodes"]!.AsArray(); nodes.Clear();
        for (int i = 0; i < 16000; i++) {
            var node = new JsonObject();
            if (i < 15999) node["children"] = new JsonArray(i + 1);
            nodes.Add(node);
        }
        nodes[15999]!["mesh"] = 0;
        var model = Load(root); var pose = model.CreatePose(); pose.Evaluate(null, 0);
        Assert.Equal(16000, model.Order.Length); Assert.True(float.IsFinite(pose.World[15999].M44));
    }
}
