using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

// Native/backend lifecycle fixtures run without other collections.
[CollectionDefinition("Graphics backend", DisableParallelization = true)]
public sealed class GraphicsBackendCollection { }

[Collection("Graphics backend")]
public sealed class GraphicsBackendTests
{
    [Fact]
    public void TitleFontsFollowTheCurrentUiContextAndSurviveAnotherControllersDisposal()
    {
        using var first = new ImGuiController(new RecordingUiBackend());
        first.MakeCurrent(); var original = ImGuiController.TitleFont;
        bool hasOriginal = ImGuiController.HasTitleFonts;
        using (var second = new ImGuiController(new RecordingUiBackend())) {
            if (hasOriginal) Assert.NotEqual(original, ImGuiController.TitleFont);
            first.MakeCurrent(); Assert.Equal(original, ImGuiController.TitleFont);
            second.Dispose(); first.MakeCurrent(); Assert.Equal(original, ImGuiController.TitleFont);
            Assert.Equal(hasOriginal, ImGuiController.HasTitleFonts);
        }
        first.Dispose(); Assert.False(ImGuiController.HasTitleFonts);
    }

    [Fact]
    public void CpuConstructedGpuOwnershipBindsOnFirstUseAndRejectsForeignEnvironment()
    {
        var owner = new RenderResourceOwner();
        var a = new RenderEnvironment(new RecordingGraphicsBackend()); var b = new RenderEnvironment(new RecordingGraphicsBackend());
        using (a.Activate()) { owner.CheckIfBound(); a.Dispose(); }
        using (b.Activate()) { owner.Check(); owner.CheckIfBound(); }
        var c = new RenderEnvironment(new RecordingGraphicsBackend());
        using (c.Activate()) {
            Assert.Throws<InvalidOperationException>(() => owner.Check());
            Assert.Throws<InvalidOperationException>(() => owner.CheckIfBound()); c.Dispose();
        }
        using (b.Activate()) { owner.Check(); b.Dispose(); }
        Assert.Throws<ObjectDisposedException>(() => owner.CheckIfBound());
    }

    [Fact]
    public void RenderEnvironmentsIsolateFrameStateFactoriesAndNestedPrimitiveBatches()
    {
        var backendA = new RecordingGraphicsBackend(); var backendB = new RecordingGraphicsBackend();
        var a = new RenderEnvironment(backendA); var b = new RenderEnvironment(backendB);
        Func<AnimatedGlbModel, IModelRenderBackend> factory = _ => throw new NotSupportedException("fixture");
        using (a.Activate()) {
            RenderDevice.Initialize(); RenderDevice.SetCamera(Matrix4.CreateTranslation(1, 2, 3));
            Daylight.Override = Daylight.Neutral with { Azimuth = 17 }; Daylight.Current = Daylight.Override.Value;
            RenderMetrics.BeginFrame(); RenderMetrics.RecordDraw(12); ModelRenderBackends.Create = factory;
            var effectsA = EffectRenderBackends.Current; var sceneA = SceneRenderBackends.Current;
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Lines, () => {
                DynamicPrimitiveBatch.ColorPacked(123); DynamicPrimitiveBatch.Vertex3(Vector3.Zero);
                using (b.Activate()) {
                    Assert.Equal(Matrix4.Identity, RenderDevice.ViewProjection); Assert.Null(Daylight.Override);
                    Assert.Equal(0, RenderMetrics.DrawCalls); Assert.NotSame(factory, ModelRenderBackends.Create);
                    Assert.NotSame(effectsA, EffectRenderBackends.Current); Assert.NotSame(sceneA, SceneRenderBackends.Current);
                    RenderDevice.Initialize(); RenderDevice.SetCamera(Matrix4.CreateScale(2));
                    DynamicPrimitiveBatch.Draw(PrimitiveTopology.Lines, () => {
                        DynamicPrimitiveBatch.ColorPacked(456); DynamicPrimitiveBatch.Vertex3(Vector3.UnitY);
                        DynamicPrimitiveBatch.Vertex3(Vector3.UnitZ);
                    });
                    RenderMetrics.RecordDraw(99); b.Dispose();
                }
                Assert.Equal(Matrix4.CreateTranslation(1, 2, 3), RenderDevice.ViewProjection);
                Assert.Equal(17, Daylight.Current.Azimuth); Assert.Equal(12, RenderMetrics.SubmittedVertices);
                Assert.Same(factory, ModelRenderBackends.Create); DynamicPrimitiveBatch.Vertex3(Vector3.UnitX);
            });
            Assert.Equal(new[] { Vector3.Zero, Vector3.UnitX }, backendA.Vertices.Select(v => v.Position));
            Assert.All(backendA.Vertices, v => Assert.Equal(123u, v.Color));
            Assert.All(backendB.Vertices, v => Assert.Equal(456u, v.Color)); a.Dispose();
        }
        Assert.Throws<ObjectDisposedException>(() => b.Activate());
    }

    [Fact]
    public void BufferAccessAndDeletionRejectAnotherEnvironmentWithoutChangingEitherBackend()
    {
        var backendA = new RecordingGraphicsBackend(); var backendB = new RecordingGraphicsBackend();
        var a = new RenderEnvironment(backendA); var b = new RenderEnvironment(backendB);
        using (a.Activate()) {
            var buffer = new VertexBuffer(PrimitiveTopology.Triangles);
            buffer.SetData(new[] { new Vertex(Vector3.Zero, Vector3.UnitZ, 0xffffffff) });
            using (b.Activate()) {
                Assert.Throws<InvalidOperationException>(() => buffer.DrawArray());
                Assert.Throws<InvalidOperationException>(() => buffer.SetData(Array.Empty<Vertex>()));
                Assert.Throws<InvalidOperationException>(() => buffer.Dispose());
                Assert.Equal(1, backendA.LiveBuffers); Assert.Equal(0, backendB.LiveBuffers); b.Dispose();
            }
            buffer.DrawArray(); buffer.Dispose(); buffer.Dispose(); Assert.Equal(0, backendA.LiveBuffers); a.Dispose();
        }
    }

    [Fact]
    public void EnvironmentScopesRequireReverseOrderAndTheOwningThread()
    {
        var a = new RenderEnvironment(new RecordingGraphicsBackend());
        var b = new RenderEnvironment(new RecordingGraphicsBackend());
        var first = a.Activate(); var second = b.Activate();
        Assert.Throws<InvalidOperationException>(() => first.Dispose());
        b.Dispose(); second.Dispose();
        Assert.Same(a, RenderDevice.Environment);
        Exception? otherThreadError = null;
        var otherThread = new System.Threading.Thread(() => otherThreadError = Record.Exception(() => a.Activate()));
        otherThread.Start(); otherThread.Join(); Assert.IsType<InvalidOperationException>(otherThreadError);
        a.Dispose(); first.Dispose(); first.Dispose();
    }

    [Fact]
    public void NativeContextValidationRunsBeforeUsingOrDeletingOwnedBuffers()
    {
        bool current = true; var backend = new RecordingGraphicsBackend();
        var environment = new RenderEnvironment(backend, () => { if (!current) throw new InvalidOperationException("Wrong native context."); });
        using (environment.Activate()) {
            var buffer = new VertexBuffer(PrimitiveTopology.Lines); current = false;
            Assert.Throws<InvalidOperationException>(() => buffer.SetData(Array.Empty<Vertex>()));
            Assert.Throws<InvalidOperationException>(() => buffer.Dispose());
            Assert.Throws<InvalidOperationException>(() => environment.Dispose());
            current = true; buffer.Dispose(); environment.Dispose();
        }
    }

    [Fact]
    public void DeferredUploadsCheckOwnershipBeforeResumingTheBackendIterator()
    {
        var backend = new RecordingGraphicsBackend();
        var a = new RenderEnvironment(backend); var b = new RenderEnvironment(new RecordingGraphicsBackend());
        using (a.Activate()) {
            using var buffer = new VertexBuffer(PrimitiveTopology.Triangles);
            using var pages = buffer.UploadForestPages(new(), new()).GetEnumerator();
            Assert.True(pages.MoveNext()); Assert.Equal(1, backend.UploadedPages);
            using (b.Activate()) {
                Assert.Throws<InvalidOperationException>(() => pages.MoveNext());
                Assert.Equal(1, backend.UploadedPages); b.Dispose();
            }
            buffer.Dispose(); a.Dispose();
        }
    }

    [Fact]
    public void MarkerSubmissionIsBoundedWhileFeedbackStateRemainsIndependent()
    {
        var backend = new RecordingGraphicsBackend(); RenderDevice.Configure(backend);
        try {
            var effects = new WorldEffectSystem();
            for (int i = 0; i < 1000; i++) effects.Spawn(WorldEffectKind.TreePlanted, new(i, 0, 0));
            Assert.Equal(128, EffectRenderer.Draw(effects, .5f, 128));
            Assert.Equal(128 * 40, backend.Vertices.Length); Assert.Equal(1000, effects.Count);
            Assert.Equal(0, EffectRenderer.Draw(effects, .5f, 0));
        } finally { RenderDevice.Dispose(); RenderBackendSelection.UseOpenGl(); }
    }

    [Fact]
    public void FailedTruckUploadReleasesCandidateAndCanRetryWithoutLosingCpuAsset()
    {
        var backend = new RecordingGraphicsBackend(); RenderDevice.Configure(backend);
        try {
            using var truck = GlbTruckModel.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Vehicles", "log-truck.glb"));
            backend.FailUploads = true;
            Assert.Throws<IOException>(() => truck.Draw(Matrix4.Identity, 1, 0));
            Assert.Equal(0, backend.LiveBuffers);
            backend.FailUploads = false; truck.Draw(Matrix4.Identity, 1, 0);
            Assert.Equal(truck.DrawGroupCount, backend.LiveBuffers);
            truck.Dispose(); truck.Dispose(); Assert.Equal(0, backend.LiveBuffers);
            Assert.Throws<ObjectDisposedException>(() => truck.Draw(Matrix4.Identity, 1, 0));
        } finally { RenderDevice.Dispose(); RenderBackendSelection.UseOpenGl(); }
    }

    [Fact]
    public void FailedPresentationConstructionReleasesNewBuffersAndKeepsOldWorld()
    {
        var backend = new RecordingGraphicsBackend();
        RenderDevice.Configure(backend);
        try
        {
            var settings = TerrainSettings.Default.WithNodeSize(5, 42);
            using var world = new GameWorld(settings);
            world.Update(0.3); world.QueueWeather(WeatherPreset.Storm, 10, 20);
            using var before = new MemoryStream(); world.Save(before);
            var originalMap = world.Map;
            int live = backend.LiveBuffers;
            backend.FailUploads = true;
            Assert.Throws<IOException>(() => world.Regenerate(settings.WithSeed(43)));
            backend.FailUploads = false;
            Assert.Equal(live, backend.LiveBuffers);
            Assert.Same(originalMap, world.Map); Assert.True(world.HasPresentation);
            using var after = new MemoryStream(); world.Save(after);
            Assert.Equal(before.ToArray(), after.ToArray());
            Assert.Equal(1, world.ExecutePendingCommands()); world.Update(1.0 / 30);
            world.Dispose(); Assert.Equal(0, backend.LiveBuffers);
        }
        finally { RenderDevice.Dispose(); RenderBackendSelection.UseOpenGl(); }
    }

    [Fact]
    public void UiControllerUsesInjectedRendererWithoutAGraphicsContext()
    {
        var backend = new RecordingUiBackend();
        using var controller = new ImGuiController(backend);
        Assert.True(backend.Initialized);
        controller.Update(320, 200, 320, 200, System.Numerics.Vector2.One, 1f / 60f);
        ImGuiNET.ImGui.Begin("Backend test");
        ImGuiNET.ImGui.Text("CPU UI submission");
        ImGuiNET.ImGui.End();
        controller.Render();
        Assert.Equal(1, backend.Draws);
        controller.Dispose();
        controller.Dispose();
        Assert.Equal(1, backend.Disposals);
    }

    private sealed class RecordingUiBackend : IUiRenderBackend
    {
        internal bool Initialized;
        internal int Draws, Disposals;
        public void Initialize(ImGuiNET.ImGuiIOPtr io)
        {
            Assert.False(io.BackendFlags.HasFlag(ImGuiNET.ImGuiBackendFlags.RendererHasVtxOffset));
            Assert.True(io.Fonts.Build());
            Initialized = true;
        }
        public void Draw(ImGuiNET.ImDrawDataPtr data) { Assert.True(data.Valid); Draws++; }
        public void Dispose() => Disposals++;
    }

    [Fact]
    public void PrimitiveAndFrameSubmissionWorkWithoutANativeContext()
    {
        var backend = new RecordingGraphicsBackend();
        RenderDevice.Configure(backend);
        try
        {
            RenderDevice.Initialize();
            RenderDevice.SetViewport(320, 200);
            var clear = new Vector4(0.1f, 0.2f, 0.3f, 1);
            RenderDevice.Clear(clear);
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                DynamicPrimitiveBatch.ColorPacked(0x12345678);
                DynamicPrimitiveBatch.Vertex3(0, 0, 0);
                DynamicPrimitiveBatch.Vertex3(1, 0, 0);
                DynamicPrimitiveBatch.Vertex3(1, 1, 0);
                DynamicPrimitiveBatch.Vertex3(0, 1, 0);
            });
            Assert.Equal(PrimitiveTopology.Triangles, backend.Topology);
            Assert.Equal(new[] { Vector3.Zero, Vector3.UnitX, new Vector3(1, 1, 0),
                Vector3.Zero, new Vector3(1, 1, 0), Vector3.UnitY }, backend.Vertices.Select(v => v.Position));
            Assert.All(backend.Vertices, v => Assert.Equal(0x12345678u, v.Color));
            Assert.Equal(new Vector2i(320, 200), backend.Viewport);
            Assert.Equal(clear, backend.ClearColor);
            var rgba = new byte[4];
            RenderDevice.ReadPixels(0, 0, 1, 1, rgba);
            Assert.Equal(new byte[] { 1, 2, 3, 255 }, rgba);
            RenderDevice.Dispose();
            RenderDevice.Dispose();
            Assert.Equal(1, backend.Disposals);
        }
        finally { RenderDevice.Dispose(); RenderBackendSelection.UseOpenGl(); }
    }

    [Fact]
    public void BackendSelectionCannotPartiallyChangeALiveRenderer()
    {
        var backend = new RecordingGraphicsBackend();
        var bundle = OpenGl.OpenGlRenderBackendBundle.Create() with { Graphics = backend };
        RenderBackendSelection.Configure(bundle);
        try
        {
            RenderDevice.Initialize();
            var replacement = OpenGl.OpenGlRenderBackendBundle.Create();
            Assert.Throws<InvalidOperationException>(() => RenderBackendSelection.Configure(replacement));
            Assert.Same(backend, RenderDevice.Backend);
            Assert.Same(bundle.Models, ModelRenderBackends.Create);
            Assert.Same(bundle.Effects, EffectRenderBackends.Current);
            Assert.Same(bundle.Scene, SceneRenderBackends.Current);
            Assert.Same(bundle.Window, RenderBackendSelection.Window);
        }
        finally { RenderDevice.Dispose(); RenderBackendSelection.UseOpenGl(); }
    }

    [Fact]
    public void ResourcesLockSelectionAndCanBeReleasedBeforeInitialization()
    {
        var backend = new RecordingGraphicsBackend();
        RenderDevice.Configure(backend);
        try
        {
            using var buffer = new VertexBuffer(PrimitiveTopology.Lines, GeometryBufferUsage.Dynamic);
            Assert.Equal((PrimitiveTopology.Lines, GeometryBufferUsage.Dynamic), backend.BufferRequest);
            var vertices = new[] { new Vertex(Vector3.Zero, Vector3.UnitZ, 1u),
                new Vertex(Vector3.UnitX, Vector3.UnitZ, 2u) };
            buffer.SetData(vertices);
            Assert.Equal(vertices, buffer.CpuVertices.ToArray());
            Assert.Throws<InvalidOperationException>(() => RenderDevice.Configure(new RecordingGraphicsBackend()));
            buffer.Dispose();
            RenderDevice.Dispose();
            Assert.Equal(1, backend.Disposals);
            // Disposal must unlock even if no geometry shader was initialized.
            RenderDevice.Configure(new RecordingGraphicsBackend());
        }
        finally { RenderDevice.Dispose(); RenderBackendSelection.UseOpenGl(); }
    }

    private sealed class RecordingStateScope : RenderStateScope
    {
        private bool disposed;
        internal override bool IsDisposed => disposed;
        private RenderStateScope Alive() { ObjectDisposedException.ThrowIf(disposed, this); return this; }
        public override RenderStateScope Enable(RenderCapability capability) => Alive();
        public override RenderStateScope Disable(RenderCapability capability) => Alive();
        public override RenderStateScope AlphaBlend() => Alive();
        public override RenderStateScope DepthWrite(bool enabled) => Alive();
        public override RenderStateScope ThinLines() => Alive();
        public override RenderStateScope PolygonOffset(float factor, float units) => Alive();
        public override RenderStateScope Cull(RenderCullFace face) => Alive();
        public override void Dispose() => disposed = true;
    }

    private sealed class RecordingGraphicsBackend : IGraphicsBackend
    {
        internal int Disposals;
        internal int LiveBuffers;
        internal int UploadedPages;
        internal bool FailUploads;
        internal Vector2i Viewport;
        internal Vector4 ClearColor;
        internal PrimitiveTopology Topology;
        internal ColoredVertex[] Vertices = [];
        internal (PrimitiveTopology, GeometryBufferUsage) BufferRequest;
        public void Initialize() { }
        public void UseGeometryShader() { }
        public void UseScreenLineShader(float widthPixels, Vector4 colorOverride = default) { }
        public void InitializeFrameState() { }
        public void SetViewport(int width, int height) => Viewport = new(width, height);
        public void Clear(Vector4 color) => ClearColor = color;
        public void ReadPixels(int x, int y, int width, int height, byte[] rgba) => new byte[] { 1, 2, 3, 255 }.CopyTo(rgba, 0);
        public void CheckErrors(string operation) { }
        public void DrawPrimitives(PrimitiveTopology topology, ReadOnlySpan<ColoredVertex> vertices)
        { Topology = topology; Vertices = vertices.ToArray(); }
        public IGeometryBufferBackend CreateGeometryBuffer(PrimitiveTopology topology, GeometryBufferUsage usage)
        { BufferRequest = (topology, usage); LiveBuffers++; return new RecordingGeometryBuffer(this); }
        public IForestStateBuffer CreateForestStateBuffer() => new RecordingForestStateBuffer();
        public RenderStateScope CreateStateScope() => new RecordingStateScope();
        public IPostProcessBackend CreatePostProcess() => throw new NotSupportedException();
        public void Dispose() => Disposals++;
    }

    private sealed class RecordingGeometryBuffer : IGeometryBufferBackend
    {
        private readonly RecordingGraphicsBackend owner;
        private bool disposed;
        internal RecordingGeometryBuffer(RecordingGraphicsBackend owner) => this.owner = owner;
        private Vertex[] vertices = [];
        public ReadOnlySpan<Vertex> CpuVertices => vertices;
        public float ForestElapsedYears { get; set; }
        public float ForestCurrentYear { get; set; }
        public IForestStateBuffer ForestState { get; set; } = null!;
        public void SetData(Vertex[] data, bool retainCpuCopy)
        {
            if (owner.FailUploads) throw new IOException("Injected resource allocation failure.");
            vertices = retainCpuCopy ? data.ToArray() : [];
        }
        public void SetElements(uint[] data) { }
        public void SetForestGrowth(ForestVertexGrowth[] data) { }
        public IEnumerable<bool> UploadForestPages(List<Vertex> data, List<ForestVertexGrowth> growth)
        {
            if (owner.FailUploads) throw new IOException("Injected resource allocation failure.");
            owner.UploadedPages++;
            yield return true;
            owner.UploadedPages++;
            yield return true;
        }
        public void DrawArray(bool useGeometryShader) { }
        public void DrawElements() { }
        public void ReadVertices(Vertex[] destination) => vertices.CopyTo(destination, 0);
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; owner.LiveBuffers--; vertices = [];
        }
    }

    private sealed class RecordingForestStateBuffer : IForestStateBuffer
    {
        public void SetData(Vector4[] data, int count) { }
        public void Dispose() { }
    }
}
