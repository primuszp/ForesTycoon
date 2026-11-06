using ForesTycoon.Editor;

namespace ForesTycoon.Tests;

public sealed class BehaviorAuthoringTests
{
    [Fact]
    public void OutputModifiersKeepTheOriginalExpressionAndUndoAsSingleOperations()
    {
        var state = Graph(); var graph = state.Draft.Graphs[0];
        var scale = state.Graphs.AddOutputMultiplier(graph)!;
        var product = graph.Nodes.Single(n => n.Id == graph.Output);
        Assert.Equal("value", product.A); Assert.Equal(scale.Id, product.B); Assert.Equal(1, scale.Value);
        state.Validate(); Assert.Null(state.Error);
        Assert.Equal(1, state.Compiled!.Evaluate(graph.Hook, graph.Kind, 1, default));
        var curve = state.Graphs.AddOutputCurve(graph)!;
        Assert.Equal(product.Id, curve.A); Assert.Equal(curve.Id, graph.Output);
        Assert.True(state.Undo()); Assert.Equal(product.Id, state.Draft.Graphs[0].Output);
        Assert.True(state.Undo()); Assert.Single(state.Draft.Graphs[0].Nodes);
        Assert.True(state.Redo()); Assert.Equal(3, state.Draft.Graphs[0].Nodes.Count);
    }

    [Fact]
    public void InfluenceCommandProducesAMeasurableEffectAndIsUndoable()
    {
        var state = Graph(); var graph = state.Draft.Graphs[0];
        state.Configure(state.Hooks, new() { "stock.level" }, _ => 3);
        string strength = state.Graphs.AddInfluence(graph, "stock.level", "Level", 2, .2)!;
        Assert.Equal(.2, graph.Nodes.Single(n => n.Id == strength).Value);
        state.Validate(); Assert.Null(state.Error);
        Assert.Equal(1.2, state.Compiled!.Evaluate(graph.Hook, graph.Kind, 1, default), 8);
        Assert.True(state.Undo()); Assert.Single(state.Draft.Graphs[0].Nodes);
        Assert.True(state.Redo()); Assert.Equal(11, state.Draft.Graphs[0].Nodes.Count);
    }

    [Fact]
    public void NodeCapacityIsCheckedBeforeChangingTheGraphOrItsHistory()
    {
        var state = Graph(); var source = state.Capture();
        for (int i = 1; i < 255; i++) source.Graphs[0].Nodes.Add(new() { Id = "extra." + i });
        state.LoadDraft(source); var graph = state.Draft.Graphs[0];
        Assert.Null(state.Graphs.AddOutputMultiplier(graph)); Assert.False(state.Undo());
        Assert.NotNull(state.Graphs.AddOutputCurve(graph)); Assert.Equal(256, graph.Nodes.Count);
        string before = state.Capture().ToJson();
        Assert.False(state.Graphs.CanAddNode(graph, BehaviorOperation.Constant));
        Assert.Throws<InvalidOperationException>(() => state.Graphs.AddNode(graph, BehaviorOperation.Constant, "Extra"));
        Assert.Equal(before, state.Capture().ToJson());
        Assert.True(state.Undo()); Assert.Equal(255, state.Draft.Graphs[0].Nodes.Count);
    }

    [Fact]
    public void MissingOutputModifiersLeaveARepairableDraftAndItsHistoryIntact()
    {
        var state = Graph(); var source = state.Capture(); source.Graphs[0].Output = "missing"; state.LoadDraft(source);
        var graph = state.Draft.Graphs[0]; string before = state.Capture().ToJson();
        Assert.Null(state.Graphs.AddOutputCurve(graph)); Assert.Null(state.Graphs.AddOutputMultiplier(graph));
        Assert.Null(state.Graphs.AddInfluence(graph, "stock.level", "Level", 0, 1));
        Assert.Equal(before, state.Capture().ToJson()); Assert.False(state.Undo());
    }

    [Fact]
    public void NodeDeletionPreservesMissingPortsForRepairAndUndoRestoresTheOriginalFormula()
    {
        var state = Graph(); var graph = state.Draft.Graphs[0];
        state.Graphs.AddNode(graph, BehaviorOperation.Multiply, "Multiply");
        state.Graphs.DeleteNode(graph, graph.Nodes[0]);
        var saved = state.Capture().Graphs[0];
        Assert.Equal("value", saved.Output); Assert.Equal("value", saved.Nodes[0].A);
        state.Validate(); Assert.NotNull(state.Error);
        Assert.True(state.Undo()); state.Validate(); Assert.Null(state.Error);
        Assert.Equal(2, state.Draft.Graphs[0].Nodes.Count);
    }

    [Fact]
    public void PortsRejectMissingNodesButAllowUnfinishedCyclicDraftsToBeRepaired()
    {
        var state = Graph(); var graph = state.Draft.Graphs[0];
        var node = state.Graphs.AddNode(graph, BehaviorOperation.Add, "Sum");
        string before = state.Capture().ToJson();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Graphs.SetInput(graph, node, 2, "value"));
        Assert.Throws<ArgumentException>(() => state.Graphs.SetInput(graph, node, 0, "missing"));
        Assert.Throws<ArgumentException>(() => state.Graphs.SetOutput(graph, "missing"));
        Assert.Equal(before, state.Capture().ToJson());
        state.Graphs.SetInput(graph, node, 0, node.Id); state.Graphs.SetOutput(graph, node.Id);
        state.Validate(); Assert.NotNull(state.Error);
        state.Graphs.SetInput(graph, node, 0, "value"); state.Validate(); Assert.Null(state.Error);
        Assert.Equal(2, state.Compiled!.Evaluate(graph.Hook, graph.Kind, 1, default));
    }

    [Fact]
    public void RejectedEditsToAnOldGraphCannotDiscardRedoOrMutateTheCurrentDocument()
    {
        var state = Graph(); var stale = state.Draft.Graphs[0];
        state.Graphs.Edit(stale, g => g.Name = "Changed"); Assert.True(state.Undo());
        string before = state.Capture().ToJson();
        Assert.Throws<InvalidOperationException>(() => state.Graphs.Edit(stale, g => g.Name = "Wrong"));
        Assert.Throws<InvalidOperationException>(() => state.Graphs.EditNode(state.Draft.Graphs[0], stale.Nodes[0], n => n.Value = 9));
        Assert.Equal(before, state.Capture().ToJson()); Assert.True(state.Redo());
        Assert.Equal("Changed", state.Draft.Graphs[0].Name);
    }

    [Fact]
    public void SelectingAnExistingHookDoesNotCreateAnotherGraphOrUndoStep()
    {
        var state = new BehaviorEditorState();
        var hook = WorldBehaviorPolicy.Hooks.First(h => !EcologyResponseProgram.IsPureKind(h.Kind));
        var graph = state.Graphs.Ensure(hook);
        Assert.Same(graph, state.Graphs.Ensure(hook)); Assert.Single(state.Draft.Graphs);
        Assert.True(state.Undo()); Assert.Empty(state.Draft.Graphs); Assert.False(state.Undo());
    }

    [Fact]
    public void RestoringAPureTypeGraphPreservesObjectOverridesAndCanBeUndone()
    {
        var state = new BehaviorEditorState();
        var hook = WorldBehaviorPolicy.Hooks.First(h => EcologyResponseProgram.IsPureKind(h.Kind) && h.SupportsObjects);
        var graph = state.Graphs.Ensure(hook);
        var source = state.Capture();
        source.Graphs[0].Disabled = true;
        var local = new BehaviorModel { Graphs = new() { graph } }.Clone().Graphs[0]; local.Id = "local"; local.ObjectId = 7;
        source.Graphs.Add(local); state.LoadDraft(source);
        graph = state.Draft.Graphs[0]; Assert.False(state.Graphs.CanDelete(graph));
        Assert.Throws<InvalidOperationException>(() => state.Graphs.Delete(graph));
        state.Graphs.RestoreDefault(hook.Id, hook.Kind, null);
        Assert.Equal(2, state.Draft.Graphs.Count);
        Assert.False(state.Draft.Graphs.Single(g => g.ObjectId == null).Disabled);
        Assert.Equal("local", state.Draft.Graphs.Single(g => g.ObjectId == 7).Id);
        Assert.True(state.Undo()); Assert.True(state.Draft.Graphs.Single(g => g.ObjectId == null).Disabled);
    }

    [Fact]
    public void InvalidTemplateDoesNotMutateTheDocumentOrDiscardRedo()
    {
        var state = Graph(); state.Graphs.Edit(state.Draft.Graphs[0], g => g.Name = "Changed"); Assert.True(state.Undo());
        string before = state.Capture().ToJson();
        Assert.Throws<InvalidDataException>(() => state.Instantiate(new(), null));
        Assert.Equal(before, state.Capture().ToJson()); Assert.True(state.Redo());
        Assert.Equal("Changed", state.Draft.Graphs[0].Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemplateApplicationReplacesOnlyTheChosenScopeAndKeepsIndependentCopies(bool controller)
    {
        var state = controller ? Controller() : Graph();
        RuleTemplate template = controller ? AuthoringOperations.Template(state.Draft.Controllers[0]) : AuthoringOperations.Template(state.Draft.Graphs[0]);
        string before = state.Capture().ToJson();
        state.Instantiate(template, 7); state.Instantiate(template, 8);
        var installed = state.Instantiate(template, 7);
        if (controller) {
            Assert.Equal(3, state.Draft.Controllers.Count);
            installed.Controller!.Name = "Local";
            Assert.NotEqual("Local", template.Controller!.Name);
            Assert.NotEqual("Local", state.Draft.Controllers.Single(c => c.ObjectId == 8).Name);
        } else {
            Assert.Equal(3, state.Draft.Graphs.Count);
            installed.Graph!.Nodes[0].Value = 9;
            Assert.Equal(1, template.Graph!.Nodes[0].Value);
            Assert.Equal(1, state.Draft.Graphs.Single(g => g.ObjectId == 8).Nodes[0].Value);
        }
        Assert.True(state.Undo()); Assert.True(state.Undo()); Assert.True(state.Undo());
        Assert.Equal(before, state.Capture().ToJson());
    }

    [Fact]
    public void DeletingTheInitialStateRemovesIncomingTransitionsAndUndoRestoresTheWholeCycle()
    {
        var state = Controller(); var controller = state.Draft.Controllers[0];
        controller.States[1].Transitions.Add(new() { Target = controller.InitialState });
        state.LoadDraft(state.Capture()); controller = state.Draft.Controllers[0];
        string before = state.Capture().ToJson(), removed = controller.InitialState;
        Assert.True(state.Controllers.DeleteState(controller, controller.States[0]));
        Assert.NotEqual(removed, controller.InitialState);
        Assert.DoesNotContain(controller.States.SelectMany(s => s.Transitions), t => t.Target == removed);
        Assert.True(state.Undo()); Assert.Equal(before, state.Capture().ToJson());
    }

    [Fact]
    public void StateAndTransitionCreationHaveSeparateUndoCheckpoints()
    {
        var state = Controller(); var controller = state.Draft.Controllers[0]; int count = controller.States.Count;
        var added = state.Controllers.AddState(controller);
        var transition = state.Controllers.AddTransition(controller, added);
        Assert.NotEqual(added.Id, transition.Target);
        Assert.True(state.Undo()); Assert.Empty(state.Draft.Controllers[0].States.Last().Transitions);
        Assert.True(state.Undo()); Assert.Equal(count, state.Draft.Controllers[0].States.Count);
    }

    [Fact]
    public void LastStateLastConditionAndFirstPriorityAreProtectedWithoutAddingHistory()
    {
        var state = new BehaviorEditorState();
        state.LoadDraft(new() { Controllers = new() { new() { Id = "single", Kind = "truck" } } });
        var controller = state.Draft.Controllers[0]; var only = controller.States[0];
        var transition = state.Controllers.AddTransition(controller, only);
        Assert.Equal(only.Id, transition.Target);
        state.LoadDraft(state.Capture()); controller = state.Draft.Controllers[0]; only = controller.States[0]; transition = only.Transitions[0];
        Assert.False(state.Controllers.DeleteState(controller, only));
        Assert.False(state.Controllers.DeleteCondition(controller, only, transition, transition.Conditions[0]));
        Assert.False(state.Controllers.MoveTransitionUp(controller, only, transition));
        Assert.False(state.Undo()); Assert.Single(controller.States); Assert.Single(transition.Conditions);
    }

    [Fact]
    public void TransitionPriorityAndConditionChangesAreUndoable()
    {
        var state = Controller(); var controller = state.Draft.Controllers[0]; var wait = controller.States[0];
        var added = state.Controllers.AddTransition(controller, wait);
        state.Controllers.MoveTransitionUp(controller, wait, added);
        Assert.Same(added, wait.Transitions[0]); Assert.True(state.Undo());
        controller = state.Draft.Controllers[0]; wait = controller.States[0]; var transition = wait.Transitions[0]; var condition = transition.Conditions[0];
        state.Controllers.SetTrigger(controller, wait, transition, condition, BehaviorTrigger.CargoAtLeast);
        Assert.Equal(0, condition.Value);
        state.Controllers.SetConditionValue(controller, wait, transition, condition, 12);
        Assert.Equal(12, condition.Value); Assert.True(state.Undo()); Assert.True(state.Undo());
        var restored = state.Draft.Controllers[0].States[0].Transitions[0].Conditions[0];
        Assert.Equal(BehaviorTrigger.Elapsed, restored.Trigger); Assert.Equal(5, restored.Value);
    }

    [Fact]
    public void InvalidTargetsAndForeignConditionsDoNotDiscardControllerRedo()
    {
        var state = Controller(); var controller = state.Draft.Controllers[0];
        state.Controllers.Edit(controller, c => c.Name = "Changed"); Assert.True(state.Undo());
        controller = state.Draft.Controllers[0]; var wait = controller.States[0]; var transition = wait.Transitions[0];
        string before = state.Capture().ToJson();
        Assert.Throws<ArgumentException>(() => state.Controllers.SetInitialState(controller, "missing"));
        Assert.Throws<ArgumentException>(() => state.Controllers.SetTarget(controller, wait, transition, "missing"));
        Assert.Throws<InvalidOperationException>(() => state.Controllers.SetTrigger(controller, wait, transition, new(), BehaviorTrigger.Always));
        Assert.Equal(before, state.Capture().ToJson()); Assert.True(state.Redo());
        Assert.Equal("Changed", state.Draft.Controllers[0].Name);
    }

    [Fact]
    public void RestoringTheTypeControllerKeepsObjectSpecificCycles()
    {
        var state = Controller(); var controller = state.Draft.Controllers[0];
        state.Instantiate(AuthoringOperations.Template(controller), 7);
        state.Controllers.RestoreDefault(controller.Kind, null);
        Assert.Equal(7UL, Assert.Single(state.Draft.Controllers).ObjectId);
        Assert.True(state.Undo()); Assert.Equal(2, state.Draft.Controllers.Count);
    }

    private static BehaviorEditorState Graph()
    {
        var graph = new BehaviorGraph { Id = "graph", Name = "Original", Kind = "dynamics", Hook = "flow.graph", Output = "value",
            Nodes = new() { new() { Id = "value", Name = "Value", Operation = BehaviorOperation.Constant, Value = 1 } } };
        var state = new BehaviorEditorState();
        state.Configure(new[] { DynamicsRuntime.FlowHook(new() { Id = "graph" }) }, new(), _ => 0);
        state.LoadDraft(new() { Graphs = new() { graph } }); return state;
    }
    private static BehaviorEditorState Controller()
    {
        var state = new BehaviorEditorState();
        state.Controllers.Ensure("truck", false); state.LoadDraft(state.Capture()); return state;
    }
}
