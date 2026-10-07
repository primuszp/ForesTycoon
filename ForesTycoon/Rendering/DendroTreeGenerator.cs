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
    internal enum ShrubForm { Hazel = 1, Hawthorn = 2 }

    // Leaves are construction samples only. The renderer receives one opaque crown
    // and a few coarse woody stems, never leaf cards or the hidden twig mesh.
    // Species parameters follow docs/tree-generation-literature.md.
    internal static class DendroTreeGenerator
    {
        internal sealed record Mesh(Vertex[] Trunk, Vertex[] Branches, Vertex[] Crown, long StemCount, int LeafCount);
        private readonly record struct Key(ForestSpecies Species, int Variant, TreeLifeStage Stage, int Light, int Form);
        private static readonly Dictionary<Key, Samples> Cache = new();
        private static readonly Queue<Key> Order = new();
        // Every tree key (4 species x 4 stages x 3 light bands x 32 variants) plus shrubs.
        // Entries are small (<=160 leaf points and level 0-1 polylines, ~5 KB), so a
        // full-size cache avoids FIFO thrashing in large mixed forests.
        private const int Capacity = 1536 + 2 * 4 * 3 * 32;
        // Leaf positions only shape the crown; more than this adds cost, not silhouette.
        private const int MaxLeafSamples = 160;
        internal static int LightBand(float light) => light < 0.3f ? 0 : light < 0.65f ? 1 : 2;
        internal static float BandLight(int band) => band == 0 ? 0.15f : band == 1 ? 0.48f : 0.85f;

        internal static Mesh Generate(ForestSpecies species, int seed, TreeLifeStage stage,
            float light, ForestTreeDimensions size, float yaw, ForestLod lod, bool includeCrown = true) =>
            Build(species, seed, stage, light, size, yaw, lod, includeCrown, 0);

        internal static Mesh GenerateShrub(int seed, TreeLifeStage stage, float light,
            ForestTreeDimensions size, float yaw, ForestLod lod, ShrubForm form = ShrubForm.Hazel) =>
            Build(ForestSpecies.Beech, seed, stage, light, size, yaw, lod, true, (int)form);

        private static Mesh Build(ForestSpecies species, int seed, TreeLifeStage stage,
            float light, ForestTreeDimensions size, float yaw, ForestLod lod, bool includeCrown, int form)
        {
            if (species == ForestSpecies.None || !float.IsFinite(light) || !float.IsFinite(yaw) ||
                !float.IsFinite(size.Height + size.Diameter + size.CrownRadius) ||
                size.Height <= 0 || size.Diameter <= 0 || size.CrownRadius <= 0 ||
                (form != 0 && !Enum.IsDefined((ShrubForm)form)))
                throw new ArgumentOutOfRangeException(nameof(size));
            bool shrub = form != 0;
            light = Math.Clamp(light, 0, 1);
            float response = MathF.Pow(light, 1 - ForestSpeciesProfile.For(species).ShadeTolerance * 0.65f);
            float fraction = Math.Clamp(CrownFraction(species, stage, form) * (0.72f + 0.28f * response), 0.2f, 0.95f);
            float height = size.Height * Terrain.TreeMetresToWorld;
            float radius = size.CrownRadius * Terrain.TreeMetresToWorld * (0.74f + 0.26f * response);
            var key = new Key(species, seed & 31, stage, LightBand(light), form);
            Samples samples;
            lock (Cache)
            {
                if (!Cache.TryGetValue(key, out samples))
                {
                    var tree = new TreeImpl(key.Variant, Parameters(key)); tree.Make();
                    samples = new Samples(); tree.TraverseTree(samples);
                    samples.Height = (float)tree.Height; samples.StemCount = tree.StemCount;
                    samples.LeafCount = samples.Leaves.Count;
                    if (samples.Leaves.Count > MaxLeafSamples)
                    {
                        // Deterministic stride thinning keeps the spatial spread of the cloud.
                        var thinned = new List<Vector3>(MaxLeafSamples);
                        for (int i = 0; i < MaxLeafSamples; i++) thinned.Add(samples.Leaves[(int)((long)i * samples.Leaves.Count / MaxLeafSamples)]);
                        samples.Leaves.Clear(); samples.Leaves.AddRange(thinned);
                    }
                    foreach (var p in samples.Leaves) samples.Radius = Math.Max(samples.Radius, p.Xy.Length);
                    foreach (var stem in samples.Stems)
                        if (stem.Level == 0) samples.TrunkRadius = Math.Max(samples.TrunkRadius, stem.Radii[0]);
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
            Color bark = form == (int)ShrubForm.Hazel ? Color.FromArgb(128, 98, 72)
                : form == (int)ShrubForm.Hawthorn ? Color.FromArgb(104, 96, 86)
                : species == ForestSpecies.Birch && stage == TreeLifeStage.Seedling ? Color.FromArgb(115, 77, 48) : model.TrunkColor;
            Color leaf = form == (int)ShrubForm.Hazel ? Color.FromArgb(104, 146, 62)
                : form == (int)ShrubForm.Hawthorn ? Color.FromArgb(66, 104, 50) : model.CrownColor;
            uint woodColor = Pack(bark, species, pigment);
            // The widest DendroKit trunk section maps to the simulated breast-height radius.
            float woodScale = size.Diameter * 0.5f * Terrain.TreeMetresToWorld / Math.Max(1e-6f, samples.TrunkRadius);
            float referencePixels = DendroCrownMesh.ReferencePixels(lod);
            int branchBudget = lod == ForestLod.Near ? (species == ForestSpecies.Oak || shrub ? 8 : 6) : 3;
            float exposure = shrub ? 0.6f : species switch
            {
                // Fraction of a first-order limb shown below/inside the crown surface.
                ForestSpecies.Oak => 0.50f, ForestSpecies.Beech => 0.45f,
                ForestSpecies.Birch => 0.35f, _ => 0.25f
            };
            bool firstTrunk = true;
            if (lod != ForestLod.Far)
            foreach (var stem in samples.Stems)
            {
                if (stem.Level > 1) continue;
                // Only the main stem gets the full curve resolution; forks and shrub stems
                // are short or thin enough that two segments keep the silhouette.
                bool mainStem = stem.Level == 0 && firstTrunk && !shrub;
                if (stem.Level == 0) firstTrunk = false;
                float baseRadius = stem.Radii[0] * woodScale;
                // Skip limbs thinner than ~0.6 px at the LOD reference zoom: invisible, not free.
                if (stem.Level == 1 && (baseRadius * referencePixels < 0.6f || branchBudget-- <= 0)) continue;
                var target = stem.Level == 0 ? trunk : branches;
                int segments = mainStem ? (lod == ForestLod.Near ? 4 : 2) : stem.Level == 0 && lod == ForestLod.Near ? 2 : 1;
                int sides = DendroCrownMesh.Sides(baseRadius, lod, 3, stem.Level == 0 ? 6 : 4, 0.5f);
                float limit = includeCrown ? height * (1 - fraction * (stem.Level == 0 ? 0.45f : 0.15f)) : float.MaxValue;
                for (int segment = 0; segment < segments; segment++)
                {
                    int a = segment * (stem.Points.Count - 1) / segments;
                    int b = (segment + 1) * (stem.Points.Count - 1) / segments;
                    if (a == b) continue;
                    Vector3 start = Transform(stem.Points[a]), end = Transform(stem.Points[b]);
                    if (start.Z > limit) break;
                    float ra = stem.Radii[a] * woodScale, rb = stem.Radii[b] * woodScale;
                    if (includeCrown && stem.Level > 0)
                    {
                        // Only expose the structural fork. Fine outer branches are
                        // hidden by foliage and can protrude through a coarse envelope.
                        end = Vector3.Lerp(start, end, exposure);
                        rb = ra + (rb - ra) * exposure;
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
            var crownForm = form == (int)ShrubForm.Hazel ? CrownForm.Hazel
                : form == (int)ShrubForm.Hawthorn ? CrownForm.Hawthorn : DendroCrownMesh.For(species);
            var crown = includeCrown ? DendroCrownMesh.Build(crownForm, seed, stage, height, radius,
                fraction, yaw, support, Pack(leaf, species, pigment), lod) : Array.Empty<Vertex>();
            return new(trunk.ToArray(), branches.ToArray(), crown, samples.StemCount, samples.LeafCount);

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

        private static float CrownFraction(ForestSpecies species, TreeLifeStage stage, int form) =>
            form == (int)ShrubForm.Hazel ? 0.90f : form == (int)ShrubForm.Hawthorn ? 0.80f
            : ForestTreeAppearance.CrownFraction(species, stage);

        // Weber & Penn (1995) parameters. Baselines come from the Arbaro presets
        // (european_larch/tamarack, ca_black_oak, quaking_aspen, black_tupelo, desert_bush)
        // and are adjusted to the architecture of the European species:
        // spruce = Massart model, oak = Rauh model, beech = Troll model.
        private static TreeParams Parameters(Key key)
        {
            var p = new TreeParams();
            void Set(string name, double value) => p.SetParam(name, value.ToString(CultureInfo.InvariantCulture));
            void Level(int level, double branches, double length, double lengthV, double down, double downV,
                double rotate, double curve, double curveBack, double curveV, int curveRes = 3,
                double segSplits = 0, double splitAngle = 0)
            {
                string l = level.ToString(CultureInfo.InvariantCulture);
                if (level > 0) Set(l + "Branches", Math.Max(1, Math.Round(branches)));
                Set(l + "Length", length); Set(l + "LengthV", lengthV);
                if (level > 0) { Set(l + "DownAngle", down); Set(l + "DownAngleV", downV); Set(l + "Rotate", rotate); Set(l + "RotateV", 20); }
                Set(l + "Curve", curve); Set(l + "CurveBack", curveBack); Set(l + "CurveV", curveV); Set(l + "CurveRes", curveRes);
                Set(l + "SegSplits", segSplits); Set(l + "SplitAngle", splitAngle); Set(l + "SplitAngleV", splitAngle * 0.4);
                Set(l + "Taper", 1);
            }
            int stageIndex = (int)key.Stage; // seedling 0 .. old 3
            bool juvenile = key.Stage is TreeLifeStage.Seedling or TreeLifeStage.Young;
            bool old = key.Stage == TreeLifeStage.Old;
            float light = BandLight(key.Light), shade = 1 - light;
            float fraction = CrownFraction(key.Species, key.Stage, key.Form);
            Set("Scale", 1); Set("ScaleV", 0); Set("Leaves", 4); Set("LeafBend", 0); Set("LeafDistrib", 4);
            Set("Levels", key.Stage == TreeLifeStage.Seedling ? 2 : 3); Set("Smooth", 0);
            Set("Ratio", 0.02); Set("RatioPower", 1.4); Set("Flare", 0);
            Set("BaseSize", 1 - fraction);
            Set("0Branches", 1); Set("0BaseSplits", 0); Set("0DownAngle", 0); Set("0DownAngleV", 0);
            Set("0BranchDist", 0); Set("0Rotate", 0); Set("0RotateV", 0);

            switch (key.Form)
            {
                case (int)ShrubForm.Hazel:
                    // Corylus avellana: 5-9 ascending basal shoots forming an open vase.
                    Set("Shape", TreeParams.TendFlame); Set("AttractionUp", 0.6); Set("BaseSize", 0.15);
                    Set("0Branches", 5 + key.Variant % 4); Set("0Rotate", 137.5); Set("0RotateV", 30); Set("0DownAngle", 22); Set("0DownAngleV", 10);
                    Set("0BranchDist", 0.02);
                    Level(0, 1, 1, 0.25, 0, 0, 0, 18, -10, 20, 4);
                    Level(1, 6 + 4 * light, 0.45, 0.15, 55, 15, 140, -15, 0, 30);
                    Level(2, 5, 0.40, 0.10, 45, 10, 137.5, 0, 0, 30, 2);
                    return p;
                case (int)ShrubForm.Hawthorn:
                    // Crataegus monogyna: short, often forked stems; dense tangled crown.
                    Set("Shape", TreeParams.Spherical); Set("AttractionUp", 0.3); Set("BaseSize", 0.2);
                    Set("0Branches", 2 + key.Variant % 2); Set("0Rotate", 150); Set("0RotateV", 30); Set("0DownAngle", 10); Set("0DownAngleV", 8);
                    Level(0, 1, 1, 0.15, 0, 0, 0, 0, 0, 40, 4, 0.35, 25);
                    Level(1, 10 + 6 * light, 0.60, 0.20, 60, 20, 110, 0, 0, 140, 3, 0.2, 20);
                    Level(2, 6, 0.40, 0.10, 50, 15, 137.5, 0, 0, 90, 2);
                    return p;
            }

            switch (key.Species)
            {
                case ForestSpecies.Spruce:
                    // Picea abies (Massart): straight monopodial stem, regular tiers of
                    // plagiotropic branches; lower branches droop, tips curve up; second-order
                    // twigs hang ("comb" spruce). Shade-tolerant: in shade branches flatten.
                    Set("Shape", TreeParams.Conical);
                    Set("AttractionUp", -1.2 - 0.6 * stageIndex / 3.0);
                    Level(0, 1, 1, 0, 0, 0, 0, 0, 0, 3, 4);
                    Level(1, (juvenile ? 14 : 22) * (0.7 + 0.3 * light), 0.42, 0.12,
                        78 + 8 * shade, -32, 137.5, 26, -50, 10, 3);
                    Level(2, 6, 0.36, 0.08, 62, 10, 137.5, 0, 0, 25, 2);
                    break;
                case ForestSpecies.Oak:
                    // Quercus robur/petraea (Rauh, but tortuous by abortion of terminal buds):
                    // the trunk dissolves into few massive limbs; wide branch angles, zigzag
                    // twigs, broad crown. Light-demanding: in shade few, short, steep limbs.
                    Set("Shape", juvenile ? TreeParams.Spherical : TreeParams.Hemispherical);
                    Set("AttractionUp", 0.25 + 0.7 * shade);
                    Level(0, 1, 1, 0, 0, 0, 0, 0, 0, juvenile ? 15 : 40, 5,
                        juvenile ? 0 : old ? 0.55 : 0.30, old ? 26 : 18);
                    // Few, long, unequal scaffold limbs give the lobed, irregular outline.
                    Level(1, (juvenile ? 9 : 11) * (0.6 + 0.4 * light), (juvenile ? 0.62 : 0.85) * (0.75 + 0.25 * light), 0.32,
                        (juvenile ? 48 : 62) - 16 * shade, -24, 125, 30, -40, 120, 4, juvenile ? 0 : 0.3, 24);
                    Level(2, 8, 0.42, 0.12, 50, 15, 140, -20, 0, 90, 2);
                    break;
                case ForestSpecies.Birch:
                    // Betula pendula: slender excurrent stem, narrow ovoid crown, steep main
                    // branches arching outwards with long pendulous shoots. Pioneer, very
                    // light-demanding: shade gives a short, high, narrow crown.
                    Set("Shape", TreeParams.TendFlame);
                    Set("AttractionUp", -1.0 - 0.4 * stageIndex / 3.0 + 0.8 * shade);
                    Level(0, 1, 1, 0, 0, 0, 0, 0, 0, 12, 4);
                    Level(1, (juvenile ? 12 : 18) * (0.6 + 0.4 * light), 0.40 * (0.75 + 0.25 * light), 0.12,
                        42 - 8 * shade, -24, 137.5, -30, 0, 35, 3);
                    Level(2, 6, old ? 0.70 : 0.55, 0.12, 30, 10, 137.5, -50, 0, 30, 2);
                    break;
                default:
                    // Fagus sylvatica (Troll): smooth limbs ascending steeply (often a forked
                    // V in old trees) carrying flat, layered sprays that fill a dense dome.
                    // Very shade-tolerant: shade yields a flatter, wider monolayer.
                    Set("Shape", juvenile ? TreeParams.TendFlame : TreeParams.Spherical);
                    Set("AttractionUp", 0.3 - 0.5 * shade);
                    Level(0, 1, 1, 0, 0, 0, 0, 0, 0, 12, 5, old ? 0.35 : 0, 16);
                    Level(1, (juvenile ? 12 : 20) * (0.75 + 0.25 * light), juvenile ? 0.55 : 0.62, 0.15,
                        (juvenile ? 42 : 38) + 22 * shade, -18, 137.5, 18, 0, 45, 3);
                    Level(2, 9, 0.45, 0.10, 72, 10, 137.5, 0, 0, 20, 2);
                    break;
            }
            return p;
        }

        private sealed record Stem(int Level, List<Vector3> Points, List<float> Radii);
        private sealed class Samples : DefaultTreeTraversal
        {
            internal readonly List<Vector3> Leaves = new();
            internal readonly List<Stem> Stems = new();
            internal float Height, Radius, TrunkRadius;
            internal long StemCount;
            internal int LeafCount;
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
                if (points.Count > 1) Stems.Add(new(stem.Level, points, radii));
                return true;
            }
            public override bool VisitLeaf(ILeaf leaf) { Leaves.Add(Convert(leaf.Transform.GetT())); return true; }
        }
        private static Vector3 Convert(Vector3d p) => new((float)p.X, (float)p.Y, (float)p.Z);
        private static uint Pack(Color color, ForestSpecies species, float shade) =>
            (uint)Terrain.SurfaceSpeciesCode(species) << 24 | (uint)Math.Clamp((int)(color.R * shade), 0, 255)
            | (uint)Math.Clamp((int)(color.G * shade), 0, 255) << 8 | (uint)Math.Clamp((int)(color.B * shade), 0, 255) << 16;
    }
}
