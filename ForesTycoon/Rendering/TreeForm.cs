using System;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>
    /// Smooth, continuous warp of the ground plane applied to every point of a tree (skeleton, crown
    /// and roots alike), so the topology is untouched: a competitor-driven lean towards the nearest
    /// gap, and a crown skew that stretches towards open space or downwind.
    /// </summary>
    internal readonly record struct TreeDeformation(Vector2 Lean, Vector2 Skew, float Height)
    {
        internal bool IsIdentity => Lean == Vector2.Zero && Skew == Vector2.Zero;

        internal static TreeDeformation From(in TreeShapeBands bands, float height)
        {
            var gap = new Vector2(MathF.Cos(bands.GapAngle), MathF.Sin(bands.GapAngle)) * bands.GapBias;
            // The wind blows towards WindAngle, which is not part of the bands: it is only known
            // as a strength here, the direction is applied by TreeForm.
            return new(gap * (0.05f * (1 - 0.5f * bands.Light01)), gap * 0.40f, height);
        }

        internal TreeDeformation WithWind(float angle, float bias)
        {
            if (bias <= 0) return this;
            var down = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * bias;
            return this with { Lean = Lean + down * 0.035f, Skew = Skew + down * 0.30f };
        }

        /// <summary>Displaces only horizontally, so heights are preserved.</summary>
        internal Vector3 Apply(Vector3 p)
        {
            if (IsIdentity) return p;
            float t = Math.Clamp(p.Z / Math.Max(1e-6f, Height), 0, 1);
            Vector2 rel = p.Xy;
            float r = rel.Length;
            if (r > 1e-7f) rel *= Math.Clamp(1 + Vector2.Dot(Skew, rel / r), 0.55f, 1.6f);
            rel += Lean * Height * t * t;
            return new(rel.X, rel.Y, p.Z);
        }
    }

    /// <summary>
    /// A <see cref="TreeShapeSpec"/> resolved against its architecture: the skeleton, the mapping from
    /// generator units into the world, the environmental warp, the thickness of the twigs and the
    /// colours. Both the mesh generator and the shape metrics are built on it.
    /// </summary>
    internal sealed class TreeForm
    {
        internal readonly TreeShapeSpec Spec;
        internal readonly TreeShapeBands Bands;
        internal readonly TreeSkeleton Skeleton;
        internal readonly float Height, CrownRadius, CrownFraction, CrownBase;
        internal readonly float XyScale, ZScale;
        /// <summary>Radius of the thinnest twig, world units. A stem of flow f has radius TwigRadius * f^(1/n).</summary>
        internal readonly float TwigRadius;
        internal readonly float BreastRadius, BreastHeight;
        internal readonly float FlareStrength, FlareHeight;
        internal readonly float Dieback, Foliage;
        internal readonly bool[] Dead;
        internal readonly TreeDeformation Warp;
        internal readonly bool Shrub;
        internal readonly uint WoodColor, DeadColor, CrownColor;
        private readonly float cos, sin;

        internal TreeForm(in TreeShapeSpec spec, TreeSkeleton skeleton = null)
        {
            var size = spec.Size;
            if (spec.Species == ForestSpecies.None || !float.IsFinite(spec.Yaw)
                || !float.IsFinite(size.Height + size.Diameter + size.CrownRadius)
                || size.Height <= 0 || size.Diameter <= 0 || size.CrownRadius <= 0
                || (spec.Form != 0 && !Enum.IsDefined((ShrubForm)spec.Form)))
                throw new ArgumentOutOfRangeException(nameof(spec));
            Spec = spec; Bands = spec.Bands; Shrub = spec.Form != 0;
            Skeleton = skeleton ?? TreeArchitecture.Skeleton(spec.Species, spec.Seed, spec.Phase, Bands.Light, spec.Form);
            float light = Bands.Light01;
            float response = TreeArchitecture.LightResponse(spec.Species, light);
            CrownFraction = TreeArchitecture.CrownFraction(spec.Species, spec.Phase, Bands.Light, spec.Form);
            Height = size.Height * Terrain.TreeMetresToWorld;
            CrownRadius = size.CrownRadius * Terrain.TreeMetresToWorld * (0.74f + 0.26f * response);
            CrownBase = Height * (1 - CrownFraction);
            ZScale = Height / Skeleton.Height;
            XyScale = CrownRadius * 0.88f / Math.Max(0.001f, Skeleton.LeafRadius);
            cos = MathF.Cos(spec.Yaw); sin = MathF.Sin(spec.Yaw);

            Dieback = Bands.Dieback(spec.Phase);
            Dead = Skeleton.DeadMask(spec.Dead ? 1 : Dieback);
            float leaves = spec.Leaves switch { LeafState.Bare => 0f, LeafState.Budding => 0.55f, LeafState.Falling => 0.6f, _ => 1f };
            Foliage = leaves * (1 - 0.35f * Dieback);
            Warp = TreeDeformation.From(Bands, Height).WithWind(spec.Site.WindAngle, Bands.WindBias);

            // Breast-height radius pins the trunk to the simulated diameter.
            BreastRadius = size.Diameter * 0.5f * Terrain.TreeMetresToWorld;
            BreastHeight = Math.Min(1.3f * Terrain.TreeMetresToWorld, 0.2f * Height);
            (float k, float scale) = spec.Species switch
            {
                ForestSpecies.Beech => (0.65f, 1f), ForestSpecies.Oak => (0.55f, 1f),
                ForestSpecies.Spruce => (0.40f, 1f), _ => (0.25f, 1f)
            };
            float age = spec.Phase switch { TreeLifePhase.Seedling => 0.1f, TreeLifePhase.Sapling => 0.3f,
                TreeLifePhase.Young => 0.6f, TreeLifePhase.Mature => 1f, _ => 1.15f };
            FlareStrength = Shrub ? 0 : k * scale * age;
            FlareHeight = 1.1f * BreastRadius;
            float unit = Skeleton.MainStemUnitRadiusAt(BreastHeight / ZScale);
            TwigRadius = BreastRadius / (Math.Max(unit, 1e-3f) * (1 + FlareStrength * MathF.Exp(-BreastHeight / FlareHeight)));

            var model = Terrain.TreeModel.For(spec.Species);
            float pigment = ForestTreeVariation.Range(spec.Seed, 601, 0.90f, 1.08f);
            Color bark = spec.Form == (int)ShrubForm.Hazel ? Color.FromArgb(128, 98, 72)
                : spec.Form == (int)ShrubForm.Hawthorn ? Color.FromArgb(104, 96, 86)
                : spec.Species == ForestSpecies.Birch && spec.Phase <= TreeLifePhase.Sapling ? Color.FromArgb(115, 77, 48) : model.TrunkColor;
            Color crown = spec.Form == (int)ShrubForm.Hazel ? Color.FromArgb(104, 146, 62)
                : spec.Form == (int)ShrubForm.Hawthorn ? Color.FromArgb(66, 104, 50) : model.CrownColor;
            DeadColor = Pack(Color.FromArgb(112, 102, 92), spec.Species, pigment);
            WoodColor = spec.Dead ? DeadColor : Pack(bark, spec.Species, pigment);
            CrownColor = Pack(LeafTint(crown, spec), spec.Species, pigment);
        }

        /// <summary>Generator-unit point to the world frame of the tree base.</summary>
        internal Vector3 ToWorld(Vector3 p) => Warp.Apply(ToFrame(p));

        /// <summary>Rotation and scale only, before the environmental warp.</summary>
        internal Vector3 ToFrame(Vector3 p) =>
            new(p.X * cos * XyScale - p.Y * sin * XyScale, (p.X * sin + p.Y * cos) * XyScale, p.Z * ZScale);

        /// <summary>Radius of the vertex before flare, world units.</summary>
        internal float RadiusOf(float flow) => TwigRadius * TreeSkeleton.UnitRadius(flow);

        internal float Flare(float heightAboveGround) =>
            1 + FlareStrength * MathF.Exp(-Math.Max(0, heightAboveGround) / FlareHeight);

        internal static Color LeafTint(Color crown, in TreeShapeSpec spec)
        {
            Color target = spec.Leaves switch
            {
                LeafState.Budding => Color.FromArgb(150, 190, 70),
                LeafState.Autumn or LeafState.Falling => spec.Species switch
                {
                    ForestSpecies.Birch => Color.FromArgb(214, 180, 50),
                    ForestSpecies.Beech => Color.FromArgb(190, 110, 40),
                    _ => Color.FromArgb(176, 112, 40)
                },
                _ => crown
            };
            float amount = spec.Leaves switch { LeafState.Budding => 0.6f, LeafState.Autumn => 0.8f, LeafState.Falling => 0.9f, _ => 0 };
            Color c = Lerp(crown, target, amount);
            // Weak trees carry dull, yellowing foliage.
            float weak = TreeShapeBands.VigorBand(spec.Vigor) switch
            { TreeVigorBand.Reduced => 0.08f, TreeVigorBand.Declining => 0.25f, TreeVigorBand.Dying => 0.4f, _ => 0f };
            return Lerp(c, Color.FromArgb(140, 130, 70), weak);
        }

        private static Color Lerp(Color a, Color b, float t) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        internal static uint Pack(Color color, ForestSpecies species, float shade) =>
            (uint)Terrain.SurfaceSpeciesCode(species) << 24 | (uint)Math.Clamp((int)(color.R * shade), 0, 255)
            | (uint)Math.Clamp((int)(color.G * shade), 0, 255) << 8 | (uint)Math.Clamp((int)(color.B * shade), 0, 255) << 16;
    }
}
