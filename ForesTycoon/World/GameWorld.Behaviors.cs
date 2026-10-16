using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon
{
    internal sealed class WorldBehaviorPolicy : IBehaviorPolicy
    {
        internal static readonly BehaviorHook[] Hooks = new BehaviorHook[] {
            new("machine.pace", "Forwarder munkatempó", "forwarder", "munka-s", 0, 10, "Kopás (0–1)", "Rakomány (m³)", "Gép al-lépés"),
            new("machine.pace", "Processzor munkatempó", "processor", "munka-s", 0, 10, "Kopás (0–1)", "Rakomány (m³)", "Gép al-lépés"),
            new("mill.process", "Malom feldolgozása", "mill", "m³", 0, 1e6, "Már feldolgozott mennyiség (m³)", "Készlet (m³)", "Logisztikai lépés"),
            new("trail.trafficability", "Földút járhatósága", "tile", "0–1", 0, 1, "Átnedvesedés (0–1)", "Talaj teherbírása (0–1)", "Jármű haladása burkolatlan csempén; 1 = száraz, jól járható",
                Formula: "# Átázott, gyenge teherbírású talajon a földút nehezen járható.\n1 - átnedvesedés * (1 - talaj.teherbírás)")
        }.Concat(EcologyResponseProgram.Hooks).Concat(TransportHooks()).Concat(GameTuning.Specs.Select(s => new BehaviorHook("tuning." + s.Id, s.Name, "world", s.Unit, s.Min, s.Max,
            "Nem használt", "Nem használt", "Paraméter lekérdezése; járműspecifikáció alkalmazáskor", false))).ToArray();
        private readonly CompiledBehaviorModel model;
        internal CompiledBehaviorControllers Controllers { get; }
        internal EcologyResponseProgram Responses { get; }
        internal WorldBehaviorPolicy(BehaviorModel document, IReadOnlySet<string> keys = null, Func<string, double> read = null)
        {
            document = document.Clone();
            foreach (var hook in EcologyResponseProgram.Hooks)
                if (!document.Graphs.Any(g => g.Hook == hook.Id && g.Kind == hook.Kind)) document.Graphs.Add(DefaultGraph(hook));
            model = new(document, Hooks, keys, read); Controllers = new(document); Responses = new(document);
        }

        internal static BehaviorGraph DefaultGraph(BehaviorHook hook) => hook.Kind == "site" ? EcologyResponseProgram.DefaultGraph(hook) : new() {
            Id = "default." + hook.Kind + "." + hook.Id, Name = hook.Name, Hook = hook.Id, Kind = hook.Kind
        };
        internal static BehaviorModel DefaultDocument() => new() { Graphs = Hooks.Select(DefaultGraph).ToList() };
        internal BehaviorModel Document => model.Document;
        internal string LastError => model.LastError ?? Responses.LastError;
        internal bool HasGraphs => model.HasGraphs;
        public bool Overrides(string hook, string kind) => model.Overrides(hook, kind);
        public double Evaluate(in BehaviorQuery query) => model.Evaluate(query.Hook, query.Kind, query.Id,
            new(query.Native, query.Delta, query.Time, query.State, query.Amount, query.Capacity, query.Available, query.From, query.To, query.Surface), query.Extras);
        private static IEnumerable<BehaviorHook> TransportHooks()
        {
            foreach (string kind in new[] { "forwarder", "processor", "truck" }) {
                yield return new("route.cost", kind + " útvonalköltség", kind, "költség/szakasz", .001, 1e6, "Célcsempe sérültsége (0–1)", "Rakomány (m³)", "Útkeresés; From/To: csempe-ID, Surface: terep=0, aszfalt=1, makadám=2, nyom=3");
                yield return new("route.allow", kind + " útvonalszakasz engedélyezése", kind, "0 tiltás; ≥0,5 engedély", 0, 1, "Célcsempe sérültsége (0–1)", "Rakomány (m³)", "Útkeresés; a hálózati járhatóságot nem írhatja felül");
            }
            foreach (string kind in new[] { "forwarder", "truck" }) {
                yield return new("loading.amount", kind + " felrakott mennyiség", kind, "m³", 0, 1e6, "Rakodási fázis (0 fel, 1 le)", "Rakomány (m³)", "Forwarder: megfogás; teherautó: al-lépés; Capacity és Available: m³");
                yield return new("unloading.amount", kind + " lerakott mennyiség", kind, "m³", 0, 1e6, "Rakodási fázis (0 fel, 1 le)", "Rakomány (m³)", "Forwarder: megfogás; teherautó: al-lépés; készletkorláttal");
                yield return new("loading.depart", kind + " rakodás befejezése", kind, "≥0,5 indulás", 0, 1, "Telítettség (0–1)", "Rakomány (m³)", "Forwarder: lezárt rönkciklus; teherautó: rakodási al-lépés; üresen nem indul");
            }
            yield return new("loading.cycleSeconds", "Forwarder rönkciklus ideje", "forwarder", "munka-s", .1, 3600, "Rakodási fázis (0 fel, 1 le)", "Rakomány (m³)", "Ciklus kezdetén rögzítve és mentve");
            yield return new("unloading.amount", "Processzor lerakott mennyisége", "processor", "m³", 0, 1e6, "Lerakodás (1)", "Rakomány (m³)", "Gép al-lépés; legfeljebb a tényleges rakomány");
            yield return new("loading.gripPhase", "Forwarder megfogási pillanat", "forwarder", "ciklusarány", .01, .98, "Rakodási fázis (0 fel, 1 le)", "Rakomány (m³)", "Ciklus kezdetén rögzítve; az animáció is követi");
            yield return new("loading.releasePhase", "Forwarder elengedési pillanat", "forwarder", "ciklusarány", .02, .99, "Rakodási fázis (0 fel, 1 le)", "Rakomány (m³)", "Legalább 0,01-dal a megfogás után; ciklus kezdetén rögzítve");
        }
    }
    sealed partial class GameWorld
    {
        private WorldBehaviorPolicy behaviorPolicy = new(new BehaviorModel());
        internal BehaviorModel BehaviorDocument => behaviorPolicy.Document;
        internal string BehaviorError => behaviorPolicy.LastError;
        internal BehaviorControllerSnapshot[] BehaviorStates => behaviorPolicy.Controllers.Capture();
        internal IEnumerable<(ulong Id, string Label)> BehaviorTargets(string kind)
        {
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
        internal static string[] RuleObjectKinds => WorldBehaviorPolicy.Hooks.Select(h => h.Kind).Concat(new[] { "wildlife", "depot", "stack" }).Distinct().ToArray();
        internal void QueueBehaviors(BehaviorModel model) => Enqueue(new SetBehaviorsCommand(model.ToJson()));
        void IWorldCommandTarget.ExecuteBehaviors(string json) => ApplyBehaviors(BehaviorModel.FromJson(json));
        private void ApplyBehaviors(BehaviorModel model)
        {
            var replacement = new WorldBehaviorPolicy(model, DynamicsRuntime.Keys(dynamics.Document, RuleWorldFields), ruleValues.ReadRule);
            replacement.Controllers.PreserveUnchanged(behaviorPolicy.Controllers);
            if (SiteSignature(behaviorPolicy.Document) != SiteSignature(replacement.Document)) pendingForestRateRefresh = true;
            behaviorPolicy = replacement;
            BindBehaviors();
            ApplyTuning(Tuning);
        }
        private void BindBehaviors()
        {
            IBehaviorPolicy active = behaviorPolicy;
            Environment.Behaviors = active;
            Environment.ResponseRules = behaviorPolicy.Responses;
            forest.ResponseRules = behaviorPolicy.Responses;
            forest.MonthlyRules = TreeMonthWork;
            forest.GrowthRules = TreeGrowthWork;
            Logistics.Behaviors = active;
            Logistics.Controllers = behaviorPolicy.Controllers.HasControllers ? behaviorPolicy.Controllers : null;
        }
    }
}
