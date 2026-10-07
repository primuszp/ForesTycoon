using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using DendroKit.Core.Params;
using DendroKit.Core.Tree;
using OpenTK.Mathematics;
using Vector3d = DendroKit.Core.Geom.Vector3d;

namespace ForesTycoon
{
    // Leaves are construction samples only. The renderer receives one opaque crown
    // and a few coarse woody stems, never leaf cards or the hidden twig mesh.
    internal static class DendroTreeGenerator
    {
        internal sealed record Mesh(Vertex[] Trunk, Vertex[] Branches, Vertex[] Crown, long StemCount, int LeafCount);
        private readonly record struct Key(ForestSpecies Species, int Variant, TreeLifeStage Stage, int Light, bool Shrub);
        private static readonly Dictionary<Key, Samples> Cache = new();
        private static readonly Queue<Key> Order = new();
        private const int Capacity = 256;
        internal static int LightBand(float light) => light < 0.3f ? 0 : light < 0.65f ? 1 : 2;
        internal static float BandLight(int band) => band == 0 ? 0.15f : band == 1 ? 0.48f : 0.85f;

        internal static Mesh Generate(ForestSpecies species, int seed, TreeLifeStage stage,
            float light, ForestTreeDimensions size, float yaw, ForestLod lod, bool includeCrown = true) =>
            Build(species, seed, stage, light, size, yaw, lod, includeCrown, false);

        internal static Mesh GenerateShrub(int seed, TreeLifeStage stage, float light,
            ForestTreeDimensions size, float yaw, ForestLod lod) =>
            Build(ForestSpecies.Beech, seed, stage, light, size, yaw, lod, true, true);

        private static Mesh Build(ForestSpecies species, int seed, TreeLifeStage stage,
            float light, ForestTreeDimensions size, float yaw, ForestLod lod, bool includeCrown, bool shrub)
        {
            if (species == ForestSpecies.None || !float.IsFinite(light) || !float.IsFinite(yaw) ||
                !float.IsFinite(size.Height + size.Diameter + size.CrownRadius) ||
                size.Height <= 0 || size.Diameter <= 0 || size.CrownRadius <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            light = Math.Clamp(light, 0, 1);
            float response = MathF.Pow(light, 1 - ForestSpeciesProfile.For(species).ShadeTolerance * 0.65f);
            float fraction = Math.Clamp((shrub ? 0.94f : ForestTreeAppearance.CrownFraction(species, stage))
                * (0.72f + 0.28f * response), 0.2f, 0.95f);
            float height = size.Height * Terrain.TreeMetresToWorld;
            float radius = size.CrownRadius * Terrain.TreeMetresToWorld * (0.74f + 0.26f * response);
            var key = new Key(species, seed & 31, stage, LightBand(light), shrub);
            Samples samples;
            lock (Cache)
            {
                if (!Cache.TryGetValue(key, out samples))
                {
                    var tree = new TreeImpl(key.Variant, Parameters(key)); tree.Make();
                    samples = new Samples(); tree.TraverseTree(samples);
                    samples.Height = (float)tree.Height; samples.StemCount = tree.StemCount;
                    foreach (var p in samples.Leaves) samples.Radius = Math.Max(samples.Radius, p.Xy.Length);
                    if (Cache.Count == Capacity) Cache.Remove(Order.Dequeue());
                    Cache.Add(key, samples); Order.Enqueue(key);
                }
            }
            float zScale = height / samples.Height, xyScale = radius * 0.88f / Math.Max(0.001f, samples.Radius);
            float cosine = MathF.Cos(yaw), sine = MathF.Sin(yaw);
            Vector3 Rotate(Vector3 p) => new(p.X * cosine - p.Y * sine, p.X * sine + p.Y * cosine, p.Z);
            Vector3 Transform(Vector3 p) => Rotate(p) * new Vector3(xyScale, xyScale, zScale);
            var trunk = new List<Vertex>(); var branches = new List<Vertex>();
            var model = Terrain.TreeModel.For(species);
            float pigment = ForestTreeVariation.Range(seed, 601, 0.90f, 1.08f);
            Color bark = species == ForestSpecies.Birch && stage == TreeLifeStage.Seedling
                ? Color.FromArgb(115, 77, 48) : model.TrunkColor;
            uint woodColor = Pack(bark, species, pigment);
            int branchBudget = lod == ForestLod.Near ? 6 : 3;
            if (lod != ForestLod.Far)
            foreach (var stem in samples.Stems)
            {
                if (stem.Level > 1 || (stem.Level == 1 && branchBudget-- <= 0)) continue;
                var target = stem.Level == 0 ? trunk : branches;
                int segments = stem.Level == 0 ? (lod == ForestLod.Near ? 4 : 2) : 1;
                int sides = stem.Level == 0 ? (lod == ForestLod.Near ? 5 : 4) : 3;
                float limit = includeCrown ? height * (1 - fraction * (stem.Level == 0 ? 0.45f : 0.15f)) : float.MaxValue;
                for (int segment = 0; segment < segments; segment++)
                {
                    int a = segment * (stem.Points.Count - 1) / segments;
                    int b = (segment + 1) * (stem.Points.Count - 1) / segments;
                    if (a == b) continue;
                    Vector3 start = Transform(stem.Points[a]), end = Transform(stem.Points[b]);
                    if (start.Z > limit) break;
                    float ra = stem.Radii[a] * size.Diameter * Terrain.TreeMetresToWorld / 0.04f;
                    float rb = stem.Radii[b] * size.Diameter * Terrain.TreeMetresToWorld / 0.04f;
                    if (includeCrown && stem.Level > 0)
                    {
                        // Only expose the structural fork. Fine outer branches are
                        // hidden by foliage and can protrude through a coarse envelope.
                        end = Vector3.Lerp(start, end, 0.35f);
                        rb = ra + (rb - ra) * 0.35f;
                    }
                    if (end.Z > limit)
                    {
                        float t = (limit - start.Z) / (end.Z - start.Z);
                        end = Vector3.Lerp(start, end, t); rb = ra + (rb - ra) * t;
                    }
                    Tube(start, end, ra, rb, sides, target);
                }
            }
            var support = new List<Vector3>(samples.Leaves.Count);
            foreach (var p in samples.Leaves) support.Add(Transform(p));
            var crown = includeCrown ? DendroCrownMesh.Build(species, seed, stage, height, radius,
                fraction, yaw, support, Pack(model.CrownColor, species, pigment), lod) : Array.Empty<Vertex>();
            return new(trunk.ToArray(), branches.ToArray(), crown, samples.StemCount, samples.Leaves.Count);

            void Tube(Vector3 a, Vector3 b, float ra, float rb, int sides, List<Vertex> target)
            {
                Vector3 axis = b - a;
                if (axis.LengthSquared < 1e-12f) return;
                axis.Normalize();
                Vector3 u = Vector3.Cross(axis, Math.Abs(axis.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ).Normalized();
                Vector3 v = Vector3.Cross(axis, u);
                rb = Math.Max(rb, ra * 0.025f);
                for (int i = 0; i < sides; i++)
                {
                    float t = MathF.Tau * i / sides, next = MathF.Tau * (i + 1) / sides;
                    Vector3 d = u * MathF.Cos(t) + v * MathF.Sin(t), e = u * MathF.Cos(next) + v * MathF.Sin(next);
                    Triangle(a + d * ra, a + e * ra, b + e * rb);
                    Triangle(a + d * ra, b + e * rb, b + d * rb);
                }
                void Triangle(Vector3 p, Vector3 q, Vector3 r)
                {
                    Vector3 n = Vector3.Cross(q - p, r - p);
                    if (n.LengthSquared < 1e-20f) return;
                    n.Normalize(); target.Add(new(p, n, woodColor)); target.Add(new(q, n, woodColor)); target.Add(new(r, n, woodColor));
                }
            }
        }

        private static TreeParams Parameters(Key key)
        {
            bool spruce = key.Species == ForestSpecies.Spruce;
            bool juvenile = key.Stage is TreeLifeStage.Seedling or TreeLifeStage.Young;
            float light = BandLight(key.Light);
            float fraction = key.Shrub ? 0.94f : ForestTreeAppearance.CrownFraction(key.Species, key.Stage);
            var p = new TreeParams();
            void Set(string name, double value) => p.SetParam(name, value.ToString(CultureInfo.InvariantCulture));
            Set("Scale", 1); Set("ScaleV", 0); Set("Leaves", 8); Set("LeafBend", 0);
            Set("Levels", juvenile ? 2 : 3); Set("Smooth", 0);
            Set("Shape", spruce ? TreeParams.Conical : key.Species == ForestSpecies.Birch ? TreeParams.Flame
                : key.Species == ForestSpecies.Oak ? TreeParams.Hemispherical : TreeParams.Spherical);
            Set("BaseSize", 1 - fraction); Set("Ratio", 0.02); Set("RatioPower", 1.6); Set("Flare", 0);
            Set("AttractionUp", spruce ? -0.4 : 0.5);
            Set("0Branches", key.Shrub ? 3 : 1); Set("0Length", 1); Set("0LengthV", key.Shrub ? 0.15 : 0);
            Set("0CurveRes", 4); Set("0Curve", spruce ? 0 : key.Stage == TreeLifeStage.Old ? 10 : 3);
            Set("0CurveV", spruce ? 2 : 5); Set("0DownAngle", key.Shrub ? 22 : 0); Set("0DownAngleV", key.Shrub ? 12 : 0);
            Set("0BranchDist", 0); Set("0SegSplits", 0); Set("0BaseSplits", 0);
            int count = juvenile ? 7 : spruce ? 14 : 11;
            if (key.Shrub) count = 5;
            Set("1Branches", Math.Max(4, (int)Math.Round(count * (0.62 + 0.38 * light))));
            Set("1Length", spruce ? 0.65 : key.Species == ForestSpecies.Oak ? 0.85 : 0.68);
            Set("1LengthV", 0.16); Set("1CurveRes", 2); Set("1Curve", spruce ? -22 : 28);
            Set("1CurveV", spruce ? 7 : 18); Set("1DownAngle", spruce ? 82 : juvenile ? 40 : 58);
            Set("1DownAngleV", 14); Set("1Rotate", spruce ? 137.5 : 110); Set("1RotateV", 20);
            Set("1SegSplits", 0); Set("1Taper", 1);
            Set("2Branches", 3); Set("2Length", 0.38); Set("2LengthV", 0.12); Set("2CurveRes", 2);
            Set("2Curve", spruce ? -12 : 18); Set("2CurveV", 12); Set("2DownAngle", 45);
            Set("2DownAngleV", 12); Set("2Rotate", 137.5); Set("2RotateV", 20); Set("2SegSplits", 0); Set("2Taper", 1);
            return p;
        }

        private sealed record Stem(int Level, List<Vector3> Points, List<float> Radii);
        private sealed class Samples : DefaultTreeTraversal
        {
            internal readonly List<Vector3> Leaves = new();
            internal readonly List<Stem> Stems = new();
            internal float Height, Radius;
            internal long StemCount;
            public override bool EnterStem(IStem stem)
            {
                if (stem.Level > 1) return true;
                var points = new List<Vector3>(); var radii = new List<float>(); int index = -1;
                foreach (var section in stem.Sections())
                {
                    if (index == section.Index) continue;
                    index = section.Index;
                    if (points.Count == 0) { points.Add(Convert(section.LowerPosition)); radii.Add((float)section.LowerRadius); }
                    points.Add(Convert(section.UpperPosition)); radii.Add((float)section.UpperRadius);
                }
                Stems.Add(new(stem.Level, points, radii)); return true;
            }
            public override bool VisitLeaf(ILeaf leaf) { Leaves.Add(Convert(leaf.Transform.GetT())); return true; }
        }
        private static Vector3 Convert(Vector3d p) => new((float)p.X, (float)p.Y, (float)p.Z);
        private static uint Pack(Color color, ForestSpecies species, float shade) =>
            (uint)Terrain.SurfaceSpeciesCode(species) << 24 | (uint)Math.Clamp((int)(color.R * shade), 0, 255)
            | (uint)Math.Clamp((int)(color.G * shade), 0, 255) << 8 | (uint)Math.Clamp((int)(color.B * shade), 0, 255) << 16;
    }
}
