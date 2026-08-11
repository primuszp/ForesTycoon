using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class IsometricCameraTests
{
    [Fact]
    public void Zoom_KeepsWorldAnchorAtSameScreenPosition()
    {
        IsometricCamera camera = new IsometricCamera { ScreenX = 5, ScreenY = 7 };
        double anchorX = 12;
        double anchorY = 15;
        double screenBeforeX = (anchorX - camera.ScreenX) * camera.Zoom;
        double screenBeforeY = (anchorY - camera.ScreenY) * camera.Zoom;
        camera.ZoomBy(2f);

        camera.Update(10f, anchorX, anchorY);

        Assert.Equal(screenBeforeX, (anchorX - camera.ScreenX) * camera.Zoom, 4);
        Assert.Equal(screenBeforeY, (anchorY - camera.ScreenY) * camera.Zoom, 4);
    }

    [Fact]
    public void Reset_RestoresCanonicalIsometricView()
    {
        IsometricCamera camera = new IsometricCamera { ScreenX = 100, ScreenY = 50, TargetYaw = 90 };

        camera.Reset();

        Assert.Equal(-60f, camera.Tilt);
        Assert.Equal(-45f, camera.Yaw);
        Assert.Equal(10f, camera.Zoom);
        Assert.Equal(0, camera.ScreenX);
    }

    [Fact]
    public void ViewMatrix_MatchesYawThenTiltWorldTransform()
    {
        IsometricCamera camera = new IsometricCamera();
        Vector3 point = new Vector3(10, 20, 5);

        Vector4 transformed = Vector4.TransformRow(new Vector4(point, 1), camera.CreateViewMatrix());

        Assert.Equal(21.213f, transformed.X, 3);
        Assert.Equal(7.866f, transformed.Y, 3);
        Assert.Equal(-3.624f, transformed.Z, 3);
    }
}
