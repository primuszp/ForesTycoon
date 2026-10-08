using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

// RenderDevice and primitive emitters are process-wide; run these tests without other collections.
[CollectionDefinition("Graphics backend", DisableParallelization = true)]
public sealed class GraphicsBackendCollection { }

[Collection("Graphics backend")]
public sealed class GraphicsBackendTests
{
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

    private sealed class RecordingGraphicsBackend : IGraphicsBackend
    {
        internal int Disposals;
        internal Vector2i Viewport;
        internal Vector4 ClearColor;
        internal PrimitiveTopology Topology;
        internal ColoredVertex[] Vertices = [];
        internal (PrimitiveTopology, GeometryBufferUsage) BufferRequest;
        public void Initialize() { }
        public void UseGeometryShader() { }
        public void InitializeFrameState() { }
        public void SetViewport(int width, int height) => Viewport = new(width, height);
        public void Clear(Vector4 color) => ClearColor = color;
        public void ReadPixels(int x, int y, int width, int height, byte[] rgba) => new byte[] { 1, 2, 3, 255 }.CopyTo(rgba, 0);
        public void CheckErrors(string operation) { }
        public void DrawPrimitives(PrimitiveTopology topology, ReadOnlySpan<ColoredVertex> vertices)
        { Topology = topology; Vertices = vertices.ToArray(); }
        public IGeometryBufferBackend CreateGeometryBuffer(PrimitiveTopology topology, GeometryBufferUsage usage)
        { BufferRequest = (topology, usage); return new RecordingGeometryBuffer(); }
        public IForestStateBuffer CreateForestStateBuffer() => throw new NotSupportedException();
        public RenderStateScope CreateStateScope() => throw new NotSupportedException();
        public IPostProcessBackend CreatePostProcess() => throw new NotSupportedException();
        public void Dispose() => Disposals++;
    }

    private sealed class RecordingGeometryBuffer : IGeometryBufferBackend
    {
        private Vertex[] vertices = [];
        public ReadOnlySpan<Vertex> CpuVertices => vertices;
        public float ForestElapsedYears { get; set; }
        public float ForestCurrentYear { get; set; }
        public IForestStateBuffer ForestState { get; set; } = null!;
        public void SetData(Vertex[] data, bool retainCpuCopy) => vertices = retainCpuCopy ? data.ToArray() : [];
        public void SetElements(uint[] data) { }
        public void SetForestGrowth(ForestVertexGrowth[] data) { }
        public IEnumerable<bool> UploadForestPages(List<Vertex> data, List<ForestVertexGrowth> growth) => throw new NotSupportedException();
        public void DrawArray(bool useGeometryShader) { }
        public void DrawElements() { }
        public void ReadVertices(Vertex[] destination) => vertices.CopyTo(destination, 0);
        public void Dispose() => vertices = [];
    }
}
