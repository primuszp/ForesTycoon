using System;
using System.Collections.Generic;
namespace ForesTycoon.Rendering
{
    public sealed class VertexBuffer : IDisposable
    {
        private readonly IGeometryBufferBackend backend;
        public VertexBuffer(PrimitiveTopology topology, GeometryBufferUsage usage = GeometryBufferUsage.Static)
            : this(RenderDevice.Backend.CreateGeometryBuffer(topology, usage)) { }
        internal VertexBuffer(IGeometryBufferBackend backend) => this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        internal ReadOnlySpan<Vertex> CpuVertices => backend.CpuVertices;
        internal float ForestElapsedYears { get => backend.ForestElapsedYears; set => backend.ForestElapsedYears = value; }
        internal float ForestCurrentYear { get => backend.ForestCurrentYear; set => backend.ForestCurrentYear = value; }
        internal IForestStateBuffer ForestState { get => backend.ForestState; set => backend.ForestState = value; }
        public void SetData(Vertex[] data, bool retainCpuCopy = true) => backend.SetData(data, retainCpuCopy);
        public void SetElements(uint[] data) => backend.SetElements(data);
        internal void SetForestGrowth(ForestVertexGrowth[] data) => backend.SetForestGrowth(data);
        internal IEnumerable<bool> UploadForestPages(List<Vertex> data, List<ForestVertexGrowth> growth) => backend.UploadForestPages(data, growth);
        public void DrawArray() => backend.DrawArray(true);
        internal void DrawArray(bool useGeometryShader) => backend.DrawArray(useGeometryShader);
        public void DrawElements() => backend.DrawElements();
        internal void ReadVertices(Vertex[] destination) => backend.ReadVertices(destination);
        public void Dispose() => backend.Dispose();
    }
}
