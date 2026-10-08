using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.TreeModels
{
    /// <summary>
    /// Overlapping, closed foliage masses ("lobes") from live leaf clusters of the skeleton, for every
    /// broad-leaved growth form. Near LOD keeps individual faceted masses with valleys and sky gaps
    /// between them; coarser LODs sample the outer envelope of the same masses, so the silhouette is
    /// LOD-stable. Clusters are deterministic and independent of tessellation.
    /// See docs/tree-realism-plan.md (Livny et al. 2011 texture-lobes, low-poly variant).
    /// </summary>
    internal static class LobeCrownMesh
    {
        /// <summary>Per growth form shape of the foliage masses. Radii are in crown radii.</summary>
        /// <param name="Young">Leaf clusters besides the central spine (young, mature, old).</param>
        /// <param name="CoreRadius">Horizontal radius of the central spine, which spans the crown height
        /// so that every outer mass overlaps it (no floating foliage).</param>
        /// <param name="Flatten">Vertical / horizontal radius of a mass in world space.</param>
        /// <param name="Bottom">Scale of the lower half: below 1 a flat cloud base, above 1 a hanging fringe.</param>
        /// <param name="MinZ">Lowest and highest allowed mass centre, relative crown height.</param>
        /// <param name="Reach">How far a mass centre may sit from the stem axis.</param>
        /// <param name="Jitter">Radial vertex noise of the faceted masses.</param>
        /// <param name="Layers">Above 1, mass centres are pulled towards this many horizontal tiers (beech sprays).</param>
        /// <param name="Vase">Above 0, higher masses grow larger and reach further out (an opening vase).</param>
        internal readonly record struct Profile(int Young, int Mature, int Old, float CoreRadius,
            float MinRadius, float MaxRadius, float Flatten, float Bottom, float MinZ, float MaxZ, float Reach,
            float Jitter, int Layers = 0, float Vase = 0);

        private readonly record struct Lobe(Vector3 Centre, Vector3 Radius, float Bottom, float Tint);

        internal static Profile? For(CrownForm form) => form switch
        {
            // Open-grown oaks: few, large, irregular masses, flat cloud base, rough outline.
            CrownForm.Oak => new(5, 7, 8, 0.26f, 0.42f, 0.56f, 0.80f, 0.75f, 0.24f, 0.80f, 0.52f, 0.12f),
            // Beech: flat, layered sprays filling a smooth dome.
            CrownForm.Beech => new(5, 7, 8, 0.32f, 0.44f, 0.56f, 0.68f, 0.70f, 0.20f, 0.84f, 0.48f, 0.07f, 3),
            // Birch: narrow, loose crown; lower masses hang (weeping fringe).
            CrownForm.Birch => new(4, 6, 7, 0.24f, 0.36f, 0.48f, 1.00f, 1.40f, 0.20f, 0.82f, 0.46f, 0.08f),
            // Sycamore: dense, rounded masses.
            CrownForm.Maple => new(5, 7, 7, 0.28f, 0.42f, 0.56f, 0.90f, 0.80f, 0.22f, 0.80f, 0.46f, 0.08f),
            // Ash: open, sparse crown with smaller, more separated masses.
            CrownForm.Ash => new(5, 6, 7, 0.22f, 0.36f, 0.48f, 0.85f, 0.78f, 0.22f, 0.82f, 0.54f, 0.12f),
            // Mature Scots pine: flat cushions high on the bole.
            CrownForm.Pine => new(4, 5, 6, 0.30f, 0.40f, 0.50f, 0.62f, 0.70f, 0.34f, 0.82f, 0.46f, 0.07f),
            // Shrubs: hazel opens into a vase (masses high and wide), hawthorn is a low, dense dome.
            CrownForm.Hazel => new(4, 4, 4, 0.24f, 0.42f, 0.54f, 0.85f, 0.75f, 0.50f, 0.90f, 0.56f, 0.10f, 0, 0.9f),
            CrownForm.Hawthorn => new(4, 4, 4, 0.34f, 0.44f, 0.54f, 0.70f, 0.70f, 0.15f, 0.45f, 0.46f, 0.10f),
            _ => null
        };

        /// <summary>Broad-leaved crowns beyond the sapling phase are built from lobes; whorled conifers are not.</summary>
        internal static bool Applies(ForestSpecies species, TreeLifePhase phase) =>
            phase >= TreeLifePhase.Young && For(DendroCrownMesh.For(species, phase)).HasValue;

        internal static Vertex[] Build(TreeForm form, ForestLod lod)
        {
            var spec = form.Spec;
            var profile = For(DendroCrownMesh.For(spec.Species, spec.Phase))
                ?? throw new ArgumentOutOfRangeException(nameof(form));
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
            int requested = spec.Phase >= TreeLifePhase.Old ? profile.Old : spec.Phase == TreeLifePhase.Mature ? profile.Mature : profile.Young;
            // Normalised z is in crown heights, x and y in crown radii: this converts a world-space
            // vertical/horizontal ratio into the normalised frame.
            float aspect = form.CrownRadius / crownHeight;
            var lobes = Cluster(leaves, requested, profile, spec.Seed, aspect);
            int sides = DendroCrownMesh.Sides(form.CrownRadius, lod, lod == ForestLod.Far ? 5 : 6,
                lod == ForestLod.Near ? 12 : lod == ForestLod.Medium ? 9 : 5);
            int rings = lod == ForestLod.Near ? 6 : lod == ForestLod.Medium ? 4 : 3;
            // Every LOD derives from the same closed masses: icosahedra fitted to each lobe. Near draws
            // them, Medium draws octahedra of the same masses, Far samples their outer envelope.
            var (plain, jittered) = Masses(lobes, Ico, 1, 1);
            // Bounds ignore the facet noise, so every LOD maps the same masses onto the crown size.
            float minZ = float.MaxValue, maxZ = float.MinValue, maxR = 0, noisyR = 0;
            foreach (var p in plain) { minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z); maxR = Math.Max(maxR, p.Xy.Length); }
            foreach (var p in jittered) noisyR = Math.Max(noisyR, p.Xy.Length);
            Vector3[] points;
            float[] tints;
            var indices = new List<int>();
            // Masses only pay off where they span a few pixels; tiny crowns use the envelope.
            bool massive = DendroCrownMesh.Sides(form.CrownRadius, ForestLod.Near, 6, 12) >= 8;
            bool faceted = massive && lod != ForestLod.Far;
            float fill = 1;
            if (lod == ForestLod.Far)
            {
                // Three coarse masses (ten-triangle bipyramids) merged from the close ones keep the
                // lumpy outline down to a few pixels.
                var merged = Merge(lobes, 3);
                (_, points) = Masses(merged, Bipyramid, 1.21f, 1);
                minZ = float.MaxValue; maxZ = float.MinValue;
                foreach (var p in points) { minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z); }
                maxR = noisyR;
                tints = new float[points.Length];
                for (int l = 0; l < merged.Count; l++)
                    foreach (int index in BipyramidFaces) indices.Add(l * Bipyramid.Length + index);
                faceted = true;
            }
            else if (faceted)
            {
                // The close model retains individual, overlapping closed foliage masses.
                // Twenty triangles per mass reveal the valleys which a single shell hides; the medium
                // model keeps the same masses with eight. An inscribed octahedron covers less than an
                // icosahedron, so it is drawn a little larger.
                var shape = lod == ForestLod.Near ? Ico : Octa;
                var faces = lod == ForestLod.Near ? IcoFaces : OctaFaces;
                if (lod != ForestLod.Near)
                {
                    (_, points) = Masses(lobes, Octa, 1.2f, 1.08f);
                    // Grown octahedra may pass the icosahedra's height range; keep them inside the crown.
                    minZ = float.MaxValue; maxZ = float.MinValue;
                    foreach (var p in points) { minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z); }
                }
                else points = jittered;
                // The noisy outline of the close masses, not the smooth one, reaches the crown radius.
                maxR = noisyR;
                tints = new float[points.Length];
                for (int l = 0; l < lobes.Count; l++)
                {
                    for (int v = 0; v < shape.Length; v++) tints[l * shape.Length + v] = lobes[l].Tint;
                    foreach (int index in faces) indices.Add(l * shape.Length + index);
                }
            }
            else
            {
                // Outer envelope of the same masses, sampled ring by ring: a horizontal ray from the
                // stem axis at each ring height. Tall, narrow crowns keep their outline, which a
                // single radial origin cannot do.
                int perLobe = IcoFaces.Length / 3;
                var planes = new Vector4[lobes.Count * perLobe];
                for (int f = 0; f < planes.Length; f++)
                {
                    int l = f / perLobe, face = f % perLobe * 3;
                    Vector3 a = plain[l * Ico.Length + IcoFaces[face]], b = plain[l * Ico.Length + IcoFaces[face + 1]],
                        c = plain[l * Ico.Length + IcoFaces[face + 2]];
                    Vector3 n = Vector3.Cross(b - a, c - a).Normalized();
                    planes[f] = new(n, Vector3.Dot(n, a));
                }
                float bottomZ = minZ, topZ = maxZ;
                points = new Vector3[2 + sides * rings];
                points[0] = new(0, 0, bottomZ); points[^1] = new(0, 0, topZ);
                // Sphere-like ring spacing: denser near the base and the top, giving rounded caps.
                float RingZ(int ring) => ring < 0 ? bottomZ : ring >= rings ? topZ
                    : bottomZ + (topZ - bottomZ) * (1 - MathF.Cos(MathF.PI * (ring + 1) / (rings + 1))) * 0.5f;
                for (int ring = 0; ring < rings; ring++)
                {
                    float z = RingZ(ring);
                    // A ring stands for the band half-way to its neighbours: take the outline over the
                    // whole band, so a ring that falls between two tiers of masses does not pinch the crown.
                    float low = (RingZ(ring - 1) + z) * 0.5f, high = (z + RingZ(ring + 1)) * 0.5f;
                    for (int side = 0; side < sides; side++)
                    {
                        float angle = spec.Yaw + MathF.Tau * side / sides;
                        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                        float reach = 0;
                        const int Band = 5;
                        // Power mean: close to the band maximum without letting one mass dominate.
                        for (int b = 0; b < Band; b++) reach = Math.Max(reach, Reach(low + (high - low) * (b + 0.5f) / Band, direction));

                        var p = direction * reach;
                        points[At(ring, side)] = new(p.X, p.Y, z);
                    }
                    // Seen from any side, the close masses stand all round the stem: pull each ring half-way
                    // towards its widest radius, so the outline does not narrow between two ring vertices.
                    float widest = 0;
                    for (int side = 0; side < sides; side++) widest = Math.Max(widest, points[At(ring, side)].Xy.Length);
                    for (int side = 0; side < sides; side++)
                    {
                        var p = points[At(ring, side)];
                        float r = p.Xy.Length, rounded = r + (widest - r) * 0.5f;
                        if (r > 1e-6f) points[At(ring, side)] = new(p.X * rounded / r, p.Y * rounded / r, p.Z);
                    }
                }
                tints = new float[points.Length];
                // Few rings cut the corners of the true outline: match the side-view area of a finely
                // sampled profile, so distant trees neither shrink nor swell at the LOD switch. The
                // side-view width of a slice, averaged over view directions, is its perimeter / pi.
                float coarse = 0, previousZ = bottomZ, previousW = 0;
                var ringPoints = new Vector2[sides];
                for (int ring = 0; ring <= rings; ring++)
                {
                    float z = ring < rings ? points[At(ring, 0)].Z : topZ, w = 0;
                    if (ring < rings)
                    {
                        for (int side = 0; side < sides; side++) ringPoints[side] = points[At(ring, side)].Xy;
                        w = Perimeter(ringPoints) / MathF.PI;
                    }
                    coarse += (w + previousW) * 0.5f * (z - previousZ);
                    previousZ = z; previousW = w;
                }
                float fine = 0;
                const int Samples = 24, Directions = 16;
                var slice = new Vector2[Directions];
                for (int i = 0; i < Samples; i++)
                {
                    float z = bottomZ + (topZ - bottomZ) * (i + 0.5f) / Samples;
                    for (int j = 0; j < Directions; j++)
                    {
                        var direction = new Vector2(MathF.Cos(MathF.Tau * j / Directions), MathF.Sin(MathF.Tau * j / Directions));
                        slice[j] = direction * Reach(z, direction);
                    }
                    fine += Perimeter(slice) / MathF.PI * (topZ - bottomZ) / Samples;
                }
                static float Perimeter(Vector2[] polygon)
                {
                    float length = 0;
                    for (int i = 0; i < polygon.Length; i++) length += (polygon[(i + 1) % polygon.Length] - polygon[i]).Length;
                    return length;
                }
                fill = coarse > 0 ? Math.Clamp(fine / coarse, 0.7f, 1.6f) : 1;
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

                // Farthest exit of a horizontal ray from the stem axis at height z through any mass.
                float Reach(float z, Vector2 direction)
                {
                    float reach = 0.02f;
                    for (int l = 0; l < lobes.Count; l++)
                    {
                        float enter = float.MinValue, exit = float.MaxValue;
                        for (int f = l * perLobe; f < (l + 1) * perLobe && enter <= exit; f++)
                        {
                            var plane = planes[f];
                            float facing = plane.X * direction.X + plane.Y * direction.Y;
                            float room = plane.W - plane.Z * z;
                            if (MathF.Abs(facing) < 1e-7f) { if (room < 0) exit = float.MinValue; continue; }
                            float t = room / facing;
                            if (facing > 0) exit = Math.Min(exit, t); else enter = Math.Max(enter, t);
                        }
                        if (enter <= exit && exit > 0) reach = Math.Max(reach, exit);
                    }
                    return reach;
                }
            }
            float thin = 0.72f + 0.28f * form.Foliage;
            float topLoss = form.Dieback > 0.45f && spec.Phase >= TreeLifePhase.Old
                ? Math.Min(0.25f, (form.Dieback - 0.3f) * 0.5f) : 0;
            // Compensate the coarser inscribed outline, without adding lobe geometry.
            float inscribed = faceted ? 1 : MathF.Sqrt(sides * MathF.Sin(MathF.PI / sides) / MathF.PI);
            float xy = form.CrownRadius * thin / maxR / inscribed;
            if (!faceted)
            {
                // The lumpy close crown is wider for its area than any smooth envelope. Blend (weighted
                // geometric mean) between matching its width and matching its side-view area, so
                // neither visibly jumps at the LOD switch; the outline width is the more visible cue.
                float envelope = 0;
                foreach (var p in points) envelope = Math.Max(envelope, p.Xy.Length);
                xy = form.CrownRadius * thin * MathF.Pow(1 / (envelope * inscribed), 0.5f) * MathF.Pow(fill / maxR, 0.5f);
            }
            var shades = new float[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                float t = Math.Clamp((points[i].Z - minZ) / (maxZ - minZ), 0, 1);
                float exposure = Math.Clamp(points[i].Xy.Length / maxR, 0, 1);
                // Darker crown interior and base, lighter sunlit outer masses.
                shades[i] = 0.72f + 0.20f * t + 0.12f * exposure + tints[i];
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
            // Faceted masses keep part of the face normal, so each facet catches the light on its own.
            float facet = faceted ? 0.35f : 0.08f;
            var result = new Vertex[indices.Count];
            for (int i = 0; i < result.Length; i += 3)
            {
                Vector3 face = Vector3.Cross(points[indices[i + 1]] - points[indices[i]], points[indices[i + 2]] - points[indices[i]]).Normalized();
                for (int j = 0; j < 3; j++)
                {
                    int v = indices[i + j];
                    var normal = ((1 - facet) * normals[v] + facet * face).Normalized();
                    if (Vector3.Dot(normal, face) < 0.15f) normal = face;
                    // Sky light from above, shadowed undersides. Zero mean over a closed surface,
                    // so the area-weighted crown colour stays the same at every LOD.
                    result[i + j] = new(points[v], normal, Tint(form.CrownColor, shades[v] + 0.09f * normals[v].Z));
                }
            }
            return result;

            (Vector3[] Plain, Vector3[] Jittered) Masses(List<Lobe> lobes, Vector3[] shape, float grow, float growZ)
            {
                var smooth = new Vector3[lobes.Count * shape.Length];
                var noisy = new Vector3[smooth.Length];
                float cos = MathF.Cos(spec.Yaw), sin = MathF.Sin(spec.Yaw);
                float tiltCos = MathF.Cos(0.37f), tiltSin = MathF.Sin(0.37f);
                for (int l = 0; l < lobes.Count; l++)
                {
                    var lobe = lobes[l];
                    // Different orientations avoid repeated horizontal polygon edges.
                    float turn = l * 12 * 0.137f;
                    float ct = MathF.Cos(turn), st = MathF.Sin(turn);
                    for (int v = 0; v < shape.Length; v++)
                    {
                        Vector3 d = shape[v];
                        d = new(d.X, d.Y * tiltCos - d.Z * tiltSin, d.Y * tiltSin + d.Z * tiltCos);
                        d = new(d.X * ct - d.Y * st, d.X * st + d.Y * ct, d.Z);
                        d = new(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos, d.Z);
                        var r = lobe.Radius * new Vector3(grow, grow, growZ);
                        if (d.Z < 0) r.Z *= lobe.Bottom;
                        // Faceted low-poly character: a little horizontal noise per vertex. Heights stay
                        // exact, so the crown top still meets the simulated tree height.
                        float noise = 1 + profile.Jitter * (2 * ForestTreeVariation.Unit(spec.Seed, 9100 + l * 16 + v) - 1);
                        smooth[l * shape.Length + v] = lobe.Centre + d * r;
                        noisy[l * shape.Length + v] = lobe.Centre + d * r * new Vector3(noise, noise, 1);
                    }
                }
                return (smooth, noisy);
            }
            int At(int ring, int side) => 1 + ring * sides + side % sides;
            void Face(int a, int b, int c)
            {
                indices.Add(a); indices.Add(b); indices.Add(c);
            }
        }

        private static readonly Vector3[] Ico = BuildIco();
        private static readonly int[] IcoFaces = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2,
            10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };

        private static readonly Vector3[] Bipyramid =
        {
            new(1, 0, 0), new(MathF.Cos(MathF.Tau / 5), MathF.Sin(MathF.Tau / 5), 0), new(MathF.Cos(2 * MathF.Tau / 5), MathF.Sin(2 * MathF.Tau / 5), 0),
            new(MathF.Cos(3 * MathF.Tau / 5), MathF.Sin(3 * MathF.Tau / 5), 0), new(MathF.Cos(4 * MathF.Tau / 5), MathF.Sin(4 * MathF.Tau / 5), 0),
            new(0, 0, 1), new(0, 0, -1)
        };
        private static readonly int[] BipyramidFaces = { 0,1,5, 1,2,5, 2,3,5, 3,4,5, 4,0,5, 1,0,6, 2,1,6, 3,2,6, 4,3,6, 0,4,6 };

        // Deterministic merge of the masses into a few larger ones: farthest-point seeds, nearest
        // assignment, and an enclosing (not bounding) ellipsoid per group.
        private static List<Lobe> Merge(List<Lobe> lobes, int count)
        {
            count = Math.Min(count, lobes.Count);
            var seeds = new List<Vector3> { lobes[0].Centre };
            while (seeds.Count < count)
            {
                Vector3 best = default; float far = -1;
                foreach (var lobe in lobes)
                {
                    float nearest = float.MaxValue;
                    foreach (var seed in seeds) nearest = Math.Min(nearest, (lobe.Centre - seed).LengthSquared);
                    if (nearest > far) { far = nearest; best = lobe.Centre; }
                }
                seeds.Add(best);
            }
            var groups = new List<Lobe>[count];
            for (int g = 0; g < count; g++) groups[g] = new();
            foreach (var lobe in lobes)
            {
                int winner = 0;
                for (int g = 1; g < count; g++)
                    if ((lobe.Centre - seeds[g]).LengthSquared < (lobe.Centre - seeds[winner]).LengthSquared) winner = g;
                groups[winner].Add(lobe);
            }
            var merged = new List<Lobe>(count);
            foreach (var group in groups)
            {
                if (group.Count == 0) continue;
                Vector3 centre = Vector3.Zero; float weight = 0;
                foreach (var lobe in group) { float w = lobe.Radius.X * lobe.Radius.Y * lobe.Radius.Z; centre += lobe.Centre * w; weight += w; }
                centre /= weight;
                float horizontal = 0, top = centre.Z, bottom = centre.Z;
                foreach (var lobe in group)
                {
                    horizontal = Math.Max(horizontal, (lobe.Centre.Xy - centre.Xy).Length * 0.6f + lobe.Radius.X);
                    top = Math.Max(top, lobe.Centre.Z + lobe.Radius.Z);
                    bottom = Math.Min(bottom, lobe.Centre.Z - lobe.Radius.Z * lobe.Bottom);
                }
                merged.Add(new(new(centre.X, centre.Y, (top + bottom) * 0.5f), new(horizontal, horizontal, (top - bottom) * 0.5f), 1, 0));
            }
            return merged;
        }

        private static readonly Vector3[] Octa = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };
        private static readonly int[] OctaFaces = { 0,2,4, 2,1,4, 1,3,4, 3,0,4, 2,0,5, 1,2,5, 3,1,5, 0,3,5 };

        private static Vector3[] BuildIco()
        {
            float g = (1 + MathF.Sqrt(5)) / 2;
            Vector3[] ico = { new(-1,g,0), new(1,g,0), new(-1,-g,0), new(1,-g,0), new(0,-1,g), new(0,1,g),
                new(0,-1,-g), new(0,1,-g), new(g,0,-1), new(g,0,1), new(-g,0,-1), new(-g,0,1) };
            for (int i = 0; i < ico.Length; i++) ico[i] = ico[i].Normalized();
            return ico;
        }

        // Farthest-point seeds + a fixed Lloyd iteration count: deterministic spatial
        // clusters, independent of mesh LOD and with no extra random stream.
        private static List<Lobe> Cluster(List<Vector3> leaves, int requested, Profile p, int seed, float aspect)
        {
            // The core is a vertical spine through the whole crown: every outer mass within Reach
            // of the axis overlaps it, whatever its height.
            float flatten = p.Flatten * aspect;
            int count = Math.Min(requested, leaves.Count);
            // About three masses ring the spine per tier. Tall, narrow crowns need masses tall
            // enough for the tiers to meet; broad crowns keep the world-space flattening.
            int tiers = Math.Max(1, (count + 2) / 3);
            float tierHalf = (p.MaxZ - p.MinZ) / (2 * tiers) * 1.25f;
            // The spine always starts low in the crown, so masses held high (a hazel vase) widen the
            // upper crown rather than being re-centred.
            // Its top stops a little above the highest mass centre: it still pierces the top masses,
            // but the masses (not a bare pole) form the crown top.
            float spineBottom = Math.Min(p.MinZ, 0.2f) - tierHalf * 0.6f, spineTop = p.MaxZ + tierHalf * 0.4f;
            Vector3 core = new(0, 0, (spineBottom + spineTop) * 0.5f);
            float spineHalf = Math.Max(p.CoreRadius * flatten, (spineTop - spineBottom) * 0.5f);
            // The spine is the shaded crown interior where it shows between masses.
            var lobes = new List<Lobe> { new(core, new(p.CoreRadius, p.CoreRadius, spineHalf), 1, -0.08f) };
            if (leaves.Count == 0) return lobes;
            // Cluster in world-isotropic space: a crown height spans several crown radii, so tall crowns
            // split into masses along their height rather than leaving gaps between few tall groups.
            var metric = new Vector3(1, 1, Math.Clamp(1 / aspect, 1, 4));
            var centres = new Vector3[count]; centres[0] = leaves[0];
            for (int k = 1; k < count; k++)
            {
                float best = -1;
                foreach (var leaf in leaves)
                {
                    float nearest = float.MaxValue;
                    for (int j = 0; j < k; j++) nearest = Math.Min(nearest, ((leaf - centres[j]) * metric).LengthSquared);
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
                        float distance = ((leaves[i] - centres[k]) * metric).LengthSquared;
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
                centre.Z = Math.Clamp(centre.Z, p.MinZ, p.MaxZ);
                // Layered sprays: pull the centre part of the way to the nearest tier.
                if (p.Layers > 1)
                {
                    float step = (p.MaxZ - p.MinZ) / (p.Layers - 1);
                    float tier = p.MinZ + step * MathF.Round((centre.Z - p.MinZ) / step);
                    centre.Z += (tier - centre.Z) * 0.45f;
                }
                float spread = 0;
                for (int i = 0; i < leaves.Count; i++) if (assignment[i] == k) spread += (leaves[i] - centres[k]).LengthSquared;
                float radius = Math.Clamp(0.28f + MathF.Sqrt(spread / totals[k]), p.MinRadius, p.MaxRadius);
                float rise = (centre.Z - p.MinZ) / Math.Max(1e-3f, p.MaxZ - p.MinZ) - 0.5f;
                radius *= 1 + p.Vase * rise * 0.6f;
                // Even the inscribed icosahedra (0.79 of the radius) overlap the spine at the mass's own
                // height, where the spine narrows towards its ends: no floating foliage, and the masses
                // close in towards a rounded top and base.
                float along = (centre.Z - core.Z) / spineHalf;
                float spine = p.CoreRadius * MathF.Sqrt(Math.Max(0, 1 - along * along));
                float limit = Math.Min(p.Reach * (1 + p.Vase * rise), 0.68f * (spine + radius));
                float axis = centre.Xy.Length;
                if (axis > limit) centre = new(centre.X * limit / axis, centre.Y * limit / axis, centre.Z);
                // Lower masses of a weeping crown hang further; upper masses of a flat-based one stay compact.
                float bottom = p.Bottom > 1 ? 1 + (p.Bottom - 1) * Math.Clamp((core.Z + 0.2f - centre.Z) * 2.5f, 0.2f, 1) : p.Bottom;
                float tint = 0.05f * (2 * ForestTreeVariation.Unit(seed, 9000 + k) - 1);
                var size = new Vector3(radius, radius, Math.Max(radius * flatten, tierHalf));
                // Guarantee it: the shapes inscribed in both polytopes (0.79 of an icosahedron's radius;
                // 0.75 leaves a margin) must share a point, else move the mass in towards the spine.
                for (int step = 0; step < 16 && !Touches(centre, size, bottom); step++)
                    centre = new(centre.X * 0.85f, centre.Y * 0.85f, core.Z + (centre.Z - core.Z) * 0.92f);
                lobes.Add(new(centre, size, bottom, tint));
            }
            return lobes;

            bool Touches(Vector3 centre, Vector3 size, float bottom)
            {
                const float Inscribed = 0.75f;
                Vector3 axis = new(0, 0, Math.Clamp(centre.Z, core.Z - Inscribed * spineHalf * 0.9f, core.Z + Inscribed * spineHalf * 0.9f));
                for (int i = 0; i <= 8; i++)
                {
                    Vector3 q = Vector3.Lerp(centre, axis, i / 8f);
                    Vector3 own = (q - centre) / (size * Inscribed);
                    if (q.Z < centre.Z) own.Z /= bottom;
                    Vector3 spine = (q - core) / (new Vector3(p.CoreRadius, p.CoreRadius, spineHalf) * Inscribed);
                    if (own.LengthSquared <= 1 && spine.LengthSquared <= 1) return true;
                }
                return false;
            }
        }

        private static uint Tint(uint color, float shade)
        {
            uint C(int shift) => (uint)Math.Clamp((int)MathF.Round(((color >> shift) & 255) * shade), 0, 255);
            return color & 0xff000000 | C(0) | C(8) << 8 | C(16) << 16;
        }
    }
}
