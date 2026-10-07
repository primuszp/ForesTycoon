using System;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    // The contract between the forest simulation and the procedural tree generator.
    // The simulation describes one individual (species, physical size, life phase,
    // vigour, site and season) as a TreeShapeSpec; the generator answers with a mesh
    // (DendroTreeGenerator) or with physical shape metrics (TreeShapeModel). Nothing
    // here depends on GL, so the simulation, tests and tools can use it freely.

    /// <summary>Finer-grained life phases than the legacy four-step <see cref="TreeLifeStage"/>.</summary>
    internal enum TreeLifePhase : byte { Seedling, Sapling, Young, Mature, Old, Senescent }

    /// <summary>Foliage state of a (deciduous) crown through the year.</summary>
    internal enum LeafState : byte { Bare, Budding, Full, Autumn, Falling }

    /// <summary>Coarse vitality class: drives crown dieback, not physical size.</summary>
    internal enum TreeVigorBand : byte { Vigorous, Reduced, Declining, Dying }

    internal static class TreeLifePhases
    {
        internal static TreeLifePhase Of(ForestSpecies species, float age)
        {
            var profile = ForestSpeciesProfile.For(species);
            return age < SeedlingAge(species) ? TreeLifePhase.Seedling
                : age < profile.MatureAgeYears * 0.30f ? TreeLifePhase.Sapling
                : age < profile.MatureAgeYears * 0.75f ? TreeLifePhase.Young
                : age < profile.MaximumAgeYears * 0.65f ? TreeLifePhase.Mature
                : age < profile.MaximumAgeYears * 0.88f ? TreeLifePhase.Old : TreeLifePhase.Senescent;
        }

        /// <summary>Age at which the phase after <paramref name="phase"/> begins.</summary>
        internal static float NextAge(ForestSpecies species, TreeLifePhase phase)
        {
            var profile = ForestSpeciesProfile.For(species);
            return phase switch
            {
                TreeLifePhase.Seedling => SeedlingAge(species),
                TreeLifePhase.Sapling => profile.MatureAgeYears * 0.30f,
                TreeLifePhase.Young => profile.MatureAgeYears * 0.75f,
                TreeLifePhase.Mature => profile.MaximumAgeYears * 0.65f,
                TreeLifePhase.Old => profile.MaximumAgeYears * 0.88f,
                _ => float.PositiveInfinity
            };
        }

        // Shrubs mature in a few years, so their seedling phase is correspondingly short.
        private static float SeedlingAge(ForestSpecies species) =>
            Math.Min(species == ForestSpecies.Birch ? 2f : 3f, 0.15f * ForestSpeciesProfile.For(species).MatureAgeYears);

        /// <summary>Mapping onto the legacy stages used by the glTF and lobe-based crowns.</summary>
        internal static TreeLifeStage Coarse(TreeLifePhase phase) => phase switch
        {
            TreeLifePhase.Seedling => TreeLifeStage.Seedling,
            TreeLifePhase.Sapling or TreeLifePhase.Young => TreeLifeStage.Young,
            TreeLifePhase.Mature => TreeLifeStage.Mature,
            _ => TreeLifeStage.Old
        };

        internal static TreeLifePhase From(TreeLifeStage stage) => stage switch
        {
            TreeLifeStage.Seedling => TreeLifePhase.Seedling,
            TreeLifeStage.Young => TreeLifePhase.Young,
            TreeLifeStage.Mature => TreeLifePhase.Mature,
            _ => TreeLifePhase.Old
        };

        /// <summary>0 = just germinated ... 1 = fully grown; later phases stay above 1.</summary>
        internal static float Maturity(TreeLifePhase phase) => phase switch
        {
            TreeLifePhase.Seedling => 0f, TreeLifePhase.Sapling => 0.2f, TreeLifePhase.Young => 0.45f,
            TreeLifePhase.Mature => 0.75f, TreeLifePhase.Old => 1f, _ => 1.15f
        };

        /// <summary>Broadly the crown fraction of the total height, before light and vigour.</summary>
        internal static float CrownFraction(ForestSpecies species, TreeLifePhase phase)
        {
            if (ForestSpeciesTraits.For(species).Shrub) return 0.98f;
            if (species == ForestSpecies.Pine)
                // Scots pine self-prunes early: tiered cone when young, a high flat crown on a long clear bole later.
                return phase switch { TreeLifePhase.Seedling => 0.95f, TreeLifePhase.Sapling => 0.92f, TreeLifePhase.Young => 0.78f,
                    TreeLifePhase.Mature => 0.46f, TreeLifePhase.Old => 0.40f, _ => 0.34f };
            float mature = species switch { ForestSpecies.Ash => 0.62f, ForestSpecies.Maple => 0.68f, ForestSpecies.SessileOak => 0.66f,
                ForestSpecies.TurkeyOak => 0.70f, ForestSpecies.Larch or ForestSpecies.Fir => 0.86f, _ => -1f };
            if (mature < 0)
                return phase switch
                {
                    TreeLifePhase.Sapling => 0.5f * (ForestTreeAppearance.CrownFraction(species, TreeLifeStage.Seedling)
                        + ForestTreeAppearance.CrownFraction(species, TreeLifeStage.Young)),
                    // Old broadleaves keep a broad low crown; they lose a little from the base and a lot from the top.
                    TreeLifePhase.Old => species == ForestSpecies.Spruce ? ForestTreeAppearance.CrownFraction(species, TreeLifeStage.Old)
                        : ForestTreeAppearance.CrownFraction(species, TreeLifeStage.Mature) - 0.04f,
                    TreeLifePhase.Senescent => species == ForestSpecies.Spruce ? ForestTreeAppearance.CrownFraction(species, TreeLifeStage.Old) - 0.05f
                        : ForestTreeAppearance.CrownFraction(species, TreeLifeStage.Mature) - 0.10f,
                    _ => ForestTreeAppearance.CrownFraction(species, Coarse(phase))
                };
            return phase switch
            {
                TreeLifePhase.Seedling => 0.90f, TreeLifePhase.Sapling => 0.86f, TreeLifePhase.Young => 0.80f,
                TreeLifePhase.Mature => mature, TreeLifePhase.Old => mature - 0.04f, _ => mature - 0.10f
            };
        }
    }

    /// <summary>
    /// Leaf phenology as a pure function of the forest year fraction and the tree seed, so that it
    /// replays exactly. The year starts in spring: 0.25 is mid-summer, 0.75 the coldest point
    /// (matching <see cref="WeatherSystem"/>'s temperature curve).
    /// </summary>
    internal static class TreePhenology
    {
        internal static bool Evergreen(ForestSpecies species) => ForestSpeciesTraits.For(species).Evergreen;

        // Segment ends within the cycle that starts at bud burst: budding, full, autumn colour, leaf fall.
        private readonly record struct Calendar(float Start, float Budding, float Full, float Autumn, float Falling);
        private static Calendar For(ForestSpecies species) => species switch
        {
            ForestSpecies.Birch => new(0.95f, 0.07f, 0.49f, 0.60f, 0.69f),
            ForestSpecies.Oak or ForestSpecies.SessileOak => new(0.97f, 0.07f, 0.49f, 0.59f, 0.67f),
            ForestSpecies.TurkeyOak => new(0.99f, 0.07f, 0.53f, 0.64f, 0.72f),
            ForestSpecies.Maple => new(0.96f, 0.07f, 0.50f, 0.60f, 0.68f),
            // Ash breaks bud late and drops its leaves early, often still green.
            ForestSpecies.Ash => new(0.04f, 0.08f, 0.46f, 0.54f, 0.62f),
            ForestSpecies.Larch => new(0.95f, 0.08f, 0.52f, 0.64f, 0.72f),
            ForestSpecies.Hazel => new(0.93f, 0.07f, 0.50f, 0.60f, 0.68f),
            ForestSpecies.Hawthorn or ForestSpecies.Blackthorn => new(0.95f, 0.07f, 0.52f, 0.62f, 0.69f),
            ForestSpecies.Elder => new(0.92f, 0.07f, 0.52f, 0.62f, 0.69f),
            _ => new(0.98f, 0.07f, 0.52f, 0.62f, 0.69f)
        };

        // Individuals differ by a few days; the offset is part of the tree, not of the weather.
        private static float Offset(uint seed) => (ForestTreeStore.Unit(ForestTreeStore.Random(seed + 4027)) - 0.5f) * 0.03f;

        private static float Phase(ForestSpecies species, uint seed, double year)
        {
            var calendar = For(species);
            double t = year - calendar.Start - Offset(seed);
            return (float)(t - Math.Floor(t));
        }

        internal static LeafState At(ForestSpecies species, uint seed, double year)
        {
            if (Evergreen(species)) return LeafState.Full;
            var c = For(species);
            float t = Phase(species, seed, year);
            return t < c.Budding ? LeafState.Budding : t < c.Full ? LeafState.Full
                : t < c.Autumn ? LeafState.Autumn : t < c.Falling ? LeafState.Falling : LeafState.Bare;
        }

        /// <summary>First forest year after <paramref name="year"/> at which the leaf state differs.</summary>
        internal static double NextChange(ForestSpecies species, uint seed, double year)
        {
            if (Evergreen(species)) return double.PositiveInfinity;
            var c = For(species);
            float t = Phase(species, seed, year);
            float next = t < c.Budding ? c.Budding : t < c.Full ? c.Full : t < c.Autumn ? c.Autumn
                : t < c.Falling ? c.Falling : 1f;
            return year + (next - t) + 1e-4;
        }
    }

    /// <summary>Long-lived growing conditions of one individual that shape its form.</summary>
    /// <param name="Light">Relative light 0..1 (<see cref="ForestResources.Light"/>).</param>
    /// <param name="Water">Water availability 0..1; low values mean drought stress.</param>
    /// <param name="Wind">Persistent wind exposure 0..1 (open, elevated sites).</param>
    /// <param name="WindAngle">Direction the wind blows towards, radians in the ground plane.</param>
    /// <param name="GapAngle">Direction of the nearest canopy gap, radians in the ground plane.</param>
    /// <param name="GapStrength">0 = evenly surrounded, 1 = neighbours all on one side.</param>
    internal readonly record struct TreeSite(float Light = 1, float Water = 1, float Wind = 0,
        float WindAngle = 0, float GapAngle = 0, float GapStrength = 0)
    {
        internal static TreeSite Open => new();

        internal TreeSite Sanitized() => new(Clamp01(Light, 1), Clamp01(Water, 1), Clamp01(Wind, 0),
            Finite(WindAngle), Finite(GapAngle), Clamp01(GapStrength, 0));
        private static float Clamp01(float v, float fallback) => float.IsFinite(v) ? Math.Clamp(v, 0, 1) : fallback;
        private static float Finite(float v) => float.IsFinite(v) ? v : 0;
    }

    /// <summary>
    /// Everything the generator needs to know about one tree or shrub. Continuous inputs are quantised by
    /// <see cref="Bands"/>, so a tree only needs a new mesh when a band boundary is crossed.
    /// </summary>
    internal readonly record struct TreeShapeSpec(ForestSpecies Species, int Seed, TreeLifePhase Phase,
        ForestTreeDimensions Size, float Vigor, TreeSite Site, LeafState Leaves, float Yaw, bool Dead = false)
    {
        internal static TreeShapeSpec From(in ForestTree tree, double year, TreeSite site, float yaw, bool dead = false)
        {
            // Persistent stress erodes vigour beyond the momentary health value.
            float vigor = dead ? 0 : tree.Health * (1 - 0.5f * Math.Clamp(tree.StressYears / 4f, 0, 1));
            site = site with { Light = tree.Resources.Light, Water = tree.Resources.Water };
            return new(tree.Species, unchecked((int)tree.Seed), TreeLifePhases.Of(tree.Species, tree.Age(year)),
                tree.At(year), vigor, site, dead ? LeafState.Bare : TreePhenology.At(tree.Species, tree.Seed, year), yaw, dead);
        }

        internal TreeShapeBands Bands => TreeShapeBands.Of(this);

        /// <summary>Equal keys mean equal geometry: changes only when a band boundary or the phase changes.</summary>
        internal int ShapeKey => Bands.Pack() | (int)Phase << 17;
    }

    /// <summary>Quantised shape inputs. Equal bands always produce an identical model.</summary>
    internal readonly record struct TreeShapeBands(int Light, TreeVigorBand Vigor, int Water, int Wind,
        int GapDirection, int GapStrength, LeafState Leaves, bool Dead)
    {
        internal const int GapSectors = 8;
        internal static float BandLight(int band) => band == 0 ? 0.15f : band == 1 ? 0.48f : 0.85f;
        internal static int LightBand(float light) => light < 0.3f ? 0 : light < 0.65f ? 1 : 2;
        internal static TreeVigorBand VigorBand(float vigor) =>
            vigor < 0.25f ? TreeVigorBand.Dying : vigor < 0.5f ? TreeVigorBand.Declining
            : vigor < 0.75f ? TreeVigorBand.Reduced : TreeVigorBand.Vigorous;

        internal static TreeShapeBands Of(in TreeShapeSpec spec)
        {
            var site = spec.Site.Sanitized();
            int strength = site.GapStrength < 0.25f ? 0 : site.GapStrength < 0.6f ? 1 : 2;
            int sector = strength == 0 ? 0 : (int)MathF.Round(Wrap(site.GapAngle) / MathF.Tau * GapSectors) % GapSectors;
            return new(LightBand(site.Light), spec.Dead ? TreeVigorBand.Dying : VigorBand(float.IsFinite(spec.Vigor) ? spec.Vigor : 1),
                site.Water < 0.3f ? 0 : site.Water < 0.6f ? 1 : 2, site.Wind < 0.3f ? 0 : site.Wind < 0.65f ? 1 : 2,
                sector, strength, spec.Leaves, spec.Dead);
        }

        internal float Light01 => BandLight(Light);
        internal float GapAngle => GapDirection * MathF.Tau / GapSectors;
        internal float GapBias => GapStrength == 0 ? 0 : GapStrength == 1 ? 0.5f : 1f;
        internal float WindBias => Wind == 0 ? 0 : Wind == 1 ? 0.5f : 1f;
        internal bool Drought => Water == 0;

        /// <summary>Share of the live crown (0..1) lost to dieback for the phase.</summary>
        internal float Dieback(TreeLifePhase phase)
        {
            float d = Vigor switch { TreeVigorBand.Vigorous => 0f, TreeVigorBand.Reduced => 0.08f,
                TreeVigorBand.Declining => 0.30f, _ => 0.60f };
            if (Water == 0) d += 0.08f;
            if (phase == TreeLifePhase.Senescent) d += 0.18f;
            else if (phase == TreeLifePhase.Old) d += 0.04f;
            return Math.Clamp(d, 0, 0.9f);
        }

        internal int Pack() => Light | (int)Vigor << 2 | Water << 4 | Wind << 6 | GapDirection << 8
            | GapStrength << 11 | (int)Leaves << 13 | (Dead ? 1 : 0) << 16;

        private static float Wrap(float angle) { angle %= MathF.Tau; return angle < 0 ? angle + MathF.Tau : angle; }
    }

    /// <summary>Physical shape facts the generator reports back to the simulation (metres).</summary>
    /// <param name="TrunkVolume">Volume of the main stem from the ground to its tip, m3.</param>
    /// <param name="WoodVolume">Stem plus all limbs, m3.</param>
    /// <param name="FormFactor">Trunk volume relative to a cylinder of breast-height diameter and full height.</param>
    /// <param name="CrownBaseHeight">Height of the lowest living limb, m.</param>
    /// <param name="CrownRadius">Largest living crown radius after environmental shaping, m.</param>
    /// <param name="CrownProjectionArea">Ground area under the crown outline, m2.</param>
    /// <param name="CrownVolume">Volume enclosed by the crown envelope, m3.</param>
    /// <param name="CrownOffset">Horizontal offset of the crown centre from the stem base, m.</param>
    /// <param name="LiveFoliage">Share of the crown carrying leaves now (phenology and dieback), 0..1.</param>
    /// <param name="Dieback">Share of the crown lost to dieback, 0..1.</param>
    /// <param name="Limbs">Number of first-order limbs.</param>
    internal readonly record struct TreeShapeMetrics(float TrunkVolume, float WoodVolume, float FormFactor,
        float CrownBaseHeight, float CrownRadius, float CrownProjectionArea, float CrownVolume,
        Vector2 CrownOffset, float LiveFoliage, float Dieback, int Limbs);
}
