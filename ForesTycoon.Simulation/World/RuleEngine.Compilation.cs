using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ForesTycoon;

internal sealed partial class RuleEngine
{
    // ── Starter rules: what a new project begins with ────────────────────
    // They were C# once; now they are ordinary rules of the project, edited, switched off or deleted like any other.
    // A starter only seeds a project (and older projects that have not seen it yet); the game itself owns no rule.
    internal static List<WorldRule> StarterRules() => RuleProject.CreateDefault().Rules;

    /// <summary>
    /// Starter rules the project has not seen yet are added (a new project gets them all); one the author deleted is
    /// listed in <paramref name="removed"/> and stays deleted. A rule made from a starter counts as having seen it.
    /// </summary>
    internal static void AddMissingStarters(List<WorldRule> rules, ICollection<string> removed)
    {
        foreach (var rule in StarterRules())
            if (!rules.Exists(r => r.Id == rule.Id || r.Template == rule.Id) && removed?.Contains(rule.Id) != true) rules.Add(rule);
    }

    // ── Compilation ──────────────────────────────────────────────────────
    private sealed class Expression
    {
        private readonly Func<BehaviorInputs, double[], double> evaluate;
        private readonly string hook, kind;
        internal string[] NamedKeys { get; }
        internal Expression(CompiledBehaviorModel model, string hook, string kind, bool prepare = true) { NamedKeys = model.NamedKeys; if (prepare) model.Prepare(); evaluate = model.Evaluator(hook, kind); this.hook = hook; this.kind = kind; }
        internal double Evaluate(double years, double time, double[] inputs, out string error)
        {
            error = null;
            try { return evaluate(new BehaviorInputs(0, years, time, 0, 0), inputs); }
            catch (ArithmeticException e) { error = $"{hook} / {kind} #0: {e.Message} A hibás szabályrész kimaradt; javítsd a kijelzett gráfot."; return 0; }
        }
    }
    private sealed record CompiledEffect(RuleEffect Source, Expression Value, int Field, RuleScopeAction Action, Expression[] Parts = null, CompiledFlow Flow = null);
    /// <summary>
    /// A flow: what <see cref="From"/> loses <see cref="To"/> gains (−1: outside the model). Into a collection it is shared among
    /// the members' <see cref="SpreadField"/>, evenly or by how much lower their <see cref="SpreadLevel"/> is than <see cref="OwnLevel"/>.
    /// </summary>
    private sealed record CompiledFlow(string Name, int From, int To, RuleScopeCollection Spread, RuleScope SpreadScope, int SpreadField,
        bool Downhill, int SpreadLevel, int OwnLevel)
    {
        /// <summary>The flow's line in the step's ledger (<see cref="RuleFlowLedger"/>); flows of the same name share it.</summary>
        public int Slot { get; init; }
    }
    /// <summary><paramref name="Used"/>: the inputs the rule mentions; only these are read per element (a tree rule touches thousands).</summary>
    private sealed record CompiledRule(WorldRule Source, RuleScope Scope, string[] Inputs, bool[] Used, Expression Condition, CompiledEffect[] Effects)
    {
        public RuleFieldDefinition[] CustomDefinitions { get; init; } = Array.Empty<RuleFieldDefinition>();
        /// <summary>The scope fields the rule reads, by index: a scope has hundreds (summaries), a rule reads a few.</summary>
        public int[] Reads { get; } = Enumerable.Range(0, Scope.Fields.Length).Where(i => Used[i]).ToArray();
        /// <summary>The readers of <see cref="Reads"/>, in the same order.</summary>
        public Func<SimulationWorld, int, double>[] Readers { get; } = Enumerable.Range(0, Scope.Fields.Length).Where(i => Used[i]).Select(i => Scope.Fields[i].Read).ToArray();
    }

    /// <summary>The names a rule's formulas may use on a scope: fields, custom fields, step, time, world values, stocks, tuning.</summary>
    internal static IReadOnlyList<FormulaName> Names(string scope, IEnumerable<RuleFieldDefinition> fields, IEnumerable<string> stocks)
        => Names(Scope(scope) ?? throw new InvalidDataException("Ismeretlen hatókör: " + scope), fields, stocks);
    private static IReadOnlyList<FormulaName> Names(RuleScope s, IEnumerable<RuleFieldDefinition> fields, IEnumerable<string> stocks)
    {
        var inputs = InputNames(s, fields);
        var names = RuleFormula.Names(Hook("rule", s, inputs), stocks).Where(n => n.Operation is not (BehaviorOperation.Native or BehaviorOperation.State or BehaviorOperation.Amount)).ToList();
        names.RemoveAll(n => n.Operation == BehaviorOperation.Delta || n.Operation == BehaviorOperation.Time);
        names.Insert(0, new("lépés", BehaviorOperation.Delta, Help: "Az eltelt idő a választott időzítés egységében; állati lépésnél másodperc, erdőlépésnél erdőév."));
        names.Insert(1, new("idő", BehaviorOperation.Time, Help: "Eltelt idő a játék kezdete óta (erdőév)."));
        names.Insert(0, new("deltaYears", BehaviorOperation.Delta, Help: "Elapsed forest years in this rule step."));
        for (int i = 0; i < names.Count; i++)
            if (names[i].Operation == BehaviorOperation.Input)
                names[i] = names[i] with { Help = s.Fields.FirstOrDefault(f => f.Name == names[i].Name)?.Help
                    ?? fields.FirstOrDefault(f => f.Name == names[i].Name)?.Description ?? "" };
        // Every field of the world and every stock by its code name, as the editor's pickers offer them.
        var world = SimulationWorld.RuleObjects.Find("world");
        foreach (string key in SimulationWorld.RuleWorldFields)
            names.Add(new(key, BehaviorOperation.NamedValue, key, world?.Fields.Concat(world.Summaries).FirstOrDefault(f => "world." + f.Name == key)?.Help ?? ""));
        foreach (string id in stocks ?? Enumerable.Empty<string>()) names.Add(new("stock." + id, BehaviorOperation.NamedValue, "stock." + id, "Saját készlet."));
        foreach (var spec in GameTuning.Specs) names.Add(new("tuning." + spec.Id, BehaviorOperation.NamedValue, "tuning." + spec.Id, $"{spec.Name} ({spec.Unit})."));
        return names;
    }

    private static string[] InputNames(RuleScope scope, IEnumerable<RuleFieldDefinition> fields) =>
        scope.Fields.Select(f => f.Name).Concat(fields.Where(f => f.Scope == scope.Id).Select(f => f.Name)).ToArray();

    // The world-kind hook strips per-element inputs, so rules use their own kind per scope.
    private static BehaviorHook Hook(string id, RuleScope scope, string[] inputs) =>
        new(id, "Szabály", "rule." + scope.Id, "", -1e12, 1e12, "Nem használt", "Nem használt", "Szabálykönyv", false, inputs);

    internal static IReadOnlySet<string> NamedKeys(IEnumerable<string> stockKeys) =>
        new HashSet<string>(SimulationWorld.RuleWorldFields.Concat(stockKeys).Concat(GameTuning.Specs.Select(s => "tuning." + s.Id)), StringComparer.Ordinal);

    internal RuleEngine(List<WorldRule> rules, List<RuleFieldDefinition> fields, IEnumerable<string> stockIds, Func<string, double> readValue, bool prepareExecutables = true, IEnumerable<RuleScope> scopes = null)
    {
        objectScopes = (scopes ?? Scopes).ToDictionary(s => s.Id, StringComparer.Ordinal);
        namedInputs = new(readValue);
        Rules = rules; Fields = fields;
        if (rules.Count > 256 || fields.Count > 64) throw new InvalidDataException("Legfeljebb 256 szabály és 64 saját mező lehet.");
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in fields) {
            f.Validate();
            if (FindScope(f.Scope) == null) throw new InvalidDataException($"{f.Name}: ismeretlen hatókör.");
            if (!fieldNames.Add(f.Name) || FindScope(f.Scope).Fields.Any(x => x.Name == f.Name)) throw new InvalidDataException($"{f.Name}: ez a név már foglalt.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var stocks = stockIds.ToArray();
        var named = NamedKeys(stocks.Select(s => "stock." + s));
        var list = new List<CompiledRule>();
        int counter = 0;
        foreach (var rule in rules) {
            rule.Validate();
            if (!ids.Add(rule.Id)) throw new InvalidDataException($"{rule.Name}: az azonosító ({rule.Id}) már foglalt.");
            var scope = FindScope(rule.Scope) ?? throw new InvalidDataException($"{rule.Name}: ismeretlen hatókör ({rule.Scope}).");
            var inputs = InputNames(scope, fields);
            var names = Names(scope, fields, stocks);
            Expression Compile(string text, string part)
            {
                string id = "rule." + counter++;
                var hook = Hook(id, scope, inputs);
                try {
                    var graph = RuleFormula.Compile(text, new BehaviorGraph { Id = id, Name = rule.Name, Hook = id, Kind = hook.Kind }, names);
                    return new(new CompiledBehaviorModel(new BehaviorModel { Graphs = { graph } }, new[] { hook }, named, ReadNamed, BindNamed), id, hook.Kind, prepareExecutables && part != "kapcsolat");
                } catch (Exception e) when (e is FormatException or InvalidDataException) {
                    throw new InvalidDataException($"{rule.Name} › {part}: {e.Message}", e);
                }
            }
            Expression CompileGraph(BehaviorGraph source) {
                var graph = new BehaviorModel { Graphs = { source } }.Clone().Graphs[0];
                string id = "rule.graph." + counter++; var hook = Hook(id, scope, inputs);
                graph.Id = graph.Hook = id; graph.Kind = hook.Kind; graph.ObjectId = null;
                return new(new CompiledBehaviorModel(new BehaviorModel { Graphs = { graph } }, new[] { hook }, named, ReadNamed, BindNamed), id, hook.Kind, prepareExecutables);
            }
            var condition = rule.ConditionGraph != null ? CompileGraph(rule.ConditionGraph)
                : string.IsNullOrWhiteSpace(rule.Condition) ? null : Compile(rule.Condition, "feltétel");
            var sourceEffects = rule.EffectiveEffects();
            var effects = sourceEffects.Select((e, i) => {
                if (e.Mode == "action") {
                    var action = Array.Find(scope.Actions, a => a.Name == e.Target) ?? throw new InvalidDataException($"{rule.Name}: a(z) {scope.Name} hatókörben nincs ilyen művelet: {e.Target}.");
                    return new CompiledEffect(e, e.Computation != null ? CompileGraph(e.Computation) : string.IsNullOrWhiteSpace(e.Formula) ? null : Compile(e.Formula, $"{i + 1}. hatás"), -1, action);
                }
                if (e.Mode == "flow") {
                    int Writable(string name)
                    {
                        int f = Array.IndexOf(inputs, name);
                        if (f < 0 || f < scope.Fields.Length && scope.Fields[f].Write == null)
                            throw new InvalidDataException($"{rule.Name}: a(z) {name} nem írható készlet ebben a hatókörben.");
                        return f;
                    }
                    int from = e.Target.Length == 0 ? -1 : Writable(e.Target);
                    int dot = e.To.IndexOf('.');
                    var spread = dot > 0 ? Array.Find(scope.Collections, c => c.Name == e.To[..dot]) : null;
                    CompiledFlow flow;
                    if (spread != null) {
                        var into = FindScope(spread.Target);
                        int intoField = Array.FindIndex(into.Fields, f => f.Name == e.To[(dot + 1)..] && f.Write != null);
                        if (intoField < 0) throw new InvalidDataException($"{rule.Name}: a(z) {e.To} nem írható készlet.");
                        bool downhill = e.Spread == "downhill";
                        int level = downhill ? Array.FindIndex(into.Fields, f => f.Name == e.Level) : -1;
                        int own = downhill ? Array.FindIndex(scope.Fields, f => f.Name == e.Level) : -1;
                        if (downhill && (level < 0 || own < 0)) throw new InvalidDataException($"{rule.Name}: a lejtés szerinti áramláshoz adj meg szintet ({e.Level}).");
                        flow = new(e.FlowName, from, -1, spread, into, intoField, downhill, level, own);
                    } else flow = new(e.FlowName, from, e.To.Length == 0 ? -1 : Writable(e.To), null, null, -1, false, -1, -1);
                    flow = flow with { Slot = flowLedger.Register(e.FlowName.Length > 0 ? e.FlowName : "áramlás") };
                    var flowParts = rule.Effects.ElementAtOrDefault(i)?.Influence is { } fi
                        ? fi.Terms.Select(t => Compile(new RuleInfluence { PerYear = fi.PerYear, PerHour = fi.PerHour, Terms = new() { t } }.Compile(), "kapcsolat")).ToArray() : null;
                    return new CompiledEffect(e, e.Computation != null ? CompileGraph(e.Computation) : Compile(e.Formula, $"{i + 1}. áramlás"), from, null, flowParts, flow);
                }
                int field = Array.IndexOf(inputs, e.Target);
                bool writable = field >= 0 && (field >= scope.Fields.Length || scope.Fields[field].Write != null);
                if (!writable) throw new InvalidDataException($"{rule.Name}: a(z) {e.Target} nem írható mező ebben a hatókörben.");
                var influence = rule.Effects.ElementAtOrDefault(i)?.Influence;
                var parts = influence?.Terms.Select(t => Compile(new RuleInfluence { PerYear = influence.PerYear, PerHour = influence.PerHour, Terms = new() { t } }.Compile(), "kapcsolat")).ToArray();
                return new CompiledEffect(e, e.Computation != null ? CompileGraph(e.Computation) : Compile(e.Formula, $"{i + 1}. hatás"), field, null, parts);
            }).ToArray();
            string text = System.Text.Json.JsonSerializer.Serialize(rule.ConditionGraph) + " " + System.Text.Json.JsonSerializer.Serialize(rule.Effects.Select(e => e.Computation)) + " " + rule.Condition + " " + string.Join(" ", sourceEffects.Select(e => e.Target + " " + e.To + " " + e.Level + " " + e.Formula));
            list.Add(new(rule, scope, inputs, inputs.Select(n => text.Contains(n, StringComparison.Ordinal)).ToArray(), condition, effects) {
                CustomDefinitions = inputs.Skip(scope.Fields.Length).Select(n => fields.Find(f => f.Name == n)).ToArray()
            });
        }
        compiled = list.ToArray();
        ruleBuffers = compiled.Select(rule => new RuleWorkBuffers(rule.Inputs.Length, rule.CustomDefinitions.Length)).ToArray();
        flowLedger.Prepare();
    }
}
