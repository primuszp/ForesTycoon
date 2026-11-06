using ForesTycoon.Editor;

namespace ForesTycoon.Tests;

public sealed class EditorSceneCommandsTests
{
    [Fact]
    public void AStandaloneSceneCommandCanBeUndoneAndRedoneWithoutADrawFrame()
    {
        var document = Open(); string before = document.Revision();
        document.Scene.PaintSoil(new[] { 1, 2 }, document.World!.SoilTypes[0].Id);
        string after = document.Revision(); Assert.NotEqual(before, after);
        Assert.NotNull(document.History.Undo()); Assert.Equal(before, document.Revision());
        Assert.NotNull(document.History.Redo()); Assert.Equal(after, document.Revision());
    }

    [Fact]
    public void SceneRuleAndTuningCommandsJoinTheSameGesture()
    {
        var document = Open(); string before = document.Capture().ToJson();
        using (var gesture = document.History.BeginEdit("mixed gesture")) {
            document.Scene.PaintSprings(new[] { 3 }, 300);
            document.Scene.SetTuning("DieselPrice", 1.23);
            document.RuleBook.CreateEmpty();
            gesture.Commit();
        }
        Assert.Equal(2, document.History.Count);
        Assert.NotNull(document.History.Undo()); Assert.Equal(before, document.Capture().ToJson());
        Assert.NotNull(document.History.Redo()); Assert.Single(document.World!.Springs); Assert.Single(document.Rules);
        Assert.Equal(1.23, document.Tuning.Get("DieselPrice"));
    }

    [Fact]
    public void AnUnchangedCommandOrSelectionGesturePreservesRedo()
    {
        var document = Open(); document.Scene.PaintSprings(new[] { 3 }, 300); document.History.Undo();
        using (var selection = document.History.BeginEdit("selection")) selection.Commit();
        document.Scene.EditWorld(_ => { });
        Assert.True(document.History.CanRedo); Assert.Empty(document.World!.Springs);
        document.History.Redo(); Assert.Equal(300, document.World.Springs[3]);
    }

    [Fact]
    public void FailedCommandsRestorePartialChangesAndKeepRedo()
    {
        var document = Open(); document.Scene.PaintSprings(new[] { 3 }, 300); document.History.Undo();
        string before = document.Capture().ToJson();
        Assert.Throws<InvalidOperationException>(() => document.Scene.EditWorld(world => {
            world.Name = "partial"; world.SoilPaint[2] = "partial"; throw new InvalidOperationException("rejected");
        }));
        Assert.Equal(before, document.Capture().ToJson()); Assert.True(document.History.CanRedo);
        Assert.False(document.History.IsEditing);
    }

    [Fact]
    public void SceneCommandsRejectForeignAndPreUndoObjectsBeforeInvalidating()
    {
        var document = Open(); var soil = document.World!.SoilTypes[0];
        document.Scene.Edit(soil, value => value.Name = "edited"); document.History.Undo();
        long version = document.Version; string before = document.Revision();
        Assert.Throws<InvalidOperationException>(() => document.Scene.Edit(soil, value => value.Name = "stale"));
        Assert.Throws<InvalidOperationException>(() => document.Scene.Edit(new SpeciesDefinition(), value => value.Name = "foreign"));
        Assert.Equal(version, document.Version); Assert.Equal(before, document.Revision()); Assert.True(document.History.CanRedo);
    }

    [Fact]
    public void EditingABuiltInHorizonCreatesAnOwnedProfileAndUndoRestoresInheritance()
    {
        var document = Open(); var soil = document.World!.SoilTypes[0]; soil.Horizons.Clear(); document.History.Reset();
        document.Scene.EditHorizons(soil, horizons => horizons[0].Depth = 2.5);
        Assert.Equal(2.5, soil.Horizons[0].Depth);
        document.History.Undo(); Assert.Empty(document.World.SoilTypes[0].Horizons);
        document.History.Redo(); Assert.Equal(2.5, document.World.SoilTypes[0].Horizons[0].Depth);
    }

    [Fact]
    public void ShapeAndColourCommandsRemoveEmptyOverridesAndAreUndoable()
    {
        var document = Open(); var species = document.World!.Species[0];
        document.Scene.EditShape(species, null, values => values["Scale"] = 2);
        document.Scene.EditShape(species, "mature", values => values["Scale"] = 3);
        document.Scene.EditShape(species, "mature", values => values.Clear());
        Assert.Empty(species.Shape!.Phases); Assert.Equal(2, species.Shape.All["Scale"]);
        document.Scene.EditShape(species, null, values => values.Clear()); Assert.Null(species.Shape);
        document.History.Undo(); Assert.Equal(2, document.World.Species[0].Shape!.All["Scale"]);
        species = document.World.Species[0];
        document.Scene.EditColours(species, value => value.BarkPattern = "plain");
        Assert.NotNull(species.Colours); document.History.Undo(); Assert.Null(document.World.Species[0].Colours);
    }

    [Fact]
    public void ResizeClearsPaintInOneStepAndUndoRestoresAllLayers()
    {
        var document = Open();
        document.Scene.EditWorld(world => {
            world.SoilPaint[1] = "soil"; world.HeightPaint[2] = 4; world.ForestPaint[3] = "Spruce";
            world.ForestFelling.Add(4); world.Springs[5] = 200;
        });
        string before = document.Capture().ToJson(); document.Scene.Resize(48);
        Assert.Empty(document.World!.SoilPaint); Assert.Empty(document.World.HeightPaint);
        Assert.Empty(document.World.ForestPaint); Assert.Empty(document.World.ForestFelling); Assert.Empty(document.World.Springs);
        document.History.Undo(); Assert.Equal(before, document.Capture().ToJson());
    }

    [Fact]
    public void LibraryImportsAreAtomicAndUseAnIndependentUndoScope()
    {
        var document = Open(); var graph = new BehaviorGraph { Id = "template", Name = "template" };
        document.Library.Store(graph); graph.Name = "external change";
        string library = document.Library.Export(); Assert.Equal("template", document.Library.Templates[0].Name);
        document.Scene.PaintSprings(new[] { 3 }, 300); document.History.Undo();
        document.Library.Import(library); Assert.Equal(2, document.Library.Templates.Count);
        Assert.True(document.History.CanRedo); Assert.True(document.Library.Undo()); Assert.Single(document.Library.Templates);
        string before = document.Revision();
        Assert.Throws<System.IO.InvalidDataException>(() => document.Library.Import("{\"schema\":\"forest-rule-template-library\",\"templates\":[{}]}"));
        Assert.Equal(before, document.Revision()); Assert.True(document.Library.Redo()); Assert.Equal(2, document.Library.Templates.Count);
    }

    [Fact]
    public void DynamicsCommandsGuardReferencesAndKeepTheRateGraphAttached()
    {
        var dynamics = new DynamicsEditorState(); string id = dynamics.CreateStock(); string flowId = dynamics.CreateFlow(id);
        var stock = dynamics.Draft.Stocks[0]; var flow = dynamics.Draft.Flows[0];
        Assert.Throws<InvalidOperationException>(() => dynamics.DeleteStock(stock));
        dynamics.EditFlow(flow, value => value.Name = "renamed");
        Assert.Same(flow.Rate, dynamics.Equation.Draft.Graphs[0]);
        Assert.True(dynamics.Undo()); Assert.NotEqual("renamed", dynamics.Draft.Flows[0].Name);
        Assert.Same(dynamics.Draft.Flows[0].Rate, dynamics.Equation.Draft.Graphs[0]);
        dynamics.DeleteFlow(flowId); stock = dynamics.Draft.Stocks[0]; dynamics.DeleteStock(stock); Assert.Empty(dynamics.Draft.Stocks);
        Assert.Throws<InvalidOperationException>(() => dynamics.EditStock(stock, value => value.Name = "stale"));
    }

    [Fact]
    public void PageChoicesAreWindowLocalAndDoNotChangeTheSavedDocument()
    {
        var document = Open(); var first = new EditorUiState(); var second = new EditorUiState();
        string json = document.Capture().ToJson(); string revision = document.Revision(); long version = document.Version;
        first.Map.SelectedTile = 13; first.Tree.Zoom = 4; first.Rules.SelectedBookRule = 4;
        var behavior = new BehaviorEditorView(document.Behaviors, first.Behaviors); behavior.SelectGraph("selected");
        Assert.Equal("selected", first.Behaviors.SelectedGraph); Assert.Null(second.Behaviors.SelectedGraph);
        Assert.Equal(-1, second.Map.SelectedTile); Assert.Equal(1, second.Tree.Zoom);
        Assert.Equal(json, document.Capture().ToJson()); Assert.Equal(revision, document.Revision()); Assert.Equal(version, document.Version);
        Assert.NotSame(first.Expression, first.Behaviors); Assert.NotSame(first.FlowEquation, first.Expression);
    }

    [Fact]
    public void CalculationEffectCommandsKeepTheirRepresentationAndParticipateInHistory()
    {
        var document = Open(); var rule = document.RuleBook.CreateForTarget("road", "condition", "set", "test");
        var effect = document.RuleBook.AddCalculation(rule); Assert.Null(effect.Influence);
        document.RuleBook.EditEffect(rule, effect, value => { value.Mode = "flow"; value.To = "condition"; value.FlowName = "test flow"; });
        Assert.Null(effect.Influence); Assert.Equal("test flow", effect.FlowName);
        document.History.Undo(); effect = document.Rules[0].Effects[1];
        Assert.Equal("set", effect.Mode); Assert.NotEqual("test flow", effect.FlowName); Assert.Null(effect.Influence);
        document.History.Redo(); Assert.Equal("test flow", document.Rules[0].Effects[1].FlowName);
    }

    [Fact]
    public void DesignerNotesAreSavedAsAuthoringAndUseTheLibraryHistory()
    {
        var document = Open(); string revision = document.Revision();
        document.Library.SetNotes("rule", "design note");
        Assert.NotEqual(revision, document.Revision());
        Assert.Equal("design note", RuleProject.FromJson(document.Capture().ToJson()).Authoring!.Entries["rule"].Notes);
        Assert.True(document.Library.Undo()); Assert.Equal("", document.Library.Notes("rule"));
        Assert.True(document.Library.Redo()); Assert.Equal("design note", document.Library.Notes("rule"));
    }

    [Fact]
    public void AnAlreadyNormalizedInspectorDoesNotCaptureTheSceneOnEveryFrame()
    {
        var document = Open(); var rule = document.RuleBook.CreateForTarget("road", "condition", "add", "test");
        var effect = rule.Effects[0]; var editor = document.RuleBook.Influences;
        var term = editor.AddTerm(rule, effect)!; var condition = editor.AddCondition(rule, effect, term, "condition")!;
        editor.NormalizeCondition(rule, effect, term, condition, Array.Empty<RuleFieldOption>());
        long version = document.Version; int count = document.History.Count;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) Assert.False(editor.NormalizeCondition(rule, effect, term, condition, Array.Empty<RuleFieldOption>()));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 64 * 1024);
        Assert.Equal(version, document.Version); Assert.Equal(count, document.History.Count);
    }

    [Fact]
    public void TheCoreRainTrialMatchesTheHostWorldWithoutUsingItsPresentationAdapter()
    {
        var project = SimulationWorld.DefaultRuleProject(); project.World = SimulationWorld.DefaultScenario(42, 16); project.Algorithms = null;
        var result = TrialService.Run(project, project, 0, 1);
        using var host = ForestSpeciesBook.WithContext<GameWorld>(null, () => WorldLoader.CreateProject(project));
        for (int tick = 0; tick < 30; tick++) host.Update(1d / 30);
        int count = host.TileGrid.Columns * host.TileGrid.Rows;
        Assert.Equal(Enumerable.Range(0, count).Select(host.TileSurfaceWater), result.Water);
        Assert.Equal(Enumerable.Range(0, count).Select(tile => (double)host.Trafficability(tile)), result.Edited);
        Assert.Equal(result.Baseline, result.Edited); Assert.True(result.Fits(16, 16));
    }

    private static EditorDocument Open()
    {
        var document = new EditorDocument(); document.Load(new RuleProject { World = SimulationWorld.DefaultScenario(42, 16) }); return document;
    }
}
