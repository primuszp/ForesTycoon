using System;
using System.Collections.Generic;
namespace ForesTycoon.Rendering
{
    public sealed class VertexBuffer : IDisposable
    {
        private readonly IGeometryBufferBackend backend;
        private readonly RenderEnvironment owner;
        private long vertexBytes, elementBytes, growthBytes;
        internal long GpuPayloadBytes => vertexBytes + elementBytes + growthBytes;
        internal long CpuPayloadBytes => (long)backend.CpuVertices.Length * Vertex.Stride;
        public VertexBuffer(PrimitiveTopology topology, GeometryBufferUsage usage = GeometryBufferUsage.Static)
            : this(RenderDevice.Backend.CreateGeometryBuffer(topology, usage)) { owner = RenderDevice.Environment; }
        internal VertexBuffer(IGeometryBufferBackend backend) => this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        internal ReadOnlySpan<Vertex> CpuVertices => backend.CpuVertices;
        internal float ForestElapsedYears { get => backend.ForestElapsedYears; set { CheckOwner(); backend.ForestElapsedYears = value; } }
        internal float ForestCurrentYear { get => backend.ForestCurrentYear; set { CheckOwner(); backend.ForestCurrentYear = value; } }
        internal IForestStateBuffer ForestState { get => backend.ForestState; set { CheckOwner(); backend.ForestState = value; } }
        public void SetData(Vertex[] data, bool retainCpuCopy = true)
        { CheckOwner(); backend.SetData(data, retainCpuCopy); vertexBytes = (long)data.Length * Vertex.Stride; }
        public void SetElements(uint[] data)
        { CheckOwner(); backend.SetElements(data); elementBytes = (long)data.Length * sizeof(uint); }
        internal void SetForestGrowth(ForestVertexGrowth[] data)
        { CheckOwner(); backend.SetForestGrowth(data); growthBytes = (long)data.Length * ForestVertexGrowth.Stride; }
        internal IEnumerable<bool> UploadForestPages(List<Vertex> data, List<ForestVertexGrowth> growth)
        {
            CheckOwner();
            // Account for the reserved buffers while their bounded upload is still in progress.
            vertexBytes = (long)data.Count * Vertex.Stride;
            growthBytes = (long)growth.Count * ForestVertexGrowth.Stride;
            using var pages = backend.UploadForestPages(data, growth).GetEnumerator();
            while (true) {
                // MoveNext performs the upload, so ownership must be checked before it.
                CheckOwner();
                if (!pages.MoveNext()) yield break;
                yield return pages.Current;
            }
        }
        public void DrawArray() { CheckOwner(); backend.DrawArray(true); }
        internal void DrawArray(bool useGeometryShader) { CheckOwner(); backend.DrawArray(useGeometryShader); }
        public void DrawElements() { CheckOwner(); backend.DrawElements(); }
        internal void ReadVertices(Vertex[] destination) { CheckOwner(); backend.ReadVertices(destination); }
        private bool disposed;
        public void Dispose() { if (disposed) return; CheckOwner(); backend.Dispose(); vertexBytes = elementBytes = growthBytes = 0; disposed = true; }
        private void CheckOwner() { ObjectDisposedException.ThrowIf(disposed, this); owner?.VerifyAccess(); }
    }
}
