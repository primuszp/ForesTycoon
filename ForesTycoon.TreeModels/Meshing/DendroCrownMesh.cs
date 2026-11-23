using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ForesTycoon.TreeModels
{
    internal enum CrownForm { Spruce, Oak, Birch, Beech, Hazel, Hawthorn, Maple, Ash, Pine, Juniper, Fir, Larch, YoungPine }

    /// <summary>Season, vitality and site adjustments of the crown envelope.</summary>
    /// <param name="Foliage">1 = full leaf, lower values shrink the crown (budding, leaf fall, thin foliage).</param>
    /// <param name="Dieback">Share of the live crown lost: the envelope follows the surviving leaves more closely.</param>
    /// <param name="TopLoss">Share of the crown height lost at the top (a dead leader).</param>
    internal readonly record struct CrownShaping(float Foliage, float Dieback, float TopLoss, TreeDeformation Warp)
    {
        internal static CrownShaping Full => new(1, 0, 0, default);
    }

    internal static class DendroCrownMesh
    {
        internal static CrownForm For(ForestSpecies species, TreeLifePhase phase = TreeLifePhase.Mature) => species switch
        {
            ForestSpecies.Spruce => CrownForm.Spruce,
            ForestSpecies.Fir => CrownForm.Fir,
            ForestSpecies.Larch => CrownForm.Larch,
            // Young pines are tiered cones; the flat, high crown comes with age.
            ForestSpecies.Pine => phase <= TreeLifePhase.Young ? CrownForm.YoungPine : CrownForm.Pine,
            ForestSpecies.Juniper => CrownForm.Juniper,
            ForestSpecies.Oak or ForestSpecies.SessileOak or ForestSpecies.TurkeyOak => CrownForm.Oak,
            ForestSpecies.Birch => CrownForm.Birch,
            ForestSpecies.Maple => CrownForm.Maple,
            ForestSpecies.Ash => CrownForm.Ash,
            ForestSpecies.Hazel => CrownForm.Hazel,
            ForestSpecies.Hawthorn or ForestSpecies.Blackthorn or ForestSpecies.Elder => CrownForm.Hawthorn,
            _ => CrownForm.Beech
        };

        internal static bool Whorled(CrownForm form) => form is CrownForm.Spruce or CrownForm.Fir or CrownForm.Larch or CrownForm.YoungPine;

        // Reference zoom (pixels per world unit) at the fine end of each LOD band.
        // Tessellation is chosen so the silhouette chord error stays below one pixel there.
        internal static float ReferencePixels(ForestLod lod) =>
            lod == ForestLod.Near ? 64 : lod == ForestLod.Medium ? 9 : 3.5f;

        // Fewest polygon sides whose chord error r(1-cos(pi/n)) stays within the tolerance.
        internal static int Sides(float radiusWorld, ForestLod lod, int min, int max, float tolerancePixels = 1)
        {
            float pixels = radiusWorld * ReferencePixels(lod);
            if (pixels <= tolerancePixels * 2) return min;
            int sides = (int)MathF.Ceiling(MathF.PI / MathF.Acos(1 - tolerancePixels / pixels));
            return Math.Clamp(sides, min, max);
        }

        // Silhouette prior per growth form, t=0 at the crown base and t=1 at the apex.
        // peak = relative height of the widest point; exponents < 1 give broad, flat
        // bases/tops, > 1 tapering ones. Leaf weight says how much of the final radius
        // follows the DendroKit leaf distribution instead of the prior.
        // Window = exponent of the cos(angle) kernel; larger means narrower, deeper lobes.
        private readonly record struct Profile(float Peak, float Bottom, float Top, float LeafWeight, float Pole, int Window = 4);
        private static float Pow(float value, int exponent)
        {
            float result = 1;
            for (int i = 0; i < exponent; i++) result *= value;
            return result;
        }
        private static Profile For(CrownForm form, TreeLifeStage stage) => form switch
        {
            // Open-grown oaks spread into a broad, irregular, flat-topped dome with age.
            CrownForm.Oak => stage == TreeLifeStage.Old ? new(0.40f, 0.45f, 0.60f, 0.85f, 0.16f, 10)
                : stage == TreeLifeStage.Mature ? new(0.42f, 0.45f, 0.62f, 0.82f, 0.12f, 9)
                : new(0.45f, 0.55f, 0.75f, 0.60f, 0.06f, 5),
            // Beech: dense, smooth dome (Troll model sprays fill the envelope evenly).
            CrownForm.Beech => stage == TreeLifeStage.Old ? new(0.38f, 0.45f, 0.65f, 0.55f, 0.14f, 4)
                : new(0.36f, 0.38f, 0.75f, 0.50f, 0.10f, 3),
            // Birch: narrow ovoid crown with a pointed top and hanging lower fringe.
            CrownForm.Birch => stage == TreeLifeStage.Old ? new(0.40f, 0.45f, 0.90f, 0.62f, 0.02f, 5)
                : new(0.32f, 0.55f, 1.25f, 0.55f, 0f, 4),
            // Hazel: many ascending stems open into a vase, widest high up.
            CrownForm.Hazel => new(0.66f, 0.85f, 0.60f, 0.65f, 0.02f, 6),
            // Hawthorn: compact, dense, roughly hemispherical.
            CrownForm.Hawthorn => new(0.45f, 0.40f, 0.50f, 0.55f, 0.06f, 5),
            // Sycamore maple: dense, rounded dome.
            CrownForm.Maple => stage == TreeLifeStage.Old ? new(0.40f, 0.50f, 0.65f, 0.55f, 0.12f, 4) : new(0.40f, 0.55f, 0.75f, 0.50f, 0.08f, 3),
            // Ash: open, oval, irregular crown that thins with age.
            CrownForm.Ash => stage == TreeLifeStage.Old ? new(0.50f, 0.55f, 0.70f, 0.75f, 0.10f, 7) : new(0.48f, 0.60f, 0.80f, 0.70f, 0.06f, 6),
            // Mature Scots pine: broad flat-topped, clumpy crown high on a clear bole.
            CrownForm.Pine => new(0.50f, 0.50f, 0.70f, 0.85f, 0.08f, 6),
            CrownForm.YoungPine => new(.30f, .68f, .71f, .42f, .08f, 6),
            // Juniper: narrow, pointed, columnar.
            CrownForm.Juniper => new(.29f, .42f, 1.55f, .32f, .02f, 5),
            // The silver fir loses its pointed leader silhouette as the upper whorls catch up.
            CrownForm.Fir when stage == TreeLifeStage.Old => new(.48f, .26f, .12f, .15f, .03f),
            CrownForm.Fir when stage == TreeLifeStage.Mature => new(.30f, .36f, .46f, .26f, .02f),
            CrownForm.Fir => new(.12f, .40f, 1.0f, .36f, 0),
            CrownForm.Larch => new(.23f, .42f, .85f, .54f, 0, 6),
            _ => new(0.10f, 0.40f, 0.95f, 0.40f, 0f, 3) // spruce: cone, shaped further by whorl tiers
        };

        private static float Envelope(Profile p, float t) => t < p.Peak
            ? MathF.Pow(MathF.Sin(MathF.PI * 0.5f * t / p.Peak), p.Bottom)
            : MathF.Pow(MathF.Cos(MathF.PI * 0.5f * (t - p.Peak) / (1 - p.Peak)), p.Top);

        // One closed, regular-topology manifold. Leaf positions push directional bulges
        // into a species prior; spruce additionally gets stacked whorl tiers.
        internal static Vertex[] Build(CrownForm form, int seed, TreeLifeStage stage,
            float height, float radius, float fraction, float yaw, IReadOnlyList<Vector3> leaves,
            uint color, ForestLod lod, CrownShaping? shape = null, bool senescent = false)
        {
            var shaping = shape ?? CrownShaping.Full;
            var profile = For(form, stage);
            profile = profile with { LeafWeight = profile.LeafWeight + (0.97f - profile.LeafWeight) * Math.Min(1, shaping.Dieback * 1.6f) };
            float minShare = 0.35f * (1 - 0.8f * shaping.Dieback);
            float bottom = height * (1 - fraction), crownHeight = height * fraction * (1 - shaping.TopLoss);
            float topZ = bottom + crownHeight;
            bool whorled = Whorled(form);
            int sides = whorled
                ? Sides(radius, lod, lod == ForestLod.Far ? 4 : lod == ForestLod.Medium ? 6 : 12, lod == ForestLod.Far ? 5 : lod == ForestLod.Medium ? 10 : 24)
                : Sides(radius, lod, lod == ForestLod.Far ? 4 : lod == ForestLod.Near ? 12 : 5,
                    lod == ForestLod.Far ? 5 : lod == ForestLod.Medium ? 9 : 24);
            var rings = Rings(form, stage, sides, crownHeight, radius, lod);
            int ringCount = rings.Count;
            var points = new Vector3[2 + ringCount * sides];
            // Leaves in polar form. A directional max of projections would give the convex
            // hull; an angular window keeps the gaps between scaffold limbs as lobes.
            var leafDirection = new Vector2[leaves.Count];
            var leafRadius = new float[leaves.Count];
            var leafWeight = new float[leaves.Count];
            for (int i = 0; i < leaves.Count; i++)
            {
                leafRadius[i] = leaves[i].Xy.Length;
                leafDirection[i] = leafRadius[i] > 1e-6f ? leaves[i].Xy / leafRadius[i] : Vector2.Zero;
            }
            int window = profile.Window;
            Span<Vector2> directions = stackalloc Vector2[sides];
            for (int side = 0; side < sides; side++)
            {
                float angle = RingAngle(side);
                directions[side] = new(MathF.Cos(angle), MathF.Sin(angle));
            }
            var support = new float[sides];
            float bulgeAmount = ForestTreeVariation.Range(seed, 711, 0.06f, 0.16f) * (form == CrownForm.Oak ? 1.4f : 1);
            int bulgeLobes = form == CrownForm.Oak || stage == TreeLifeStage.Old ? 3 : 5;
            points[0] = new(0, 0, bottom + crownHeight * profile.Pole); points[^1] = new(0, 0, topZ);
            float maxRadius = 0;
            var ringRadius = new float[ringCount * sides];
            for (int ring = 0; ring < ringCount; ring++)
            {
                var (t, scale) = rings[ring];
                float prior = Envelope(profile, t) * scale;
                // The axial weight is independent of azimuth: evaluate it once per leaf/ring.
                float sharpness = whorled ? 40 : form == CrownForm.Oak ? 34 : 22;
                for (int sample = 0; sample < leaves.Count; sample++)
                {
                    float dz = (leaves[sample].Z - bottom) / crownHeight - t;
                    leafWeight[sample] = leafRadius[sample] * MathF.Exp(-dz * dz * sharpness);
                }
                for (int side = 0; side < sides; side++)
                {
                    Vector2 direction = directions[side];
                    float best = 0;
                    for (int sample = 0; sample < leaves.Count; sample++)
                    {
                        if (leafWeight[sample] <= best) continue;
                        float c = Vector2.Dot(leafDirection[sample], direction);
                        if (c <= 0) continue;
                        best = Math.Max(best, leafWeight[sample] * Pow(c, window));
                    }
                    support[side] = best;
                }
                for (int side = 0; side < sides; side++)
                {
                    // Circular [1 2 1] smoothing removes single-leaf spikes but keeps lobes.
                    float smooth = (support[(side + sides - 1) % sides] + 2 * support[side] + support[(side + 1) % sides]) * 0.25f;
                    float angle = RingAngle(side);
                    float bulge = 1 + bulgeAmount * MathF.Sin(angle * bulgeLobes + t * 7 + seed % 17);
                    float lobe = smooth * (whorled ? scale : 1);
                    float r = (prior * radius * (1 - profile.LeafWeight) + lobe * profile.LeafWeight) * bulge;
                    r = Math.Max(r, prior * radius * minShare);
                    ringRadius[ring * sides + side] = r;
                }
            }
            // Vertical [1 2 1] smoothing removes shelves and flat brims where a single far leaf sat in
            // one ring; spruce keeps its tiers.
            if (!whorled && ringCount > 2)
            {
                var smoothed = new float[ringRadius.Length];
                for (int ring = 0; ring < ringCount; ring++)
                    for (int side = 0; side < sides; side++)
                    {
                        float below = ringRadius[Math.Max(0, ring - 1) * sides + side], above = ringRadius[Math.Min(ringCount - 1, ring + 1) * sides + side];
                        smoothed[ring * sides + side] = (below + 2 * ringRadius[ring * sides + side] + above) * 0.25f;
                    }
                ringRadius = smoothed;
            }
            bool tiered = whorled && ringCount > 3;
            for (int ring = 0; ring < ringCount; ring++)
            {
                float ringTurn = tiered ? .25f * (2 * ForestTreeVariation.Unit(seed, 820 + ring) - 1) : 0;
                for (int side = 0; side < sides; side++)
                {
                    float t = rings[ring].T;
                    float r = ringRadius[ring * sides + side], angle = RingAngle(side);
                    if (form == CrownForm.Larch)
                    {
                        // Foliage follows spreading sprays. Neighbouring sectors belong to the same
                        // branch, so the uneven whorl edges survive a change of tessellation.
                        float spray = MathF.Cos(3 * (angle - yaw) + t * 8 + seed % 17);
                        r *= 1 + .24f * spray * (1 - t);
                    }
                    // Break horizontal shelves without adding vertices. The displacement is
                    // bounded by the neighbouring ring gaps, so rings cannot cross.
                    float gap = Math.Min(t - (ring == 0 ? 0 : rings[ring - 1].T),
                        (ring == ringCount - 1 ? 1 : rings[ring + 1].T) - t);
                    float dz = gap * 0.12f * MathF.Sin(angle * 3 + t * 4 + seed % 23);
                    if (tiered)
                    {
                        // Whorls are not stacked copies: each ring turns a little, the branch tips of a
                        // skirt reach unevenly and droop (more on the lower, heavier whorls), and the top
                        // shoulder narrows into a slim leader.
                        angle += ringTurn;
                        if (rings[ring].Scale >= 1 && ring > 0)
                        {
                            r *= 1 + 0.1f * (2 * ForestTreeVariation.Unit(seed, 840 + ring * 16 + side) - 1);
                            dz -= gap * 0.3f * (1 - t);
                        }
                        if (ring == ringCount - 1 && (form != CrownForm.Fir || stage < TreeLifeStage.Mature)) r *= .7f;
                    }
                    float level = t + dz;
                    if (form is CrownForm.Fir or CrownForm.YoungPine)
                    {
                        // Branch fans project beyond their whorl at discrete azimuths. Their
                        // edges lie almost level in fir and sweep upwards in young pine.
                        float fan = MathF.Cos(3 * (angle - yaw) + t * 14 + seed % 17);
                        float edge = rings[ring].Scale;
                        float depth = form == CrownForm.YoungPine ? .38f : senescent ? .25f : .17f;
                        r *= 1 + depth * fan * edge;
                        level += gap * edge * fan * (form == CrownForm.YoungPine ? .58f : .32f);
                        if (form == CrownForm.Fir && stage == TreeLifeStage.Old)
                        {
                            // Upper lateral shoots catch the terminal leader. Patchier old
                            // crowns retain a few high tips instead of one symmetrical cap.
                            float upper = Math.Max(0, 2 * t - 1);
                            level += (1 - level) * upper * (.38f + .29f * fan);
                            if (senescent) r *= 1 - .38f * (.5f - .5f * fan) * (.35f + upper);
                        }
                    }
                    if (form is CrownForm.Fir or CrownForm.Larch)
                    {
                        // Whorls tilt together towards one side of the tree. This continuous
                        // vertical warp fixes both poles and is monotone at every azimuth.
                        float tilt = form == CrownForm.Fir ? .15f : .23f;
                        level += tilt * level * (1 - level) * MathF.Sin(angle - yaw + seed % 19 + level * 3);
                    }
                    maxRadius = Math.Max(maxRadius, r);
                    points[1 + ring * sides + side] = new(MathF.Cos(angle) * r, MathF.Sin(angle) * r, bottom + level * crownHeight);
                }
            }
            if (form == CrownForm.Fir && stage == TreeLifeStage.Old && tiered)
            {
                // The terminal leader stops overtopping the lateral shoots. The highest
                // point belongs to the outer rim of the stork's nest.
                int rim = 1 + (ringCount - 1) * sides;
                int highest = rim;
                for (int i = rim + 1; i < rim + sides; i++)
                    if (points[i].Z > points[highest].Z) highest = i;
                points[highest].Z = topZ;
                points[^1].Z = topZ - crownHeight * (senescent ? .043f : .024f);
            }
            // Coarser levels of detail must fill the same silhouette, or trees visibly shrink and grow as the
            // camera zooms: compensate the inscribed polygon (mean width n·sin(pi/n)/pi of a circle) and the
            // wider gaps between fewer rings, relative to the smooth envelope of the growth form.
            float width = radius / Math.Max(1e-6f, maxRadius) * FillCompensation(profile, form, rings, sides);
            // The distant, high pine canopy also stands in for its omitted long bole.
            // A small width allowance preserves filled area at the LOD transition.
            if (lod == ForestLod.Far)
            {
                // Two rings bridge the gaps between whorls. Match their projected fill to the
                // near mesh, especially the long upper shoulder on an old fir.
                width *= form switch
                {
                    CrownForm.Pine => 1.025f,
                    CrownForm.YoungPine => 1.12f,
                    CrownForm.Fir when senescent => 1.04f,
                    CrownForm.Fir when stage == TreeLifeStage.Old => .89f,
                    CrownForm.Fir => .92f,
                    CrownForm.Larch when stage == TreeLifeStage.Old => .98f,
                    CrownForm.Larch => .94f,
                    _ => 1
                };
            }
            // Thin foliage (bud burst, leaf fall) shrinks the envelope sideways and a little downwards.
            float thin = 0.72f + 0.28f * shaping.Foliage;
            for (int i = 1; i < points.Length - 1; i++) { points[i].X *= width * thin; points[i].Y *= width * thin; }
            // A cheap, object-space occlusion cue: dark interior/underside, lighter outer
            // lobes. Bake RGB only; alpha carries the species code, not transparency.
            var colors = new uint[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                float t = Math.Clamp((points[i].Z - bottom) / crownHeight, 0, 1);
                float angle = MathF.Atan2(points[i].Y, points[i].X);
                float lobes = MathF.Sin(angle * bulgeLobes + t * 2 + seed % 17);
                float shade = 0.76f + 0.27f * MathF.Sqrt(t) + 0.065f * lobes * MathF.Sin(t * MathF.PI);
                colors[i] = Shade(color, shade);
            }
            if (!shaping.Warp.IsIdentity)
                for (int i = 0; i < points.Length; i++) points[i] = shaping.Warp.Apply(points[i]);

            var indices = new List<int>(6 * sides * ringCount); var normals = new Vector3[points.Length];
            for (int side = 0; side < sides; side++) Triangle(0, At(0, side + 1), At(0, side));
            for (int ring = 0; ring < ringCount - 1; ring++)
                for (int side = 0; side < sides; side++)
                {
                    int a = At(ring, side), b = At(ring, side + 1);
                    int c = At(ring + 1, side + 1), d = At(ring + 1, side);
                    // Shorter diagonals avoid skinny triangles across the lobe folds.
                    if ((points[a] - points[c]).LengthSquared <= (points[b] - points[d]).LengthSquared)
                    { Triangle(a, b, c); Triangle(a, c, d); }
                    else { Triangle(a, b, d); Triangle(b, c, d); }
                }
            for (int side = 0; side < sides; side++) Triangle(At(ringCount - 1, side), At(ringCount - 1, side + 1), points.Length - 1);
            for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].Normalized();
            var result = new Vertex[indices.Count];
            for (int i = 0; i < result.Length; i += 3)
            {
                Vector3 face = Vector3.Cross(points[indices[i + 1]] - points[indices[i]], points[indices[i + 2]] - points[indices[i]]).Normalized();
                for (int j = 0; j < 3; j++)
                {
                    int vertex = indices[i + j];
                    Vector3 normal = (normals[vertex] * 0.82f + face * 0.18f).Normalized();
                    // Deep folds must never shade as if their outward face were reversed.
                    if (Vector3.Dot(normal, face) < 0.15f) normal = face;
                    result[i + j] = new(points[vertex], normal, colors[vertex]);
                }
            }
            return result;

            float RingAngle(int side) => yaw + MathF.Tau * side / sides;
            int At(int ring, int side) => 1 + ring * sides + side % sides;
            void Triangle(int a, int b, int c)
            {
                indices.Add(a); indices.Add(b); indices.Add(c);
                Vector3 n = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                normals[a] += n; normals[b] += n; normals[c] += n;
            }
        }

        private static uint Shade(uint color, float shade)
        {
            uint Channel(int shift) => (uint)Math.Clamp((int)MathF.Round(((color >> shift) & 255) * shade), 0, 255);
            return (color & 0xff000000) | Channel(0) | (Channel(8) << 8) | (Channel(16) << 16);
        }

        private static float FillCompensation(Profile profile, CrownForm form, List<(float T, float Scale)> rings, int sides)
        {
            const int Steps = 48;
            float ideal = 0;
            for (int i = 0; i < Steps; i++) ideal += Envelope(profile, (i + 0.5f) / Steps) / Steps;
            if (Whorled(form)) ideal *= (1 + WhorlShoulder(form)) * .5f + .02f;
            // Area under the piecewise-linear outline through the ring radii, closed at both poles.
            float piece = 0, previousT = 0, previous = 0;
            foreach (var (t, scale) in rings)
            {
                float value = Envelope(profile, t) * scale;
                piece += (t - previousT) * (previous + value) * 0.5f;
                previousT = t; previous = value;
            }
            piece += (1 - previousT) * previous * 0.5f;
            float polygon = sides * MathF.Sin(MathF.PI / sides) / MathF.PI;
            return Math.Clamp(ideal / Math.Max(1e-4f, piece), 0.85f, 1.25f) / MathF.Sqrt(polygon);
        }

        // Radius at the next whorl insertion, relative to the skirt edge.
        private static float WhorlShoulder(CrownForm form) => form switch
        {
            CrownForm.Fir => .78f,
            CrownForm.YoungPine => .57f,
            CrownForm.Larch => .51f,
            _ => .64f
        };

        // Ring heights and per-ring radius multipliers. Deciduous crowns use evenly
        // spaced rings whose count follows the profile's vertical chord error; spruce
        // uses pairs of rings per whorl tier: a wide skirt edge and a narrow shoulder.
        private static List<(float T, float Scale)> Rings(CrownForm form, TreeLifeStage stage, int sides, float crownHeight, float radius, ForestLod lod)
        {
            var rings = new List<(float, float)>();
            float aspect = Math.Clamp(crownHeight / Math.Max(1e-4f, 2 * radius), 0.5f, 3f);
            if (Whorled(form))
            {
                // Show a tier only when it is at least ~5 px tall at the LOD reference zoom.
                int tiers = Math.Clamp((int)(crownHeight * ReferencePixels(lod) / 5), 0, lod == ForestLod.Near ? 8 : lod == ForestLod.Medium ? 5 : 0);
                if (form == CrownForm.Fir && stage >= TreeLifeStage.Mature)
                    tiers = Math.Min(tiers, lod == ForestLod.Near ? 7 : 4);
                // A seedling has only a few shoot increments, even when inspected close up.
                if (stage == TreeLifeStage.Seedling) tiers = Math.Min(tiers, 2);
                if (tiers < 2)
                {
                    bool blunt = form == CrownForm.Fir && stage == TreeLifeStage.Old;
                    rings.Add((.08f, 1)); rings.Add((blunt && lod == ForestLod.Far ? .82f : .45f, 1));
                    if (lod != ForestLod.Far) rings.Add((blunt ? .92f : .75f, 1));
                    return rings;
                }
                rings.Add((0.04f, 0.92f));
                float extent = form == CrownForm.Fir && stage == TreeLifeStage.Old ? .94f : .86f;
                float span = extent / tiers;
                for (int k = 0; k < tiers; k++)
                {
                    float start = .04f + span * k;
                    rings.Add((start + span * 0.18f, 1.00f));  // skirt: drooping branch tips
                    rings.Add((start + span * 0.92f, WhorlShoulder(form)));  // shoulder: next whorl insertion
                }
                return rings;
            }
            int count = lod == ForestLod.Far ? 3 : Math.Clamp((int)MathF.Round(sides * 0.42f * aspect), 3, lod == ForestLod.Near ? 8 : 6);
            for (int i = 1; i <= count; i++) rings.Add((i / (float)(count + 1), 1));
            return rings;
        }
    }
}
