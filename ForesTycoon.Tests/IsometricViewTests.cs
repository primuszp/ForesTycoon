using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class IsometricViewTests
{
    [Fact]
    public void SupportBoundsExactlyMatchAllEightCornersAcrossRotationsAndReliefs()
    {
        var random = new Random(42);
        foreach (float tilt in new[] { -180f, -90f, -60f, -45f, -30f, 0f, 30f, 90f, 150f })
        for (int i = 0; i < 200; i++)
        {
            float yaw = random.NextSingle() * 1440 - 720;
            var rotation = new IsometricView(tilt, yaw);
            var min = new Vector3(random.NextSingle() * 500 - 250, random.NextSingle() * 500 - 250, -12);
            var max = min + new Vector3(random.NextSingle() * 80, random.NextSingle() * 80, random.NextSingle() * 64);
            rotation.ProjectBounds(min, max, out var lower, out var upper);
            Vector2 expectedMin = new(float.MaxValue), expectedMax = new(float.MinValue);
            var matrix = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(yaw))
                * Matrix4.CreateRotationX(MathHelper.DegreesToRadians(tilt));
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
            {
                var point = new Vector3(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
                var projected = rotation.Transform(point);
                Assert.InRange((projected - Vector3.TransformPosition(point, matrix)).Length, 0, 0.0005f);
                expectedMin = Vector2.ComponentMin(expectedMin, projected.Xy);
                expectedMax = Vector2.ComponentMax(expectedMax, projected.Xy);
            }
            Assert.Equal(expectedMin, lower);
            Assert.Equal(expectedMax, upper);
        }
    }
}
