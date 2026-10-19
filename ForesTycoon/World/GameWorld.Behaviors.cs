using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon
{
    internal sealed class WorldBehaviorPolicy : IBehaviorPolicy
    {
        internal static readonly BehaviorHook[] Hooks = EcologyResponseProgram.Hooks.Concat(VehicleResponseRules.Hooks).Concat(LogisticsRules.Hooks).Concat(GameplayRules.Hooks).Concat(GameTuning.Specs.Select(s => new BehaviorHook("tuning." + s.Id, s.Name, "world", s.Unit, s.Min, s.Max,
            "Nem használt", "Nem használt", "Paraméter lekérdezése; járműspecifikáció alkalmazáskor", false))).ToArray();
        private readonly CompiledBehaviorModel model;
        internal CompiledBehaviorControllers Controllers { get; }
        internal EcologyResponseProgram Responses { get; }
        internal VehicleRuleProgram VehicleResponses { get; }
        internal GameplayProgram Gameplay { get; }
        internal WorldBehaviorPolicy(BehaviorModel document, IReadOnlySet<string> keys = null, Func<string, double> read = null, bool prepareExecutables = true)
        {
            document = document.Clone();
            foreach (var hook in EcologyResponseProgram.Hooks.Concat(VehicleResponseRules.Hooks).Concat(LogisticsRules.Hooks).Concat(GameplayRules.Hooks))
                if (!document.Graphs.Any(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null)) document.Graphs.Add(DefaultGraph(hook));
            model = new(document, Hooks, keys, read); if (prepareExecutables) model.Prepare();
            Controllers = model.Controllers; Responses = new(document, model, prepareExecutables); VehicleResponses = new(document, model);
            Gameplay = Responses.Gameplay;
        }

        internal static BehaviorGraph DefaultGraph(BehaviorHook hook) => RuleProject.DefaultGraph(hook);
        internal static BehaviorModel DefaultDocument() => RuleProject.CreateDefault().Behaviors;
        internal static BehaviorGraph InactiveGraph(BehaviorHook hook) => new() {
            Id = "inactive." + hook.Kind + "." + hook.Id, Name = hook.Name, Hook = hook.Id, Kind = hook.Kind,
            Output = "inactive", Nodes = new() { new() { Id = "inactive", Name = "Inaktív szabály", Operation = BehaviorOperation.Constant, Value = Math.Clamp(0, hook.Min, hook.Max) } }
        };
        internal BehaviorModel Document => model.Document;
        internal string LastError => model.LastError ?? Responses.LastError ?? VehicleResponses.LastError ?? Gameplay.LastError;
        internal bool HasGraphs => model.HasGraphs;
        public bool Overrides(string hook, string kind) => model.Overrides(hook, kind);
        public double Evaluate(in BehaviorQuery query) => model.Evaluate(query.Hook, query.Kind, query.Id,
            new(query.Native, query.Delta, query.Time, query.State, query.Amount, query.Capacity, query.Available, query.From, query.To, query.Surface), query.Extras);

    }
    sealed partial class GameWorld
    {
        private WorldBehaviorPolicy behaviorPolicy = new(WorldBehaviorPolicy.DefaultDocument());
        internal BehaviorModel BehaviorDocument => behaviorPolicy.Document;
        internal string BehaviorError => behaviorPolicy.LastError;
        internal BehaviorControllerSnapshot[] BehaviorStates => behaviorPolicy.Controllers.Capture();
        internal IEnumerable<(ulong Id, string Label)> BehaviorTargets(string kind)
        {
            if (LogisticsRules.IsKind(kind)) kind = kind[9..].ToLowerInvariant();
            if (RuleObjects.Find(kind) is { Type.IsClass: true } entity && RuleEngine.Scope(kind) is { } scope) {
                foreach (int id in scope.Elements(this)) yield return ((ulong)id, scope.Label?.Invoke(this, id) ?? entity.Name + " #" + id);
                yield break;
            }
            if (kind == "world") { yield return (0, "Teljes játékvilág"); }
            else if (kind == "tree") {
                foreach (var entry in forest.IndividualTrees.Patches)
                    for (int i = 0; i < entry.Value.Count; i++) {
                        var tree = entry.Value.Trees[i]; yield return (tree.Id, $"Fa #{tree.Id} · {tree.Species} · csempe {entry.Key}");
                    }
            } else if (kind is "processor" or "forwarder") {
                foreach (var machine in Logistics.Machines)
                    if ((machine.Kind == ForestMachineKind.Harvester) == (kind == "processor"))
                        yield return ((ulong)machine.Id, $"{kind} #{machine.Id} · csempe {machine.Tile}");
            } else if (kind == "mill") {
                foreach (var mill in Logistics.Mills) yield return ((ulong)mill.TileId, $"Malom · csempe {mill.TileId}");
            } else if (kind == "truck") {
                foreach (var truck in Logistics.Trucks) yield return ((ulong)truck.Id, $"Teherautó #{truck.Id} · {truck.Phase}");
            } else if (kind == "wildlife") {
                foreach (var animal in wildlife.Animals) yield return ((ulong)animal.Id, $"Vad #{animal.Id} · csempe {animal.TileId}");
            } else if (kind == "depot") {
                foreach (var depot in Logistics.Depots) yield return ((ulong)depot.TileId, $"Telephely · csempe {depot.TileId}");
            } else if (kind == "stack") {
                foreach (var stack in Logistics.Stacks) yield return ((ulong)stack.Id, $"Rakat #{stack.Id} · csempe {stack.Tile}");
            } else {
                for (int i = 0; i < map.Tiles.Count; i++)
                    if (kind == "tile" || kind == "road" && map.IsRoadTile(i)) yield return ((ulong)i, $"{kind} #{i}");
            }
        }
        internal static string[] RuleObjectKinds => WorldBehaviorPolicy.Hooks.Where(h => h.SupportsObjects).Select(h => LogisticsRules.IsKind(h.Kind) ? h.Kind[9..].ToLowerInvariant() : h.Kind).Concat(RuleEngine.Scopes.Select(s => s.Id)).Concat(new[] { "wildlife", "depot", "stack" }).Distinct().ToArray();
        internal void QueueBehaviors(BehaviorModel model) => Enqueue(new SetBehaviorsCommand(model.ToJson()));
        void IWorldCommandTarget.ExecuteBehaviors(string json) => SimulationWork.Complete(ApplyBehaviorsWork(json));
        private IEnumerable<int> ApplyBehaviorsWork(string json)
        {
            var keys = DynamicsRuntime.Keys(dynamics.Document, RuleWorldFields);
            var preparing = System.Threading.Tasks.Task.Run(() => new WorldBehaviorPolicy(BehaviorModel.FromJson(json), keys, ruleValues.ReadRule));
            while (!preparing.IsCompleted) yield return 1;
            var replacement = preparing.GetAwaiter().GetResult();
            replacement.Controllers.PreserveUnchanged(behaviorPolicy.Controllers);
            if (ResponseSignature(behaviorPolicy.Document) != ResponseSignature(replacement.Document)) {
                pendingForestRateRefresh = true; Environment.InvalidateVegetation();
            }
            behaviorPolicy = replacement;
            wildlife.InvalidateRules();
            BindBehaviors();
            ApplyTuning(Tuning);
            yield return 1;
        }
        private void BindBehaviors()
        {
            IBehaviorPolicy active = behaviorPolicy;
            Environment.ResponseRules = behaviorPolicy.Responses;
            forest.ResponseRules = behaviorPolicy.Responses;
            forest.MonthlyRules = TreeMonthWork;
            forest.GrowthRules = TreeGrowthWork;
            Logistics.Behaviors = active;
            Logistics.Gameplay = wildlife.Gameplay = map.Gameplay = behaviorPolicy.Gameplay;
            wildlife.RuleStep = WildlifeRuleWork;
            wildlife.PopulationRuleStep = PopulationRuleWork;
            wildlife.BirthRuleStep = ids => CreatedObjectsWork("animal", ids);
            Logistics.Controllers = behaviorPolicy.Controllers.HasControllers ? behaviorPolicy.Controllers : null;
        }
    }
}
