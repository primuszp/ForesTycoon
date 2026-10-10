using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon
{
    internal sealed class WorldBehaviorPolicy : IBehaviorPolicy
    {
        internal static readonly BehaviorHook[] Hooks = new BehaviorHook[] {
            new("road.trafficWear", "Forgalmi útkopás", "road", "állapotveszteség", 0, 1, "Út sérültsége", "Áthaladási terhelés", "Teherautó csempeváltás"),
            new("water.snowmelt", "Hóolvadás", "tile", "mm", 0, 1e9, "Helyi hőmérséklet (°C)", "Hókészlet (mm)", "Környezeti lépés, beszivárgás előtt"),
            new("water.infiltration", "Beszivárgás", "tile", "mm", 0, 1e9, "Gyökérzónavíz (mm)", "Felszíni víz (mm)", "Környezeti lépés"),
            new("forest.growth", "Fanövekedési tényező", "tree", "1", 0, 10, "Egészség (0–1)", "Kor (erdőév)", "Növekedési ráta frissítése"),
            new("forest.health", "Fa egészségének célértéke", "tree", "1", 0, 1, "Egészség (0–1)", "Kor (erdőév)", "Erdőhónap"),
            new("machine.pace", "Forwarder munkatempó", "forwarder", "munka-s", 0, 10, "Kopás (0–1)", "Rakomány (m³)", "Gép al-lépés"),
            new("machine.pace", "Processzor munkatempó", "processor", "munka-s", 0, 10, "Kopás (0–1)", "Rakomány (m³)", "Gép al-lépés"),
            new("mill.process", "Malom feldolgozása", "mill", "m³", 0, 1e6, "Már feldolgozott mennyiség (m³)", "Készlet (m³)", "Logisztikai lépés")
        }.Concat(TransportHooks()).Concat(GameTuning.Specs.Select(s => new BehaviorHook("tuning." + s.Id, s.Name, "world", s.Unit, s.Min, s.Max,
            "Nem használt", "Nem használt", "Paraméter lekérdezése; járműspecifikáció alkalmazáskor", false))).ToArray();
        private readonly CompiledBehaviorModel model;
        internal CompiledBehaviorControllers Controllers { get; }
        internal WorldBehaviorPolicy(BehaviorModel document) { model = new(document, Hooks); Controllers = new(document); }
        internal BehaviorModel Document => model.Document;
        internal string LastError => model.LastError;
        internal bool HasGraphs => model.HasGraphs;
        public double Evaluate(in BehaviorQuery query) => model.Evaluate(query.Hook, query.Kind, query.Id,
            new(query.Native, query.Delta, query.Time, query.State, query.Amount, query.Capacity, query.Available, query.From, query.To, query.Surface));
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
            if (kind == "tree") {
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
            } else {
                for (int i = 0; i < map.Tiles.Count; i++)
                    if (kind == "tile" || kind == "road" && map.IsRoadTile(i)) yield return ((ulong)i, $"{kind} #{i}");
            }
        }
        internal void QueueBehaviors(BehaviorModel model) => Enqueue(new SetBehaviorsCommand(model.ToJson()));
        void IWorldCommandTarget.ExecuteBehaviors(string json) => ApplyBehaviors(BehaviorModel.FromJson(json));
        private void ApplyBehaviors(BehaviorModel model)
        {
            static string ForestRules(BehaviorModel value) => new BehaviorModel { Graphs = value.Graphs.Where(g => g.Kind == "tree").ToList() }.ToJson();
            bool refreshForest = ForestRules(BehaviorDocument) != ForestRules(model);
            var replacement = new WorldBehaviorPolicy(model);
            replacement.Controllers.PreserveUnchanged(behaviorPolicy.Controllers);
            behaviorPolicy = replacement;
            BindBehaviors();
            ApplyTuning(Tuning);
            if (refreshForest) forest.RefreshEnvironmentRates();
        }
        private void BindBehaviors()
        {
            IBehaviorPolicy active = behaviorPolicy.HasGraphs ? behaviorPolicy : null;
            Environment.Behaviors = active;
            forest.Behaviors = active;
            Logistics.Behaviors = active;
            Logistics.Controllers = behaviorPolicy.Controllers.HasControllers ? behaviorPolicy.Controllers : null;
        }
    }
}
