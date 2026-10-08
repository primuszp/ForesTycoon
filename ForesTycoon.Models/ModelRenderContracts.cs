using System;
using OpenTK.Mathematics;

namespace ForesTycoon.Models
{
    // Backend-independent inputs: no native handles, GL state or shader source.
    internal readonly record struct ModelSceneParameters(Matrix4 Camera, Matrix4 Light, Vector4 Climate,
        float SunAzimuth, float SunElevation, bool Lit, bool Shadowed, bool Shadow, bool SourceMaterial);

    internal readonly record struct ModelRenderFrame(ModelSceneParameters Scene, bool Textures,
        float OutlineWorldWidth);

    /// <summary>A backend-owned state scope shared by consecutive model submissions.</summary>
    internal interface IModelRenderBatch : IDisposable { }

    /// <summary>Owns GPU resources for one asset. Pose data and draw order stay on the CPU.</summary>
    internal interface IModelRenderBackend : IDisposable
    {
        IModelRenderBatch BeginBatch();
        void Draw(AnimatedGlbModel.Pose pose, Matrix4 transform, in ModelRenderFrame frame,
            ReadOnlySpan<int> drawOrder, IModelRenderBatch batch);
    }
}
