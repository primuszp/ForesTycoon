#nullable enable
using System;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor.Core;

/// <summary>Authors machine cycles and maintains their initial state, transition targets and priority order.</summary>
internal sealed class BehaviorControllerEditor(BehaviorEditorState owner)
{
    internal BehaviorController Ensure(string kind, bool transportCycle)
    {
        var existing = owner.Draft.Controllers.Find(c => c.Kind == kind && c.ObjectId == null);
        if (existing != null) return existing;
        var controller = transportCycle && kind is "truck" or "forwarder" ? BehaviorController.TransportCycle(kind) : BehaviorController.TimedPause(kind);
        owner.Remember(); owner.Draft.Controllers.Add(controller); return controller;
    }

    internal void RestoreDefault(string kind, ulong? objectId)
    {
        if (!owner.Draft.Controllers.Any(c => c.Kind == kind && c.ObjectId == objectId)) return;
        owner.Remember(); owner.Draft.Controllers.RemoveAll(c => c.Kind == kind && c.ObjectId == objectId);
    }
    internal void Delete(BehaviorController controller)
    {
        Require(controller); owner.Remember(); owner.Draft.Controllers.Remove(controller);
    }
    internal void Edit(BehaviorController controller, Action<BehaviorController> edit)
    {
        Require(controller); owner.Remember(); edit(controller);
    }
    internal void EditState(BehaviorController controller, BehaviorState state, Action<BehaviorState> edit)
    {
        Require(controller, state); owner.Remember(); edit(state);
    }
    internal void SetInitialState(BehaviorController controller, string stateId)
    {
        RequireTarget(controller, stateId); Edit(controller, c => c.InitialState = stateId);
    }

    internal BehaviorState AddState(BehaviorController controller)
    {
        Require(controller);
        var state = new BehaviorState { Id = Guid.NewGuid().ToString("N")[..8], Name = "Új állapot", X = 250, Y = 60 + controller.States.Count * 100 };
        owner.Remember(); controller.States.Add(state); return state;
    }
    internal bool DeleteState(BehaviorController controller, BehaviorState state)
    {
        Require(controller, state);
        if (controller.States.Count <= 1) return false;
        owner.Remember(); controller.States.Remove(state);
        foreach (var other in controller.States) other.Transitions.RemoveAll(t => t.Target == state.Id);
        if (controller.InitialState == state.Id) controller.InitialState = controller.States[0].Id;
        return true;
    }

    internal BehaviorTransition AddTransition(BehaviorController controller, BehaviorState state)
    {
        Require(controller, state);
        var transition = new BehaviorTransition { Target = controller.States.First(s => s.Id != state.Id || controller.States.Count == 1).Id };
        owner.Remember(); state.Transitions.Add(transition); return transition;
    }
    internal void SetTarget(BehaviorController controller, BehaviorState state, BehaviorTransition transition, string target)
    {
        Require(controller, state, transition); RequireTarget(controller, target);
        owner.Remember(); transition.Target = target;
    }
    internal void DeleteTransition(BehaviorController controller, BehaviorState state, BehaviorTransition transition)
    {
        Require(controller, state, transition); owner.Remember(); state.Transitions.Remove(transition);
    }
    internal bool MoveTransitionUp(BehaviorController controller, BehaviorState state, BehaviorTransition transition)
    {
        Require(controller, state, transition);
        int index = state.Transitions.IndexOf(transition);
        if (index == 0) return false;
        owner.Remember(); (state.Transitions[index - 1], state.Transitions[index]) = (state.Transitions[index], state.Transitions[index - 1]);
        return true;
    }
    internal BehaviorCondition AddCondition(BehaviorController controller, BehaviorState state, BehaviorTransition transition)
    {
        Require(controller, state, transition);
        var condition = new BehaviorCondition(); owner.Remember(); transition.Conditions.Add(condition); return condition;
    }
    internal bool DeleteCondition(BehaviorController controller, BehaviorState state, BehaviorTransition transition, BehaviorCondition condition)
    {
        Require(controller, state, transition, condition);
        if (transition.Conditions.Count <= 1) return false;
        owner.Remember(); transition.Conditions.Remove(condition); return true;
    }
    internal void SetTrigger(BehaviorController controller, BehaviorState state, BehaviorTransition transition, BehaviorCondition condition, BehaviorTrigger trigger)
    {
        Require(controller, state, transition, condition);
        if (!Enum.IsDefined(trigger)) throw new ArgumentOutOfRangeException(nameof(trigger));
        owner.Remember(); condition.Trigger = trigger; condition.Value = 0;
    }
    internal void SetConditionValue(BehaviorController controller, BehaviorState state, BehaviorTransition transition, BehaviorCondition condition, double value)
    {
        Require(controller, state, transition, condition); owner.Remember(); condition.Value = value;
    }

    private void RequireTarget(BehaviorController controller, string id)
    {
        Require(controller);
        if (!controller.States.Any(s => s.Id == id)) throw new ArgumentException("Hiányzó célállapot.", nameof(id));
    }
    private void Require(BehaviorController controller)
    {
        if (!owner.Draft.Controllers.Contains(controller)) throw new InvalidOperationException("A vezérlés már nem az aktuális munkapéldány része.");
    }
    private void Require(BehaviorController controller, BehaviorState state)
    {
        Require(controller);
        if (!controller.States.Contains(state)) throw new InvalidOperationException("Az állapot már nem az aktuális vezérlés része.");
    }
    private void Require(BehaviorController controller, BehaviorState state, BehaviorTransition transition)
    {
        Require(controller, state);
        if (!state.Transitions.Contains(transition)) throw new InvalidOperationException("Az átmenet már nem az aktuális állapot része.");
    }
    private void Require(BehaviorController controller, BehaviorState state, BehaviorTransition transition, BehaviorCondition condition)
    {
        Require(controller, state, transition);
        if (!transition.Conditions.Contains(condition)) throw new InvalidOperationException("A feltétel már nem az aktuális átmenet része.");
    }
}
