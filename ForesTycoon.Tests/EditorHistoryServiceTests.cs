using System.Reflection;
using ForesTycoon.Editor;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class EditorHistoryServiceTests
{
    [Fact]
    public void ManyUpdatesFormOneStepAndUndoClosesAnUnfinishedGesture()
    {
        int value = 0;
        var history = new EditorHistoryService<int>(() => value, v => value = v); history.Reset();
        using var edit = history.BeginEdit("drag");
        for (int i = 1; i <= 50; i++) { int next = i; edit.Apply(() => value = next); Assert.False(history.Checkpoint()); }
        Assert.Equal(1, history.Count); Assert.True(history.Undo()); Assert.Equal(0, value);
        Assert.False(edit.IsActive); Assert.False(edit.Commit()); Assert.Equal(2, history.Count);
        Assert.True(history.Redo()); Assert.Equal(50, value); Assert.Equal("drag", history.UndoSummary);
    }

    [Fact]
    public void CancellationAndNoOpGesturesKeepTheRedoBranch()
    {
        int value = 0;
        var history = new EditorHistoryService<int>(() => value, v => value = v); history.Reset();
        history.Edit("first", () => value = 1); history.Edit("second", () => value = 2);
        Assert.True(history.Undo());
        using (var edit = history.BeginEdit("cancelled")) { edit.Apply(() => value = 9); Assert.True(edit.Cancel()); }
        Assert.Equal(1, value); Assert.True(history.CanRedo); Assert.Equal(3, history.Count);
        using (var edit = history.BeginEdit("unchanged")) { edit.Apply(() => value = 8); edit.Apply(() => value = 1); Assert.False(edit.Commit()); }
        Assert.True(history.Redo()); Assert.Equal(2, value); Assert.Equal(3, history.Count);
    }

    [Fact]
    public void AFailedEditRollsBackAndDoesNotBecomeAnUndoEntry()
    {
        int value = 1;
        var history = new EditorHistoryService<int>(() => value, v => value = v); history.Reset();
        Assert.Throws<InvalidOperationException>(() => history.Edit("failed", () => { value = 9; throw new InvalidOperationException("failed"); }));
        Assert.Equal(1, value); Assert.Equal(1, history.Count); Assert.False(history.IsEditing);
        Assert.True(history.Edit("next", () => value = 2)); Assert.True(history.Undo()); Assert.Equal(1, value);
    }

    [Fact]
    public void NestedTransactionsAreRejectedWithoutEndingTheOuterTransaction()
    {
        int value = 1;
        var history = new EditorHistoryService<int>(() => value, v => value = v); history.Reset();
        using var edit = history.BeginEdit("outer");
        Assert.Throws<InvalidOperationException>(() => history.BeginEdit("nested"));
        Assert.True(edit.IsActive); edit.Apply(() => value = 2); Assert.True(edit.Commit());
        Assert.True(history.Undo()); Assert.Equal(1, value);
    }

    [Fact]
    public void ReloadAbandonsStaleHandlesAndKeepsTheNewDocumentsValue()
    {
        var buffer = Numbers(); using var old = buffer.BeginEdit("old"); old.Apply(() => buffer.Value.Add(1));
        buffer.Load(new() { 9 });
        Assert.False(old.IsActive); Assert.False(old.Cancel()); Assert.False(old.Commit());
        Assert.Throws<InvalidOperationException>(() => old.Apply(() => buffer.Value.Add(2)));
        Assert.Equal(new[] { 9 }, buffer.Value); Assert.False(buffer.Undo()); Assert.False(buffer.Redo());
    }

    [Fact]
    public void TransactionsRetainHistoryBoundsAndDiscardRedoOnlyAfterActualChanges()
    {
        int value = 0;
        var history = new EditorHistoryService<int>(() => value, v => value = v, limit: 3); history.Reset();
        for (int i = 1; i <= 5; i++) { int next = i; history.Edit("edit", () => value = next); }
        Assert.Equal(3, history.Count); Assert.True(history.Undo()); Assert.Equal(4, value);
        Assert.True(history.Undo()); Assert.Equal(3, value); Assert.False(history.Undo());
        using (history.BeginEdit("selection")) { }
        Assert.True(history.CanRedo);
        history.Edit("branch", () => value = 8); Assert.False(history.Redo()); Assert.Equal(8, value);
    }

    [Fact]
    public void MapSnapshotsArePureAndDoNotRetainOrRestoreTheLargeInitialState()
    {
        var document = Document(); var original = document.World!;
        string initial = "world-gzip-v1:" + new string('a', 1_000_000); original.InitialState = initial;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        document.History.Reset(); Assert.Same(initial, original.InitialState);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < initial.Length, "Scene history must omit the large initial-state payload.");
        document.History.Edit("paint", () => document.World!.SoilPaint[1] = "soil");
        Assert.Same(initial, original.InitialState);
        string replacement = "world-gzip-v1:new-runtime-state"; document.World!.InitialState = replacement;
        Assert.True(document.History.Undo()!.RebuildWorld);
        Assert.Same(replacement, document.World!.InitialState); Assert.Empty(document.World.SoilPaint);
        Assert.Same(replacement, original.InitialState);
    }

    [Fact]
    public void DocumentTransactionsRestoreAllSceneDataAndLeaveAuthoringAndAlgorithmsInTheirOwnScope()
    {
        var document = Document(); var history = document.History;
        history.Edit("mixed", () => {
            document.World!.SoilPaint[1] = "soil"; document.Tuning.Set("DieselPrice", 1.21);
            document.Rules.Add(new() { Id = "rule" }); document.Behaviors.Draft.Graphs[0].Nodes[0].Value = 2;
            document.Dynamics.Draft.Stocks.Add(new() { Id = "stock" });
        });
        document.RememberAuthoring(); document.Authoring.Entries["notes"] = new() { Notes = "keep" };
        document.Algorithms = new() { Revision = "keep" };
        var restored = history.Undo(); Assert.NotNull(restored); Assert.True(restored.RebuildWorld);
        Assert.Empty(document.World!.SoilPaint); Assert.Empty(document.Tuning.Capture()); Assert.Empty(document.Rules);
        Assert.Equal(1, document.Behaviors.Draft.Graphs[0].Nodes[0].Value); Assert.Empty(document.Dynamics.Draft.Stocks);
        Assert.Equal("keep", document.Authoring.Entries["notes"].Notes); Assert.Equal("keep", document.Algorithms.Revision);
        Assert.NotNull(history.Redo()); Assert.Equal(2, document.Behaviors.Draft.Graphs[0].Nodes[0].Value);
        Assert.Equal(1.21, document.Tuning.Get("DieselPrice")); Assert.Single(document.Dynamics.Draft.Stocks);
    }

    [Fact]
    public void CancellingOrUndoingMapEditsKeepsAnUnchangedGraphsLocalRedo()
    {
        var document = Document(); var graph = document.Behaviors.Draft.Graphs[0];
        document.Behaviors.Remember(); graph.Nodes[0].Value = 2; Assert.True(document.Behaviors.Undo());
        graph = document.Behaviors.Draft.Graphs[0]; document.History.Reset();
        using (var edit = document.History.BeginEdit("cancel")) { edit.Apply(() => document.World!.SoilPaint[1] = "soil"); edit.Cancel(); }
        document.History.Edit("paint", () => document.World!.SoilPaint[2] = "soil");
        Assert.NotNull(document.History.Undo()); Assert.Same(graph, document.Behaviors.Draft.Graphs[0]);
        Assert.True(document.Behaviors.Redo()); Assert.Equal(2, document.Behaviors.Draft.Graphs[0].Nodes[0].Value);
    }

    [Fact]
    public void NonStructuralSceneChangesDoNotRequireRebuildingThePreview()
    {
        var document = Document();
        document.History.Edit("profile", () => document.World!.ProfileScale += .1f);
        Assert.False(document.History.Undo()!.RebuildWorld);
        document.History.Edit("height", () => document.World!.HeightPaint[0] = 4);
        Assert.True(document.History.Undo()!.RebuildWorld);
    }

    [Fact]
    public void IdleHistoryChecksDoNotSerializeTheSceneOnEveryFrame()
    {
        var document = Document(); long version = document.Version;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) Assert.False(document.History.Checkpoint());
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 64 * 1024);
        Assert.Equal(version, document.Version); Assert.Equal(1, document.History.Count);
    }

    [Fact]
    public void AFlowEquationGestureIsOneStepInBothItsEquationAndDynamicsHistory()
    {
        var dynamics = Dynamics(); string flow = dynamics.BoundFlow!;
        using (var edit = dynamics.Equation.BeginEdit("drag")) {
            for (int i = 1; i <= 30; i++) { int next = i; edit.Apply(() => dynamics.Equation.Draft.Graphs[0].Nodes[0].X = next); }
            edit.Commit();
        }
        Assert.True(dynamics.Undo()); Assert.Equal(40, dynamics.Draft.Flows[0].Rate.Nodes[0].X);
        Assert.True(dynamics.Redo()); Assert.Equal(30, dynamics.Draft.Flows[0].Rate.Nodes[0].X);
        Assert.Equal(flow, dynamics.BoundFlow); Assert.Same(dynamics.Draft.Flows[0].Rate, dynamics.Equation.Draft.Graphs[0]);
        Assert.True(dynamics.Undo()); Assert.True(dynamics.Undo()); Assert.Empty(dynamics.Draft.Flows);
    }

    [Fact]
    public void CancellingAnEquationGesturePreservesParentRedoAndTheLiveBinding()
    {
        var dynamics = Dynamics(); var node = dynamics.Equation.Draft.Graphs[0].Nodes[0];
        using (var edit = dynamics.Equation.BeginEdit("first")) { edit.Apply(() => node.Value = 2); edit.Commit(); }
        Assert.True(dynamics.Undo());
        using (var edit = dynamics.Equation.BeginEdit("cancel")) { edit.Apply(() => dynamics.Equation.Draft.Graphs[0].Nodes[0].Value = 9); edit.Cancel(); }
        Assert.Equal(1, dynamics.Draft.Flows[0].Rate.Nodes[0].Value);
        Assert.True(dynamics.Redo()); Assert.Equal(2, dynamics.Draft.Flows[0].Rate.Nodes[0].Value);
        Assert.Same(dynamics.Draft.Flows[0].Rate, dynamics.Equation.Draft.Graphs[0]);
    }

    [Fact]
    public void CreatingAnotherFlowClosesTheEquationGestureBeforeRecordingTheNewFlow()
    {
        var dynamics = Dynamics();
        using var edit = dynamics.Equation.BeginEdit("rate");
        edit.Apply(() => dynamics.Equation.Draft.Graphs[0].Nodes[0].Value = 3);
        dynamics.CreateFlow(dynamics.Draft.Stocks[0].Id);
        Assert.False(edit.IsActive); Assert.Equal(2, dynamics.Draft.Flows.Count);
        Assert.True(dynamics.Undo()); Assert.Single(dynamics.Draft.Flows); Assert.Equal(3, dynamics.Draft.Flows[0].Rate.Nodes[0].Value);
        Assert.True(dynamics.Undo()); Assert.Equal(1, dynamics.Draft.Flows[0].Rate.Nodes[0].Value);
    }

    [Fact]
    public void ReloadingDynamicsDuringAnEquationGestureInvalidatesItsOldHandle()
    {
        var dynamics = Dynamics(); using var edit = dynamics.Equation.BeginEdit("old");
        edit.Apply(() => dynamics.Equation.Draft.Graphs[0].Nodes[0].Value = 3);
        var replacement = dynamics.Capture(); replacement.Flows[0].Rate.Nodes[0].Value = 9;
        dynamics.Load(replacement);
        Assert.False(edit.IsActive); Assert.False(edit.Cancel()); Assert.Equal(9, dynamics.Draft.Flows[0].Rate.Nodes[0].Value);
        Assert.False(dynamics.Undo()); Assert.Same(dynamics.Draft.Flows[0].Rate, dynamics.Equation.Draft.Graphs[0]);
        using var next = dynamics.Equation.BeginEdit("new");
        next.Apply(() => dynamics.Equation.Draft.Graphs[0].Nodes[0].Value = 10); next.Commit();
        Assert.True(dynamics.Undo()); Assert.Equal(9, dynamics.Draft.Flows[0].Rate.Nodes[0].Value);
    }

    [Fact]
    public void TwoBrushStrokesWithoutAnyRenderedFramesAreTwoUndoSteps()
    {
        using var world = FoundationPersistenceTests.FlatWorld();
        var view = new RuleEditorView(); Invoke(view, "EnsureProject", world);
        var document = Field<EditorDocument>(view, "document"); var stroke = Field<HashSet<int>>(view, "stroke");
        stroke.Add(1); Invoke(view, "CommitStroke", world); stroke.Clear();
        stroke.Add(2); Invoke(view, "CommitStroke", world); stroke.Clear();
        Assert.Equal(3, document.History.Count); Assert.True(document.World!.SoilPaint.ContainsKey(1)); Assert.True(document.World.SoilPaint.ContainsKey(2));
        Assert.NotNull(document.History.Undo()); Assert.True(document.World.SoilPaint.ContainsKey(1)); Assert.False(document.World.SoilPaint.ContainsKey(2));
        Assert.NotNull(document.History.Undo()); Assert.Empty(document.World.SoilPaint);
        Assert.NotNull(document.History.Redo()); Assert.Single(document.World.SoilPaint);
    }

    [Fact]
    public void CancellingAnUncommittedBrushDoesNotUndoThePreviousEdit()
    {
        using var world = FoundationPersistenceTests.FlatWorld();
        var view = new RuleEditorView(); Invoke(view, "EnsureProject", world); view.SetTuningForTest("DieselPrice", 1.21);
        var document = Field<EditorDocument>(view, "document"); var stroke = Field<HashSet<int>>(view, "stroke");
        int count = document.History.Count; stroke.Add(1); view.Undo(world);
        Assert.Empty(stroke); Assert.Empty(document.World!.SoilPaint); Assert.Equal(count, document.History.Count);
        Assert.Equal(1.21, document.Tuning.Get("DieselPrice")); Assert.True(view.CanUndo);
    }

    private static EditorBuffer<List<int>> Numbers() => new(new(), v => System.Text.Json.JsonSerializer.Serialize(v), s => System.Text.Json.JsonSerializer.Deserialize<List<int>>(s)!);
    private static EditorDocument Document()
    {
        var document = new EditorDocument();
        document.Load(new RuleProject { World = GameWorld.DefaultScenario(42, 16), Behaviors = new() { Graphs = new() { new() {
            Id = "graph", Hook = "formula", Kind = "world", Output = "value", Nodes = new() { new() { Id = "value", Operation = BehaviorOperation.Constant, Value = 1 } } } } } });
        return document;
    }
    private static DynamicsEditorState Dynamics()
    { var state = new DynamicsEditorState(); string stock = state.CreateStock(); state.CreateFlow(stock); return state; }
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static T Field<T>(object target, string name) {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return (T)(target.GetType().GetField(name, flags) is { } field ? field.GetValue(target) : target.GetType().GetProperty(name, flags)!.GetValue(target))!;
    }
}
