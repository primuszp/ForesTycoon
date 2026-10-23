using ForesTycoon.TreeModels;

namespace ForesTycoon.Tests;

/// <summary>A species' authored tree form: how the editor's values reach the tree generator and its caches.</summary>
[Collection("SpeciesBook")]
public sealed class TreeShapeAuthoringTests
{
    private static WorldScenario WithOakForm(TreeShapeDefinition shape)
    {
        var scenario = GameWorld.DefaultScenario();
        scenario.Species.Single(s => s.Id == "Oak").Shape = shape;
        return scenario;
    }

    [Fact]
    public void CommonValuesSetTheBaseAndAPhaseValueIsFinal()
    {
        try {
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
        } finally { ForestSpeciesBook.Apply(null); }
    }

    [Fact]
    public void ADeadTreeKeepsItsLivingStructureUnlessTheFormSaysOtherwise()
    {
        try {
            var living = TreeArchitecture.Skeleton(ForestSpecies.Oak, 5, TreeLifePhase.Old, 2);
            Assert.Same(living, TreeArchitecture.Skeleton(ForestSpecies.Oak, 5, TreeLifePhase.Old, 2, dead: true));
            ForestSpeciesBook.Apply(GameWorld.SpeciesEdits(WithOakForm(new() {
                Phases = { [TreeShapeDefinition.DeadState] = new() { ["2Branches"] = 0 } } })));
            Assert.Equal(0, TreeArchitecture.EffectiveParameters(ForestSpecies.Oak, TreeLifePhase.Old, 2, dead: true)["2Branches"]);
            Assert.NotEqual(0, TreeArchitecture.EffectiveParameters(ForestSpecies.Oak, TreeLifePhase.Old, 2)["2Branches"]);
            var snag = TreeArchitecture.Skeleton(ForestSpecies.Oak, 5, TreeLifePhase.Old, 2, dead: true);
            Assert.True(snag.StemCount < TreeArchitecture.Skeleton(ForestSpecies.Oak, 5, TreeLifePhase.Old, 2).StemCount);
        } finally { ForestSpeciesBook.Apply(null); }
    }

    [Fact]
    public void AFormChangesTheShapeKeySoBuiltTreesAreRebuilt()
    {
        var spec = new TreeShapeSpec(ForestSpecies.Oak, 7, TreeLifePhase.Mature, new(.4f, 20, 4), 1, new TreeSite(1), LeafState.Full, 0);
        long builtin = spec.ShapeKey;
        try {
            ForestSpeciesBook.Apply(GameWorld.SpeciesEdits(WithOakForm(new() { All = { ["1Branches"] = 11 } })));
            Assert.NotEqual(builtin, spec.ShapeKey);
            var first = TreeArchitecture.Skeleton(ForestSpecies.Oak, 7, TreeLifePhase.Mature, 2);
            ForestSpeciesBook.Apply(GameWorld.SpeciesEdits(WithOakForm(new() { All = { ["1Branches"] = 30 } })));
            Assert.NotSame(first, TreeArchitecture.Skeleton(ForestSpecies.Oak, 7, TreeLifePhase.Mature, 2));
        } finally { ForestSpeciesBook.Apply(null); }
        Assert.Equal(builtin, spec.ShapeKey);
    }

    [Fact]
    public void ValuesOutsideTheGeneratorsSafeRangeAreClamped()
    {
        try {
            ForestSpeciesBook.Apply(GameWorld.SpeciesEdits(WithOakForm(new() { All = { ["Levels"] = 40, ["BaseSize"] = 7 } })));
            var values = TreeArchitecture.BaseParameters(ForestSpecies.Oak);
            Assert.True(values["Levels"] <= 4 && values["BaseSize"] < 1);
            Assert.True(TreeArchitecture.Skeleton(ForestSpecies.Oak, 1, TreeLifePhase.Young, 1).StemCount > 0);
        } finally { ForestSpeciesBook.Apply(null); }
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
