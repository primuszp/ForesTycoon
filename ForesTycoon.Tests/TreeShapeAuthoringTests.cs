using ForesTycoon.TreeModels;

namespace ForesTycoon.Tests;

/// <summary>A species' authored tree form: how the editor's values reach the tree generator and its caches.</summary>
[Collection("SpeciesBook")]
public sealed class TreeShapeAuthoringTests
{
    /// <summary>
    /// Runs with a species book of the test's own: edits applied inside stay in this flow, so tests building trees in
    /// parallel keep the built-in species.
    /// </summary>
    private static void Isolated(Action test) => ForestSpeciesBook.WithContext<object?>(null, () => { test(); return null; });

    private static WorldScenario WithOakForm(TreeShapeDefinition shape)
    {
        var scenario = GameWorld.DefaultScenario();
        scenario.Species.Single(s => s.Id == "Oak").Shape = shape;
        return scenario;
    }

    [Fact]
    public void CommonValuesSetTheBaseAndAPhaseValueIsFinal()
    {
        Isolated(() => {
            ForestSpeciesBook.Apply(GameWorld.SpeciesEdits(WithOakForm(new() {
                All = { ["1Branches"] = 11, ["AttractionUp"] = -1 },
                Phases = { ["Old"] = new() { ["1DownAngle"] = 100, ["BaseSize"] = .5 } } })));
            Assert.Equal(11, TreeArchitecture.BaseParameters(ForestSpecies.Oak)["1Branches"]);
            // The phase value wins over the game's age and light adaptation, in that phase only.
            Assert.Equal(100, TreeArchitecture.EffectiveParameters(ForestSpecies.Oak, TreeLifePhase.Old, 0)["1DownAngle"]);
            Assert.NotEqual(100, TreeArchitecture.EffectiveParameters(ForestSpecies.Oak, TreeLifePhase.Mature, 0)["1DownAngle"]);
            // The crown base the mesher uses follows the authored base size.
            Assert.Equal(.5f, TreeArchitecture.CrownFraction(ForestSpecies.Oak, TreeLifePhase.Old, 2), 3);
            Assert.True(TreeArchitecture.Skeleton(ForestSpecies.Oak, 3, TreeLifePhase.Old, 2).StemCount > 0);
        });
    }

    [Fact]
    public void ADeadTreeKeepsItsLivingStructureUnlessTheFormSaysOtherwise()
    {
        Isolated(() => {
            var living = TreeArchitecture.Skeleton(ForestSpecies.Oak, 5, TreeLifePhase.Old, 2);
            Assert.Same(living, TreeArchitecture.Skeleton(ForestSpecies.Oak, 5, TreeLifePhase.Old, 2, dead: true));
            ForestSpeciesBook.Apply(GameWorld.SpeciesEdits(WithOakForm(new() {
                Phases = { [TreeShapeDefinition.DeadState] = new() { ["2Branches"] = 0 } } })));
            Assert.Equal(0, TreeArchitecture.EffectiveParameters(ForestSpecies.Oak, TreeLifePhase.Old, 2, dead: true)["2Branches"]);
            Assert.NotEqual(0, TreeArchitecture.EffectiveParameters(ForestSpecies.Oak, TreeLifePhase.Old, 2)["2Branches"]);
            var snag = TreeArchitecture.Skeleton(ForestSpecies.Oak, 5, TreeLifePhase.Old, 2, dead: true);
            Assert.True(snag.StemCount < TreeArchitecture.Skeleton(ForestSpecies.Oak, 5, TreeLifePhase.Old, 2).StemCount);
        });
    }

    [Fact]
    public void AFormChangesTheShapeKeySoBuiltTreesAreRebuilt()
    {
        var spec = new TreeShapeSpec(ForestSpecies.Oak, 7, TreeLifePhase.Mature, new(.4f, 20, 4), 1, new TreeSite(1), LeafState.Full, 0);
        long builtin = spec.ShapeKey;
        Isolated(() => {
            ForestSpeciesBook.Apply(GameWorld.SpeciesEdits(WithOakForm(new() { All = { ["1Branches"] = 11 } })));
            Assert.NotEqual(builtin, spec.ShapeKey);
            var first = TreeArchitecture.Skeleton(ForestSpecies.Oak, 7, TreeLifePhase.Mature, 2);
            ForestSpeciesBook.Apply(GameWorld.SpeciesEdits(WithOakForm(new() { All = { ["1Branches"] = 30 } })));
            Assert.NotSame(first, TreeArchitecture.Skeleton(ForestSpecies.Oak, 7, TreeLifePhase.Mature, 2));
        });
        Assert.Equal(builtin, spec.ShapeKey);
    }

    [Fact]
    public void ValuesOutsideTheGeneratorsSafeRangeAreClamped()
    {
        Isolated(() => {
            ForestSpeciesBook.Apply(GameWorld.SpeciesEdits(WithOakForm(new() { All = { ["Levels"] = 40, ["BaseSize"] = 7 } })));
            var values = TreeArchitecture.BaseParameters(ForestSpecies.Oak);
            Assert.True(values["Levels"] <= 4 && values["BaseSize"] < 1);
            Assert.True(TreeArchitecture.Skeleton(ForestSpecies.Oak, 1, TreeLifePhase.Young, 1).StemCount > 0);
        });
    }

    private static WorldScenario WithOakColours(TreeColoursDefinition colours)
    {
        var scenario = GameWorld.DefaultScenario();
        scenario.Species.Single(s => s.Id == "Oak").Colours = colours;
        return scenario;
    }

    [Fact]
    public void AuthoredColoursAreReachedAndTheBarkPatternIsTheShadersFamily()
    {
        var builtin = TreeForm.Colours(ForestSpecies.Oak);
        var spec = new TreeShapeSpec(ForestSpecies.Oak, 7, TreeLifePhase.Mature, new(.4f, 20, 4), 1, new TreeSite(1), LeafState.Full, 0);
        long key = spec.ShapeKey;
        Isolated(() => {
            ForestSpeciesBook.Apply(GameWorld.SpeciesEdits(WithOakColours(new() {
                Spring = "#C8E050", Summer = "#3C6428", Autumn = "#B8321E", Bark = "#D8D6CC", Dead = "#605850", BarkPattern = "birch" })));
            var colours = TreeForm.Colours(ForestSpecies.Oak);
            Assert.Equal(System.Drawing.Color.FromArgb(0xC8, 0xE0, 0x50).ToArgb(), colours.Spring.ToArgb());
            Assert.Equal(System.Drawing.Color.FromArgb(0x3C, 0x64, 0x28).ToArgb(), colours.Summer.ToArgb());
            Assert.Equal(System.Drawing.Color.FromArgb(0xB8, 0x32, 0x1E).ToArgb(), colours.Autumn.ToArgb());
            Assert.Equal(System.Drawing.Color.FromArgb(0xD8, 0xD6, 0xCC).ToArgb(), colours.Bark.ToArgb());
            Assert.Equal(3, ForestSpeciesTraits.For(ForestSpecies.Oak).Pattern);
            Assert.Equal(246 + 3, TreeScale.SurfaceSpeciesCode(ForestSpecies.Oak));
            // Built trees are rebuilt with the new colours.
            Assert.NotEqual(key, spec.ShapeKey);
            var form = new TreeForm(spec with { Dead = true, Leaves = LeafState.Bare, Vigor = 0 });
            // Dead wood takes the authored colour, with the tree's own slight pigment variation.
            Assert.InRange((int)(form.WoodColor & 0xFF), 0x60 - 10, 0x60 + 10);
        });
        Assert.Equal(builtin, TreeForm.Colours(ForestSpecies.Oak));
        Assert.Equal(key, spec.ShapeKey);
    }

    [Fact]
    public void ColoursAreCheckedAndKeepTheJsonOfUntouchedSpecies()
    {
        var project = GameWorld.DefaultRuleProject();
        project.World = WithOakColours(new() { Autumn = "red" });
        Assert.Throws<InvalidDataException>(() => GameWorld.ValidateRuleProject(project));
        project.World = WithOakColours(new() { BarkPattern = "zebra" });
        Assert.Throws<InvalidDataException>(() => GameWorld.ValidateRuleProject(project));
        Assert.DoesNotContain("\"colours\"", new RuleProject { World = GameWorld.DefaultScenario() }.ToJson());
        var round = RuleProject.FromJson(new RuleProject { World = WithOakColours(new() { Bark = "#112233", BarkPattern = "scaly" }) }.ToJson());
        Assert.Equal("scaly", round.World.Species.Single(s => s.Id == "Oak").Colours.BarkPattern);
    }

    [Fact]
    public void AnUnknownParameterIsAnErrorOfTheProject()
    {
        var project = GameWorld.DefaultRuleProject();
        project.World = WithOakForm(new() { All = { ["NoSuchParameter"] = 1 } });
        Assert.Throws<InvalidDataException>(() => GameWorld.ValidateRuleProject(project));
        project.World = WithOakForm(new() { Phases = { ["Ancient"] = new() { ["1Branches"] = 3 } } });
        Assert.Throws<InvalidDataException>(() => GameWorld.ValidateRuleProject(project));
    }

    [Fact]
    public void ASpeciesWithoutAFormKeepsItsJsonAndRoundTripsAForm()
    {
        var project = new RuleProject { World = GameWorld.DefaultScenario() };
        Assert.DoesNotContain("\"shape\"", project.ToJson());
        project.World.Species[0].Shape = new() { All = { ["1Length"] = .4 }, Phases = { ["Young"] = new() { ["Shape"] = 2 } } };
        var copy = RuleProject.FromJson(project.ToJson()).World.Species[0].Shape;
        Assert.Equal(.4, copy.All["1Length"]);
        Assert.Equal(2, copy.Phases["Young"]["Shape"]);
    }
}
