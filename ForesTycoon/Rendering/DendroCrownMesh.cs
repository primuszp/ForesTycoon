using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    internal static class DendroCrownMesh
    {
        // A radial envelope avoids intersecting/disconnected leaf cards and lobe shells.
        // Leaf positions supply directional bulges to a single closed manifold grid.
        // This regularized envelope preserves the species silhouette without tiny leaf detail.
        internal static Vertex[] Build(ForestSpecies species, int seed, TreeLifeStage stage,
            float height, float radius, float fraction, float yaw, IReadOnlyList<Vector3> leaves,
            uint color, ForestLod lod)
        {
            int sides = lod == ForestLod.Near ? 10 : lod == ForestLod.Medium ? 8 : 5;
            int rings = lod == ForestLod.Near ? 7 : lod == ForestLod.Medium ? 5 : 3;
            float bottom = height * (1 - fraction), crownHeight = height * fraction;
            var points = new Vector3[2 + (rings - 1) * sides];
            var weightedLeaves = new Vector2[leaves.Count];
            points[0] = new(0, 0, bottom); points[^1] = new(0, 0, height);
            float maxRadius = 0;
            for (int ring = 1; ring < rings; ring++)
            {
                float t = ring / (float)rings;
                float envelope = species == ForestSpecies.Spruce
                    ? MathF.Pow(1 - t, 0.85f) * MathF.Sin(MathF.PI * 0.5f * Math.Min(1, t * 4))
                    : MathF.Pow(MathF.Sin(MathF.PI * t), species == ForestSpecies.Oak ? 0.48f : 0.72f);
                if (species == ForestSpecies.Birch) envelope *= 1.10f - 0.32f * t;
                if (species == ForestSpecies.Beech) envelope *= 0.78f + 0.30f * t;
                if (species == ForestSpecies.Spruce && lod != ForestLod.Far)
                    envelope *= 0.96f - 0.12f * MathF.Cos(t * MathF.PI * 7);
                // The axial weight is independent of azimuth. Evaluate its exponential
                // once per leaf/ring, rather than once for every grid vertex.
                for (int sample = 0; sample < leaves.Count; sample++)
                {
                    var leaf = leaves[sample];
                    float dz = (leaf.Z - bottom) / crownHeight - t;
                    weightedLeaves[sample] = leaf.Xy * MathF.Exp(-dz * dz * 28);
                }
                for (int side = 0; side < sides; side++)
                {
                    float angle = yaw + MathF.Tau * side / sides;
                    Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                    float support = 0;
                    foreach (var leaf in weightedLeaves)
                    {
                        support = Math.Max(support, Vector2.Dot(leaf, direction));
                    }
                    float bulge = 1 + ForestTreeVariation.Range(seed, 711, 0.10f, 0.22f)
                        * MathF.Sin(angle * (stage == TreeLifeStage.Old ? 3 : 5) + t * 8 + seed % 17);
                    float r = envelope * (radius * 0.65f + support * 0.45f) * bulge;
                    maxRadius = Math.Max(maxRadius, r);
                    points[1 + (ring - 1) * sides + side] = new(direction.X * r, direction.Y * r, bottom + t * crownHeight);
                }
            }
            float width = radius / maxRadius;
            for (int i = 1; i < points.Length - 1; i++) { points[i].X *= width; points[i].Y *= width; }
            var indices = new List<int>(); var normals = new Vector3[points.Length];
            for (int side = 0; side < sides; side++) Triangle(0, At(1, side + 1), At(1, side));
            for (int ring = 1; ring < rings - 1; ring++)
                for (int side = 0; side < sides; side++)
                {
                    Triangle(At(ring, side), At(ring, side + 1), At(ring + 1, side + 1));
                    Triangle(At(ring, side), At(ring + 1, side + 1), At(ring + 1, side));
                }
            for (int side = 0; side < sides; side++) Triangle(At(rings - 1, side), At(rings - 1, side + 1), points.Length - 1);
            for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].Normalized();
            var result = new Vertex[indices.Count];
            for (int i = 0; i < result.Length; i += 3)
            {
                Vector3 face = Vector3.Cross(points[indices[i + 1]] - points[indices[i]], points[indices[i + 2]] - points[indices[i]]).Normalized();
                for (int j = 0; j < 3; j++)
                    result[i + j] = new(points[indices[i + j]], (normals[indices[i + j]] * 0.65f + face * 0.35f).Normalized(), color);
            }
            return result;

            int At(int ring, int side) => 1 + (ring - 1) * sides + side % sides;
            void Triangle(int a, int b, int c)
            {
                indices.Add(a); indices.Add(b); indices.Add(c);
                Vector3 n = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                normals[a] += n; normals[b] += n; normals[c] += n;
            }
        }
    }
}
