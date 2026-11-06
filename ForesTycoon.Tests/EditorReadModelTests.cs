using System.Reflection;
using ForesTycoon.Editor;

namespace ForesTycoon.Tests;

public sealed class EditorReadModelTests
{
    [Fact]
    public void RuleQueriesExposeNoSettersOrMutableCollections()
    {
        foreach (var type in new[] { typeof(RuleView), typeof(RuleEffectView), typeof(RuleInfluenceView), typeof(InfluenceTermView),
                     typeof(InfluenceConditionView), typeof(InfluenceFactorView), typeof(RuleFieldView), typeof(RuleTemplateView) }) {
            Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
            Assert.DoesNotContain(type.GetProperties(), property => property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition() == typeof(List<>));
        }
        var (document, session, rule) = Open();
        Assert.IsNotAssignableFrom<ICollection<RuleView>>(session.Entries);
        Assert.IsNotAssignableFrom<ICollection<RuleEffectView>>(rule.Effects);
        Assert.IsNotAssignableFrom<ICollection<InfluenceTermView>>(rule.Effects[0].Influence!.Terms);
        Assert.DoesNotContain(typeof(RuleBookSession).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic), method =>
            method.IsAssembly && method.ReturnType == typeof(WorldRule) && method.GetParameters().Length == 1);
    }

    [Fact]
    public void RepeatedQueriesKeepIdentityAndDoNotAllocateOrChangeTheRevision()
    {
        var (document, session, rule) = Open(); var effect = rule.Effects[0]; var influence = effect.Influence;
        long version = document.Version; long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) {
            Assert.Same(rule, session.Entries[0]); Assert.Same(effect, rule.Effects[0]); Assert.Same(influence, effect.Influence);
        }
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 32 * 1024);
        Assert.Equal(version, document.Version);
    }

    [Fact]
    public void CommandsUpdateExistingReadHandlesAndCollectionsWithoutLeakingOwnedDtos()
    {
        var (document, session, rule) = Open(); var effects = rule.Effects;
        session.Edit(rule, value => value.Name = "renamed"); Assert.Equal("renamed", rule.Name);
        var effect = effects[0]; var term = session.Influences.AddTerm(rule, effect)!;
        session.Influences.EditTerm(rule, effect, term, value => value.Amount = .25);
        Assert.Same(term, effect.Influence!.Terms[0]); Assert.Equal(.25, term.Amount);
        session.AddCalculation(rule); Assert.Equal(2, effects.Count);
        var copy = rule.Capture(); copy.Name = "foreign"; copy.Effects.Clear();
        Assert.Equal("renamed", rule.Name); Assert.Equal(2, effects.Count); Assert.Equal("renamed", document.Rules[0].Name);
    }

    [Fact]
    public void CalculationAndCurveCapturesAreIndependent()
    {
        var (document, session, rule) = Open(); var effect = rule.Effects[0]; var term = session.Influences.AddTerm(rule, effect)!;
        var factor = session.Influences.AddFactor(rule, effect, term, "condition")!;
        var curve = factor.ToCurve(); curve.Points[0] = (10, 10); Assert.Equal(0, factor.InputLow);
        var graph = new BehaviorGraph { Id = "expression", Hook = "expression", Kind = "world", Output = "value",
            Nodes = new() { new() { Id = "value", Operation = BehaviorOperation.Constant, Value = 3 } } };
        session.SetCalculation(rule, null, graph); var captured = rule.ConditionGraph!; captured.Nodes.Clear();
        Assert.Single(rule.ConditionGraph!.Nodes); Assert.Single(document.Rules[0].ConditionGraph.Nodes);
        session.SetCalculation(rule, effect, graph); effect.Computation!.Nodes.Clear();
        Assert.Single(document.Rules[0].Effects[0].Computation.Nodes);
    }

    [Fact]
    public void UndoAndReloadRejectStaleHandlesWhileTheQueryListFollowsTheCurrentDocument()
    {
        var (document, session, rule) = Open(); var entries = session.Entries;
        session.Edit(rule, value => value.Name = "changed"); document.History.Undo();
        var restored = entries[0]; Assert.NotSame(rule, restored);
        long version = document.Version;
        Assert.Throws<InvalidOperationException>(() => session.Edit(rule, value => value.Name = "stale"));
        Assert.Equal(version, document.Version); Assert.True(document.History.CanRedo);
        document.Load(document.Capture());
        Assert.Throws<InvalidOperationException>(() => session.Delete(restored));
        Assert.NotSame(restored, entries[0]);
    }

    [Fact]
    public void HandlesCannotCrossSessionsOrParentRules()
    {
        var (document, session, first) = Open(); var second = session.CreateEmpty(); var effect = first.Effects[0];
        long version = document.Version;
        Assert.Throws<InvalidOperationException>(() => session.DeleteEffect(second, effect));
        var other = new RuleBookSession(document);
        Assert.Throws<InvalidOperationException>(() => other.Edit(first, value => value.Name = "foreign session"));
        Assert.Equal(version, document.Version); Assert.Single(first.Effects);
    }

    [Fact]
    public void TemplateQueriesAndCopiesCannotMutateTheLibraryAndOldHandlesAreRejected()
    {
        var document = new EditorDocument(); var library = new RuleLibrarySession(document.Library);
        library.Store(new BehaviorGraph { Id = "template", Name = "template", Nodes = new() { new() { Id = "node", Value = 2 } } });
        var template = library.Templates[0]; var capture = template.Capture(); capture.Name = "foreign"; capture.Graph.Nodes.Clear();
        Assert.Equal("template", template.Name); Assert.Single(template.Capture().Graph.Nodes);
        library.Rename(template, "changed"); Assert.Equal("changed", template.Name);
        Assert.True(library.Undo()); Assert.Equal("template", library.Templates[0].Name);
        Assert.Throws<InvalidOperationException>(() => library.Delete(template));
        Assert.Throws<InvalidOperationException>(() => new RuleLibrarySession(document.Library).Delete(library.Templates[0]));
    }

    [Fact]
    public void FieldAnalysisResultsAreFrozenAndRefreshAfterAnEdit()
    {
        var (document, session, rule) = Open(); session.Edit(rule, value => value.Enabled = true); var first = session.FieldUses();
        Assert.Same(first, session.FieldUses());
        var target = first.Single(use => use.Key == "road.condition"); Assert.Single(target.Writers);
        Assert.Throws<NotSupportedException>(() => ((ICollection<RuleFieldWrite>)target.Writers).Clear());
        session.Edit(rule, value => value.Enabled = false); var changed = session.FieldUses(); Assert.NotSame(first, changed);
        Assert.True(target.Writers[0].Enabled); Assert.False(changed.Single(use => use.Key == "road.condition").Writers[0].Enabled);
    }

    [Fact]
    public void PageContractsUseAuthoringPortsInsteadOfMutableRuleBookServices()
    {
        foreach (var type in new[] { typeof(RuleWorkspaceContext), typeof(RuleBookContext), typeof(RuleLibraryContext), typeof(RuleObjectBrowserContext) }) {
            Assert.DoesNotContain(type.GetProperties(), property => property.PropertyType == typeof(EditorDocument)
                || property.PropertyType == typeof(RuleBookEditor) || property.PropertyType == typeof(EditorAuthoringCommands)
                || property.PropertyType == typeof(RuleWorkspaceView));
        }
    }

    [Fact]
    public void SceneQueriesExposeOnlyReadOnlyMembersAndCollections()
    {
        foreach (var type in new[] { typeof(SceneView), typeof(SoilView), typeof(SoilHorizonView), typeof(SpeciesView), typeof(TreeShapeView),
                     typeof(ElevationZoneView), typeof(BiomeView), typeof(GroundMaterialView), typeof(MissionView), typeof(RoadView) })
            Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        var document = new EditorDocument(); document.Load(new RuleProject { World = SimulationWorld.DefaultScenario(42, 16) });
        var scene = new SceneSession(document.Scene);
        Assert.IsNotAssignableFrom<IList<SoilView>>(scene.World.SoilTypes);
        Assert.IsNotAssignableFrom<IDictionary<int, string>>(scene.World.SoilPaint);
        Assert.Equal(typeof(SceneSession), typeof(EditorPageContext).GetProperty("Scene")!.PropertyType);
    }

    [Fact]
    public void SceneCommandsResolveReadHandlesAndOldHandlesCannotMutateAfterUndo()
    {
        var document = new EditorDocument(); document.Load(new RuleProject { World = SimulationWorld.DefaultScenario(42, 16) });
        var scene = new SceneSession(document.Scene); var world = scene.World; var soil = world.SoilTypes[0];
        scene.Edit(soil, value => value.Name = "changed"); Assert.Equal("changed", soil.Name);
        scene.PaintSoil(new[] { 1 }, soil.Id); Assert.Equal(soil.Id, world.SoilPaint[1]);
        document.History.Undo(); Assert.Empty(scene.World.SoilPaint);
        long version = document.Version;
        Assert.Throws<InvalidOperationException>(() => scene.Edit(soil, value => value.Name = "stale"));
        Assert.Equal(version, document.Version); Assert.True(document.History.CanRedo);
        Assert.Throws<InvalidOperationException>(() => new SceneSession(document.Scene).Edit(scene.World.SoilTypes[0], value => value.Name = "other session"));
    }

    [Fact]
    public void SpeciesShapeDictionariesAndColourCopiesCannotChangeTheScene()
    {
        var document = new EditorDocument(); document.Load(new RuleProject { World = SimulationWorld.DefaultScenario(42, 16) });
        var scene = new SceneSession(document.Scene); var species = scene.World.Species[0];
        scene.EditShape(species, null, values => values["Scale"] = 2);
        scene.EditShape(species, TreeShapeDefinition.PhaseNames[0], values => values["Scale"] = 3);
        Assert.Equal(2, species.Shape!.All["Scale"]); Assert.Equal(3, species.Shape.Phases[TreeShapeDefinition.PhaseNames[0]]["Scale"]);
        Assert.IsNotAssignableFrom<IDictionary<string, double>>(species.Shape.All);
        Assert.IsNotAssignableFrom<IDictionary<string, double>>(species.Shape.Phases[TreeShapeDefinition.PhaseNames[0]]);
        scene.EditColours(species, value => value.Bark = "#112233");
        species.Colours!.Bark = "#FFFFFF"; Assert.Equal("#112233", species.Colours.Bark);
        Assert.Equal("#112233", document.World!.Species[0].Colours.Bark);
    }

    [Fact]
    public void SceneQueriesKeepStableIdentityWithoutCopyingTheCheckpoint()
    {
        var document = new EditorDocument(); document.Load(new RuleProject { World = SimulationWorld.DefaultScenario(42, 16) });
        document.World!.InitialState = new string('x', 100_000);
        var scene = new SceneSession(document.Scene); var world = scene.World; var soil = world.SoilTypes[0];
        long version = document.Version; long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) {
            Assert.Same(world, scene.World); Assert.Same(soil, scene.World.SoilTypes[0]);
            Assert.Same(document.World.InitialState, world.InitialState);
        }
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 32 * 1024);
        Assert.Equal(version, document.Version);
    }

    [Fact]
    public void DynamicsListsExposeReadOnlyHandlesAndFollowRestoredModels()
    {
        var state = new DynamicsEditorState(); state.CreateStock(); var stocks = state.Stocks; var first = stocks[0];
        state.EditStock(state.Draft.Stocks[0], stock => stock.Name = "changed"); Assert.Equal("changed", first.Name);
        state.Undo(); Assert.NotSame(first, stocks[0]); Assert.NotEqual("changed", stocks[0].Name);
        Assert.All(typeof(DynamicsStockView).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.All(typeof(DynamicsFlowView).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.Null(typeof(DynamicsEditorView).GetProperty("Draft", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.DoesNotContain(typeof(EditorMissionProgress).GetProperties(), property => property.PropertyType == typeof(MissionGoal));
    }

    [Fact]
    public void MissingOptionalProfilesRemainInheritedWithoutInitializingTheDocumentOnRead()
    {
        var document = new EditorDocument(); document.Load(new RuleProject { World = SimulationWorld.DefaultScenario(42, 16) });
        document.World!.SoilTypes[0].Horizons = null!;
        document.World.Species[0].Shape = new() { All = null!, Phases = null! };
        var scene = new SceneSession(document.Scene); long version = document.Version;
        Assert.Empty(scene.World.SoilTypes[0].Horizons); Assert.NotEmpty(scene.World.SoilTypes[0].Profile());
        Assert.Empty(scene.World.Species[0].Shape!.All); Assert.Empty(scene.World.Species[0].Shape!.Phases);
        Assert.Null(document.World.SoilTypes[0].Horizons); Assert.Null(document.World.Species[0].Shape.All);
        Assert.Null(document.World.Species[0].Shape.Phases); Assert.Equal(version, document.Version);
    }

    [Fact]
    public void ReadModelsCoverAllPersistedPropertiesOfTheirDomainTypes()
    {
        foreach (var (model, view) in new[] {
            (typeof(WorldRule), typeof(RuleView)), (typeof(RuleEffect), typeof(RuleEffectView)), (typeof(RuleInfluence), typeof(RuleInfluenceView)),
            (typeof(InfluenceTerm), typeof(InfluenceTermView)), (typeof(InfluenceFactor), typeof(InfluenceFactorView)), (typeof(InfluenceCondition), typeof(InfluenceConditionView)),
            (typeof(RuleFieldDefinition), typeof(RuleFieldView)), (typeof(WorldScenario), typeof(SceneView)), (typeof(SoilTypeDefinition), typeof(SoilView)),
            (typeof(SoilHorizon), typeof(SoilHorizonView)), (typeof(SpeciesDefinition), typeof(SpeciesView)), (typeof(TreeShapeDefinition), typeof(TreeShapeView)),
            (typeof(ElevationZoneDefinition), typeof(ElevationZoneView)), (typeof(BiomeDefinition), typeof(BiomeView)),
            (typeof(GroundMaterialDefinition), typeof(GroundMaterialView)), (typeof(MissionGoal), typeof(MissionView)), (typeof(RoadSegment), typeof(RoadView)) }) {
            foreach (var property in model.GetProperties().Where(property => property.SetMethod is { IsPublic: true }))
                Assert.NotNull(view.GetProperty(property.Name));
        }
    }

    private static (EditorDocument Document, RuleBookSession Session, RuleView Rule) Open()
    {
        var document = new EditorDocument(); var session = new RuleBookSession(document);
        var rule = session.CreateForTarget("road", "condition", "add", "Road"); return (document, session, rule);
    }
}
