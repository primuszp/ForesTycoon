using System.Security.Cryptography;

namespace ForesTycoon.Tests;

public class ImportedPineAssetTests
{
    [Fact]
    public void SuppliedPineIsPackagedWithoutChangingTheSourceFile()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Forest", "pine-tree-original.glb");
        Assert.Equal("A25B9EDEF38E149BCCC8B70CE48DB4F715B77BE71B36A59D3E0EDF1A983874F0",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        var model = AnimatedGlbModel.Load(path);
        Assert.Equal(12, model.Meshes.Length);
        Assert.Empty(model.Images);
        Assert.All(model.Meshes, mesh => Assert.Equal(-1, mesh.Skin));
        Assert.True(model.Meshes.Sum(mesh => mesh.Indices.Length / 3) > 430000);
        Assert.Equal(0.0256848f, model.Meshes[2].Color.Y, 6);
    }

    [Fact]
    public void ImportedAssetKeepsAllTrianglesAndTheNativeCrownProportions()
    {
        using var asset = new ImportedPineAsset();
        Assert.True(asset.TriangleCount > 430000);
        Assert.InRange(asset.CrownRatio, 0.25f, 0.35f);
    }
}
