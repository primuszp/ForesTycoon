using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class AnimationSamplingTests
{
    [Theory]
    [InlineData(0, 0.5f, 3f)]
    [InlineData(1, 0.5f, 2f)]
    [InlineData(0, -1f, 2f)]
    [InlineData(0, 3f, 6f)]
    public void TranslationSamplingClampsAndInterpolates(int interpolation, float time, float expected)
    {
        var channel = new AnimatedGlbModel.Channel {
            Path = AnimatedGlbModel.AnimationPath.Translation, Arity = 3,
            Interpolation = (AnimatedGlbModel.AnimationInterpolation)interpolation, Times = new[] { 0f, 2f },
            Values = new[] { 2f, 0f, 0f, 6f, 0f, 0f } };
        Assert.Equal(expected, channel.Sample(time).X);
    }

    [Fact]
    public void CubicTangentsUseTheKeyframeInterval()
    {
        var channel = new AnimatedGlbModel.Channel {
            Path = AnimatedGlbModel.AnimationPath.Translation, Arity = 3,
            Interpolation = AnimatedGlbModel.AnimationInterpolation.CubicSpline,
            Times = new[] { 0f, 2f },
            // incoming tangent, value, outgoing tangent per keyframe
            Values = new[] { 0f,0f,0f, 0f,0f,0f, 2f,0f,0f,
                             0f,0f,0f, 2f,0f,0f, 0f,0f,0f } };
        Assert.Equal(1.5f, channel.Sample(1).X);
    }

    [Fact]
    public void RotationSamplingUsesTheShortestArc()
    {
        var channel = new AnimatedGlbModel.Channel {
            Path = AnimatedGlbModel.AnimationPath.Rotation, Arity = 4,
            Times = new[] { 0f, 1f },
            Values = new[] { 0f,0f,0f,1f, 0f,0f,0f,-1f } };
        Vector4 result = channel.Sample(0.5f);
        Assert.Equal(1f, MathF.Abs(result.W));
        Assert.Equal(1f, result.Length);
    }

    [Fact]
    public void EndpointCrossfadeDoesNotSampleTheInactiveClip()
    {
        var model = Model();
        var pose = model.CreatePose();
        pose.Evaluate("active", 0.5, "missing", 0, 0);
        Assert.Equal(3f, pose.World[0].M41);
        pose.Evaluate("missing", 0, "active", 0.5, 1);
        Assert.Equal(3f, pose.World[0].M41);
    }

    [Fact]
    public void NegativeTimeWrapsAndInPlaceRootPreservesRestOfHierarchy()
    {
        var model = Model();
        var pose = model.CreatePose();
        pose.Evaluate("active", -1.5);
        Assert.Equal(3f, pose.World[0].M41);
        pose.Evaluate("active", -1.5, inPlaceRoot: 0);
        Assert.Equal(2f, pose.World[0].M41);
        Assert.Equal(2f, pose.World[1].M41);
        Assert.Equal(4f, pose.World[1].M42);
    }

    [Theory]
    [InlineData(double.NaN, 0f, -1)]
    [InlineData(double.PositiveInfinity, 0f, -1)]
    [InlineData(0d, float.NaN, -1)]
    [InlineData(0d, -0.1f, -1)]
    [InlineData(0d, 1.1f, -1)]
    [InlineData(0d, 0f, 2)]
    public void InvalidPoseInputsFailBeforeProducingMatrices(double time, float blend, int root)
    {
        var pose = Model().CreatePose();
        Assert.Throws<ArgumentOutOfRangeException>(() => pose.Evaluate("active", time, blend: blend, inPlaceRoot: root));
    }

    [Fact]
    public void RepeatedCrossfadesDoNotAllocateOnTheRenderThread()
    {
        var pose = Model().CreatePose();
        for (int i = 0; i < 128; i++) pose.Evaluate("active", i * 0.01, "active", i * 0.02, 0.5f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++) pose.Evaluate("active", i * 0.01, "active", i * 0.02, 0.5f);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static AnimatedGlbModel Model()
    {
        var model = new AnimatedGlbModel {
            Nodes = new[] { new AnimatedGlbModel.Node(), new AnimatedGlbModel.Node {
                Parent = 0, Translation = new Vector3(0,4,0) } }, Order = new[] { 0, 1 } };
        model.Clips.Add("active", new AnimatedGlbModel.Clip {
            Duration = 2, Channels = new[] { new AnimatedGlbModel.Channel {
                Node = 0, Path = AnimatedGlbModel.AnimationPath.Translation, Arity = 3,
                Times = new[] { 0f, 2f }, Values = new[] { 2f,0f,0f, 6f,0f,0f } } } });
        return model;
    }
}
