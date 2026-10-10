using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class ModelRenderBackendTests
{
    [Fact]
    public void ExplicitFrameCanRenderThroughAnotherBackendWithoutAnOpenGlContext()
    {
        var model = Model();
        var backend = new RecordingBackend();
        using var renderer = new AnimatedModelRenderer(model, backend);
        var pose = model.CreatePose();
        pose.Evaluate(null, 0);
        var frame = Frame();
        var placement = Matrix4.CreateTranslation(2, 3, 4);
        using var batch = renderer.BeginBatch();

        renderer.Draw(pose, placement, frame, batch);

        Assert.Same(pose, backend.Pose);
        Assert.Same(batch, backend.Batch);
        Assert.Equal(placement, backend.Transform);
        Assert.Equal(frame, backend.Frame);
        Assert.Equal(new[] { 2, 1, 0 }, backend.Order);
    }

    [Fact]
    public void MaterialAndShadowChangesRecomputeBackendIndependentOrder()
    {
        var model = Model();
        var pose = model.CreatePose();
        pose.Evaluate(null, 0);
        var order = new ModelDrawOrder(model);
        Assert.Equal(new[] { 2, 1, 0 }, order.Prepare(pose, Matrix4.Identity, Matrix4.Identity, false).ToArray());
        model.Meshes[0].Alpha = AnimatedGlbModel.AlphaMode.Mask;
        Assert.Equal(new[] { 0, 2, 1 }, order.Prepare(pose, Matrix4.Identity, Matrix4.Identity, false).ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, order.Prepare(pose, Matrix4.Identity, Matrix4.Identity, true).ToArray());
    }

    [Fact]
    public void CameraChangesReverseTransparentOrderWhilePreservingSolids()
    {
        var model = Model();
        var pose = model.CreatePose();
        pose.Evaluate(null, 0);
        var order = new ModelDrawOrder(model);
        Assert.Equal(new[] { 2, 0, 1 }, order.Prepare(pose, Matrix4.Identity,
            Matrix4.CreateScale(1, 1, -1), false).ToArray());
    }

    [Fact]
    public void RendererDisposesOwnedBackendOnceAndRejectsFurtherUse()
    {
        var backend = new RecordingBackend();
        var model = Model();
        var renderer = new AnimatedModelRenderer(model, backend);
        renderer.Dispose();
        renderer.Dispose();
        Assert.Equal(1, backend.Disposals);
        Assert.Throws<ObjectDisposedException>(() => renderer.BeginBatch());
        Assert.Throws<ObjectDisposedException>(() => renderer.Draw(model.CreatePose(), Matrix4.Identity, Frame()));
    }

    [Fact]
    public void MultiplePosesShareOneRendererButKeepTheirCpuStateIndependent()
    {
        var model = Model(); var backend = new RecordingBackend();
        var first = model.CreatePose(); var second = model.CreatePose();
        first.Evaluate(null, 0); second.Evaluate(null, 0);
        Assert.NotSame(first.World, second.World);
        first.World[0] = Matrix4.CreateTranslation(7, 0, 0);
        second.Evaluate(null, 1);
        Assert.Equal(7, first.World[0].M41); Assert.Equal(0, second.World[0].M41);
        var renderer = new AnimatedModelRenderer(model, backend);
        renderer.Draw(first, Matrix4.Identity, Frame()); Assert.Same(first, backend.Pose);
        renderer.Draw(second, Matrix4.Identity, Frame()); Assert.Same(second, backend.Pose);
        renderer.Dispose(); renderer.Dispose(); Assert.Equal(1, backend.Disposals);
        // Disposing GPU ownership does not invalidate the shared CPU asset or its poses.
        first.Evaluate(null, 2); Assert.Equal(0, first.World[0].M41);
    }

    [Fact]
    public void ForeignPoseIsRejectedBeforeBackendSubmission()
    {
        var backend = new RecordingBackend(); using var renderer = new AnimatedModelRenderer(Model(), backend);
        var foreign = Model().CreatePose(); foreign.Evaluate(null, 0);
        Assert.Throws<ArgumentException>(() => renderer.Draw(foreign, Matrix4.Identity, Frame()));
        Assert.Null(backend.Pose);
    }

    [Fact]
    public void CpuSubmissionAddsNoPerDrawAllocations()
    {
        var model = Model();
        var backend = new RecordingBackend { CaptureOrder = false };
        using var renderer = new AnimatedModelRenderer(model, backend);
        var pose = model.CreatePose();
        pose.Evaluate(null, 0);
        var frame = Frame();
        for (int i = 0; i < 128; i++) renderer.Draw(pose, Matrix4.Identity, frame);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++) renderer.Draw(pose, Matrix4.Identity, frame);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static ModelRenderFrame Frame() => new(new ModelSceneParameters(Matrix4.Identity,
        Matrix4.Identity, new Vector4(0.2f), 30, 45, true, false, false, true), true, 0.01f);

    private static AnimatedGlbModel Model() => new() {
        Nodes = new[] { new AnimatedGlbModel.Node() }, Order = new[] { 0 },
        Meshes = new[] {
            new AnimatedGlbModel.Mesh { Node = 0, Alpha = AnimatedGlbModel.AlphaMode.Blend, Center = new Vector3(0,0,-0.3f) },
            new AnimatedGlbModel.Mesh { Node = 0, Alpha = AnimatedGlbModel.AlphaMode.Blend, Center = new Vector3(0,0,0.3f) },
            new AnimatedGlbModel.Mesh { Node = 0, Alpha = AnimatedGlbModel.AlphaMode.Opaque } } };

    private sealed class RecordingBackend : IModelRenderBackend
    {
        internal bool CaptureOrder = true;
        internal AnimatedGlbModel.Pose? Pose;
        internal Matrix4 Transform;
        internal ModelRenderFrame Frame;
        internal IModelRenderBatch? Batch;
        internal int[] Order = Array.Empty<int>();
        internal int Disposals;

        public IModelRenderBatch BeginBatch() => new RecordingBatch();
        public void Draw(AnimatedGlbModel.Pose pose, Matrix4 transform, in ModelRenderFrame frame,
            ReadOnlySpan<int> drawOrder, IModelRenderBatch batch)
        {
            Pose = pose; Transform = transform; Frame = frame; Batch = batch;
            if (CaptureOrder) Order = drawOrder.ToArray();
        }
        public void Dispose() => Disposals++;
    }

    private sealed class RecordingBatch : IModelRenderBatch
    {
        public void Dispose() { }
    }
}
