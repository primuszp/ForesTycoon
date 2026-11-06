using System.Text.Json;
using ForesTycoon.Editor;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class EditorStateTests
{
    [Fact]
    public void BeforeEditHistorySupportsUndoRedoBranchingAndIgnoresUnchangedSnapshots()
    {
        var buffer = Numbers();
        for (int i = 0; i < 10; i++) buffer.Remember();
        Assert.False(buffer.Undo());
        buffer.Remember(); buffer.Value.Add(1);
        buffer.Remember(); buffer.Value.Add(2);
        Assert.True(buffer.Undo()); Assert.Equal(new[] { 1 }, buffer.Value);
        Assert.True(buffer.Redo()); Assert.Equal(new[] { 1, 2 }, buffer.Value);
        Assert.True(buffer.Undo());
        buffer.Remember(); buffer.Value.Add(3);
        Assert.False(buffer.Redo());
        Assert.True(buffer.Undo()); Assert.Equal(new[] { 1 }, buffer.Value);
        Assert.True(buffer.Redo()); Assert.Equal(new[] { 1, 3 }, buffer.Value);
    }

    [Fact]
    public void LocalHistoryIsBoundedAndLoadingAnotherDocumentClearsIt()
    {
        var buffer = Numbers(3);
        for (int i = 1; i <= 10; i++) { buffer.Remember(); buffer.Value.Add(i); }
        Assert.True(buffer.Undo()); Assert.Equal(9, buffer.Value.Count);
        Assert.True(buffer.Undo()); Assert.Equal(8, buffer.Value.Count);
        Assert.False(buffer.Undo());
        buffer.Load(new() { 20 });
        Assert.False(buffer.Undo()); Assert.False(buffer.Redo()); Assert.Equal(new[] { 20 }, buffer.Value);
    }

    [Fact]
    public void CapturesAndLoadedModelsAreIndependentAndSnapshotReadsDoNotChangeHistory()
    {
        var buffer = Numbers(); var source = new List<int> { 1 };
        buffer.Load(source); source.Add(2);
        buffer.Remember(); buffer.Value.Add(3);
        Assert.True(buffer.Undo());
        int changes = 0; buffer.Changing += () => changes++;
        var copy = buffer.Capture(); copy.Add(99);
        Assert.Equal(new[] { 1 }, buffer.Value); Assert.Equal(0, changes);
        Assert.True(buffer.Redo()); Assert.Equal(new[] { 1, 3 }, buffer.Value);
    }

    [Fact]
    public void RepairableGraphsCanBeLoadedEditedAndUndoneWithoutCompilingThem()
    {
        var (state, _) = Equation();
        var incomplete = state.Capture(); incomplete.Graphs[0].Output = "missing";
        state.LoadDraft(incomplete);
        state.Remember(); state.Draft.Graphs[0].Output = "rate";
        Assert.True(state.Undo()); Assert.Equal("missing", state.Draft.Graphs[0].Output);
        state.Validate(); Assert.NotNull(state.Error); Assert.Null(state.Compiled);
        Assert.True(state.Redo()); state.Validate(); Assert.Null(state.Error); Assert.NotNull(state.Compiled);
    }

    [Fact]
    public void RejectedValidatedLoadPreservesTheWorkingGraphAndItsHistory()
    {
        var (state, _) = Equation();
        state.Remember(); state.Draft.Graphs[0].Nodes[0].Value = 3;
        var invalid = state.Capture(); invalid.Graphs[0].Output = "missing";
        Assert.Throws<InvalidDataException>(() => state.Load(invalid));
        Assert.Equal(3, state.Draft.Graphs[0].Nodes[0].Value);
        Assert.True(state.Undo()); Assert.Equal(1, state.Draft.Graphs[0].Nodes[0].Value);
    }

    [Fact]
    public void BehaviorHistoryRestoresControllerTransitionsAndObjectSpecificBindings()
    {
        var (state, _) = Equation();
        var source = state.Capture();
        var controller = BehaviorController.TimedPause("truck"); controller.ObjectId = 7;
        source.Controllers.Add(controller); state.LoadDraft(source);
        string original = state.Capture().ToJson();
        state.Remember(); state.Draft.Controllers.Clear(); state.Draft.Graphs[0].ObjectId = 42;
        Assert.True(state.Undo()); Assert.Equal(original, state.Capture().ToJson());
        Assert.True(state.Redo()); Assert.Empty(state.Draft.Controllers); Assert.Equal(42UL, state.Draft.Graphs[0].ObjectId);
    }

    [Fact]
    public void ValidationReusesCompilationButSwitchingTheValueSourceUsesTheNewReader()
    {
        var (state, hook) = Equation();
        var graph = state.Draft.Graphs[0];
        graph.Nodes[0].Operation = BehaviorOperation.NamedValue; graph.Nodes[0].Key = "stock.test";
        var keys = new HashSet<string> { "stock.test" };
        var hooks = new[] { hook };
        state.Configure(hooks, keys, _ => 4); state.Validate();
        var compiled = Assert.IsType<CompiledBehaviorModel>(state.Compiled);
        Assert.Equal(4, Evaluate(compiled, hook));
        state.Configure(new[] { hook }, new() { "stock.test" }, _ => 8); state.Validate();
        Assert.Same(compiled, state.Compiled); Assert.Equal(8, Evaluate(compiled, hook));
        keys.Clear(); hooks[0] = hook with { Id = "changed" };
        Assert.Contains("stock.test", state.NamedKeys); Assert.Equal(hook.Id, state.Hooks[0].Id);
        state.Configure(new[] { hook }, new(), _ => 8); state.Validate();
        Assert.NotNull(state.Error); Assert.Null(state.Compiled);
    }

    [Fact]
    public void GraphDragsAndEditsBetweenCheckpointsStillInvalidateValidation()
    {
        var (state, _) = Equation(); state.Validate();
        var compiled = state.Compiled;
        state.Draft.Graphs[0].Output = "missing";
        state.Validate(); Assert.NotNull(state.Error); Assert.Null(state.Compiled);
        state.Draft.Graphs[0].Output = "rate";
        state.Validate(); Assert.Null(state.Error); Assert.NotSame(compiled, state.Compiled);
    }

    [Fact]
    public void FlowEquationEditsImmediatelyBelongToTheDocumentAndCaptureIsPure()
    {
        var state = Dynamics();
        state.Equation.Remember(); state.Equation.Draft.Graphs[0].Nodes[0].Value = 3;
        Assert.Same(state.Draft.Flows[0].Rate, state.Equation.Draft.Graphs[0]);
        int changes = 0; state.Changing += () => changes++;
        for (int i = 0; i < 5; i++) Assert.Equal(3, state.Capture().Flows[0].Rate.Nodes[0].Value);
        Assert.Equal(0, changes);
        Assert.True(state.Undo()); Assert.Equal(1, state.Draft.Flows[0].Rate.Nodes[0].Value);
        Assert.True(state.Redo()); Assert.Equal(3, state.Draft.Flows[0].Rate.Nodes[0].Value);
        Assert.Same(state.Draft.Flows[0].Rate, state.Equation.Draft.Graphs[0]);
    }

    [Fact]
    public void EquationUndoRedoReattachesTheRestoredGraphToItsFlow()
    {
        var state = Dynamics();
        state.Equation.Remember(); state.Equation.Draft.Graphs[0].Nodes[0].Value = 3;
        Assert.True(state.Equation.Undo()); Assert.Equal(1, state.Draft.Flows[0].Rate.Nodes[0].Value);
        Assert.True(state.Equation.Redo()); Assert.Equal(3, state.Draft.Flows[0].Rate.Nodes[0].Value);
        Assert.Same(state.Draft.Flows[0].Rate, state.Equation.Draft.Graphs[0]);
    }

    [Fact]
    public void SwitchingFlowsKeepsTheirEditsSeparateAndDeletingTheBoundFlowCanBeUndone()
    {
        var state = Dynamics();
        string first = state.BoundFlow!;
        state.Equation.Remember(); state.Equation.Draft.Graphs[0].Nodes[0].Value = 3;
        string second = state.CreateFlow(state.Draft.Stocks[0].Id);
        Assert.Equal(1, state.Equation.Draft.Graphs[0].Nodes[0].Value);
        state.BindFlow(first); Assert.Equal(3, state.Equation.Draft.Graphs[0].Nodes[0].Value);
        state.DeleteFlow(first); Assert.Null(state.BoundFlow); Assert.Empty(state.Equation.Draft.Graphs);
        Assert.True(state.Undo()); Assert.Contains(state.Draft.Flows, f => f.Id == first);
        state.BindFlow(first); Assert.Equal(3, state.Equation.Draft.Graphs[0].Nodes[0].Value);
        state.BindFlow(second); Assert.Equal(1, state.Equation.Draft.Graphs[0].Nodes[0].Value);
    }

    [Fact]
    public void ReloadingTheSameFlowIdCannotOverwriteTheNewDocumentWithAnOldEquation()
    {
        var state = Dynamics();
        state.Equation.Remember(); state.Equation.Draft.Graphs[0].Nodes[0].Value = 3;
        var replacement = state.Capture(); replacement.Flows[0].Rate.Nodes[0].Value = 9;
        state.Load(replacement);
        Assert.Equal(9, state.Equation.Draft.Graphs[0].Nodes[0].Value);
        Assert.Equal(9, state.Capture().Flows[0].Rate.Nodes[0].Value);
        Assert.False(state.Undo()); Assert.False(state.Equation.Undo());
    }

    [Fact]
    public void BehaviorAndAuthoringUndoAutomaticallyInvalidateTheDocumentRevision()
    {
        var document = new EditorDocument();
        var (equation, _) = Equation(); document.Behaviors.LoadDraft(equation.Capture());
        string Revision() => document.Revision();
        string original = Revision();
        document.Behaviors.Remember(); document.Behaviors.Draft.Graphs[0].Nodes[0].Value = 3;
        Assert.NotEqual(original, Revision());
        Assert.True(document.Behaviors.Undo()); Assert.Equal(original, Revision());
        document.RememberAuthoring(); document.Authoring.Entries["test"] = new() { Notes = "changed" };
        Assert.NotEqual(original, Revision());
        Assert.True(document.UndoAuthoring()); Assert.Equal(original, Revision());
        Assert.True(document.RedoAuthoring()); Assert.NotEqual(original, Revision());
    }

    [Fact]
    public void DynamicsEquationEditsInvalidateTheDocumentWithoutReadingTheView()
    {
        var document = new EditorDocument(); document.Dynamics.Load(Dynamics().Capture());
        string Revision() => document.Revision();
        string original = Revision();
        document.Dynamics.Equation.Remember(); document.Dynamics.Equation.Draft.Graphs[0].Nodes[0].Value = 3;
        Assert.NotEqual(original, Revision());
        Assert.True(document.Dynamics.Undo()); Assert.Equal(original, Revision());
    }

    [Fact]
    public void LoadingAProjectResetsAllLocalHistoriesAndCapturesTheOwnedModels()
    {
        var document = new EditorDocument(); document.Dynamics.Load(Dynamics().Capture());
        document.RememberAuthoring(); document.Authoring.Entries["test"] = new();
        document.Dynamics.Equation.Remember(); document.Dynamics.Equation.Draft.Graphs[0].Nodes[0].Value = 3;
        var replacement = document.Capture(); document.Load(replacement);
        Assert.False(document.Dynamics.Undo()); Assert.False(document.Dynamics.Equation.Undo());
        Assert.False(document.UndoAuthoring()); Assert.False(document.Behaviors.Undo());
        replacement.Dynamics.Flows[0].Rate.Nodes[0].Value = 99;
        Assert.Equal(3, document.Capture().Dynamics.Flows[0].Rate.Nodes[0].Value);
    }

    [Fact]
    public void ViewsEditTheDocumentOwnedModelsAndTheirReadsDoNotChangeTheDocument()
    {
        var document = new EditorDocument(); document.Dynamics.Load(Dynamics().Capture());
        var view = new DynamicsEditorView(document.Dynamics);
        view.EquationEditor.SelectGraph(document.Dynamics.Equation.Draft.Graphs[0].Id);
        view.EquationEditor.AddOutputMultiplier();
        Assert.Equal(3, document.Dynamics.Draft.Flows[0].Rate.Nodes.Count);
        long version = document.Version;
        Assert.Equal(3, view.Document.Flows[0].Rate.Nodes.Count); Assert.Equal(version, document.Version);
        Assert.True(view.EquationEditor.Undo()); Assert.Single(document.Dynamics.Draft.Flows[0].Rate.Nodes);
    }

    [Fact]
    public void PausedSandboxStillProcessesCommandsWithoutAdvancingSimulationTime()
    {
        using var world = FoundationPersistenceTests.FlatWorld();
        var sandbox = new SandboxSession(); ulong tick = world.SimulationTick;
        world.QueueWeather(WeatherPreset.Rain, 5, 20);
        for (int i = 0; i < 1000 && world.CommandPending; i++) sandbox.Advance(world, 0);
        Assert.False(world.CommandPending); Assert.Equal(tick, world.SimulationTick); Assert.True(sandbox.IsPaused);
    }

    [Fact]
    public void SandboxCanStepWhilePausedAndItsPauseStateIsIndependentOfOtherSessions()
    {
        using var world = FoundationPersistenceTests.FlatWorld();
        var sandbox = new SandboxSession(); sandbox.Advance(world, 0);
        ulong tick = world.SimulationTick;
        sandbox.RequestTime(1d / 30);
        for (int i = 0; i < 2000 && world.SimulationTick == tick; i++) sandbox.Advance(world, 0);
        Assert.Equal(tick + 1, world.SimulationTick); Assert.True(sandbox.IsPaused);
        var other = new SandboxSession(); other.TogglePause();
        Assert.False(other.IsPaused); Assert.True(sandbox.IsPaused);
    }

    private static EditorBuffer<List<int>> Numbers(int limit = 100) => new(new(),
        value => JsonSerializer.Serialize(value), json => JsonSerializer.Deserialize<List<int>>(json)!, limit);
    private static DynamicsEditorState Dynamics()
    {
        var state = new DynamicsEditorState();
        string stock = state.CreateStock(); state.CreateFlow(stock);
        state.Load(state.Capture()); return state;
    }
    private static (BehaviorEditorState State, BehaviorHook Hook) Equation()
    {
        var dynamics = Dynamics();
        return (dynamics.Equation, DynamicsRuntime.FlowHook(dynamics.Draft.Flows[0]));
    }
    private static double Evaluate(CompiledBehaviorModel compiled, BehaviorHook hook)
        => compiled.Evaluate(hook.Id, hook.Kind, 1, new(0, 0, 0, 0, 0));
}
