using System;
using OpenTK.Mathematics;
using ForesTycoon.Models.OpenGl;

namespace ForesTycoon.Models
{
    /// <summary>Prepares model submissions; the selected backend owns all GPU work.</summary>
    internal sealed class AnimatedModelRenderer : IDisposable
    {
        private readonly IModelRenderBackend backend;
        private readonly ModelDrawOrder drawOrder;
        private bool disposed;

        // Ownership of an injected backend transfers to this renderer.
        internal AnimatedModelRenderer(AnimatedGlbModel model, IModelRenderBackend backend = null)
        {
            ArgumentNullException.ThrowIfNull(model);
            drawOrder = new ModelDrawOrder(model);
            this.backend = backend ?? new OpenGlModelRenderer(model);
        }

        internal IModelRenderBatch BeginBatch()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return backend.BeginBatch();
        }

        // Adapter for the current scene's camera and shading state. New backends can
        // use the explicit-frame overload without depending on the global RenderDevice.
        internal void Draw(AnimatedGlbModel.Pose pose, Matrix4 transform, IShadingSettings settings,
            float outlineWorldWidth = 0, bool sourceMaterial = false, IModelRenderBatch sharedState = null)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var visuals = RenderDevice.Visuals;
            bool shadow = visuals?.ShadowPass == true;
            var scene = new ModelSceneParameters(shadow ? visuals.ShadowCamera : RenderDevice.ViewProjection,
                visuals?.ShadowCamera ?? Matrix4.Identity, visuals?.Atmosphere ?? Vector4.Zero,
                settings.SunAzimuth, settings.SunElevation, settings.Enhanced && settings.Lighting,
                !shadow && visuals?.ShadowsReady == true, shadow, sourceMaterial);
            var frame = new ModelRenderFrame(scene, settings.Enhanced && settings.Textures,
                !shadow && settings.Enhanced && settings.ModelOutlines ? outlineWorldWidth : 0);
            Draw(pose, transform, frame, sharedState);
        }

        internal void Draw(AnimatedGlbModel.Pose pose, Matrix4 transform, in ModelRenderFrame frame,
            IModelRenderBatch batch = null)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentNullException.ThrowIfNull(pose);
            ReadOnlySpan<int> order = drawOrder.Prepare(pose, transform, frame.Scene.Camera, frame.Scene.Shadow);
            backend.Draw(pose, transform, frame, order, batch);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            backend.Dispose();
        }
    }
}