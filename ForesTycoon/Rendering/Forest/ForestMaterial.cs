using System;
namespace ForesTycoon
{
    internal sealed class ForestMaterial : IDisposable
    {
        private readonly IForestMaterialBackend backend;
        internal ForestMaterial(IForestMaterialBackend backend = null) => this.backend = backend ?? SceneRenderBackends.Current.CreateForestMaterial();
        internal void Use(float outlineWorldWidth = 0) => backend.Use(outlineWorldWidth);
        public void Dispose() => backend.Dispose();
    }
}
