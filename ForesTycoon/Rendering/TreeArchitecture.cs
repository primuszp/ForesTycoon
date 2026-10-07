using System;
using System.Collections.Generic;
using System.Globalization;
using DendroKit.Core.Params;
using DendroKit.Core.Tree;

namespace ForesTycoon
{
    /// <summary>
    /// Species architecture as Weber-Penn parameters (see docs/tree-generation-literature.md) and a
    /// thread-safe cache of the resulting skeletons. A skeleton depends only on species, life
    /// phase, light band, growth form and one of 32 seed variants; season, vitality and site
    /// shape are applied later, so the same branch system serves summer and winter.
    /// </summary>
    internal static class TreeArchitecture
    {
        internal const int Variants = 16;
        // Leaf positions only shape the crown; more than this adds cost, not silhouette.
        internal const int MaxLeafSamples = 160;
        // Every tree key (4 species x 6 phases x 3 light bands x 16 variants) plus both shrub forms.
        private const int Capacity = 4 * 6 * 3 * Variants + 2 * 6 * 3 * Variants;

        private readonly record struct Key(ForestSpecies Species, int Variant, TreeLifePhase Phase, int Light, int Form);
        private static readonly Dictionary<Key, TreeSkeleton> Cache = new();
        private static readonly Queue<Key> Order = new();

        internal static TreeSkeleton Skeleton(ForestSpecies species, int seed, TreeLifePhase phase, int lightBand, int form)
        {
            var key = new Key(species, seed & (Variants - 1), phase, lightBand, form);
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var skeleton)) return skeleton;
                var tree = new TreeImpl(key.Variant, Parameters(key));
                tree.Make();
                skeleton = TreeSkeleton.From(tree, key.Variant, MaxLeafSamples);
                if (Cache.Count == Capacity) Cache.Remove(Order.Dequeue());
                Cache.Add(key, skeleton); Order.Enqueue(key);
                return skeleton;
            }
        }

        /// <summary>Skeleton of an arbitrary Arbaro parameter file (reference and tooling use; not cached).</summary>
        internal static TreeSkeleton SkeletonFromXml(string xml, int variant, double baseSize = -1)
        {
            var p = new TreeParams();
            using (var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml))) p.ReadFromXml(stream);
            if (baseSize >= 0) p.SetParam("BaseSize", baseSize.ToString(CultureInfo.InvariantCulture));
            var tree = new TreeImpl(variant, p);
            tree.Make();
            return TreeSkeleton.From(tree, variant, MaxLeafSamples);
        }

        /// <summary>Crown fraction of the height for the species at the light of the band.</summary>
        internal static float CrownFraction(ForestSpecies species, TreeLifePhase phase, int lightBand, int form)
        {
            float light = TreeShapeBands.BandLight(lightBand);
            if (species == ForestSpecies.Spruce && form == 0)
            {
                // Spruce keeps its lower whorls and is branched to the ground in the open and while young;
                // only old trees in shade self-prune and become lanky with a high crown base.
                float age = phase switch { TreeLifePhase.Young => 0.25f, TreeLifePhase.Mature => 0.6f,
                    TreeLifePhase.Old or TreeLifePhase.Senescent => 1f, _ => 0f };
                return 0.98f - 0.55f * (1 - light) * age;
            }
            float fraction = form == (int)ShrubForm.Hazel ? 0.90f : form == (int)ShrubForm.Hawthorn ? 0.80f
                : TreeLifePhases.CrownFraction(species, phase);
            return Math.Clamp(fraction * (0.72f + 0.28f * LightResponse(species, light)), 0.2f, 0.95f);
        }

        internal static float LightResponse(ForestSpecies species, float light) =>
            MathF.Pow(Math.Clamp(light, 0, 1), 1 - ForestSpeciesProfile.For(species).ShadeTolerance * 0.65f);

        private static readonly Dictionary<ForestSpecies, Dictionary<string, double>> Presets = new();

        /// <summary>The Arbaro-format parameter file of a species, embedded in the assembly (Assets/Trees).</summary>
        internal static string PresetName(ForestSpecies species) => species switch
        {
            ForestSpecies.Spruce => "picea_abies", ForestSpecies.Oak => "quercus_robur",
            ForestSpecies.Birch => "betula_pendula", _ => "fagus_sylvatica"
        };

        private static Dictionary<string, double> Preset(ForestSpecies species)
        {
            lock (Presets)
            {
                if (Presets.TryGetValue(species, out var values)) return values;
                using var stream = typeof(TreeArchitecture).Assembly.GetManifestResourceStream("Trees." + PresetName(species) + ".xml")
                    ?? throw new InvalidOperationException("Missing tree preset " + PresetName(species));
                values = new(StringComparer.Ordinal);
                foreach (var el in System.Xml.Linq.XDocument.Load(stream).Descendants("param"))
                    if (double.TryParse((string)el.Attribute("value"), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        values[(string)el.Attribute("name")] = v;
                return Presets[species] = values;
            }
        }

        private static TreeParams Parameters(Key key) => key.Form != 0 ? ShrubParameters(key) : PresetParameters(key);

        /// <summary>
        /// The species' Arbaro parameter set (mature, well lit) adapted to life phase and light:
        /// fewer, shorter limbs and a single-level seedling; steeper, sparser limbs in shade;
        /// forking and coarser limbs with age.
        /// </summary>
        private static TreeParams PresetParameters(Key key)
        {
            var v = new Dictionary<string, double>(Preset(key.Species), StringComparer.Ordinal);
            var phase = key.Phase;
            float m = Math.Min(1, TreeLifePhases.Maturity(phase));
            bool juvenile = phase <= TreeLifePhase.Young, old = phase >= TreeLifePhase.Old, senescent = phase == TreeLifePhase.Senescent;
            float light = TreeShapeBands.BandLight(key.Light), shade = 1 - light;
            double count = phase switch { TreeLifePhase.Seedling => 0.5, TreeLifePhase.Sapling => 0.7, TreeLifePhase.Young => 0.85, TreeLifePhase.Senescent => 0.8, _ => 1 };
            v["BaseSize"] = 1 - CrownFraction(key.Species, phase, key.Light, 0);
            v["Levels"] = phase == TreeLifePhase.Seedling ? 2 : 3;
            v["1Branches"] = Math.Max(4, Math.Round(v["1Branches"] * count * (0.7 + 0.3 * light)));
            v["2Branches"] = Math.Max(3, Math.Round(v["2Branches"] * (juvenile ? 0.8 : 1)));
            switch (key.Species)
            {
                case ForestSpecies.Spruce:
                    // Massart model; shade-tolerant: in shade branches flatten, with age they droop.
                    v["AttractionUp"] = -0.3 - 0.3 * m;
                    v["1DownAngle"] += 8 * shade;
                    break;
                case ForestSpecies.Oak:
                    // Light-demanding: in shade few, short, steep limbs. Old oaks fork and thicken.
                    if (juvenile) { v["Shape"] = TreeParams.Spherical; v["0SegSplits"] = 0; v["0CurveV"] = 15; v["1SegSplits"] = 0; v["1Length"] = 0.62; v["1DownAngle"] = 48; }
                    v["AttractionUp"] += 0.7 * shade;
                    v["1DownAngle"] -= 16 * shade;
                    v["1Length"] *= 0.75 + 0.25 * light;
                    if (old) { v["0SegSplits"] = senescent ? 0.65 : 0.55; v["0SplitAngle"] = senescent ? 30 : 26; }
                    if (senescent) { v["1CurveV"] += 30; v["2Branches"] = Math.Max(3, Math.Round(v["2Branches"] * 0.7)); }
                    break;
                case ForestSpecies.Birch:
                    // Pioneer: shade gives a short, high, narrow crown; old trees carry longer pendulous shoots.
                    v["AttractionUp"] += 0.8 * shade;
                    v["1DownAngle"] -= 8 * shade;
                    v["1Length"] *= 0.75 + 0.25 * light;
                    if (old) v["2Length"] = 0.70;
                    break;
                default:
                    // Troll model; very shade-tolerant: shade yields a flatter, wider monolayer.
                    if (juvenile) v["Shape"] = TreeParams.TendFlame;
                    v["AttractionUp"] -= 0.5 * shade;
                    v["1DownAngle"] += 22 * shade;
                    if (old) { v["0SegSplits"] = senescent ? 0.45 : 0.35; v["0SplitAngle"] = senescent ? 20 : 16; }
                    break;
            }
            var p = new TreeParams();
            foreach (var pair in v) p.SetParam(pair.Key, pair.Value.ToString(CultureInfo.InvariantCulture));
            return p;
        }

        private static TreeParams ShrubParameters(Key key)
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
            var phase = key.Phase;
            float m = Math.Min(1, TreeLifePhases.Maturity(phase));
            bool juvenile = phase <= TreeLifePhase.Young;
            bool old = phase >= TreeLifePhase.Old;
            bool senescent = phase == TreeLifePhase.Senescent;
            float light = TreeShapeBands.BandLight(key.Light), shade = 1 - light;
            float fraction = CrownFraction(key.Species, phase, key.Light, key.Form);
            // Fewer first-order limbs on the very young; the aged lose some to self-pruning.
            double count = phase == TreeLifePhase.Seedling ? 0.6 : phase == TreeLifePhase.Sapling ? 0.8 : senescent ? 0.85 : 1;
            Set("Scale", 1); Set("ScaleV", 0); Set("Leaves", 4); Set("LeafBend", 0); Set("LeafDistrib", 4);
            Set("Levels", phase == TreeLifePhase.Seedling ? 2 : 3); Set("Smooth", 0);
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
                    // Picea abies (Massart): straight monopodial stem, regular tiers of plagiotropic
                    // branches; lower branches droop, tips curve up; second-order twigs hang
                    // ("comb" spruce). Shade-tolerant: in shade branches flatten.
                    Set("Shape", TreeParams.Conical);
                    Set("AttractionUp", -0.7 - 0.4 * m);
                    Level(0, 1, 1, 0, 0, 0, 0, 0, 0, 3, 4);
                    Level(1, (juvenile ? 14 : 22) * (0.7 + 0.3 * light) * count, 0.42, 0.12,
                        78 + 8 * shade, -32, 137.5, 26, -50, 10, 3);
                    Level(2, 6, 0.36, 0.08, 62, 10, 137.5, 0, 0, 25, 2);
                    break;
                case ForestSpecies.Oak:
                    // Quercus robur/petraea (Rauh, but tortuous by abortion of terminal buds): the
                    // trunk dissolves into few massive limbs; wide branch angles, zigzag twigs,
                    // broad crown. Light-demanding: in shade few, short, steep limbs.
                    Set("Shape", juvenile ? TreeParams.Spherical : TreeParams.Hemispherical);
                    Set("AttractionUp", (juvenile ? 0.25 : 0.10) + 0.7 * shade);
                    Level(0, 1, 1, 0, 0, 0, 0, 0, 0, juvenile ? 15 : 40, 5,
                        juvenile ? 0 : senescent ? 0.65 : old ? 0.55 : 0.30, senescent ? 30 : old ? 26 : 18);
                    // Few, long, unequal scaffold limbs give the lobed, irregular outline.
                    Level(1, (juvenile ? 9 : 11) * (0.6 + 0.4 * light) * count, (juvenile ? 0.62 : 0.85) * (0.75 + 0.25 * light), 0.32,
                        (juvenile ? 48 : 70) - 16 * shade, -24, 125, 30, -40, senescent ? 150 : 120, 4, juvenile ? 0 : 0.3, 24);
                    Level(2, senescent ? 6 : 8, 0.42, 0.12, 50, 15, 140, -20, 0, 90, 2);
                    break;
                case ForestSpecies.Birch:
                    // Betula pendula: slender excurrent stem, narrow ovoid crown, steep main
                    // branches arching outwards with long pendulous shoots. Pioneer, very
                    // light-demanding: shade gives a short, high, narrow crown.
                    Set("Shape", TreeParams.TendFlame);
                    Set("AttractionUp", -1.0 - 0.4 * m + 0.8 * shade);
                    Level(0, 1, 1, 0, 0, 0, 0, 0, 0, 12, 4);
                    Level(1, (juvenile ? 12 : 18) * (0.6 + 0.4 * light) * count, 0.40 * (0.75 + 0.25 * light), 0.12,
                        42 - 8 * shade, -24, 137.5, -30, 0, 35, 3);
                    Level(2, 6, old ? 0.70 : 0.55, 0.12, 30, 10, 137.5, -50, 0, 30, 2);
                    break;
                default:
                    // Fagus sylvatica (Troll): smooth limbs ascending steeply (often a forked V in
                    // old trees) carrying flat, layered sprays that fill a dense dome.
                    // Very shade-tolerant: shade yields a flatter, wider monolayer.
                    Set("Shape", juvenile ? TreeParams.TendFlame : TreeParams.Spherical);
                    Set("AttractionUp", (juvenile ? 0.3 : 0.12) - 0.5 * shade);
                    Level(0, 1, 1, 0, 0, 0, 0, 0, 0, 12, 5, senescent ? 0.45 : old ? 0.35 : 0, senescent ? 20 : 16);
                    Level(1, (juvenile ? 12 : 20) * (0.75 + 0.25 * light) * count, juvenile ? 0.55 : 0.62, 0.15,
                        (juvenile ? 42 : 52) + 22 * shade, -18, 137.5, 18, 0, 45, 3);
                    Level(2, 9, 0.45, 0.10, 72, 10, 137.5, 0, 0, 20, 2);
                    break;
            }
            return p;
        }
    }
}
