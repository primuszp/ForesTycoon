using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class TreeCrownAuthoringTests
{
    private static void Isolated(Action test) => ForestSpeciesBook.WithContext<object?>(null, () => { test(); return null; });
    private static TreeShapeSpec Spec(TreeLifePhase phase = TreeLifePhase.Mature) =>
        new(ForestSpecies.Pine, 13, phase, new(.4f, 20, 4), 1, new TreeSite(1), LeafState.Full, 0);
    private static void Apply(TreeShapeDefinition shape)
    {
        var scenario = GameWorld.DefaultScenario();
        scenario.Species.Single(s => s.Id == "Pine").Shape = shape;
        ForestSpeciesBook.Apply(GameWorld.SpeciesEdits(scenario));
    }

    [Fact]
    public void SurfaceControlsReachTheMeshWithoutChangingWoodOrTessellation()
    {
        Isolated(() => {
            var spec = Spec();
            var before = Enum.GetValues<ForestLod>().Select(lod => DendroTreeGenerator.Generate(spec, lod)).ToArray();
            long key = spec.ShapeKey;
            Apply(new() { Phases = { ["Mature"] = new() {
                ["CrownLayers"] = 0, ["CrownCore"] = .4, ["CrownRoundness"] = .2, ["CrownFlatten"] = .65,
                ["CrownBranchFollow"] = 0, ["CrownAnisotropy"] = 0 } } });
            Assert.NotEqual(key, spec.ShapeKey);
            TreeArchitecture.LastFormError = null;
            foreach (var lod in Enum.GetValues<ForestLod>())
            {
                var mesh = DendroTreeGenerator.Generate(spec, lod);
                var original = before[(int)lod];
                Assert.Equal(original.Trunk, mesh.Trunk); Assert.Equal(original.Branches, mesh.Branches);
                Assert.Equal(original.Crown.Length, mesh.Crown.Length);
                Assert.False(original.Crown.SequenceEqual(mesh.Crown));
                Assert.All(mesh.Crown, v => Assert.True(float.IsFinite(v.Position.LengthSquared + v.Normal.LengthSquared)));
            }
            Assert.Null(TreeArchitecture.LastFormError);
        });
    }

    [Fact]
    public void DefaultsRoundTripExactlyAndCommonPhaseAndDeadValuesHaveOnePrecedence()
    {
        Isolated(() => {
            var spec = Spec(); var original = DendroTreeGenerator.Generate(spec, ForestLod.Near);
            var defaults = TreeArchitecture.EffectiveParameters(ForestSpecies.Pine, TreeLifePhase.Mature, 2);
            var shape = new TreeShapeDefinition();
            foreach (var entry in TreeCrownParameters.Entries) shape.All[entry.Name] = defaults[entry.Name];
            Apply(shape);
            Assert.Equal(original.Crown, DendroTreeGenerator.Generate(spec, ForestLod.Near).Crown);
            shape.All["CrownLayers"] = 3;
            shape.Phases["Mature"] = new() { ["CrownLayers"] = 0 };
            shape.Phases["Dead"] = new() { ["CrownLayers"] = 4 };
            Apply(shape);
            Assert.Equal(3, TreeArchitecture.BaseParameters(ForestSpecies.Pine)["CrownLayers"]);
            Assert.Equal(0, TreeArchitecture.EffectiveParameters(ForestSpecies.Pine, TreeLifePhase.Mature, 2)["CrownLayers"]);
            Assert.Equal(3, TreeArchitecture.EffectiveParameters(ForestSpecies.Pine, TreeLifePhase.Old, 2)["CrownLayers"]);
            Assert.Equal(4, TreeArchitecture.EffectiveParameters(ForestSpecies.Pine, TreeLifePhase.Mature, 2, true)["CrownLayers"]);
        });
    }

    [Fact]
    public void YoungPinesKeepTheirConicalGeneratorAndOtherSpeciesKeepTheirProfiles()
    {
        Isolated(() => {
            var spec = Spec(TreeLifePhase.Young);
            var original = DendroTreeGenerator.Generate(spec, ForestLod.Near);
            var oak = TreeCrownParameters.Resolve(ForestSpecies.Oak, TreeLifePhase.Mature);
            Apply(new() { All = { ["CrownCore"] = .5, ["CrownLayers"] = 0, ["CrownRoundness"] = .4, ["CrownGaps"] = 0 } });
            Assert.Equal(original.Crown, DendroTreeGenerator.Generate(spec, ForestLod.Near).Crown);
            Assert.False(TreeArchitecture.EffectiveParameters(ForestSpecies.Pine, TreeLifePhase.Young, 2).ContainsKey("CrownLayers"));
            Assert.False(TreeArchitecture.EffectiveParameters(ForestSpecies.Oak, TreeLifePhase.Seedling, 2).ContainsKey("CrownLayers"));
            Assert.Equal(oak, TreeCrownParameters.Resolve(ForestSpecies.Oak, TreeLifePhase.Mature));
        });
    }

    [Fact]
    public void SavedCrownValuesAreRecognizedAndClampedBeforeBuilding()
    {
        Isolated(() => {
            var project = new RuleProject { World = GameWorld.DefaultScenario() };
            var shape = new TreeShapeDefinition { Phases = { ["Mature"] = new() {
                ["CrownClusters"] = 99, ["CrownLayers"] = -5, ["CrownCore"] = 0, ["CrownRoundness"] = 2,
                ["CrownBranchFollow"] = 2, ["CrownAnisotropy"] = -1, ["CrownGaps"] = 2 } } };
            project.World.Species.Single(s => s.Id == "Pine").Shape = shape;
            var copy = RuleProject.FromJson(project.ToJson());
            Apply(copy.World.Species.Single(s => s.Id == "Pine").Shape);
            var values = TreeArchitecture.EffectiveParameters(ForestSpecies.Pine, TreeLifePhase.Mature, 2);
            Assert.Equal(64, values["CrownClusters"]); Assert.Equal(0, values["CrownLayers"]);
            Assert.InRange(values["CrownCore"], .099, .101); Assert.Equal(1, values["CrownRoundness"]);
            Assert.Equal(1, values["CrownBranchFollow"]); Assert.Equal(0, values["CrownAnisotropy"]);
            Assert.Equal(1, values["CrownGaps"]);
            Assert.NotEmpty(DendroTreeGenerator.Generate(Spec(), ForestLod.Near).Crown);
        });
    }
}
