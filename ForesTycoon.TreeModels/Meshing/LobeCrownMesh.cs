using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.TreeModels
{
    /// <summary>
    /// Overlapping, closed foliage masses ("lobes") from live leaf clusters of the skeleton, for every
    /// broad-leaved growth form. Every LOD samples one continuous rounded union of the same masses;
    /// near views retain more of its low-poly contour. Clusters and canopy exposure are deterministic
    /// and independent of tessellation.
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
        /// <param name="Roundness">Blend towards a rounded continuous envelope; beech smooths more than oak.</param>
        internal readonly record struct Profile(int Young, int Mature, int Old, float CoreRadius,
            float MinRadius, float MaxRadius, float Flatten, float Bottom, float MinZ, float MaxZ, float Reach,
            float Jitter, int Layers = 0, float Vase = 0, float Roundness = 0);

        private readonly record struct Lobe(Vector3 Centre, Vector3 Radius, float Bottom, float Tint);

        internal static Profile? For(CrownForm form) => form switch
        {
            // Open-grown oaks: few, large, irregular masses, flat cloud base, rough outline.
            CrownForm.Oak => new(5, 7, 8, 0.26f, 0.42f, 0.56f, 0.80f, 0.75f, 0.24f, 0.88f, 0.52f, 0.12f, Roundness: .45f),
            // Beech: flat, layered sprays filling a smooth dome.
            CrownForm.Beech => new(5, 7, 8, 0.32f, 0.44f, 0.56f, 0.68f, 0.70f, 0.20f, 0.90f, 0.48f, 0.07f, 3, Roundness: .72f),
            // Birch: narrow, loose crown; lower masses hang (weeping fringe).
            CrownForm.Birch => new(4, 6, 7, 0.24f, 0.36f, 0.48f, 1.00f, 1.40f, 0.20f, 0.82f, 0.46f, 0.08f, Roundness: .25f),
            // Sycamore: dense, rounded masses.
            CrownForm.Maple => new(5, 7, 7, 0.28f, 0.42f, 0.56f, 0.90f, 0.80f, 0.22f, 0.88f, 0.46f, 0.08f, Roundness: .65f),
            // Ash: open, sparse crown with smaller, more separated masses.
            CrownForm.Ash => new(5, 6, 7, 0.22f, 0.36f, 0.48f, 0.85f, 0.78f, 0.22f, 0.88f, 0.54f, 0.12f, Roundness: .3f),
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

            // One continuous, rounded canopy (Tree3D-style "geometric" crown): the lobes are metaballs
            // whose smooth union is sampled along the rays of a Fibonacci sphere lattice, and the
            // lattice's convex-hull triangulation becomes the crown surface. Every LOD samples the same
            // field, only with fewer rays, so silhouettes agree.
            int count = Lattice(form.CrownRadius, lod, form.Shrub);
            var (directions, triangles) = Sphere(count);
            // Work in an isotropic frame (z in crown radii), so the metaballs blend like round masses.
            var iso = new Vector3(1, 1, 1 / aspect);
            var centres = new Vector3[lobes.Count]; var radii = new Vector3[lobes.Count];
            Vector3 middle = Vector3.Zero; float weight = 0;
            for (int l = 0; l < lobes.Count; l++)
            {
                centres[l] = lobes[l].Centre * iso; radii[l] = lobes[l].Radius * iso;
                float w = radii[l].X * radii[l].Y * radii[l].Z; middle += centres[l] * w; weight += w;
            }
            middle /= weight;
            var stretch = new Vector3(1, 1, 0.5f / aspect);
            float cos = MathF.Cos(spec.Yaw), sin = MathF.Sin(spec.Yaw);
            var points = new Vector3[count]; var tints = new float[count];
            for (int i = 0; i < count; i++)
            {
                // The lattice turns with the tree, so its facets differ from tree to tree and the crown
                // rotates exactly with its skeleton.
                var d = directions[i];
                d = new(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos, d.Z);
                var ray = d * stretch;
                float t = Surface(ray, out int owner);
                // A touch of spikiness keeps the low-poly character without breaking the round mass.
                t *= 1 + profile.Jitter * 0.3f * (2 * ForestTreeVariation.Unit(spec.Seed, 9100 + i) - 1);
                var q = middle + ray * t;
                points[i] = new(q.X, q.Y, q.Z * aspect);
                tints[i] = lobes[owner].Tint;
            }

            float minZ = float.MaxValue, maxZ = float.MinValue, maxR = 0;
            foreach (var p in points) { minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z); maxR = Math.Max(maxR, p.Xy.Length); }
            float thin = 0.72f + 0.28f * form.Foliage;
            if (profile.Roundness > 0)
            {
                // Blend the irregular leaf-supported shell towards an ellipsoid, not separate balls.
                // The species-specific outline remains, while broadleaves read as one rounded mass.
                for (int i = 0; i < count; i++)
                {
                    var d = directions[i];
                    d = new(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos, d.Z);
                    var p = points[i];
                    var normalized = new Vector3(p.X / maxR, p.Y / maxR, (p.Z - minZ) / (maxZ - minZ));
                    points[i] = Vector3.Lerp(normalized, new(d.X, d.Y, .5f * (d.Z + 1)), profile.Roundness);
                }
                minZ = float.MaxValue; maxZ = float.MinValue; maxR = 0;
                foreach (var p in points) { minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z); maxR = Math.Max(maxR, p.Xy.Length); }
            }
            float topLoss = form.Dieback > 0.45f && spec.Phase >= TreeLifePhase.Old
                ? Math.Min(0.25f, (form.Dieback - 0.3f) * 0.5f) : 0;
            // An inscribed lattice polytope covers less than the smooth surface; coarser lattices more so.
            float xy = form.CrownRadius * thin / maxR * Coverage(count);
            var shades = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = Math.Clamp((points[i].Z - minZ) / (maxZ - minZ), 0, 1);
                float exposure = Math.Clamp(points[i].Xy.Length / maxR, 0, 1);
                float sideLight = form.SideExposure(MathF.Atan2(points[i].Y, points[i].X));
                // A closed stand lights the upper canopy; lateral/low foliage is shaded. At an
                // edge, only the open direction keeps a full, bright skirt. This is baked once.
                float skirt = profile.Roundness > 0 ? 1 - .18f * (1 - sideLight) * Math.Clamp((.55f - t) / .55f, 0, 1) : 1;
                shades[i] = profile.Roundness > 0
                    ? .68f + .27f * t + .13f * sideLight * exposure + tints[i] * .4f - .06f * (1 - sideLight) * (1 - t)
                    : .72f + .20f * t + .12f * exposure + tints[i];
                points[i] = form.Warp.Apply(new(points[i].X * xy * skirt, points[i].Y * xy * skirt,
                    form.CrownBase + t * crownHeight * (1 - topLoss)));
            }
            var normals = new Vector3[count];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector3 n = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                normals[a] += n; normals[b] += n; normals[c] += n;
            }
            for (int i = 0; i < count; i++) normals[i] = normals[i].Normalized();
            var result = new Vertex[triangles.Length];
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
                    // Sky light from above, shadowed undersides. Zero mean over a closed surface,
                    // so the area-weighted crown colour stays the same at every LOD.
                    result[i + j] = new(points[v], normal, Tint(form.CrownColor, shades[v] + 0.09f * normals[v].Z));
                }
            }
            return result;

            // Outermost crossing of the metaball iso-surface along a ray from the crown's centre.
            float Surface(Vector3 ray, out int owner)
            {
                const float Far = 3f, Step = 0.06f;
                owner = 0;
                float t = Far;
                while (t > 0 && Field(middle + ray * t, out _) < Threshold) t -= Step;
                if (t <= 0) { Field(middle, out owner); return 0.05f; }
                float inside = t, outside = Math.Min(Far, t + Step);
                for (int k = 0; k < 10; k++)
                {
                    float m = (inside + outside) * 0.5f;
                    if (Field(middle + ray * m, out _) >= Threshold) inside = m; else outside = m;
                }
                Field(middle + ray * inside, out owner);
                return inside;
            }

            float Field(Vector3 p, out int strongest)
            {
                float sum = 0, best = 0; strongest = 0;
                for (int l = 0; l < centres.Length; l++)
                {
                    var q = (p - centres[l]) / radii[l];
                    if (q.Z < 0) q.Z /= lobes[l].Bottom;
                    float r2 = q.LengthSquared / (Influence * Influence);
                    if (r2 >= 1) continue;
                    float f = (1 - r2) * (1 - r2);
                    sum += f;
                    if (f > best) { best = f; strongest = l; }
                }
                return sum;
            }
        }

        // Metaball falloff (1 - (r/R)^2)^2 with influence R = 1.3 lobe radii; a lone lobe's surface
        // then sits exactly on its own radius, and neighbouring lobes merge smoothly.
        private const float Influence = 1.3f;
        private static readonly float Threshold = (1 - 1 / (Influence * Influence)) * (1 - 1 / (Influence * Influence));

        /// <summary>Lattice points per LOD; a closed lattice of n points has 2n - 4 triangles (Far: exactly 30).</summary>
        internal static int Lattice(float crownRadius, ForestLod lod, bool shrub = false)
        {
            // Crowns only a few pixels wide do not need the full lattice; shrubs stay cheap.
            bool large = !shrub && DendroCrownMesh.Sides(crownRadius, ForestLod.Near, 6, 12) >= 8;
            return lod switch
            {
                ForestLod.Near => large ? 96 : shrub ? 48 : 40,
                ForestLod.Medium => large ? 40 : 24,
                _ => 17
            };
        }

        private static float Coverage(int count) => count >= 96 ? 1.0f : count >= 40 ? 1.03f : count >= 24 ? 1.05f : 1.08f; // 48 → 1.03

        private static readonly Dictionary<int, (Vector3[], int[])> Spheres = new();

        /// <summary>Fibonacci sphere lattice and its convex-hull triangulation (outward winding), cached per size.</summary>
        internal static (Vector3[] Directions, int[] Triangles) Sphere(int count)
        {
            lock (Spheres)
            {
                if (Spheres.TryGetValue(count, out var cached)) return cached;
                var d = new Vector3[count];
                float golden = MathF.PI * (3 - MathF.Sqrt(5));
                for (int i = 0; i < count; i++)
                {
                    float z = 1 - (2 * i + 1f) / count, r = MathF.Sqrt(Math.Max(0, 1 - z * z));
                    d[i] = new(MathF.Cos(i * golden) * r, MathF.Sin(i * golden) * r, z);
                }
                // Points on a sphere: their convex hull is their spherical Delaunay triangulation. Brute
                // force is fine for under a hundred points, once per lattice size.
                var faces = new List<int>();
                for (int a = 0; a < count; a++)
                    for (int b = a + 1; b < count; b++)
                        for (int c = b + 1; c < count; c++)
                        {
                            var n = Vector3.Cross(d[b] - d[a], d[c] - d[a]);
                            if (Vector3.Dot(n, d[a]) < 0) n = -n;
                            bool hull = true;
                            for (int k = 0; k < count && hull; k++)
                                if (k != a && k != b && k != c && Vector3.Dot(n, d[k] - d[a]) > 1e-7f) hull = false;
                            if (!hull) continue;
                            if (Vector3.Dot(Vector3.Cross(d[b] - d[a], d[c] - d[a]), d[a]) >= 0) { faces.Add(a); faces.Add(b); faces.Add(c); }
                            else { faces.Add(a); faces.Add(c); faces.Add(b); }
                        }
                if (faces.Count != 3 * (2 * count - 4)) throw new InvalidOperationException("Degenerate crown lattice.");
                return Spheres[count] = (d, faces.ToArray());
            }
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
