using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ForesTycoon.Rules
{
    public enum BehaviorAction { Autonomous, Wait, ReturnHome, LoadOnly, UnloadOnly, TravelOnly, DepartLoaded }
    public enum BehaviorTrigger { Always, Elapsed, CargoAtLeast, CargoBelow, MachineState, EnteredMachineState, WorkAssigned, WorkFinished, Broken, Repaired }
    public sealed class BehaviorCondition
    {
        public BehaviorTrigger Trigger { get; set; }
        public double Value { get; set; }
    }
    public sealed class BehaviorTransition
    {
        public string Target { get; set; } = "";
        // All conditions must match; transitions are evaluated in authoring order.
        public List<BehaviorCondition> Conditions { get; set; } = new() { new() { Trigger = BehaviorTrigger.Elapsed, Value = 5 } };
    }
    public sealed class BehaviorState
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "Állapot";
        public BehaviorAction Action { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public List<BehaviorTransition> Transitions { get; set; } = new();
    }
    public sealed class BehaviorController
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "Gépvezérlés";
        public string Kind { get; set; } = "forwarder";
        public ulong? ObjectId { get; set; }
        public string InitialState { get; set; } = "work";
        public List<BehaviorState> States { get; set; } = new() { new() { Id = "work", Name = "Munkavégzés", X = 30, Y = 60 } };
        public static BehaviorController TimedPause(string kind) => new() {
            Id = Guid.NewGuid().ToString("N"), Name = "Indulás késleltetése", Kind = kind, InitialState = "wait",
            States = new() {
                new() { Id = "wait", Name = "Várakozás 5 s", Action = BehaviorAction.Wait, X = 30, Y = 60,
                    Transitions = new() { new() { Target = "work" } } },
                new() { Id = "work", Name = "Munkavégzés", X = 300, Y = 170 }
            }
        };
        public static BehaviorController TransportCycle(string kind)
        {
            if (kind is not ("forwarder" or "truck")) throw new ArgumentException("Szállítási ciklus: forwarder vagy truck.", nameof(kind));
            BehaviorTransition When(string target, double state) => new() { Target = target, Conditions = new() { new() { Trigger = BehaviorTrigger.MachineState, Value = state } } };
            int loading = kind == "truck" ? 2 : 3, driving = kind == "truck" ? 3 : 1;
            var travel = new BehaviorState { Id = "travel", Name = "Haladás", Action = BehaviorAction.TravelOnly, X = 20, Y = 30,
                Transitions = new() { When("load", loading), When("unload", 4) } };
            if (kind == "truck") travel.Transitions.Add(When("load", 7));
            var load = new BehaviorState { Id = "load", Name = "Rakodás 5 m³-ig", Action = BehaviorAction.LoadOnly, X = 290, Y = 30,
                Transitions = new() { new() { Target = "depart", Conditions = new() { new() { Trigger = BehaviorTrigger.CargoAtLeast, Value = 5 } } }, When("travel", driving) } };
            var depart = new BehaviorState { Id = "depart", Name = "Indulás", Action = BehaviorAction.DepartLoaded, X = 290, Y = 220,
                Transitions = new() { When("travel", driving), When("unload", 4) } };
            var unload = new BehaviorState { Id = "unload", Name = "Lerakodás", Action = BehaviorAction.UnloadOnly, X = 20, Y = 220,
                Transitions = new() { When("travel", kind == "truck" ? 5 : 1) } };
            if (kind == "forwarder") {
                load.Transitions.Add(When("unload", 4)); load.Transitions.Add(When("travel", 0));
                unload.Transitions.Add(When("load", 3)); unload.Transitions.Add(When("travel", 0));
                depart.Transitions.Add(When("travel", 0));
            } else {
                foreach (var state in new[] { load, depart, unload }) {
                    state.Transitions.Add(When("travel", 6)); state.Transitions.Add(When("travel", 0)); state.Transitions.Add(When("travel", 1));
                }
            }
            return new() { Id = Guid.NewGuid().ToString("N"), Name = "Szállítási ciklus", Kind = kind, InitialState = "travel", States = new() { travel, load, depart, unload } };
        }
    }
    public readonly record struct ControllerInputs(double Delta, double Cargo, int MachineState, bool Working, bool Broken);
    public sealed record BehaviorControllerSnapshot(string Controller, string Kind, ulong ObjectId, string State, double Elapsed,
        bool Initialized, int PreviousMachineState, bool PreviousWorking, bool PreviousBroken);

    /// <summary>Deterministic state machines. One transition per simulation substep, never a recursive event loop.</summary>
    public sealed class CompiledBehaviorControllers
    {
        private readonly Dictionary<(string Kind, ulong? Id), BehaviorController> bindings = new();
        private Dictionary<(string Kind, ulong Id), BehaviorControllerSnapshot> instances = new();
        public bool HasControllers => bindings.Count > 0;
        public CompiledBehaviorControllers(BehaviorModel source)
        {
            var model = source.Clone();
            if (model.Schema != "forest-behaviors" || model.Version is not (1 or 2 or 3) || model.Controllers == null || model.Controllers.Count > 512 || model.Version == 1 && model.Controllers.Count != 0)
                throw new InvalidDataException("Érvénytelen vezérlési modellverzió vagy túl sok vezérlés.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var controller in model.Controllers) {
                if (controller == null || string.IsNullOrWhiteSpace(controller.Id) || !ids.Add(controller.Id)
                    || string.IsNullOrWhiteSpace(controller.Name) || controller.Kind is not ("forwarder" or "processor" or "truck")
                    || !bindings.TryAdd((controller.Kind, controller.ObjectId), controller))
                    throw new InvalidDataException("Hibás vagy ismétlődő gépvezérlési kötés.");
                if (controller.States == null || controller.States.Count is < 1 or > 128)
                    throw new InvalidDataException("1–128 vezérlési állapot szükséges.");
                var states = new HashSet<string>(StringComparer.Ordinal);
                foreach (var state in controller.States)
                    if (state == null || string.IsNullOrWhiteSpace(state.Id) || !states.Add(state.Id) || string.IsNullOrWhiteSpace(state.Name)
                        || !Enum.IsDefined(state.Action) || !float.IsFinite(state.X) || !float.IsFinite(state.Y)
                        || state.Transitions == null || state.Transitions.Count > 64)
                        throw new InvalidDataException("Érvénytelen vezérlési állapot.");
                if (controller.Kind == "processor" && controller.States.Any(s => s.Action >= BehaviorAction.LoadOnly))
                    throw new InvalidDataException("A részletes szállítási műveletek forwarderhez és teherautóhoz köthetők.");
                if (controller.InitialState == null || !states.Contains(controller.InitialState)) throw new InvalidDataException("Hiányzó kezdőállapot.");
                foreach (var transition in controller.States.SelectMany(s => s.Transitions)) {
                    if (transition == null || transition.Target == null || !states.Contains(transition.Target)
                        || transition.Conditions == null || transition.Conditions.Count is < 1 or > 16)
                        throw new InvalidDataException("Hiányzó célállapot vagy átmeneti feltétel.");
                    foreach (var condition in transition.Conditions)
                        if (condition == null || !Enum.IsDefined(condition.Trigger) || !double.IsFinite(condition.Value) || condition.Value < 0
                            || condition.Trigger is BehaviorTrigger.MachineState or BehaviorTrigger.EnteredMachineState && (condition.Value > MaximumState(controller.Kind) || condition.Value != Math.Truncate(condition.Value)))
                            throw new InvalidDataException("Érvénytelen átmeneti feltétel.");
                }
            }
        }
        private BehaviorController Binding(string kind, ulong id) => bindings.TryGetValue((kind, id), out var specific) ? specific
            : bindings.GetValueOrDefault((kind, null));
        public BehaviorAction Step(string kind, ulong id, in ControllerInputs input)
        {
            var controller = Binding(kind, id);
            if (controller == null) return BehaviorAction.Autonomous;
            if (!double.IsFinite(input.Delta) || input.Delta < 0 || !double.IsFinite(input.Cargo) || input.Cargo < 0 || input.MachineState < 0 || input.MachineState > MaximumState(kind))
                throw new ArgumentOutOfRangeException(nameof(input));
            var key = (kind, id);
            if (!instances.TryGetValue(key, out var runtime))
                runtime = new(controller.Id, kind, id, controller.InitialState, 0, false, 0, false, false);
            double elapsed = runtime.Elapsed + input.Delta;
            if (!double.IsFinite(elapsed)) throw new ArithmeticException("A vezérlési idő túlcsordult.");
            var state = controller.States.Find(s => s.Id == runtime.State);
            foreach (var transition in state.Transitions) {
                bool matches = true;
                foreach (var condition in transition.Conditions)
                    if (!Matches(condition, input, runtime, elapsed)) { matches = false; break; }
                if (!matches) continue;
                state = controller.States.Find(s => s.Id == transition.Target); elapsed = 0; break;
            }
            instances[key] = runtime with { State = state.Id, Elapsed = elapsed, Initialized = true,
                PreviousMachineState = input.MachineState, PreviousWorking = input.Working, PreviousBroken = input.Broken };
            return state.Action;
        }
        private static bool Matches(BehaviorCondition condition, in ControllerInputs input, BehaviorControllerSnapshot prior, double elapsed) => condition.Trigger switch {
            BehaviorTrigger.Always => true,
            BehaviorTrigger.Elapsed => elapsed + 1e-9 >= condition.Value,
            BehaviorTrigger.CargoAtLeast => input.Cargo >= condition.Value,
            BehaviorTrigger.CargoBelow => input.Cargo < condition.Value,
            BehaviorTrigger.MachineState => input.MachineState == condition.Value,
            BehaviorTrigger.EnteredMachineState => input.MachineState == condition.Value && (!prior.Initialized || prior.PreviousMachineState != input.MachineState),
            BehaviorTrigger.WorkAssigned => input.Working && (!prior.Initialized || !prior.PreviousWorking),
            BehaviorTrigger.WorkFinished => prior.Initialized && prior.PreviousWorking && !input.Working,
            BehaviorTrigger.Broken => input.Broken,
            BehaviorTrigger.Repaired => prior.Initialized && prior.PreviousBroken && !input.Broken,
            _ => false
        };
        public BehaviorControllerSnapshot[] Capture() => instances.Values.OrderBy(s => s.Kind, StringComparer.Ordinal).ThenBy(s => s.ObjectId).ToArray();
        public static int MaximumState(string kind) => kind == "truck" ? 8 : 4;
        public void PreserveUnchanged(CompiledBehaviorControllers previous)
        {
            static string Signature(BehaviorController controller) => System.Text.Json.JsonSerializer.Serialize(new {
                controller.Id, controller.InitialState,
                States = controller.States.Select(s => new { s.Id, s.Action, s.Transitions })
            });
            Restore(previous.Capture().Where(s => {
                var current = Binding(s.Kind, s.ObjectId);
                return current != null && Signature(current) == Signature(previous.Binding(s.Kind, s.ObjectId));
            }));
        }
        public void Restore(IEnumerable<BehaviorControllerSnapshot> snapshots)
        {
            ArgumentNullException.ThrowIfNull(snapshots);
            var restored = new Dictionary<(string, ulong), BehaviorControllerSnapshot>();
            foreach (var snapshot in snapshots) {
                if (snapshot == null || snapshot.Kind == null) throw new InvalidDataException("Hiányzó vezérlési állapot.");
                var binding = Binding(snapshot.Kind, snapshot.ObjectId);
                if (binding == null || binding.Id != snapshot.Controller || !binding.States.Any(s => s.Id == snapshot.State)
                    || !double.IsFinite(snapshot.Elapsed) || snapshot.Elapsed < 0 || snapshot.PreviousMachineState < 0 || snapshot.PreviousMachineState > MaximumState(snapshot.Kind)
                    || !restored.TryAdd((snapshot.Kind, snapshot.ObjectId), snapshot))
                    throw new InvalidDataException("A mentett vezérlési állapot nem illeszkedik a gráfhoz.");
            }
            instances = restored;
        }
    }
}
