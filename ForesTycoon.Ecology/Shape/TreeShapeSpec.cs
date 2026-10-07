using System;
using OpenTK.Mathematics;

namespace ForesTycoon.Ecology
{
    // The contract between the forest simulation and the procedural tree generator.
    // The simulation describes one individual (species, physical size, life phase,
    // vigour, site and season) as a TreeShapeSpec; the generator answers with a mesh
    // or with physical shape metrics. Nothing here depends on GL.

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
