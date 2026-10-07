using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class ForestSpeciesCatalogTests
{
    public static IEnumerable<object[]> All => ForestSpeciesTraits.Playable.Select(s => new object[] { s });
    public static IEnumerable<object[]> Shrubs => ForestSpeciesTraits.Playable.Where(s => ForestSpeciesTraits.For(s).Shrub).Select(s => new object[] { s });
    public static IEnumerable<object[]> Deciduous => ForestSpeciesTraits.Playable.Where(s => !ForestSpeciesTraits.For(s).Evergreen).Select(s => new object[] { s });

    [Fact]
    public void ThereAreSixteenDistinctPlayableSpeciesWithUniqueNamesAndPresets()
    {
        var species = ForestSpeciesTraits.Playable;
        Assert.Equal(16, species.Length);
        Assert.Equal(species.Length, species.Distinct().Count());
        Assert.Equal(species.Length, species.Select(s => ForestSpeciesTraits.For(s).Name).Distinct().Count());
        Assert.Equal(species.Length, species.Select(s => ForestSpeciesTraits.For(s).Preset).Distinct().Count());
        Assert.Equal(5, species.Count(s => ForestSpeciesTraits.For(s).Shrub));
        // The classic four keep their old identity.
        Assert.Equal(new[] { ForestSpecies.Spruce, ForestSpecies.Birch, ForestSpecies.Oak, ForestSpecies.Beech }, species.Take(4));
    }

    [Theory]
    [MemberData(nameof(All))]
    internal void EverySpeciesHasEcologyGrowthAndAnEmbeddedPreset(ForestSpecies species)
    {
        var profile = ForestSpeciesProfile.For(species);
        var traits = ForestSpeciesTraits.For(species);
        Assert.True(profile.MaximumAgeYears > profile.MatureAgeYears * 2 && profile.MatureAgeYears > 3);
        Assert.InRange(profile.ShadeTolerance, 0, 1);
        Assert.True(ForestTreeGrowth.Capacity(species) > 0);
        Assert.NotNull(typeof(TreeArchitecture).Assembly.GetManifestResourceStream("Trees." + traits.Preset + ".xml"));
        Assert.InRange(Terrain.SurfaceSpeciesCode(species), 247, 251); // the shader knows five material families
        Assert.False(string.IsNullOrWhiteSpace(traits.Description));
        // Phases are ordered for the species' own lifespan.
        float last = 0;
        for (var phase = TreeLifePhase.Seedling; phase < TreeLifePhase.Senescent; phase++)
        {
            float next = TreeLifePhases.NextAge(species, phase);
            Assert.True(next > last, $"{species} {phase}");
            last = next;
        }
        Assert.True(last < profile.MaximumAgeYears);
    }

    [Theory]
    [MemberData(nameof(All))]
    internal void GrowthApproachesTheSpeciesSizeAndStaysMonotonic(ForestSpecies species)
    {
        var traits = ForestSpeciesTraits.For(species);
        var profile = ForestSpeciesProfile.For(species);
        ForestTreeDimensions previous = ForestTreeGrowth.Initial(species, 0, 1);
        Assert.True(previous.Height > 0 && previous.Diameter > 0 && previous.CrownRadius > 0);
        for (float age = 1; age <= profile.MaximumAgeYears; age += 1)
        {
            var size = ForestTreeGrowth.Initial(species, age, 1);
            Assert.True(size.Height >= previous.Height && size.Diameter >= previous.Diameter);
            previous = size;
        }
        var mature = ForestTreeGrowth.Initial(species, profile.MatureAgeYears * 1.5f, 1);
        Assert.InRange(mature.Height, traits.MatureHeight * 0.75f, traits.MatureHeight * 1.05f);
        Assert.True(previous.Height <= traits.MaxHeight);
        // The growth rate of a thriving individual falls to zero at the envelope.
        var shape = ForestTreeGrowth.Shape(species, new(0.3f, traits.MaxHeight, 3));
        Assert.Equal(0, shape.HeightMultiplier, 4);
        Assert.True(ForestTreeGrowth.Shape(species, ForestTreeGrowth.Initial(species, 2, 1)).HeightMultiplier > 0);
    }

    [Theory]
    [MemberData(nameof(All))]
    internal void EverySpeciesCanBePlantedGrownAndHarvested(ForestSpecies species)
    {
        var habitat = new ForestSystemTests.TestHabitat(96, seed: 31);
        var forest = new ForestSystem(habitat, secondsPerYear: 12.0);
        int tile = Enumerable.Range(0, habitat.TileCount).First(id => !forest.TryGetStand(id, out _) && ((IForestHabitat)habitat).CanSupportForest(id));
        Assert.Equal(ForestryActionResult.Planted, forest.Plant(tile, species));
        Assert.True(forest.IndividualTrees.TryGet(tile, out var patch) && patch.Count > 0);
        Assert.All(Enumerable.Range(0, patch.Count), i => Assert.Equal(species, patch.Trees[i].Species));
        for (int i = 0; i < 6; i++) forest.Update(1.0); // six forest months
        if (forest.TryGetStand(tile, out var stand))
        {
            Assert.Equal(species, stand.Species);
            Assert.True(float.IsFinite(stand.Biomass) && stand.Biomass >= 0 && stand.Health is >= 0 and <= 1);
        }
        Assert.Equal(ForestryActionResult.Harvested, forest.Harvest(tile, out var harvest));
        Assert.Equal(species, harvest.Species);
        Assert.True(harvest.TimberVolume >= 0 && float.IsFinite(harvest.TimberVolume));
    }

    [Theory]
    [MemberData(nameof(All))]
    internal void EverySpeciesGeneratesValidMeshesForEveryPhaseSeasonAndDetail(ForestSpecies species)
    {
        var profile = ForestSpeciesProfile.For(species);
        var traits = ForestSpeciesTraits.For(species);
        foreach (var phase in Enum.GetValues<TreeLifePhase>())
        foreach (var leaves in Enum.GetValues<LeafState>())
        foreach (var lod in Enum.GetValues<ForestLod>())
        {
            float age = phase == TreeLifePhase.Seedling ? 1 : TreeLifePhases.NextAge(species, phase - 1) + 0.5f;
            var spec = new TreeShapeSpec(species, 7, phase, ForestTreeGrowth.Initial(species, age, 1), 1, new TreeSite(0.7f), leaves, 0.4f);
            var mesh = DendroTreeGenerator.Generate(spec, lod);
            bool leafy = leaves != LeafState.Bare;
            if (!(leafy && lod == ForestLod.Far)) Assert.NotEmpty(mesh.Trunk); // far leafed trees are crown only
            if (leafy) Assert.NotEmpty(mesh.Crown);
            if (!leafy) Assert.Empty(mesh.Crown);
            foreach (var vertices in new[] { mesh.Trunk, mesh.Branches, mesh.Crown })
                Assert.All(vertices, v =>
                {
                    Assert.True(float.IsFinite(v.Position.X + v.Position.Y + v.Position.Z));
                    Assert.Equal(1, v.Normal.Length, 3);
                    Assert.Equal((uint)Terrain.SurfaceSpeciesCode(species), v.Color >> 24);
                });
            if (leafy && mesh.Crown.Length > 0)
            {
                float height = spec.Size.Height * Terrain.TreeMetresToWorld;
                Assert.Equal(height, mesh.Crown.Max(v => v.Position.Z), 3);
            }
            Assert.InRange((mesh.Trunk.Length + mesh.Branches.Length + mesh.Crown.Length) / 3, 1, leafy ? 900 : 2100);
        }
        Assert.True(profile.MaximumAgeYears > 0 && traits.Capacity > 0);
    }

    [Theory]
    [MemberData(nameof(Deciduous))]
    internal void DeciduousSpeciesLoseTheirLeavesAndEvergreensDoNot(ForestSpecies species)
    {
        Assert.False(TreePhenology.Evergreen(species));
        var states = Enumerable.Range(0, 360).Select(d => TreePhenology.At(species, 5, d / 360.0)).Distinct().ToArray();
        Assert.Equal(5, states.Length);
        Assert.Equal(LeafState.Bare, TreePhenology.At(species, 5, 0.8));
        Assert.Equal(LeafState.Full, TreePhenology.At(species, 5, 0.25));
    }

    [Fact]
    public void ConifersAndJuniperStayGreenExceptLarch()
    {
        foreach (var species in new[] { ForestSpecies.Spruce, ForestSpecies.Pine, ForestSpecies.Fir, ForestSpecies.Juniper })
            for (double year = 0; year < 1; year += 0.02) Assert.Equal(LeafState.Full, TreePhenology.At(species, 9, year));
        Assert.Equal(LeafState.Bare, TreePhenology.At(ForestSpecies.Larch, 9, 0.85));
    }

    [Theory]
    [MemberData(nameof(Shrubs))]
    internal void ShrubsAreLowManyStemmedAndReachTheGround(ForestSpecies species)
    {
        var traits = ForestSpeciesTraits.For(species);
        Assert.True(traits.MatureHeight <= 6 && ForestTreeGrowth.Capacity(species) >= 16);
        var size = ForestTreeGrowth.Initial(species, ForestSpeciesProfile.For(species).MatureAgeYears * 1.5f, 1);
        var mesh = DendroTreeGenerator.Generate(new TreeShapeSpec(species, 3, TreeLifePhase.Mature, size, 1, new TreeSite(0.85f), LeafState.Full, 0.2f), ForestLod.Near);
        Assert.True(mesh.StemCount > 10);
        Assert.True(mesh.Crown.Min(v => v.Position.Z) < size.Height * Terrain.TreeMetresToWorld * 0.2f);
        // No root flare or buttress roots on a bush.
        var spec = new TreeShapeSpec(species, 3, TreeLifePhase.Mature, size, 1, new TreeSite(0.85f), LeafState.Full, 0.2f);
        Assert.Equal(0, new TreeForm(spec).FlareStrength);
        Assert.True(new TreeForm(spec).Shrub);
    }

    [Fact]
    public void SpeciesLookLikeThemselvesTallConifersNarrowOaksWideShrubsLow()
    {
        float Crown(ForestSpecies s) { var d = ForestTreeGrowth.Initial(s, ForestSpeciesProfile.For(s).MatureAgeYears * 1.5f, 1); return d.CrownRadius / d.Height; }
        Assert.True(Crown(ForestSpecies.Oak) > Crown(ForestSpecies.Fir));
        Assert.True(Crown(ForestSpecies.Larch) < Crown(ForestSpecies.SessileOak));
        Assert.True(ForestTreeGrowth.Initial(ForestSpecies.Fir, 200, 1).Height > ForestTreeGrowth.Initial(ForestSpecies.Hazel, 20, 1).Height * 5);
        // Pine keeps a high crown on a long clear bole, spruce is branched nearly to the ground.
        Assert.True(TreeArchitecture.CrownFraction(ForestSpecies.Pine, TreeLifePhase.Mature, 2) < 0.6f);
        Assert.True(TreeArchitecture.CrownFraction(ForestSpecies.Spruce, TreeLifePhase.Mature, 2) > 0.85f);
        // Different species of one family are still different trees.
        var size = ForestTreeGrowth.Initial(ForestSpecies.Oak, 80, 1);
        DendroTreeGenerator.Mesh M(ForestSpecies s) => DendroTreeGenerator.Generate(new TreeShapeSpec(s, 5, TreeLifePhase.Mature, size, 1, new TreeSite(0.85f), LeafState.Full, 0), ForestLod.Near);
        Assert.False(M(ForestSpecies.Oak).Crown.SequenceEqual(M(ForestSpecies.SessileOak).Crown));
        Assert.NotEqual(M(ForestSpecies.Oak).Crown[0].Color, M(ForestSpecies.TurkeyOak).Crown[0].Color);
    }
}
