using OpenTK.Mathematics;
namespace ForesTycoon.Tests;
public class DioramaScaleTests
{
    private const float Tile = 5f;

    private static (Vector3 Min, Vector3 Max) Bounds(AnimatedGlbModel model, AnimatedGlbModel.Pose pose, Matrix4 transform)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var mesh in model.Meshes)
            for (int i = 0; i < mesh.Vertices.Length; i += 16)
            {
                var v = mesh.Vertices; Vector4 p = new(v[i], v[i + 1], v[i + 2], 1), point = Vector4.Zero;
                if (mesh.Skin < 0) point = Vector4.TransformRow(p, pose.World[mesh.Node]);
                else for (int j = 0; j < 4; j++) point += Vector4.TransformRow(p, pose.JointMatrix(mesh.Skin, (int)v[i + 8 + j])) * v[i + 12 + j];
                Vector3 q = Vector4.TransformRow(point, transform).Xyz;
                min = Vector3.ComponentMin(min, q); max = Vector3.ComponentMax(max, q);
            }
        return (min, max);
    }

    private static (float Length, float Height) Truck()
    {
        using var truck = GlbTruckModel.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Vehicles", "log-truck.glb"));
        var vertices = truck.Parts.SelectMany(p => p.Vertices).ToArray();
        float scale = Tile * 0.62f * DioramaScale.TruckLaneFill / truck.Width;
        return ((vertices.Max(v => v.Position.X) - vertices.Min(v => v.Position.X)) * scale, vertices.Max(v => v.Position.Z) * scale);
    }

    [Fact]
    public void SawmillFillsButNeverLeavesItsTwoByTwoFootprint()
    {
        using var mill = new ImportedSceneAsset("Assets/Buildings/sawmill.glb",
            DioramaScale.SawmillWidth(Tile, Tile), footprint: DioramaScale.SawmillFootprint(Tile, Tile));
        var (min, max) = Bounds(mill.Model, mill.Pose, mill.Normalization);
        Assert.All(new[] { min.X, min.Y }, value => Assert.True(value >= -Tile - 0.01f));
        Assert.All(new[] { max.X, max.Y }, value => Assert.True(value <= Tile + 0.01f));
        Assert.True(max.X - min.X > 1.8f * Tile, "The yard should span nearly both tiles.");
    }

    [Fact]
    public void TruckIsClearlySmallerThanTheSawmillAndLargerThanTheElk()
    {
        var (truckLength, truckHeight) = Truck();
        using var mill = new ImportedSceneAsset("Assets/Buildings/sawmill.glb",
            DioramaScale.SawmillWidth(Tile, Tile), footprint: DioramaScale.SawmillFootprint(Tile, Tile));
        var (millMin, millMax) = Bounds(mill.Model, mill.Pose, mill.Normalization);
        Assert.True(truckLength < DioramaScale.SawmillWidth(Tile, Tile) * 0.8f, $"truck {truckLength:F2} vs mill building");
        Assert.True(truckHeight * 2.5f < millMax.Z - millMin.Z, $"truck {truckHeight:F2} vs mill {millMax.Z - millMin.Z:F2}");

        var elk = AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Wildlife", "elk.glb"));
        var pose = elk.CreatePose(); pose.Evaluate("Stand_Eating_01", 0);
        var (elkMin, elkMax) = Bounds(elk, pose, WildlifeRenderer.Axis * Matrix4.CreateScale(DioramaScale.Elk));
        Assert.True(elkMax.Z - elkMin.Z < truckHeight, $"elk {elkMax.Z - elkMin.Z:F2} vs truck {truckHeight:F2}");
        Assert.True(Math.Max(elkMax.X - elkMin.X, elkMax.Y - elkMin.Y) < truckLength * 0.5f);
    }
}
