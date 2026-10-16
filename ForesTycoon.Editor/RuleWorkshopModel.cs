#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor;

internal enum WorkshopPhase { Understand, Edit, Trial, Apply }
internal sealed record WorkshopTopic(string Name, string[] Modules);

/// <summary>One thematic identity per rule. Other entry points navigate to that identity.</summary>
internal static class RuleWorkshopModel
{
    // The shared system vocabulary owns the taxonomy; the rule map, the workshop and agents use the same subsystems.
    internal static readonly WorkshopTopic[] Topics = SystemVocabulary.Default.Subsystems.Select(s => new WorkshopTopic(s.Name, s.Modules)).ToArray();
    internal static int Topic(GameRuleDefinition rule) => Math.Max(0, Array.FindIndex(Topics, t => t.Modules.Contains(rule.Module)));
    internal static GameRuleDefinition[] Search(GameRuleIndex index, int topic, string query) => index.Search("", query.Trim())
        .Where(r => query.Trim().Length > 0 || Topic(r) == topic).ToArray();
    internal static string PublishedPath(string file) {
        string full = Path.GetFullPath(file);
        return full.EndsWith(".draft.json", StringComparison.OrdinalIgnoreCase) ? full[..^11] : full;
    }
    internal static string DraftPath(string published) => PublishedPath(published) + ".draft.json";
    /// <summary>The catalog rule whose result a formula hook replaces or whose parameter it computes.</summary>
    internal static bool HookBelongsTo(BehaviorHook h, GameRuleDefinition rule) => h.Id == rule.Id
        || h.Id.StartsWith("tuning.") && GameTuning.Spec(h.Id[7..])?.Rule == rule.Id
        || h.Id.StartsWith("route.") && rule.Id is "route.find" or "route.policy"
        || (h.Id.StartsWith("loading.") || h.Id == "unloading.amount") && (rule.Id == "loading.policy" || rule.Id == "machine.logTransfer" && h.Kind == "forwarder" || rule.Id == "vehicle.transfer" && h.Kind == "truck")
        || h.Id == "machine.pace" && rule.Id == (h.Kind == "forwarder" ? "machine.forwarder" : "machine.processor");

    /// <summary>
    /// The game's system model with this project's own stocks, flows and the formulas that read model quantities, so a
    /// feedback loop authored in a project shows up next to the native ones. Shared by the editor and the agent CLI.
    /// </summary>
    internal static SystemModelAnalysis SystemModel(GameRuleCatalog catalog, DynamicsModel dynamics, IEnumerable<BehaviorGraph> graphs, Func<string, int> topicOf,
        IReadOnlyDictionary<string, QuantityPresentation>? quantities = null)
    {
        var vocabulary = SystemVocabulary.Default.With(quantities!);
        string Subsystem(int topic) => vocabulary.Subsystems[Math.Clamp(topic, 0, vocabulary.Subsystems.Count - 1)].Id;
        // Formula keys name either a project stock or the world quantity they observe.
        string? Quantity(string key) => key.StartsWith("stock.", StringComparison.Ordinal) ? key : vocabulary.Observed(key)?.Id;
        string[] Reads(BehaviorGraph graph) => graph.Nodes.Where(n => n.Operation == BehaviorOperation.NamedValue).Select(n => Quantity(n.Key)).OfType<string>().Distinct().ToArray();
        var stocks = dynamics.Stocks.Select(s => new SystemVariable { Id = "stock." + s.Id, Name = s.Name, Subsystem = Subsystem(topicOf("stock." + s.Id)),
            Kind = SystemVariableKind.Stock, Unit = s.Unit == "1" ? "" : s.Unit, Observe = "stock." + s.Id, Description = "Saját készlet ebben a projektben.",
            Layer = quantities != null && quantities.TryGetValue("stock." + s.Id, out var o) && vocabulary.LayerIndex(o.Layer) >= 0 ? o.Layer : "management" }).ToList();
        var processes = new List<SystemProcess>();
        foreach (var flow in dynamics.Flows) {
            var writes = new[] { flow.From, flow.To }.Where(e => e.Length > 0).Select(e => "stock." + e).ToArray();
            if (writes.Length > 0) processes.Add(new("flow." + flow.Id, "Áramlás · " + flow.Name, Subsystem(topicOf("flow." + flow.Id)), Reads(flow.Rate), writes));
        }
        foreach (var graph in graphs) {
            // A formula replaces the result of the processes bound to its hook; it adds its readings to theirs.
            var reads = Reads(graph);
            var hook = WorldBehaviorPolicy.Hooks.FirstOrDefault(h => h.Id == graph.Hook && h.Kind == graph.Kind);
            var rules = hook == null ? Array.Empty<GameRuleDefinition>() : catalog.Rules.Where(r => HookBelongsTo(hook, r)).ToArray();
            if (rules.Length == 0 || reads.Length == 0) continue;
            processes.Add(new("graph." + graph.Id, graph.Name + " (képlet)", Subsystem(Topic(rules[0])), reads, rules.SelectMany(r => r.Writes).Distinct().ToArray(), rules[0].Id));
        }
        return new SystemModelAnalysis(catalog, vocabulary, processes, stocks);
    }

    internal static string KindName(string kind) => kind switch { "tree" => "Fa", "truck" => "Teherautó", "forwarder" => "Forwarder",
        "processor" => "Processzor", "road" => "Út", "tile" => "Csempe", "mill" => "Malom", "world" => "Világ", "wildlife" => "Vad", "depot" => "Telephely", "stack" => "Rakat", _ => kind };
    internal static string FieldName(string field) => SystemVocabulary.Default.Find(field)?.Name ?? SystemVocabulary.Default.Observed(field)?.Name ?? field switch {
        "world.water" => "Tárolt víz", "world.temperature" => "Hőmérséklet", "forest.totalVolume" => "Élő faállomány térfogata",
        "forest.meanHeight" => "Átlagmagasság", "forest.meanDiameter" => "Átlagátmérő", "forest.meanHealth" => "Átlagos faegészség",
        "road.meanCondition" => "Átlagos útállapot", _ => field };
}

/// <summary>Trial and publish results belong to the captured draft, never to whatever is open on completion.</summary>
internal sealed class WorkshopVersions
{
    internal string? Current { get; private set; }
    internal string? Saved { get; private set; }
    internal string? Tested { get; private set; }
    internal string? Published { get; private set; }
    internal string? Running { get; private set; }
    internal bool Unsaved => Current != Saved;
    internal bool TestedCurrent => Tested != null && Tested == Current;
    internal void Observe(string revision) { if (Current == null) Saved = revision; Current = revision; }
    internal void MarkSaved() => Saved = Current;
    internal void CompleteTrial(string capturedRevision) => Tested = capturedRevision;
    internal void MarkPublished(string capturedRevision) => Published = capturedRevision;
    internal void Acknowledge(string? runtimeRevision) => Running = runtimeRevision;
    internal void Reset() { Current = Saved = Tested = Published = Running = null; }
}
