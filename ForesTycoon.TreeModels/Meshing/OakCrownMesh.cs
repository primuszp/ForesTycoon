using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.TreeModels
{
    /// <summary>
    /// Overlapping, closed foliage masses from live leaf clusters of the skeleton.
    /// Near LOD retains their concave outline; coarser LODs sample an outer radial
    /// envelope. Clusters are deterministic and independent of tessellation.
    /// This preserves upper-crown valleys which a radius-per-height profile hides.
    /// </summary>
    internal static class OakCrownMesh
    {
        private readonly record struct Lobe(Vector3 Centre, Vector3 Radius);

        internal static Vertex[] Build(TreeForm form, ForestLod lod)
        {
            float crownHeight = form.Height * form.CrownFraction;
            var leaves = new List<Vector3>();
            var sk = form.Skeleton;
            for (int i = 0; i < sk.Leaves.Length; i++)
                if (!form.Dead[sk.LeafStem[i]]) AddLeaf(sk.Leaves[i]);
            if (leaves.Count == 0) foreach (var leaf in sk.Leaves) AddLeaf(leaf);
            void AddLeaf(Vector3 leaf)
            {
                var p = form.ToFrame(leaf);
                leaves.Add(new(p.X / form.CrownRadius, p.Y / form.CrownRadius,
                    Math.Clamp((p.Z - form.CrownBase) / crownHeight, 0, 1)));
            }
            var lobes = Cluster(leaves, form.Spec.Phase >= TreeLifePhase.Old ? 8 : 7);
            Vector3 origin = new(0, 0, 0.52f);
            int sides = DendroCrownMesh.Sides(form.CrownRadius, lod, lod == ForestLod.Far ? 5 : 6,
                lod == ForestLod.Near ? 12 : lod == ForestLod.Medium ? 9 : 5);
            int rings = lod == ForestLod.Near ? 6 : lod == ForestLod.Medium ? 4 : 3;
            Vector3[] points;
            var indices = new List<int>();
            if (lod == ForestLod.Near && sides >= 8)
            {
                // The close model retains individual, overlapping closed foliage masses.
                // Twenty triangles per mass reveal the valleys which a single shell hides.
                var all = new List<Vector3>();
                float golden = (1 + MathF.Sqrt(5)) / 2;
                Vector3[] ico = { new(-1,golden,0), new(1,golden,0), new(-1,-golden,0), new(1,-golden,0),
                    new(0,-1,golden), new(0,1,golden), new(0,-1,-golden), new(0,1,-golden),
                    new(golden,0,-1), new(golden,0,1), new(-golden,0,-1), new(-golden,0,1) };
                int[] faces = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2,
                    10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
                float cos = MathF.Cos(form.Spec.Yaw), sin = MathF.Sin(form.Spec.Yaw);
                float tiltCos = MathF.Cos(0.37f), tiltSin = MathF.Sin(0.37f);
                foreach (var lobe in lobes)
                {
                    int offset = all.Count;
                    float turn = offset * 0.137f;
                    float ct = MathF.Cos(turn), st = MathF.Sin(turn);
                    foreach (var vertex in ico)
                    {
                        Vector3 d = vertex.Normalized();
                        // Different orientations avoid repeated horizontal polygon edges.
                        d = new(d.X, d.Y * tiltCos - d.Z * tiltSin, d.Y * tiltSin + d.Z * tiltCos);
                        d = new(d.X * ct - d.Y * st, d.X * st + d.Y * ct, d.Z);
                        d = new(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos, d.Z);
                        all.Add(lobe.Centre + d * lobe.Radius);
                    }
                    foreach (int index in faces) indices.Add(offset + index);
                }
                points = all.ToArray();
            }
            else
            {
                points = new Vector3[2 + sides * rings];
                points[0] = Surface(-Vector3.UnitZ); points[^1] = Surface(Vector3.UnitZ);
                for (int ring = 0; ring < rings; ring++)
                {
                    float latitude = -MathF.PI / 2 + MathF.PI * (ring + 1) / (rings + 1);
                    for (int side = 0; side < sides; side++)
                    {
                        float angle = form.Spec.Yaw + MathF.Tau * side / sides;
                        points[At(ring, side)] = Surface(new(MathF.Cos(latitude) * MathF.Cos(angle),
                            MathF.Cos(latitude) * MathF.Sin(angle), MathF.Sin(latitude)));
                    }
                }
                for (int side = 0; side < sides; side++) Face(0, At(0, side + 1), At(0, side));
                for (int ring = 0; ring < rings - 1; ring++)
                for (int side = 0; side < sides; side++)
                {
                    int a = At(ring, side), b = At(ring, side + 1), c = At(ring + 1, side + 1), d = At(ring + 1, side);
                    if ((points[a] - points[c]).LengthSquared <= (points[b] - points[d]).LengthSquared)
                    { Face(a, b, c); Face(a, c, d); }
                    else { Face(a, b, d); Face(b, c, d); }
                }
                for (int side = 0; side < sides; side++) Face(At(rings - 1, side), At(rings - 1, side + 1), points.Length - 1);
            }
            float minZ = float.MaxValue, maxZ = float.MinValue, maxR = 0;
            foreach (var p in points) { minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z); maxR = Math.Max(maxR, p.Xy.Length); }
            float thin = 0.72f + 0.28f * form.Foliage;
            float topLoss = form.Dieback > 0.45f && form.Spec.Phase >= TreeLifePhase.Old
                ? Math.Min(0.25f, (form.Dieback - 0.3f) * 0.5f) : 0;
            float xy = form.CrownRadius * thin / maxR;
            // Compensate the coarser inscribed outline, without adding lobe geometry.
            xy /= MathF.Sqrt(sides * MathF.Sin(MathF.PI / sides) / MathF.PI);
            if (lod == ForestLod.Far) xy *= 1.05f;
            var colors = new uint[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                float t = (points[i].Z - minZ) / (maxZ - minZ);
                float exposure = Math.Clamp(points[i].Xy.Length / maxR, 0, 1);
                colors[i] = Tint(form.CrownColor, 0.72f + 0.20f * t + 0.12f * exposure);
                points[i] = form.Warp.Apply(new(points[i].X * xy, points[i].Y * xy,
                    form.CrownBase + t * crownHeight * (1 - topLoss)));
            }
            var normals = new Vector3[points.Length];
            for (int i = 0; i < indices.Count; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                Vector3 n = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                normals[a] += n; normals[b] += n; normals[c] += n;
            }
            for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].Normalized();
            var result = new Vertex[indices.Count];
            for (int i = 0; i < result.Length; i += 3)
            {
                Vector3 face = Vector3.Cross(points[indices[i + 1]] - points[indices[i]], points[indices[i + 2]] - points[indices[i]]).Normalized();
                for (int j = 0; j < 3; j++)
                {
                    int v = indices[i + j];
                    var normal = (0.92f * normals[v] + 0.08f * face).Normalized();
                    if (Vector3.Dot(normal, face) < 0.15f) normal = face;
                    result[i + j] = new(points[v], normal, colors[v]);
                }
            }
            return result;

            Vector3 Surface(Vector3 direction)
            {
                float reach = 0;
                foreach (var lobe in lobes)
                {
                    Vector3 o = (origin - lobe.Centre) / lobe.Radius, d = direction / lobe.Radius;
                    float a = d.LengthSquared, b = Vector3.Dot(o, d), c = o.LengthSquared - 1;
                    float discriminant = b * b - a * c;
                    if (discriminant >= 0) reach = Math.Max(reach, (-b + MathF.Sqrt(discriminant)) / a);
                }
                return origin + direction * reach;
            }
            int At(int ring, int side) => 1 + ring * sides + side % sides;
            void Face(int a, int b, int c)
            {
                indices.Add(a); indices.Add(b); indices.Add(c);
            }
        }

        // Farthest-point seeds + a fixed Lloyd iteration count: deterministic spatial
        // clusters, independent of mesh LOD and with no extra random stream.
        private static List<Lobe> Cluster(List<Vector3> leaves, int requested)
        {
            var lobes = new List<Lobe> { new(new(0, 0, 0.52f), new(0.43f, 0.43f, 0.43f * 0.64f)) };
            if (leaves.Count == 0) return lobes;
            int count = Math.Min(requested, leaves.Count);
            var centres = new Vector3[count]; centres[0] = leaves[0];
            for (int k = 1; k < count; k++)
            {
                float best = -1;
                foreach (var leaf in leaves)
                {
                    float nearest = float.MaxValue;
                    for (int j = 0; j < k; j++) nearest = Math.Min(nearest, (leaf - centres[j]).LengthSquared);
                    if (nearest > best) { best = nearest; centres[k] = leaf; }
                }
            }
            var assignment = new int[leaves.Count]; var totals = new int[count];
            for (int iteration = 0; iteration < 6; iteration++)
            {
                var sums = new Vector3[count]; Array.Clear(totals);
                for (int i = 0; i < leaves.Count; i++)
                {
                    float nearest = float.MaxValue; int winner = 0;
                    for (int k = 0; k < count; k++)
                    {
                        float distance = (leaves[i] - centres[k]).LengthSquared;
                        if (distance < nearest) { nearest = distance; winner = k; }
                    }
                    assignment[i] = winner; sums[winner] += leaves[i]; totals[winner]++;
                }
                for (int k = 0; k < count; k++) if (totals[k] > 0) centres[k] = sums[k] / totals[k];
            }
            for (int k = 0; k < count; k++)
            {
                if (totals[k] == 0) continue;
                var centre = centres[k];
                centre.Z = Math.Clamp(centre.Z, 0.26f, 0.82f);
                Vector3 fromCore = (centre - new Vector3(0, 0, 0.52f)) / new Vector3(1, 1, 0.64f);
                // Even the inscribed icosahedra overlap the central foliage mass.
                // This avoids floating green balls while keeping visible lobe valleys.
                if (fromCore.Length > 0.56f) fromCore *= 0.56f / fromCore.Length;
                centre = new Vector3(0, 0, 0.52f) + fromCore * new Vector3(1, 1, 0.64f);
                float spread = 0;
                for (int i = 0; i < leaves.Count; i++) if (assignment[i] == k) spread += (leaves[i] - centres[k]).LengthSquared;
                float radius = Math.Clamp(0.28f + MathF.Sqrt(spread / totals[k]), 0.34f, 0.52f);
                lobes.Add(new(centre, new(radius, radius, radius * 0.64f)));
            }
            return lobes;
        }

        private static uint Tint(uint color, float shade)
        {
            uint C(int shift) => (uint)Math.Clamp((int)MathF.Round(((color >> shift) & 255) * shade), 0, 255);
            return color & 0xff000000 | C(0) | C(8) << 8 | C(16) << 16;
        }
    }
}
