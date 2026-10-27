using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.TreeModels
{
    /// <summary>
    /// Foliage from live leaf clusters of the skeleton: a continuous union for broad-leaved
    /// crowns, or individually closed branch pads for open adult pines. Both use the same
    /// implicit-field sampler and mesh emission. Clusters are independent of tessellation.
    /// See docs/tree-realism-plan.md.
    /// </summary>
    internal static partial class LobeCrownMesh
    {
        /// <summary>Per growth form shape of the foliage masses. Radii are in crown radii.</summary>
        /// <param name="Young">Leaf clusters besides the central spine (young, mature, old).</param>
        /// <param name="CoreRadius">Horizontal radius of the central spine, which spans the crown height
        /// for a joined crown; fullness of the innermost shoot groups in an open crown.</param>
        /// <param name="Flatten">Vertical / horizontal radius of a mass in world space.</param>
        /// <param name="Bottom">Scale of the lower half: below 1 a flat cloud base, above 1 a hanging fringe.</param>
        /// <param name="MinZ">Lowest and highest allowed mass centre, relative crown height.</param>
        /// <param name="Reach">How far a mass centre may sit from the stem axis.</param>
        /// <param name="Jitter">Radial vertex noise of the faceted masses.</param>
        /// <param name="Layers">Above 1, mass centres are pulled towards this many horizontal tiers (beech sprays).</param>
        /// <param name="Vase">Above 0, higher masses grow larger and reach further out (an opening vase).</param>
        /// <param name="Roundness">Blend towards a rounded continuous envelope; beech smooths more than oak.</param>
        /// <param name="BranchFollow">How far lobes keep their leaf cluster's position instead of closing towards the stem.</param>
        /// <param name="Anisotropy">How strongly a lobe follows its leaf cloud's horizontal covariance, preserving volume.</param>
        /// <param name="Gaps">Zero joins all masses; positive values keep separate branch pads and control their spacing.</param>
        internal readonly record struct Profile(int Young, int Mature, int Old, float CoreRadius,
            float MinRadius, float MaxRadius, float Flatten, float Bottom, float MinZ, float MaxZ, float Reach,
            float Jitter, int Layers = 0, float Vase = 0, float Roundness = 0,
            float BranchFollow = .65f, float Anisotropy = .7f, float Gaps = 0, int Detail = 1);

        internal readonly record struct Lobe(Vector3 Centre, Vector3 Radius, float Bottom, float Tint, Vector2 Axis = default)
        {
            // Horizontal principal axis of the supporting leaf cloud; the perpendicular axis is implicit.
            internal Vector3 Local(Vector3 p) => Axis == Vector2.Zero ? p
                : new(p.X * Axis.X + p.Y * Axis.Y, -p.X * Axis.Y + p.Y * Axis.X, p.Z);
        }

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
            // Mature Scots pine: irregular, open branch-end cushions without enforced horizontal tiers.
            CrownForm.Pine => new(5, 32, 36, .3f, .12f, .34f, .72f, .9f, .04f, .98f, 1.05f, .24f,
                Roundness: 0, BranchFollow: 1, Anisotropy: 1, Gaps: .35f, Detail: 4),
            // Shrubs: hazel opens into a vase (masses high and wide), hawthorn is a low, dense dome.
            CrownForm.Hazel => new(4, 4, 4, 0.24f, 0.42f, 0.54f, 0.85f, 0.75f, 0.50f, 0.90f, 0.56f, 0.10f, 0, 0.9f, BranchFollow: 0, Anisotropy: 0),
            CrownForm.Hawthorn => new(4, 4, 4, 0.34f, 0.44f, 0.54f, 0.70f, 0.70f, 0.15f, 0.45f, 0.46f, 0.10f, BranchFollow: 0, Anisotropy: 0),
            _ => null
        };

        /// <summary>Leaf-cluster crowns beyond sapling: broadleaves and adult Scots pine; young conifers remain whorled.</summary>
        internal static bool Applies(ForestSpecies species, TreeLifePhase phase) =>
            phase >= TreeLifePhase.Young && For(DendroCrownMesh.For(species, phase)).HasValue;

        // Conservative outer end of a ray through a lobe's finite influence. The lower half
        // may be stretched, so enclose both halves before solving the quadratic. Skipping empty
        // space replaces the old fixed far bound without changing the field or bisection accuracy.
        internal static float SupportExit(Vector3 origin, Vector3 ray, float bottom)
        {
            float vertical = Math.Max(1, bottom);
            origin.Z /= vertical; ray.Z /= vertical;
            float a = ray.LengthSquared, b = Vector3.Dot(origin, ray);
            float c = origin.LengthSquared - Influence * Influence;
            float discriminant = b * b - a * c;
            if (a < 1e-12f || discriminant <= 0) return 0;
            return Math.Max(0, (-b + MathF.Sqrt(discriminant)) / a);
        }

        // Metaball falloff (1 - (r/R)^2)^2 with influence R = 1.3 lobe radii; a lone lobe's surface
        // then sits exactly on its own radius, and neighbouring lobes merge smoothly.
        private const float Influence = 1.3f;
        private static readonly float Threshold = (1 - 1 / (Influence * Influence)) * (1 - 1 / (Influence * Influence));

        /// <summary>Lattice points per LOD; a closed lattice of n points has 2n - 4 triangles (Far: exactly 30).</summary>
        internal static int Lattice(float crownRadius, ForestLod lod, bool shrub = false)
        {
            // Crowns only a few pixels wide do not need the full lattice; shrubs stay cheap.
            bool large = !shrub && crownRadius >= .5f;
            return lod switch
            {
                ForestLod.Near => large ? 384 : shrub ? 128 : 192,
                ForestLod.Medium => large ? 80 : 40,
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
                var faces = HullFaces(d);
                if (faces.Length != 3 * (2 * count - 4)) throw new InvalidOperationException($"Degenerate crown lattice ({count} points, {faces.Length / 3} faces).");
                return Spheres[count] = (d, faces);
            }
        }

        /// <summary>
        /// The convex hull of points on the unit sphere (their spherical Delaunay triangulation), outward winding.
        /// A hull face joins close neighbours, so faces are formed only from each point's nearest points. A face is
        /// kept when no point lies above its plane; such a point would sit in the spherical cap the plane cuts off,
        /// within the cap's diameter of the face's corner, so only that neighbourhood is checked — all points when the
        /// cap reaches beyond it. The result equals testing every triple against every point (O(n⁴), seconds at 384).
        /// </summary>
        internal static int[] HullFaces(Vector3[] d)
        {
            int count = d.Length, near = Math.Min(count - 1, 16), wide = Math.Min(count - 1, 48);
            double planeTolerance = count > 384 ? 1e-11 : 1e-7;
            var precise = Array.ConvertAll(d, p => new Vector3d(p.X, p.Y, p.Z));
            var faces = new List<int>();
            var order = new int[count];
            var distance = new float[count];
            for (int a = 0; a < count; a++)
            {
                for (int k = 0; k < count; k++) { order[k] = k; distance[k] = k == a ? -1 : (d[k] - d[a]).LengthSquared; }
                Array.Sort(distance, order);
                // order[0] is a itself; its neighbours follow, nearest first.
                float reach = MathF.Sqrt(distance[wide]);
                var candidates = new List<int>(near);
                for (int i = 1; i <= near; i++) if (order[i] > a) candidates.Add(order[i]);
                candidates.Sort();
                for (int i = 0; i < candidates.Count; i++)
                    for (int j = i + 1; j < candidates.Count; j++)
                    {
                        int b = candidates[i], c = candidates[j];
                        var n = Vector3d.Cross(precise[b] - precise[a], precise[c] - precise[a]);
                        if (Vector3d.Dot(n, precise[a]) < 0) n = -n;
                        double cap = 2 * Math.Acos(Math.Clamp(Vector3d.Dot(n.Normalized(), precise[a]), -1, 1));
                        int checks = cap < MathF.PI && 2 * Math.Sin(cap / 2) < reach ? wide + 1 : count;
                        bool hull = true;
                        for (int i2 = 0; i2 < checks && hull; i2++)
                        {
                            int k = checks == count ? i2 : order[i2];
                            if (k != a && k != b && k != c && Vector3d.Dot(n, precise[k] - precise[a]) > planeTolerance) hull = false;
                        }
                        if (!hull) continue;
                        if (Vector3d.Dot(Vector3d.Cross(precise[b] - precise[a], precise[c] - precise[a]), precise[a]) >= 0) { faces.Add(a); faces.Add(b); faces.Add(c); }
                        else { faces.Add(a); faces.Add(c); faces.Add(b); }
                    }
            }
            return faces.ToArray();
        }

        private static uint Tint(uint color, float shade)
        {
            uint C(int shift) => (uint)Math.Clamp((int)MathF.Round(((color >> shift) & 255) * shade), 0, 255);
            return color & 0xff000000 | C(0) | C(8) << 8 | C(16) << 16;
        }
    }
}
