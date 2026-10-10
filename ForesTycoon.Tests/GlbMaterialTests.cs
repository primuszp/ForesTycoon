using System.Text.Json;

namespace ForesTycoon.Tests;

public class GlbMaterialTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("OPAQUE", 0)]
    [InlineData("MASK", 1)]
    [InlineData("BLEND", 2)]
    public void LoadsMaterialCoverageAndDefaults(string? mode, int expected)
    {
        var material = new Dictionary<string, object> {
            ["doubleSided"] = true,
            ["pbrMetallicRoughness"] = new { baseColorFactor = new[] { 1f, .5f, .25f, .3f } }
        };
        if (mode != null) material["alphaMode"] = mode;
        var mesh = Load(material);
        Assert.Equal(expected, (int)mesh.Alpha);
        Assert.Equal(.5f, mesh.AlphaCutoff);
        Assert.True(mesh.DoubleSided);
        Assert.Equal(.3f, mesh.Color.W);
    }

    [Fact]
    public void LoadsCustomCutoffAndRejectsUnknownAlphaModes()
    {
        Assert.Equal(.654f, Load(new { alphaMode = "MASK", alphaCutoff = .654f }).AlphaCutoff);
        Assert.Throws<InvalidDataException>(() => Load(new { alphaMode = "unknown" }));
        Assert.Throws<InvalidDataException>(() => Load(new { alphaMode = "MASK", alphaCutoff = -.1f }));
    }

    private static AnimatedGlbModel.Mesh Load(object material)
    {
        using var binary = new MemoryStream();
        using (var writer = new BinaryWriter(binary, System.Text.Encoding.UTF8, true)) {
            foreach (float f in new[] { -1f,-1,0, 1,-1,0, 0,1,0, 0,0,1, 0,0,1, 0,0,1 }) writer.Write(f);
            foreach (uint i in new uint[] { 0, 1, 2 }) writer.Write(i);
        }
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new {
            asset = new { version = "2.0" }, nodes = new[] { new { mesh = 0 } }, materials = new[] { material },
            buffers = new[] { new { byteLength = (int)binary.Length } },
            meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0, NORMAL = 1 }, indices = 2, material = 0 } } } },
            bufferViews = new[] { new { buffer = 0, byteOffset = 0, byteLength = 36 }, new { buffer = 0, byteOffset = 36, byteLength = 36 }, new { buffer = 0, byteOffset = 72, byteLength = 12 } },
            accessors = new[] { new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3" }, new { bufferView = 1, componentType = 5126, count = 3, type = "VEC3" }, new { bufferView = 2, componentType = 5125, count = 3, type = "SCALAR" } }
        });
        int padded = (json.Length + 3) & ~3;
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".glb");
        try {
            using (var writer = new BinaryWriter(File.Create(path))) {
                writer.Write(0x46546c67u); writer.Write(2u); writer.Write(28+padded+(int)binary.Length);
                writer.Write(padded); writer.Write(0x4e4f534au); writer.Write(json);
                for (int i=json.Length;i<padded;i++) writer.Write((byte)32);
                writer.Write((int)binary.Length); writer.Write(0x004e4942u); writer.Write(binary.ToArray());
            }
            return Assert.Single(AnimatedGlbModel.Load(path).Meshes);
        } finally { File.Delete(path); }
    }
}

