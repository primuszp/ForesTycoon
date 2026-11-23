using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.TreeModels
{
    internal static partial class LobeCrownMesh
    {
        private sealed record Shell(Vector3[] Points, float[] Tints, float[] Openness, int[] Triangles, Vector3 Centre);

        private readonly record struct RayLobe(Vector3 Origin, Vector3 Direction, float InverseBottom, int Index);

        internal static Vertex[] Build(TreeForm form, ForestLod lod)
        {
            var spec = form.Spec;
            var profile = TreeCrownParameters.Resolve(spec.Species, spec.Phase, spec.Dead)
                ?? throw new ArgumentOutOfRangeException(nameof(form));
            float crownHeight = form.Height * form.CrownFraction;
            var sk = form.Skeleton;
            var leaves = new List<Vector3>(sk.Leaves.Length);
            bool veteranPine = spec.Species == ForestSpecies.Pine && spec.Phase == TreeLifePhase.Old && profile.Gaps > 0;
            if (veteranPine)
            {
                // Old pine retains its needles on the external shoots. Seed the foliage
                // masses on the actual distal wood, leaving the crooks of the big limbs bare.
                var hasChildren = new bool[sk.StemCount];
                foreach (var stem in sk.Stems)
                    if (stem.Parent >= 0) hasChildren[stem.Parent] = true;
                for (int i = 0; i < sk.StemCount; i++)
                {
                    if (hasChildren[i] || sk.Stems[i].Level == 0 || form.Dead[i]) continue;
                    var twig = sk.PointsOf(i);
                    AddLeaf(twig[^1]);
                    AddLeaf(Vector3.Lerp(twig[^2], twig[^1], .4f));
                }
            }
            if (!veteranPine || leaves.Count == 0)
                for (int i = 0; i < sk.Leaves.Length; i++)
                    if (!form.Dead[sk.LeafStem[i]]) AddLeaf(sk.Leaves[i]);
            if (leaves.Count == 0) foreach (var leaf in sk.Leaves) AddLeaf(leaf);
            void AddLeaf(Vector3 leaf)
            {
                // Cluster once in tree-local coordinates; rotating the input first can change
                // near-equal assignments and principal-axis signs through rounding.
                var p = new Vector3(leaf.X * form.XyScale, leaf.Y * form.XyScale, leaf.Z * form.ZScale);
                leaves.Add(new(p.X / form.CrownRadius, p.Y / form.CrownRadius,
                    Math.Clamp((p.Z - form.CrownBase) / crownHeight, 0, 1)));
            }
            int requested = spec.Phase >= TreeLifePhase.Old ? profile.Old : spec.Phase == TreeLifePhase.Mature ? profile.Mature : profile.Young;
            float aspect = form.CrownRadius / crownHeight;
            var lobes = Cluster(leaves, requested, profile, spec.Seed, aspect, weightByTips: veteranPine);
            float c = MathF.Cos(spec.Yaw), s = MathF.Sin(spec.Yaw);
            Vector2 Turn(Vector2 p) => new(c * p.X - s * p.Y, s * p.X + c * p.Y);
            for (int i = 0; i < lobes.Count; i++)
            {
                var lobe = lobes[i]; var centre = Turn(lobe.Centre.Xy);
                lobes[i] = lobe with { Centre = new(centre.X, centre.Y, lobe.Centre.Z), Axis = Turn(lobe.Axis) };
            }
            int count = Lattice(form.CrownRadius, lod, form.Shrub) * profile.Detail;
            bool pads = profile.Gaps > 0;
            bool commonSurface = !pads && !form.Shrub && lod != ForestLod.Far;
            int commonSamples = commonSurface ? Lattice(form.CrownRadius, ForestLod.Medium) * profile.Detail : 0;
            var shells = MakeShells(lod, count);
            // A sparse lattice must not rescale the distances between branch pads. Derive one
            // reference envelope from the near surface, then use it for every viewing distance.
            var reference = pads && !veteranPine && lod != ForestLod.Near
                ? MakeShells(ForestLod.Near, Lattice(form.CrownRadius, ForestLod.Near, form.Shrub) * profile.Detail) : shells;
            float minZ = float.MaxValue, maxZ = float.MinValue, maxR = 0;
            foreach (var shell in reference) foreach (var p in EnvelopeSamples(shell))
                maxR = Math.Max(maxR, p.Xy.Length);
            foreach (var shell in shells) foreach (var p in EnvelopeSamples(shell))
            { minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z); }
            // Do not lift every pad to fill an empty lower band. That would pull the upper
            // foliage away from its real woody support when the leaf cloud sits high.
            if (pads) minZ = Math.Min(0, minZ);

            List<Shell> MakeShells(ForestLod detail, int samples)
            {
                var result = new List<Shell>();
                if (pads)
                {
                    var allocation = PadSamples(lobes, samples, detail);
                    for (int i = 0; i < lobes.Count; i++)
                        result.Add(Sample(PadLobes(lobes[i], spec.Seed, i, spec.Yaw), aspect, spec.Yaw, allocation[i], refinements: 6));
                }
                else result.Add(Sample(lobes, aspect, spec.Yaw, samples, anchors: commonSamples < samples ? commonSamples : 0));
                for (int i = 0; i < result.Count; i++)
                    Shape(result[i], profile, spec.Yaw, spec.Seed + i * 2053, aspect, pads, detail, commonSamples);
                return result;
            }
            ReadOnlySpan<Vector3> EnvelopeSamples(Shell shell) =>
                commonSurface ? shell.Points.AsSpan(0, commonSamples) : shell.Points;

            float thin = .72f + .28f * form.Foliage;
            float xy = form.CrownRadius * thin / (veteranPine ? 1 : maxR) * (pads || commonSurface ? 1 : Coverage(count));
            float topLoss = form.Dieback > .45f && spec.Phase >= TreeLifePhase.Old
                ? Math.Min(.25f, (form.Dieback - .3f) * .5f) : 0;
            int vertexCount = 0;
            foreach (var shell in shells) vertexCount += shell.Triangles.Length;
            var result = new Vertex[vertexCount];
            int offset = 0;
            foreach (var shell in shells)
            {
                var points = shell.Points;
                var shades = new float[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    float t = Math.Clamp((points[i].Z - minZ) / (maxZ - minZ), 0, 1);
                    float exposure = Math.Clamp(points[i].Xy.Length / maxR, 0, 1);
                    float sideLight = profile.Roundness > 0 && !form.Bands.Space.IsUnspecified
                        ? form.SideExposure(MathF.Atan2(points[i].Y, points[i].X)) : form.Bands.Light01;
                    float skirt = profile.Roundness > 0 ? 1 - .18f * (1 - sideLight) * Math.Clamp((.55f - t) / .55f, 0, 1) : 1;
                    shades[i] = profile.Roundness > 0
                        ? .68f + .27f * t + .13f * sideLight * exposure + shell.Tints[i] * .4f - .06f * (1 - sideLight) * (1 - t)
                        : .72f + .20f * t + .12f * exposure + shell.Tints[i];
                    shades[i] -= .14f * (1 - shell.Openness[i]) * (1 - profile.Roundness);
                    var footprint = points[i].Xy;
                    // Extra samples refine the surface inside the same crown-radius envelope.
                    if (commonSurface && footprint.LengthSquared > maxR * maxR)
                        footprint *= maxR / footprint.Length;
                    points[i] = form.ShapePineCrown(form.Warp.Apply(new(footprint.X * xy * skirt, footprint.Y * xy * skirt,
                        form.CrownBase + t * crownHeight * (1 - topLoss))));
                }
                Emit(points, shell.Triangles, shades, form, profile, result.AsSpan(offset, shell.Triangles.Length));
                offset += shell.Triangles.Length;
            }
            return result;
        }

        // A branch-end pad is itself a small group of shoots. Reuse the same implicit surface,
        // instead of drawing smooth, identical ellipsoids or adding a second crown renderer.
        private static List<Lobe> PadLobes(Lobe pad, int seed, int index, float yaw)
        {
            var major = pad.Axis == Vector2.Zero ? new Vector2(MathF.Cos(yaw), MathF.Sin(yaw)) : pad.Axis;
            // An eigenvector has no sign. Orient it towards its own branch so the unequal shoots
            // do not swap ends when the covariance rotates through a half-turn.
            if (Vector2.Dot(major, pad.Centre.Xy) < 0) major = -major;
            var side = new Vector2(-major.Y, major.X);
            var lobes = new List<Lobe> { pad with { Radius = pad.Radius * .75f } };
            for (int i = 0; i < 3; i++)
            {
                float sign = i == 0 ? -1 : 1;
                float turn = ForestTreeVariation.Range(seed, 17100 + index * 7 + i, -.5f, .5f);
                var offset = major * ((i == 2 ? .15f : sign * .65f) * pad.Radius.X) + side * ((i == 2 ? .65f : turn) * pad.Radius.Y);
                var centre = pad.Centre + new Vector3(offset.X, offset.Y,
                    pad.Radius.Z * ForestTreeVariation.Range(seed, 17300 + index * 7 + i, -.15f, .25f));
                float scale = ForestTreeVariation.Range(seed, 17500 + index * 7 + i, .45f, .65f);
                lobes.Add(new(centre, pad.Radius * scale, pad.Bottom,
                    pad.Tint + (i == 0 ? -.025f : .025f), pad.Axis));
            }
            return lobes;
        }

        // Every pad retains a closed low-poly shell even at long range. Spend the near crown's
        // existing triangle budget in proportion to pad area; do not discard pads in coarse LODs.
        private static int[] PadSamples(IReadOnlyList<Lobe> lobes, int count, ForestLod lod)
        {
            int minimum = lobes.Count > 14
                ? lod == ForestLod.Near ? 12 : lod == ForestLod.Medium ? 24 : 16
                : lod == ForestLod.Far ? 20 : 24;
            int total = Math.Max(count + 2 * (lobes.Count - 1), minimum * lobes.Count);
            var allocation = new int[lobes.Count];
            var weights = new float[lobes.Count]; float sum = 0;
            for (int i = 0; i < lobes.Count; i++)
            {
                var r = lobes[i].Radius;
                weights[i] = MathF.Pow(r.X * r.Y * r.Z, 2f / 3); sum += weights[i];
            }
            int extra = total - minimum * lobes.Count, used = 0;
            for (int i = 0; i < allocation.Length; i++)
            { allocation[i] = minimum + (int)(extra * weights[i] / sum); used += allocation[i]; }
            for (int i = 0; used < total; i = (i + 1) % allocation.Length, used++) allocation[i]++;
            return allocation;
        }

        private static Shell Sample(List<Lobe> lobes, float aspect, float yaw, int count, int refinements = 10, int anchors = 0)
        {
            var (directions, triangles) = Sphere(count, anchors);
            // Work in an isotropic frame (z in crown radii), so the metaballs blend like round masses.
            var iso = new Vector3(1, 1, 1 / aspect);
            var centres = new Vector3[lobes.Count]; var inverseRadii = new Vector3[lobes.Count];
            Vector3 middle = Vector3.Zero; float weight = 0;
            for (int l = 0; l < lobes.Count; l++)
            {
                centres[l] = lobes[l].Centre * iso;
                var radius = lobes[l].Radius * iso; inverseRadii[l] = Vector3.One / radius;
                float w = radius.X * radius.Y * radius.Z; middle += centres[l] * w; weight += w;
            }
            middle /= weight;
            // All samples on a ray share the same affine transform into each ellipsoid.
            var origins = new Vector3[lobes.Count];
            for (int l = 0; l < lobes.Count; l++)
                origins[l] = lobes[l].Local(middle - centres[l]) * inverseRadii[l];
            var hits = new RayLobe[lobes.Count];
            var stretch = new Vector3(1, 1, 0.5f / aspect);
            float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
            var points = new Vector3[count]; var tints = new float[count]; var openness = new float[count];
            for (int i = 0; i < count; i++)
            {
                // The lattice turns with the tree, so its facets differ from tree to tree and the crown
                // rotates exactly with its skeleton.
                var d = directions[i];
                d = new(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos, d.Z);
                var ray = d * stretch;
                float t = Surface(ray, out int owner, out openness[i]);
                var q = middle + ray * t;
                points[i] = new(q.X, q.Y, q.Z * aspect);
                tints[i] = lobes[owner].Tint;
            }

            return new(points, tints, openness, triangles, new(middle.X, middle.Y, middle.Z * aspect));

            // Outermost crossing of the metaball iso-surface along a ray from the crown's centre.
            float Surface(Vector3 ray, out int owner, out float openness)
            {
                const float Step = 0.06f;
                float far = 0;
                int hitCount = 0;
                for (int l = 0; l < lobes.Count; l++)
                {
                    var direction = lobes[l].Local(ray) * inverseRadii[l];
                    float exit = SupportExit(origins[l], direction, lobes[l].Bottom);
                    if (exit <= 0) continue;
                    hits[hitCount++] = new(origins[l], direction, 1 / lobes[l].Bottom, l);
                    far = Math.Max(far, exit);
                }
                float t = far;
                while (t > 0 && Field(t, hitCount, out _, out _) < Threshold) t -= Step;
                if (t <= 0) { Field(0, hitCount, out owner, out openness); return .05f; }
                float inside = t, outside = Math.Min(far, t + Step);
                // Six bisections on a small shoot pad bound the radial interval to .06/64.
                for (int k = 0; k < refinements; k++)
                {
                    float m = (inside + outside) * .5f;
                    if (Field(m, hitCount, out _, out _) >= Threshold) inside = m; else outside = m;
                }
                Field(inside, hitCount, out owner, out openness);
                return inside;
            }

            float Field(float t, int hitCount, out int strongest, out float openness)
            {
                float sum = 0, best = 0; strongest = 0;
                for (int i = 0; i < hitCount; i++)
                {
                    ref readonly var hit = ref hits[i];
                    var q = hit.Origin + hit.Direction * t;
                    if (q.Z < 0) q.Z *= hit.InverseBottom;
                    float r2 = q.LengthSquared / (Influence * Influence);
                    if (r2 >= 1) continue;
                    float f = (1 - r2) * (1 - r2);
                    sum += f;
                    if (f > best) { best = f; strongest = hit.Index; }
                }
                openness = best / Math.Max(sum, 1e-9f);
                return sum;
            }
        }

        private static void Shape(Shell shell, Profile profile, float yaw, int seed, float aspect, bool pads,
            ForestLod lod, int commonSamples)
        {
            var points = shell.Points; int count = points.Length;
            var (directions, _) = Sphere(count, commonSamples < count ? commonSamples : 0);
            var centre = pads ? shell.Centre.Xy : Vector2.Zero;
            var envelope = points.AsSpan(0, commonSamples == 0 ? count : commonSamples);
            float minZ = float.MaxValue, maxZ = float.MinValue, maxR = 0;
            foreach (var p in envelope)
            { minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z); maxR = Math.Max(maxR, (p.Xy - centre).Length); }
            float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw), depth = maxZ - minZ;
            if (profile.Roundness > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    var d = directions[i];
                    d = new(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos, d.Z);
                    var p = points[i];
                    var q = new Vector3((p.X - centre.X) / maxR, (p.Y - centre.Y) / maxR, (p.Z - minZ) / depth);
                    q = Vector3.Lerp(q, new(d.X, d.Y, .5f * (d.Z + 1)), profile.Roundness);
                    points[i] = new(centre.X + q.X * maxR, centre.Y + q.Y * maxR, minZ + q.Z * depth);
                }
                minZ = float.MaxValue; maxZ = float.MinValue;
                foreach (var p in envelope)
                { minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z); }
            }
            var detailCentre = new Vector3(centre.X, centre.Y, (minZ + maxZ) * .5f);
            for (int i = 0; i < count; i++)
            {
                float relief = CrownSurfaceDetail.Factor(directions[i], seed, profile.Jitter);
                points[i] = detailCentre + (points[i] - detailCentre) * relief;
                // Reuse the tree-local shoot relief as a baked light cue across all LODs.
                shell.Tints[i] += 1.2f * (relief - 1);
            }
            if (pads)
            {
                // Compensate sparse facets locally, leaving branch-pad centres in the same place.
                float coverage = lod == ForestLod.Far ? 1.16f + 1.3f * Math.Max(0, .56f - profile.Flatten)
                    : lod == ForestLod.Medium ? 1.04f : 1;
                // Few polar samples understate the thickness of a flat branch-end pad.
                float thickness = lod == ForestLod.Far ? 1 + Math.Max(0, .60f - profile.Flatten) : 1;
                for (int i = 0; i < count; i++) points[i] = new(centre.X + (points[i].X - centre.X) * coverage,
                    centre.Y + (points[i].Y - centre.Y) * coverage, detailCentre.Z + (points[i].Z - detailCentre.Z) * thickness);
                return;
            }
            if (count >= 24) return;
            // The same shoulder correction applies to a flat joined crown or to a sparse pad.
            minZ = float.MaxValue; maxZ = float.MinValue; maxR = 0;
            var local = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                local[i] = points[i] - new Vector3(centre.X, centre.Y, 0);
                minZ = Math.Min(minZ, local[i].Z); maxZ = Math.Max(maxZ, local[i].Z); maxR = Math.Max(maxR, local[i].Xy.Length);
            }
            float shoulders = Math.Clamp(2 * aspect - 1, 0, 2);
            if (shoulders <= 0) return;
            var footprint = CrownContour.Hull(local);
            for (int i = 0; i < count; i++)
            {
                float t = Math.Clamp((local[i].Z - minZ) / (maxZ - minZ), 0, 1);
                float exposure = Math.Clamp(local[i].Xy.Length / maxR, 0, 1);
                float contour = Math.Min(1 + shoulders * Math.Abs(2 * t - 1), 1 / Math.Max(exposure, 1e-6f));
                contour = CrownContour.Limit(footprint, local[i].Xy, contour);
                float outlineT = .5f + .5f * MathF.CopySign(MathF.Pow(Math.Abs(2 * t - 1), 1 / (1 + .5f * shoulders)), 2 * t - 1);
                points[i] = new(centre.X + local[i].X * contour, centre.Y + local[i].Y * contour,
                    minZ + outlineT * (maxZ - minZ));
            }
        }

        private static void Emit(Vector3[] points, int[] triangles, float[] shades, TreeForm form, Profile profile, Span<Vertex> result)
        {
            int count = points.Length;
            var normals = new Vector3[count];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector3 n = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                normals[a] += n; normals[b] += n; normals[c] += n;
            }
            var colors = new uint[count];
            for (int i = 0; i < count; i++)
            {
                normals[i] = normals[i].Normalized();
                colors[i] = Tint(form.CrownColor, shades[i] + .09f * normals[i].Z);
            }

            float smooth = profile.Roundness > 0 ? .55f + .35f * profile.Roundness : .8f;
            for (int i = 0; i < result.Length; i += 3)
            {
                Vector3 face = Vector3.Cross(points[triangles[i + 1]] - points[triangles[i]], points[triangles[i + 2]] - points[triangles[i]]).Normalized();
                for (int j = 0; j < 3; j++)
                {
                    int v = triangles[i + j];
                    // Mostly smooth shading: a rounded mass, with only a hint of its facets.
                    var normal = (smooth * normals[v] + (1 - smooth) * face).Normalized();
                    if (Vector3.Dot(normal, face) < 0.15f) normal = face;
                    result[i + j] = new(points[v], normal, colors[v]);
                }
            }
        }
    }
}
