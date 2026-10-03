using OpenTK.Mathematics;
namespace ForesTycoon.Tests;
public class RenderVisibilityTests
{
    [Fact]
    public void SphereCullingRetainsOverlappingEdgesAndUsesCameraTransform()
    {
        var camera = Matrix4.CreateTranslation(-10, 0, 0) * Matrix4.CreateOrthographic(20, 20, 1, 100);
        Assert.True(RenderVisibility.SphereVisible(new Vector3(10, 0, -5), 1, camera));
        Assert.True(RenderVisibility.SphereVisible(new Vector3(20.5f, 0, -5), 1, camera));
        Assert.False(RenderVisibility.SphereVisible(new Vector3(22, 0, -5), 1, camera));
        Assert.False(RenderVisibility.SphereVisible(new Vector3(10, 0, 5), 1, camera));
    }
}
