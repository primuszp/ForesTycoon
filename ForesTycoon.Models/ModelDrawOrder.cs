using System;
using OpenTK.Mathematics;

namespace ForesTycoon.Models
{
    /// <summary>CPU ordering shared by every backend, with reusable scratch storage.</summary>
    internal sealed class ModelDrawOrder
    {
        private readonly AnimatedGlbModel model;
        private readonly int[] indices;
        private readonly float[] depths;

        internal ModelDrawOrder(AnimatedGlbModel model)
        {
            this.model = model;
            indices = new int[model.Meshes.Length];
            depths = new float[indices.Length];
        }

        // Returned span is valid until the next preparation on this instance.
        internal ReadOnlySpan<int> Prepare(AnimatedGlbModel.Pose pose, Matrix4 transform,
            Matrix4 camera, bool shadow)
        {
            ArgumentNullException.ThrowIfNull(pose);
            if (!ReferenceEquals(pose.Model, model)) throw new ArgumentException("Pose belongs to another model.", nameof(pose));
            int solidCount = 0;
            for (int i = 0; i < model.Meshes.Length; i++)
                if (shadow || model.Meshes[i].Alpha != AnimatedGlbModel.AlphaMode.Blend)
                    indices[solidCount++] = i;
            if (solidCount == indices.Length) return indices;

            Matrix4 instanceCamera = transform * camera;
            int next = solidCount;
            for (int i = 0; i < model.Meshes.Length; i++)
            {
                var mesh = model.Meshes[i];
                if (mesh.Alpha != AnimatedGlbModel.AlphaMode.Blend) continue;
                Vector4 projected = Vector4.TransformRow(new Vector4(mesh.Center, 1),
                    pose.World[mesh.Node] * instanceCamera);
                indices[next] = i;
                depths[next++] = -(MathF.Abs(projected.W) > 0.000001f ? projected.Z / projected.W : projected.Z);
            }
            Array.Sort(depths, indices, solidCount, next - solidCount);
            return indices;
        }
    }
}
