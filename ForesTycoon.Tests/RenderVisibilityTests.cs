using OpenTK.Mathematics;
namespace ForesTycoon.Tests;
public class RenderVisibilityTests
{
    [Fact]
    public void SphereCullingRetainsOverlappingEdgesAndUsesCameraTransform()
    {
        var camera = Matrix4.CreateTranslation(-10, 0, 0) * Matrix4.CreateOrthographic(20, 20, 1, 100);
        var visibility = new RenderVisibility(camera);
        Assert.True(visibility.SphereVisible(new Vector3(10, 0, -5), 1));
        Assert.True(visibility.SphereVisible(new Vector3(20.5f, 0, -5), 1));
        Assert.False(visibility.SphereVisible(new Vector3(22, 0, -5), 1));
        Assert.False(visibility.SphereVisible(new Vector3(10, 0, 5), 1));
    }

    [Fact]
    public void PreparedPlanesPreserveClipTestsForPerspectiveOrthographicAndDegenerateCameras()
    {
        var random = new Random(17);
        foreach (var projection in new[] { Matrix4.CreatePerspectiveFieldOfView(1.1f, 1.5f, 0.1f, 100),
                     Matrix4.CreateOrthographic(20, 30, 0.1f, 100), Matrix4.Zero })
        {
            var camera = Matrix4.CreateTranslation(-3, 7, -4) * Matrix4.CreateRotationY(0.4f) * projection;
            var visibility = new RenderVisibility(camera);
            Vector4 x = camera.Column0, y = camera.Column1, z = camera.Column2, w = camera.Column3;
            var planes = new[] { w + x, w - x, w + y, w - y, w + z, w - z };
            for (int i = 0; i < 4000; i++)
            {
                var center = new Vector3(random.NextSingle() * 100 - 50, random.NextSingle() * 100 - 50,
                    random.NextSingle() * 100 - 80);
                float radius = i % 5 == 0 ? 0 : random.NextSingle() * 6;
                bool expected = planes.All(plane => Vector4.Dot(new Vector4(center, 1), plane) >= -radius * plane.Xyz.Length);
                Assert.Equal(expected, visibility.SphereVisible(center, radius));
            }
        }
    }
}
