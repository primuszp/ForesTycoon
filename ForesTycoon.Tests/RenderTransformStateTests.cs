using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class RenderTransformStateTests
{
    [Fact]
    public void CameraReplacementPreservesModelStackButNewCameraResetsIt()
    {
        var state = new RenderTransformState();
        var model = Matrix4.CreateScale(2);
        state.SetModel(model);
        state.PushModel();
        state.Translate(1, 2, 3);
        Assert.Equal(Matrix4.CreateTranslation(1, 2, 3) * model, state.Model);
        var camera = Matrix4.CreateRotationZ(0.4f);
        state.SetViewProjection(camera);
        state.PopModel();
        Assert.Equal(model, state.Model);
        Assert.Equal(camera, state.ViewProjection);
        state.PushModel();
        state.SetCamera(Matrix4.Identity);
        Assert.Equal(Matrix4.Identity, state.Model);
        Assert.Throws<InvalidOperationException>(() => state.PopModel());
    }

    [Fact]
    public void SeparateDevicesHaveIndependentTransformState()
    {
        var first = new RenderTransformState();
        var second = new RenderTransformState();
        first.SetCamera(Matrix4.CreateScale(3));
        first.Translate(1, 2, 3);
        Assert.Equal(Matrix4.Identity, second.Model);
        Assert.Equal(Matrix4.Identity, second.ViewProjection);
        first.Reset();
        Assert.Equal(Matrix4.Identity, first.Model);
        Assert.Equal(Matrix4.Identity, first.ViewProjection);
    }
}
