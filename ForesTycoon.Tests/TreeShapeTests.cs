using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class TreeShapeTests
{
    private static readonly ForestSpecies[] Species = { ForestSpecies.Spruce, ForestSpecies.Birch, ForestSpecies.Oak, ForestSpecies.Beech };
    public static IEnumerable<object[]> Each => Species.Select(s => new object[] { s });
    public static IEnumerable<object[]> Deciduous => new[] { ForestSpecies.Birch, ForestSpecies.Oak, ForestSpecies.Beech }.Select(s => new object[] { s });

    private static TreeShapeSpec Spec(ForestSpecies species, TreeLifePhase phase = TreeLifePhase.Mature, float vigor = 1,
        TreeSite? site = null, LeafState leaves = LeafState.Full, int seed = 42, bool dead = false)
    {
        var profile = ForestSpeciesProfile.For(species);
        float age = phase switch { TreeLifePhase.Seedling => 1f, TreeLifePhase.Sapling => profile.MatureAgeYears * 0.2f,
            TreeLifePhase.Young => profile.MatureAgeYears * 0.5f, TreeLifePhase.Mature => profile.MatureAgeYears * 1.5f,
            TreeLifePhase.Old => profile.MaximumAgeYears * 0.75f, _ => profile.MaximumAgeYears * 0.92f };
        return new(species, seed, phase, ForestTreeGrowth.Initial(species, age, 1), vigor, site ?? new TreeSite(0.85f), leaves, 0.3f, dead);
    }

    [Theory]
    [MemberData(nameof(Each))]
    internal void LifePhasesAreOrderedAndConsistentWithTheLegacyStages(ForestSpecies species)
    {
        TreeLifePhase last = TreeLifePhase.Seedling; float age = 0;
        foreach (var phase in Enum.GetValues<TreeLifePhase>())
        {
            Assert.Equal(phase, TreeLifePhases.Of(species, age));
            float next = TreeLifePhases.NextAge(species, phase);
            if (float.IsPositiveInfinity(next)) { Assert.Equal(TreeLifePhase.Senescent, phase); break; }
            Assert.True(next > age);
            Assert.Equal(phase, TreeLifePhases.Of(species, next - 0.001f));
            Assert.Equal(phase + 1, TreeLifePhases.Of(species, next));
            age = next; last = phase;
        }
        Assert.Equal(TreeLifePhase.Old, last + 0 == TreeLifePhase.Old ? last : TreeLifePhase.Old);
        for (float a = 0; a < 400; a += 0.5f)
            Assert.Equal(ForestTreeAppearance.Stage(species, a), TreeLifePhases.Coarse(TreeLifePhases.Of(species, a)));
    }

    [Theory]
    [MemberData(nameof(Deciduous))]
    internal void DeciduousCrownsFollowTheSeasonAndReplayExactly(ForestSpecies species)
    {
        var seen = new HashSet<LeafState>();
        LeafState previous = TreePhenology.At(species, 7, 0);
        for (double year = 0; year < 2; year += 1 / 360.0)
        {
            var state = TreePhenology.At(species, 7, year);
            Assert.Equal(state, TreePhenology.At(species, 7, year));
            seen.Add(state);
            if (state != previous)
            {
                // Scheduled change happens no later than the actual transition.
                Assert.True(TreePhenology.NextChange(species, 7, year - 1 / 360.0) <= year + 1e-6);
                previous = state;
            }
        }
        Assert.Equal(5, seen.Count);
        // Mid-summer is leafed, the coldest point of the year is bare.
        Assert.Equal(LeafState.Full, TreePhenology.At(species, 7, 0.25));
        Assert.Equal(LeafState.Bare, TreePhenology.At(species, 7, 0.78));
        // Individuals do not all change on the same day.
        var days = Enumerable.Range(0, 40).Select(seed => Enumerable.Range(576, 720).First(d => TreePhenology.At(species, (uint)seed, d / 720.0) != LeafState.Bare)).Distinct();
        Assert.True(days.Count() > 3);
    }

    [Fact]
    public void SpruceStaysGreenAllYear()
    {
        for (double year = 0; year < 1; year += 0.01) Assert.Equal(LeafState.Full, TreePhenology.At(ForestSpecies.Spruce, 3, year));
        Assert.Equal(double.PositiveInfinity, TreePhenology.NextChange(ForestSpecies.Spruce, 3, 0.5));
    }

    [Fact]
    public void BandsQuantiseContinuousInputsAndKeyDetectsChangesOnly()
    {
        var spec = Spec(ForestSpecies.Oak);
        Assert.Equal(spec.ShapeKey, (spec with { Vigor = 0.95f, Site = spec.Site with { Light = 0.9f } }).ShapeKey);
        Assert.NotEqual(spec.ShapeKey, (spec with { Vigor = 0.6f }).ShapeKey);
        Assert.NotEqual(spec.ShapeKey, (spec with { Site = new TreeSite(0.2f) }).ShapeKey);
        Assert.NotEqual(spec.ShapeKey, (spec with { Leaves = LeafState.Bare }).ShapeKey);
        Assert.NotEqual(spec.ShapeKey, (spec with { Phase = TreeLifePhase.Old }).ShapeKey);
        Assert.NotEqual(spec.ShapeKey, (spec with { Site = spec.Site with { Water = 0.1f } }).ShapeKey);
        // Gap direction only matters once there is a gap.
        Assert.Equal(spec.ShapeKey, (spec with { Site = spec.Site with { GapAngle = 2, GapStrength = 0.05f } }).ShapeKey);
        Assert.NotEqual(spec.ShapeKey, (spec with { Site = spec.Site with { GapAngle = 2, GapStrength = 0.9f } }).ShapeKey);
        // Non-finite input never produces an invalid band.
        var bad = spec with { Vigor = float.NaN, Site = new(float.NaN, float.PositiveInfinity, float.NaN, float.NaN, float.NaN, float.NaN) };
        _ = bad.ShapeKey; Assert.NotNull(DendroTreeGenerator.Generate(bad, ForestLod.Near));
    }

    [Theory]
    [MemberData(nameof(Each))]
    internal void TrunkRadiusAtBreastHeightMatchesTheSimulatedDiameterForEveryPhase(ForestSpecies species)
    {
        foreach (var phase in Enum.GetValues<TreeLifePhase>())
        {
            var spec = Spec(species, phase);
            var form = new TreeForm(spec);
            float radius = form.Skeleton.MainStemUnitRadiusAt(form.BreastHeight / form.ZScale) * form.TwigRadius * form.Flare(form.BreastHeight);
            Assert.Equal(spec.Size.Diameter * 0.5f * Terrain.TreeMetresToWorld, radius, 4);
            // The generated trunk really is that thick around breast height.
            var mesh = DendroTreeGenerator.Generate(spec, ForestLod.Near);
            var ring = mesh.Trunk.Where(v => v.Position.Z > form.BreastHeight * 0.4f && v.Position.Z < form.BreastHeight * 1.8f)
                .Select(v => v.Position.Xy.Length).ToArray();
            if (ring.Length > 0 && phase >= TreeLifePhase.Young)
                Assert.InRange(ring.Max(), radius * 0.8f, radius * 2.2f);
        }
    }

    [Theory]
    [MemberData(nameof(Each))]
    internal void LeaflessAndLeafedTreesShareOneBranchSystem(ForestSpecies species)
    {
        foreach (var phase in new[] { TreeLifePhase.Young, TreeLifePhase.Mature, TreeLifePhase.Old })
        {
            var leafed = DendroTreeGenerator.Generate(Spec(species, phase), ForestLod.Near);
            var bare = DendroTreeGenerator.Generate(Spec(species, phase, leaves: LeafState.Bare), ForestLod.Near);
            Assert.Empty(bare.Crown);
            Assert.NotEmpty(leafed.Crown);
            Assert.Equal(leafed.StemCount, bare.StemCount);
            // The whole system is shown without leaves: far more limb triangles, reaching the crown top.
            Assert.True(bare.Branches.Length > 3 * Math.Max(1, leafed.Branches.Length), $"{species} {phase}");
            float height = Spec(species, phase).Size.Height * Terrain.TreeMetresToWorld;
            Assert.True(bare.Trunk.Concat(bare.Branches).Max(v => v.Position.Z) > 0.8f * height);
            Assert.InRange((bare.Trunk.Length + bare.Branches.Length) / 3, 20, 2200);
            // The trunk of the bare tree is the same tube as the leafed one up to the clip height.
            Assert.True(bare.Trunk.Length >= leafed.Trunk.Length - 9 * 3 * 8);
        }
    }

    [Theory]
    [MemberData(nameof(Deciduous))]
    internal void LeaflessBranchBudgetFallsWithDetailAndEveryVertexIsFinite(ForestSpecies species)
    {
        var spec = Spec(species, leaves: LeafState.Bare);
        int Tris(ForestLod lod) { var m = DendroTreeGenerator.Generate(spec, lod); return (m.Trunk.Length + m.Branches.Length) / 3; }
        int near = Tris(ForestLod.Near), medium = Tris(ForestLod.Medium), far = Tris(ForestLod.Far);
        Assert.True(near > medium && medium > far && far > 0, $"{near} {medium} {far}");
        Assert.InRange(medium, 1, 520); Assert.InRange(far, 1, 200);
        foreach (var lod in Enum.GetValues<ForestLod>())
        {
            var m = DendroTreeGenerator.Generate(spec, lod);
            Assert.All(m.Trunk.Concat(m.Branches), v =>
            {
                Assert.True(float.IsFinite(v.Position.X + v.Position.Y + v.Position.Z));
                Assert.Equal(1, v.Normal.Length, 3);
            });
        }
    }

    [Fact]
    public void DiebackRemovesLiveCrownAndShowsDeadLimbs()
    {
        var healthy = DendroTreeGenerator.Generate(Spec(ForestSpecies.Oak, TreeLifePhase.Old), ForestLod.Near);
        var dying = DendroTreeGenerator.Generate(Spec(ForestSpecies.Oak, TreeLifePhase.Old, vigor: 0.1f), ForestLod.Near);
        Assert.True(TreeShapeModel.Measure(Spec(ForestSpecies.Oak, TreeLifePhase.Old, vigor: 0.1f)).LiveFoliage
            < TreeShapeModel.Measure(Spec(ForestSpecies.Oak, TreeLifePhase.Old)).LiveFoliage);
        float Volume(Vertex[] crown) => crown.Max(v => v.Position.Xy.Length) * (crown.Max(v => v.Position.Z) - crown.Min(v => v.Position.Z));
        Assert.True(Volume(dying.Crown) < Volume(healthy.Crown));
        var form = new TreeForm(Spec(ForestSpecies.Oak, TreeLifePhase.Old, vigor: 0.1f));
        Assert.True(form.Dead.Count(d => d) > 5);
        Assert.Contains(dying.Branches, v => v.Color == form.DeadColor);
        Assert.DoesNotContain(healthy.Branches, v => v.Color == new TreeForm(Spec(ForestSpecies.Oak, TreeLifePhase.Old)).DeadColor);
        // Dead limbs hang off living ones: every dead stem keeps its parent chain to the trunk.
        for (int i = 1; i < form.Dead.Length; i++)
            if (form.Dead[i] && form.Skeleton.Stems[i].Level > 0)
                Assert.True(form.Skeleton.Stems[i].Parent >= 0);
    }

    [Fact]
    public void DeadTreeIsAGreySnagWithoutCrown()
    {
        var snag = DendroTreeGenerator.Generate(Spec(ForestSpecies.Oak, vigor: 0, dead: true), ForestLod.Near);
        Assert.Empty(snag.Crown); Assert.NotEmpty(snag.Trunk); Assert.NotEmpty(snag.Branches);
        var form = new TreeForm(Spec(ForestSpecies.Oak, vigor: 0, dead: true));
        Assert.All(snag.Trunk.Concat(snag.Branches), v => Assert.Equal(form.DeadColor, v.Color));
    }

    [Fact]
    public void NeighbourGapLeansTheStemAndCrownTowardsOpenSpace()
    {
        var spec = Spec(ForestSpecies.Beech, TreeLifePhase.Mature);
        var open = DendroTreeGenerator.Generate(spec, ForestLod.Near);
        foreach (float angle in new[] { 0f, MathF.PI / 2, MathF.PI })
        {
            var gap = new TreeSite(0.85f, 1, 0, 0, angle, 1);
            var leaning = DendroTreeGenerator.Generate(spec with { Site = gap }, ForestLod.Near);
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            Vector2 Centre(Vertex[] v) => v.Aggregate(Vector2.Zero, (a, x) => a + x.Position.Xy) / v.Length;
            Assert.True(Vector2.Dot(Centre(leaning.Crown) - Centre(open.Crown), direction) > 0.02f, $"crown {angle}");
            // Heights, base and topology budget are unchanged: only horizontal displacement.
            Assert.Equal(open.Crown.Max(v => v.Position.Z), leaning.Crown.Max(v => v.Position.Z), 4);
            Assert.Equal(open.Crown.Length, leaning.Crown.Length);
            var baseOpen = open.Trunk.Where(v => v.Position.Z < 0.01f).Select(v => v.Position.Xy.Length).Max();
            var baseLean = leaning.Trunk.Where(v => v.Position.Z < 0.01f).Select(v => v.Position.Xy.Length).Max();
            Assert.Equal(baseOpen, baseLean, 2);
        }
    }

    [Fact]
    public void WindExposureSkewsTheCrownDownwind()
    {
        var spec = Spec(ForestSpecies.Oak, TreeLifePhase.Mature);
        var calm = DendroTreeGenerator.Generate(spec, ForestLod.Near);
        var windy = DendroTreeGenerator.Generate(spec with { Site = new TreeSite(0.85f, 1, 1, 0, 0, 0) }, ForestLod.Near);
        Assert.True(windy.Crown.Max(v => v.Position.X) > calm.Crown.Max(v => v.Position.X));
        Assert.True(windy.Crown.Min(v => v.Position.X) > calm.Crown.Min(v => v.Position.X));
    }

    [Theory]
    [MemberData(nameof(Deciduous))]
    internal void LeafStatesChangeColourAndFullness(ForestSpecies species)
    {
        var full = DendroTreeGenerator.Generate(Spec(species, leaves: LeafState.Full), ForestLod.Near);
        var autumn = DendroTreeGenerator.Generate(Spec(species, leaves: LeafState.Autumn), ForestLod.Near);
        var budding = DendroTreeGenerator.Generate(Spec(species, leaves: LeafState.Budding), ForestLod.Near);
        var falling = DendroTreeGenerator.Generate(Spec(species, leaves: LeafState.Falling), ForestLod.Near);
        Assert.NotEqual(full.Crown[0].Color, autumn.Crown[0].Color);
        Assert.NotEqual(full.Crown[0].Color, budding.Crown[0].Color);
        Assert.Equal(full.Crown.Length, autumn.Crown.Length);
        float Width(Vertex[] c) => c.Max(v => v.Position.Xy.Length);
        Assert.True(Width(budding.Crown) < Width(full.Crown));
        Assert.True(Width(falling.Crown) < Width(full.Crown));
        // Transitional trees show more of their limbs.
        Assert.True(budding.Branches.Select(v => v.Position.Xy.Length).DefaultIfEmpty(0).Max() >= full.Branches.Select(v => v.Position.Xy.Length).DefaultIfEmpty(0).Max());
    }

    [Theory]
    [MemberData(nameof(Each))]
    internal void ShapeMetricsReportPlausibleSizesAndRespondToTheSite(ForestSpecies species)
    {
        TreeShapeMetrics Measure(TreeShapeSpec spec) => TreeShapeModel.Measure(spec);
        var young = Measure(Spec(species, TreeLifePhase.Young));
        var mature = Measure(Spec(species, TreeLifePhase.Mature));
        Assert.True(mature.TrunkVolume > young.TrunkVolume);
        Assert.True(mature.WoodVolume > mature.TrunkVolume);
        Assert.InRange(mature.FormFactor, 0.15f, 0.75f);
        var size = Spec(species, TreeLifePhase.Mature).Size;
        Assert.InRange(mature.CrownBaseHeight, 0, size.Height);
        Assert.InRange(mature.CrownRadius, size.CrownRadius * 0.4f, size.CrownRadius * 1.3f);
        Assert.True(mature.CrownProjectionArea > 0 && mature.CrownVolume > 0 && mature.Limbs > 2);
        Assert.Equal(1, mature.LiveFoliage, 3);
        Assert.Equal(0, Measure(Spec(species, TreeLifePhase.Mature, leaves: LeafState.Bare)).LiveFoliage);
        Assert.True(Measure(Spec(species, TreeLifePhase.Mature, vigor: 0.1f)).Dieback > 0.4f);
        // A neighbour-free side pulls the crown centre that way.
        var gap = Measure(Spec(species, TreeLifePhase.Mature, site: new TreeSite(0.85f, 1, 0, 0, 0, 1)));
        Assert.True(gap.CrownOffset.X > mature.CrownOffset.X + 0.05f);
        // Identical specs measure identically; the shade crown is smaller and higher.
        Assert.Equal(mature, Measure(Spec(species, TreeLifePhase.Mature)));
        var shade = Measure(Spec(species, TreeLifePhase.Mature, site: new TreeSite(0.1f)));
        Assert.True(shade.CrownBaseHeight > mature.CrownBaseHeight);
    }

    [Fact]
    public void GapDirectionPointsAwayFromNeighbours()
    {
        var trees = new ForestTree[3];
        ForestTree At(ulong id, float u, float v) => new(id, 0, ForestSpecies.Oak, u, v, 1, -40, 0,
            new(0.5f, 22, 5), default, 1);
        trees[0] = At(1, 0.5f, 0.5f); trees[1] = At(2, 0.8f, 0.5f); trees[2] = At(3, 0.8f, 0.55f);
        var site = ForestTreeSites.Gap(trees, 3, 0, 30, 30, 0, 0.2f);
        Assert.True(MathF.Cos(site.GapAngle) < -0.8f);
        Assert.InRange(site.GapStrength, 0.3f, 1);
        Assert.Equal(0, site.Wind);
        var alone = ForestTreeSites.Gap(trees, 1, 0, 30, 30, 0, 1f);
        Assert.Equal(0, alone.GapStrength); Assert.Equal(1, alone.Wind, 4);
        // Equal neighbours on opposite sides cancel out.
        trees[1] = At(2, 0.2f, 0.5f); trees[2] = At(3, 0.8f, 0.5f);
        Assert.True(ForestTreeSites.Gap(trees, 3, 0, 30, 30, 0, 0).GapStrength < 0.05f);
    }

    [Fact]
    public void SpecToMeshIsDeterministicAndSeedDependent()
    {
        var spec = Spec(ForestSpecies.Beech, TreeLifePhase.Mature, leaves: LeafState.Bare);
        var a = DendroTreeGenerator.Generate(spec, ForestLod.Near); var b = DendroTreeGenerator.Generate(spec, ForestLod.Near);
        Assert.Equal(a.Trunk, b.Trunk); Assert.Equal(a.Branches, b.Branches);
        Assert.False(a.Branches.SequenceEqual(DendroTreeGenerator.Generate(spec with { Seed = 43 }, ForestLod.Near).Branches));
        Assert.Throws<ArgumentOutOfRangeException>(() => DendroTreeGenerator.Generate(spec with { Size = new(0, 1, 1) }, ForestLod.Near));
    }
}
