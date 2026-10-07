using System;
using System.IO;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>The original supplied geometry and materials, shared by every pine instance.</summary>
    internal sealed class ImportedPineAsset : IDisposable
    {
        private readonly AnimatedGlbModel model;
        private readonly AnimatedGlbModel.Pose pose;
        private readonly AnimatedModelRenderer renderer;
        private readonly Matrix4 normalization;
        internal readonly float CrownRatio;
        internal readonly int TriangleCount;

        internal ImportedPineAsset()
        {
            model = AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Forest", "pine-tree-original.glb"));
            pose = model.CreatePose(); pose.Evaluate(null, 0);
            var axis = Matrix4.CreateRotationX(MathF.PI / 2);
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            float radius = 0;
            foreach (var mesh in model.Meshes)
            {
                Matrix4 transform = pose.World[mesh.Node] * axis;
                TriangleCount += mesh.Indices.Length / 3;
                for (int i = 0; i < mesh.Vertices.Length; i += 16)
                {
                    var v = mesh.Vertices;
                    Vector3 point = Vector3.TransformPosition(new(v[i], v[i + 1], v[i + 2]), transform);
                    min = Vector3.ComponentMin(min, point); max = Vector3.ComponentMax(max, point);
                    radius = Math.Max(radius, point.Xy.Length);
                }
            }
            float height = max.Z - min.Z;
            if (height <= 0) throw new InvalidDataException("Pine model has no height.");
            CrownRatio = radius / height;
            normalization = axis * Matrix4.CreateTranslation(0, 0, -min.Z) * Matrix4.CreateScale(1 / height);
            renderer = new AnimatedModelRenderer(model);
        }

        internal void Draw(in ForestTree tree, double year, in Terrain.TreeInstance stem, GraphicsSettings settings)
        {
            var size = tree.At(year);
            float height = size.Height * Terrain.TreeMetresToWorld;
            // Uniform scaling preserves every branch, needle and the original crown proportions.
            Matrix4 placement = normalization * Matrix4.CreateScale(height)
                * Matrix4.CreateRotationZ(stem.Yaw) * Matrix4.CreateTranslation(stem.X, stem.Y, stem.BaseZ);
            renderer.Draw(pose, placement, settings, sourceMaterial: true);
        }
        public void Dispose() => renderer.Dispose();
    }
}
