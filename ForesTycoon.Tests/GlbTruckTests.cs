using OpenTK.Mathematics;
namespace ForesTycoon.Tests;
public class GlbTruckTests
{
    [Theory]
    [InlineData("NORMAL", 12)] [InlineData("COLOR_0", 16)]
    public void NonFiniteNormalsAndColorsAreRejected(string semantic, int defaultStride)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Vehicles", "log-truck.glb"));
        int jsonLength = BitConverter.ToInt32(bytes, 12), binaryStart = jsonLength + 28;
        using var document = System.Text.Json.JsonDocument.Parse(bytes.AsMemory(20, jsonLength));
        var root = document.RootElement;
        var primitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
        var indices = root.GetProperty("accessors")[primitive.GetProperty("indices").GetInt32()];
        var indexView = root.GetProperty("bufferViews")[indices.GetProperty("bufferView").GetInt32()];
        int Offset(System.Text.Json.JsonElement value) => value.TryGetProperty("byteOffset", out var offset) ? offset.GetInt32() : 0;
        int vertex = (int)BitConverter.ToUInt32(bytes, binaryStart + Offset(indexView) + Offset(indices));
        var accessor = root.GetProperty("accessors")[primitive.GetProperty("attributes").GetProperty(semantic).GetInt32()];
        var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        int stride = view.TryGetProperty("byteStride", out var value) ? value.GetInt32() : defaultStride;
        BitConverter.GetBytes(float.NaN).CopyTo(bytes, binaryStart + Offset(view) + Offset(accessor) + vertex * stride);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".glb");
        try { File.WriteAllBytes(path, bytes); Assert.Throws<InvalidDataException>(() => GlbTruckModel.Load(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SelectedAssetContainsOnlyLogTruck_WithSeparateCargoAndWheels()
    {
        string path=System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","Vehicles","log-truck.glb");
        using var model=GlbTruckModel.Load(path);
        Assert.InRange(model.DrawGroupCount, 7, 13);
        Assert.True(model.Radius > model.Width);
        Assert.Equal(6,model.Parts.Count(p=>p.Category=="cargo"));
        Assert.Contains(model.Parts,p=>p.Category=="wheel");
        Assert.DoesNotContain(model.Parts,p=>p.Name.Contains("Tanker")||p.Name.Contains(".002")||p.Name.Contains(".003"));
        var vertices=model.Parts.SelectMany(p=>p.Vertices).ToArray();
        Assert.Equal(8877*3,vertices.Length);
        Assert.InRange(vertices.Max(v=>v.Position.X)-vertices.Min(v=>v.Position.X),3.29f,3.31f);
        Assert.InRange(vertices.Min(v=>v.Position.Z),-0.001f,0.001f);
        Assert.All(vertices,v=>Assert.InRange(v.Normal.Length,0.999f,1.001f));
        Assert.True(vertices.Select(v=>v.Color).Distinct().Count()>5);
    }
}
