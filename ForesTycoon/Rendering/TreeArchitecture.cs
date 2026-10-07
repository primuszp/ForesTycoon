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
        // A mixed forest touches a fraction of species x phase x light x variant; ~22 KB each caps this near 45 MB.
        private const int Capacity = 2048;

        private readonly record struct Key(ForestSpecies Species, int Variant, TreeLifePhase Phase, int Light);
        private static readonly Dictionary<Key, TreeSkeleton> Cache = new();
        private static readonly Queue<Key> Order = new();

        internal static TreeSkeleton Skeleton(ForestSpecies species, int seed, TreeLifePhase phase, int lightBand)
        {
            var key = new Key(species, seed & (Variants - 1), phase, lightBand);
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
        internal static float CrownFraction(ForestSpecies species, TreeLifePhase phase, int lightBand)
        {
            float light = TreeShapeBands.BandLight(lightBand);
            // Whorled conifers keep their lower branches and are branched to the ground in the open and while
            // young; only old trees in shade self-prune and become lanky with a high crown base.
            float lift = species switch { ForestSpecies.Spruce => 0.55f, ForestSpecies.Fir => 0.40f, ForestSpecies.Larch => 0.60f, _ => 0 };
            if (lift > 0)
            {
                float age = phase switch { TreeLifePhase.Young => 0.25f, TreeLifePhase.Mature => 0.6f,
                    TreeLifePhase.Old or TreeLifePhase.Senescent => 1f, _ => 0f };
                return 0.98f - lift * (1 - light) * age;
            }
            return Math.Clamp(TreeLifePhases.CrownFraction(species, phase) * (0.72f + 0.28f * LightResponse(species, light)), 0.2f, 0.95f);
        }

        internal static float LightResponse(ForestSpecies species, float light) =>
            MathF.Pow(Math.Clamp(light, 0, 1), 1 - ForestSpeciesProfile.For(species).ShadeTolerance * 0.65f);

        private static readonly Dictionary<ForestSpecies, Dictionary<string, double>> Presets = new();

        /// <summary>The Arbaro-format parameter file of a species, embedded in the assembly (Assets/Trees).</summary>
        internal static string PresetName(ForestSpecies species) => ForestSpeciesTraits.For(species).Preset;

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

        /// <summary>
        /// The species' Arbaro parameter set (mature, well lit) adapted to life phase and light:
        /// fewer, shorter limbs and a single-level seedling; steeper, sparser limbs in shade;
        /// forking and coarser limbs with age.
        /// </summary>
        private static TreeParams Parameters(Key key)
        {
            var v = new Dictionary<string, double>(Preset(key.Species), StringComparer.Ordinal);
            var phase = key.Phase;
            float m = Math.Min(1, TreeLifePhases.Maturity(phase));
            bool juvenile = phase <= TreeLifePhase.Young, old = phase >= TreeLifePhase.Old, senescent = phase == TreeLifePhase.Senescent;
            float light = TreeShapeBands.BandLight(key.Light), shade = 1 - light;
            double count = phase switch { TreeLifePhase.Seedling => 0.5, TreeLifePhase.Sapling => 0.7, TreeLifePhase.Young => 0.85, TreeLifePhase.Senescent => 0.8, _ => 1 };
            v["BaseSize"] = 1 - CrownFraction(key.Species, phase, key.Light);
            v["Levels"] = phase == TreeLifePhase.Seedling ? 2 : 3;
            v["1Branches"] = Math.Max(4, Math.Round(v["1Branches"] * count * (0.7 + 0.3 * light)));
            v["2Branches"] = Math.Max(3, Math.Round(v["2Branches"] * (juvenile ? 0.8 : 1)));
            var traits = ForestSpeciesTraits.For(key.Species);
            // Shrubs differ in their number of basal stems from tree to tree.
            if (traits.Shrub && v.TryGetValue("0Branches", out double stems) && stems > 1) v["0Branches"] = stems + key.Variant % 3;
            float tolerance = ForestSpeciesProfile.For(key.Species).ShadeTolerance;
            switch (key.Species)
            {
                case ForestSpecies.Spruce:
                    // Massart model; shade-tolerant: in shade branches flatten, with age they droop.
                    v["AttractionUp"] = -0.3 - 0.3 * m;
                    v["1DownAngle"] += 8 * shade;
                    break;
                case ForestSpecies.Oak:
                case ForestSpecies.SessileOak:
                case ForestSpecies.TurkeyOak:
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
                case ForestSpecies.Beech:
                    // Troll model; very shade-tolerant: shade yields a flatter, wider monolayer.
                    if (juvenile) v["Shape"] = TreeParams.TendFlame;
                    v["AttractionUp"] -= 0.5 * shade;
                    v["1DownAngle"] += 22 * shade;
                    if (old) { v["0SegSplits"] = senescent ? 0.45 : 0.35; v["0SplitAngle"] = senescent ? 20 : 16; }
                    break;
                case ForestSpecies.Pine:
                    // Young pines are conical whorled trees; with age the leader gives way to a flat, irregular crown.
                    if (juvenile) { v["Shape"] = TreeParams.Conical; v["0CurveV"] = 15; v["1Length"] = 0.30; }
                    v["AttractionUp"] += 0.6 * shade;
                    v["1DownAngle"] -= 10 * shade;
                    if (old) { v["0SegSplits"] = Math.Max(v["0SegSplits"], 0.25); v["0SplitAngle"] = Math.Max(v["0SplitAngle"], 20); }
                    break;
                default:
                    // Other species: shade tolerance decides whether shade flattens (tolerant) or steepens (intolerant).
                    v["AttractionUp"] += (0.8 - 1.3 * tolerance) * shade;
                    v["1DownAngle"] += (-14 + 40 * tolerance) * shade;
                    if (tolerance < 0.5) v["1Length"] *= 0.75 + 0.25 * light;
                    if (juvenile && v["Shape"] == TreeParams.Hemispherical) v["Shape"] = TreeParams.Spherical;
                    if (old && !traits.Conifer && !traits.Shrub)
                    {
                        v["0SegSplits"] = Math.Max(v["0SegSplits"], senescent ? 0.40 : 0.25);
                        v["0SplitAngle"] = Math.Max(v["0SplitAngle"], 18);
                    }
                    break;
            }
            var p = new TreeParams();
            foreach (var pair in v) p.SetParam(pair.Key, pair.Value.ToString(CultureInfo.InvariantCulture));
            return p;
        }
    }
}
