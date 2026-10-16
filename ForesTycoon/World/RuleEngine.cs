using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ForesTycoon;

/// <summary>A field of a scope: read for every element, writable when <see cref="Write"/> is set.</summary>
internal sealed record RuleScopeField(string Name, string Help, Func<GameWorld, int, double> Read, Action<GameWorld, int, double> Write = null)
{
    /// <summary>The range the field keeps; a flow never takes a stock below <see cref="Min"/> or fills it above <see cref="Max"/>.</summary>
    public double Min { get; init; } = double.NegativeInfinity;
    public double Max { get; init; } = double.PositiveInfinity;
    /// <summary>When <see cref="Read"/> skips the view (a direct reader), the reading through the view; tests compare the two.</summary>
    public Func<GameWorld, int, double> ViewRead { get; init; }
    /// <summary>False when an optional relation does not exist for this event's subject.</summary>
    public Func<GameWorld, int, bool> Available { get; init; }
}
/// <summary>A named action of a scope; returns true when it changed the world's structure (routes must be refreshed).</summary>
internal sealed record RuleScopeAction(string Name, string Help, Func<GameWorld, int, bool> Apply);
/// <summary>An entity set a rule runs on: its elements, fields and actions. Custom fields are kept per element ID.</summary>
internal sealed record RuleScope(string Id, string Name, string Element, Func<GameWorld, int[]> Elements, Func<GameWorld, int> Size,
    RuleScopeField[] Fields, RuleScopeAction[] Actions)
{
    /// <summary>The discovered object kind (<see cref="GameWorld.RuleObjects"/>) behind the scope.</summary>
    public string Entity { get; init; }
    public Func<GameWorld, int, int> StateIndex { get; init; } = (_, id) => id;
    public Func<GameWorld, int> StateSize { get; init; }
    public Func<GameWorld, IEnumerable<int>> PrepareElements { get; init; }
    public Func<GameWorld, int[]> PreparedElements { get; init; }
    /// <summary>Collections whose members are elements of a scope, so flows can be shared out among them.</summary>
    public RuleScopeCollection[] Collections { get; init; } = Array.Empty<RuleScopeCollection>();

}
/// <summary>A collection of a scope's element (“neighbours”) whose members are elements of scope <paramref name="Target"/>.</summary>
internal sealed record RuleScopeCollection(string Name, string Target, Func<GameWorld, int, int[]> Members);

internal sealed record RuleBookCheckpoint(List<WorldRule> Rules, List<RuleFieldDefinition> Fields, Dictionary<string, double[]> Values,
    List<string> RemovedStarters = null, Dictionary<string, double> FlowTotals = null);
/// <summary>A rule step under way: its time step, where it stands (rule, element) and the named values it has read so far.</summary>
internal sealed record RuleStepCheckpoint(double Years, double Time, int Rule, int Element, Dictionary<string, double> Named);

/// <summary>
/// Runs the rule book: every enabled rule, in order, on every element of its scope. Per element the condition is
/// evaluated, then the effects in order; a later effect sees the earlier writes of the same element. Expressions are the
/// editor's formula language, compiled to the bounded behavior graphs (no IO, no C#). Deterministic: fixed element order.
/// A step or passage may suspend between bounded work units (<see cref="Begin"/>, <see cref="Continue"/>).
/// The pending logical tick or command retains ownership; other simulation work cannot overtake it.
/// </summary>
internal sealed class RuleEngine
{
    // ── Scopes: the building blocks a rule can address ──────────────────
    // Every concrete kind of the object model (RuleEntities.cs) with its view; no scope is written by hand.
    internal static readonly RuleScope[] Scopes = GameWorld.DiscoveredScopes();
    internal static RuleScope Scope(string id) => Array.Find(Scopes, s => s.Id == id);


    // ── Starter rules: what a new project begins with ────────────────────
    // They were C# once; now they are ordinary rules of the project, edited, switched off or deleted like any other.
    // A starter only seeds a project (and older projects that have not seen it yet); the game itself owns no rule.
    internal static List<WorldRule> StarterRules() => WaterRules.Defaults().Concat(TreeVitalityRules.Defaults()).Concat(new[] { TreeGrowthRule.Default() }).Concat(new List<WorldRule> {
        TrafficWearRule.Default(),
        RoadWeatherRule.Default(),
        TrailAgeRule.Default(),
        StumpDecayRule.Default(),
        // A sample of the object model: a tree rule reading its own tile through a relation. Off by default, so the
        // shipped game behaves as before; switch it on in the editor to see dry ground weaken the trees on it.
        new() { Id = "tree.drought", Enabled = false, Name = "Aszálykár a fákon", Scope = "tree", Schedule = "month",
            Description = "Kiszáradt talajon a fa egészsége romlik; az árnyéktűrő fajok jobban bírják. Opcionális saját hatás az egészséggráf mellett.",
            Effects = { new() { Target = "health", Mode = "add", Influence = new() { Terms = {
                new() { Name = "Száraz talaj gyengíti a fát", Amount = .3, Factors = {
                    new() { Source = "tile.waterSupply", InputHigh = .2, OutputLow = 1, OutputHigh = 0 },
                    new() { Source = "shadeTolerance", OutputLow = 1, OutputHigh = .5 } } }
            } } } } },
    }).Select(r => { r.Template = r.Id; return r; }).ToList();

    /// <summary>
    /// Starter rules the project has not seen yet are added (a new project gets them all); one the author deleted is
    /// listed in <paramref name="removed"/> and stays deleted. A rule made from a starter counts as having seen it.
    /// </summary>
    internal static void AddMissingStarters(List<WorldRule> rules, ICollection<string> removed)
    {
        foreach (var rule in StarterRules())
            if (!rules.Exists(r => r.Id == rule.Id || r.Template == rule.Id) && removed?.Contains(rule.Id) != true) rules.Add(rule);
    }

    internal static bool IsDefault(List<WorldRule> rules, List<RuleFieldDefinition> fields, ICollection<string> removed) =>
        fields.Count == 0 && (removed == null || removed.Count == 0) && JsonSerializer.Serialize(rules) == JsonSerializer.Serialize(StarterRules());

    // ── Compilation ──────────────────────────────────────────────────────
    private sealed class Expression
    {
        private readonly Func<BehaviorInputs, double[], double> evaluate;
        private readonly string hook, kind;
        internal Expression(CompiledBehaviorModel model, string hook, string kind) { evaluate = model.Evaluator(hook, kind); this.hook = hook; this.kind = kind; }
        internal double Evaluate(double years, double time, double[] inputs, out string error)
        {
            error = null;
            try { return evaluate(new BehaviorInputs(0, years, time, 0, 0), inputs); }
            catch (ArithmeticException e) { error = $"{hook} / {kind} #0: {e.Message} A beépített eredmény maradt érvényben."; return 0; }
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
        /// <summary>The flow's line in the step's ledger (<see cref="ledgerNames"/>); flows of the same name share it.</summary>
        public int Slot { get; init; }
    }
    /// <summary><paramref name="Used"/>: the inputs the rule mentions; only these are read per element (a tree rule touches thousands).</summary>
    private sealed record CompiledRule(WorldRule Source, RuleScope Scope, string[] Inputs, bool[] Used, Expression Condition, CompiledEffect[] Effects)
    {
        // A rule engine runs one non-reentrant step. Refill these buffers; never allocate them per growth patch.
        public double[] InputValues { get; } = new double[Inputs.Length];
        public RuleFieldDefinition[] CustomDefinitions { get; init; } = Array.Empty<RuleFieldDefinition>();
        public double[][] CustomValues { get; } = new double[Inputs.Length - Scope.Fields.Length][];
        /// <summary>The scope fields the rule reads, by index: a scope has hundreds (summaries), a rule reads a few.</summary>
        public int[] Reads { get; } = Enumerable.Range(0, Scope.Fields.Length).Where(i => Used[i]).ToArray();
        /// <summary>The readers of <see cref="Reads"/>, in the same order.</summary>
        public Func<GameWorld, int, double>[] Readers { get; } = Enumerable.Range(0, Scope.Fields.Length).Where(i => Used[i]).Select(i => Scope.Fields[i].Read).ToArray();
    }

    private readonly CompiledRule[] compiled;
    private readonly Dictionary<string, double[]> values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> flowTotals = new(StringComparer.Ordinal);
    /// <summary>The flow ledger: what each named flow has moved so far, in its source's unit.</summary>
    internal IReadOnlyDictionary<string, double> FlowTotals => flowTotals;
    internal List<WorldRule> Rules { get; }
    internal List<RuleFieldDefinition> Fields { get; }
    internal string LastError { get; private set; }

    internal sealed record InfluenceContribution(string Name, double Change);
    internal sealed record InfluencePreview(string Target, double Before, double After, InfluenceContribution[] Contributions);
    internal IReadOnlyList<InfluencePreview> PreviewInfluences(GameWorld world, string ruleId, int id, double years)
    {
        var rule = compiled.Single(r => r.Source.Id == ruleId);
        previewing = true;
        try { return PreviewInfluences(world, rule, id, years); }
        finally { previewing = false; }
    }
    private List<InfluencePreview> PreviewInfluences(GameWorld world, CompiledRule rule, int id, double years)
    {
        var inputs = new double[rule.Inputs.Length];
        foreach (int i in rule.Reads) inputs[i] = rule.Scope.Fields[i].Read(world, id);
        for (int i = rule.Scope.Fields.Length; i < inputs.Length; i++) inputs[i] = ReadCustom(world, rule.Inputs[i], id);
        string conditionError = null;
        bool applies = rule.Source.Enabled && (rule.Condition == null || rule.Condition.Evaluate(years, world.ReadRuleValue("world.time"), inputs, out conditionError) != 0);
        if (conditionError != null) throw new InvalidDataException(conditionError);
        var rows = new List<InfluencePreview>();
        for (int e = 0; e < rule.Effects.Length; e++) {
            var effect = rule.Effects[e];
            if (!applies || !EffectAvailable(world, rule.Scope, id, effect)) {
                double unchanged = effect.Field >= 0 ? inputs[effect.Field] : 0;
                rows.Add(new(effect.Field < 0 && effect.Flow != null ? effect.Source.To : effect.Source.Target, unchanged, unchanged,
                    rule.Source.Effects[e].Influence?.Terms.Select(t => new InfluenceContribution(t.Name, 0)).ToArray() ?? Array.Empty<InfluenceContribution>()));
                continue;
            }
            if (effect.Source.Mode == "action") {
                string actionError = null;
                double trigger = effect.Value == null ? 1 : effect.Value.Evaluate(years, world.ReadRuleValue("world.time"), inputs, out actionError);
                if (actionError != null) throw new InvalidDataException(actionError);
                rows.Add(new(effect.Source.Target, 0, rule.Source.Enabled && trigger != 0 ? 1 : 0, Array.Empty<InfluenceContribution>()));
                if (rule.Source.Enabled && trigger != 0) break;
                continue;
            }
            if (effect.Flow != null) {
                // A flow is previewed on its source: what it would hold after giving the amount (an inflow from outside: the amount).
                double amount = Math.Max(0, effect.Value.Evaluate(years, world.ReadRuleValue("world.time"), inputs, out string flowError));
                if (flowError != null) throw new InvalidDataException(flowError);
                var flowParts = new List<InfluenceContribution>();
                for (int p = 0; p < (effect.Parts?.Length ?? 0); p++) {
                    double change = effect.Parts[p].Evaluate(years, world.ReadRuleValue("world.time"), inputs, out flowError);
                    if (flowError != null) throw new InvalidDataException(flowError);
                    flowParts.Add(new(rule.Source.Effects[e].Influence.Terms[p].Name, rule.Source.Enabled ? change : 0));
                }
                if (!rule.Source.Enabled) amount = 0;
                if (effect.Field >= 0) {
                    double held = inputs[effect.Field], min = effect.Field < rule.Scope.Fields.Length ? Math.Max(0, rule.Scope.Fields[effect.Field].Min) : 0;
                    rows.Add(new(effect.Source.Target, held, held - Math.Min(amount, Math.Max(0, held - min)), flowParts.ToArray()));
                } else rows.Add(new(effect.Source.To, 0, amount, flowParts.ToArray()));
                continue;
            }
            double before = inputs[effect.Field];
            double value = effect.Value.Evaluate(years, world.ReadRuleValue("world.time"), inputs, out string error);
            if (error != null) throw new InvalidDataException(error);
            var field = effect.Field < rule.Scope.Fields.Length ? rule.Scope.Fields[effect.Field] : null;
            var customField = field == null ? Fields.Find(f => f.Name == effect.Source.Target) : null;
            double after = Math.Clamp(NextValue(effect.Source, before, value, years), field?.Min ?? customField?.Min ?? -1e12, field?.Max ?? customField?.Max ?? 1e12);
            inputs[effect.Field] = after;
            var parts = new List<InfluenceContribution>();
            // Inputs are the pre-effect snapshot when explaining each contribution.
            inputs[effect.Field] = before;
            for (int p = 0; p < (effect.Parts?.Length ?? 0); p++) {
                double change = effect.Parts[p].Evaluate(years, world.ReadRuleValue("world.time"), inputs, out error);
                if (error != null) throw new InvalidDataException(error);
                parts.Add(new(rule.Source.Effects[e].Influence.Terms[p].Name, rule.Source.Enabled ? change : 0));
            }
            inputs[effect.Field] = after;
            rows.Add(new(effect.Source.Target, before, after, parts.ToArray()));
        }
        return rows;
    }


    /// <summary>The names a rule's formulas may use on a scope: fields, custom fields, step, time, world values, stocks, tuning.</summary>
    internal static IReadOnlyList<FormulaName> Names(string scope, IEnumerable<RuleFieldDefinition> fields, IEnumerable<string> stocks)
    {
        var s = Scope(scope) ?? throw new InvalidDataException("Ismeretlen hatókör: " + scope);
        var inputs = InputNames(s, fields);
        var names = RuleFormula.Names(Hook("rule", s, inputs), stocks).Where(n => n.Operation is not (BehaviorOperation.Native or BehaviorOperation.State or BehaviorOperation.Amount)).ToList();
        names.RemoveAll(n => n.Operation == BehaviorOperation.Delta || n.Operation == BehaviorOperation.Time);
        names.Insert(0, new("lépés", BehaviorOperation.Delta, Help: "Az eltelt idő ebben a lépésben (erdőév)."));
        names.Insert(1, new("idő", BehaviorOperation.Time, Help: "Eltelt idő a játék kezdete óta (erdőév)."));
        names.Insert(0, new("deltaYears", BehaviorOperation.Delta, Help: "Elapsed forest years in this rule step."));
        for (int i = 0; i < names.Count; i++)
            if (names[i].Operation == BehaviorOperation.Input)
                names[i] = names[i] with { Help = s.Fields.FirstOrDefault(f => f.Name == names[i].Name)?.Help
                    ?? fields.FirstOrDefault(f => f.Name == names[i].Name)?.Description ?? "" };
        // Every field of the world and every stock by its code name, as the editor's pickers offer them.
        var world = GameWorld.RuleObjects.Find("world");
        foreach (string key in GameWorld.RuleWorldFields)
            names.Add(new(key, BehaviorOperation.NamedValue, key, world?.Fields.FirstOrDefault(f => "world." + f.Name == key)?.Help ?? ""));
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
        new HashSet<string>(GameWorld.RuleWorldFields.Concat(stockKeys).Concat(GameTuning.Specs.Select(s => "tuning." + s.Id)), StringComparer.Ordinal);

    private readonly Func<string, double> readNamed;
    // A named value as it stood when the step began: the same for every element and rule of the step. Each key the
    // formulas use gets a slot when they are compiled, so an evaluation reads an array instead of looking the name up.
    private readonly Dictionary<string, int> namedSlots = new(StringComparer.Ordinal);
    private double[] namedValues = new double[8]; private int[] namedStamps = new int[8]; private int namedStep = 1;
    private int NamedSlot(string key)
    {
        if (namedSlots.TryGetValue(key, out int slot)) return slot;
        namedSlots[key] = slot = namedSlots.Count;
        if (slot == namedValues.Length) { Array.Resize(ref namedValues, slot * 2); Array.Resize(ref namedStamps, slot * 2); }
        return slot;
    }
    private double ReadNamed(int slot, string key)
    {
        // A preview between the ticks of a step reads the world as it is and leaves the step's values alone.
        if (previewing) return readNamed(key);
        if (namedStamps[slot] != namedStep) { namedValues[slot] = readNamed(key); namedStamps[slot] = namedStep; }
        return namedValues[slot];
    }
    private bool previewing;
    private double ReadNamed(string key) => ReadNamed(NamedSlot(key), key);
    private Func<double> BindNamed(string key) { int slot = NamedSlot(key); return () => ReadNamed(slot, key); }

    // The flow ledger during a step: one line per flow name, added up in a plain array and written back to
    // flowTotals when the step ends. New names are added in the order they first moved, as the dictionary always had them.
    private readonly List<string> ledgerNames = new();
    private double[] ledger = Array.Empty<double>(); private bool[] ledgerKnown = Array.Empty<bool>();
    private bool ledgerDirty;
    private int LedgerSlot(string name)
    {
        int slot = ledgerNames.IndexOf(name);
        if (slot < 0) { slot = ledgerNames.Count; ledgerNames.Add(name); }
        return slot;
    }

    internal RuleEngine(List<WorldRule> rules, List<RuleFieldDefinition> fields, IEnumerable<string> stockIds, Func<string, double> readValue)
    {
        readNamed = readValue;
        Rules = rules; Fields = fields;
        if (rules.Count > 256 || fields.Count > 64) throw new InvalidDataException("Legfeljebb 256 szabály és 64 saját mező lehet.");
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in fields) {
            f.Validate();
            if (Scope(f.Scope) == null) throw new InvalidDataException($"{f.Name}: ismeretlen hatókör.");
            if (!fieldNames.Add(f.Name) || Scope(f.Scope).Fields.Any(x => x.Name == f.Name)) throw new InvalidDataException($"{f.Name}: ez a név már foglalt.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var stocks = stockIds.ToArray();
        var named = NamedKeys(stocks.Select(s => "stock." + s));
        var list = new List<CompiledRule>();
        int counter = 0;
        foreach (var rule in rules) {
            rule.Validate();
            if (!ids.Add(rule.Id)) throw new InvalidDataException($"{rule.Name}: az azonosító ({rule.Id}) már foglalt.");
            var scope = Scope(rule.Scope) ?? throw new InvalidDataException($"{rule.Name}: ismeretlen hatókör ({rule.Scope}).");
            var inputs = InputNames(scope, fields);
            var names = Names(rule.Scope, fields, stocks);
            Expression Compile(string text, string part)
            {
                string id = "rule." + counter++;
                var hook = Hook(id, scope, inputs);
                try {
                    var graph = RuleFormula.Compile(text, new BehaviorGraph { Id = id, Name = rule.Name, Hook = id, Kind = hook.Kind }, names);
                    return new(new CompiledBehaviorModel(new BehaviorModel { Graphs = { graph } }, new[] { hook }, named, ReadNamed, BindNamed), id, hook.Kind);
                } catch (Exception e) when (e is FormatException or InvalidDataException) {
                    throw new InvalidDataException($"{rule.Name} › {part}: {e.Message}", e);
                }
            }
            Expression CompileGraph(BehaviorGraph source) {
                var graph = new BehaviorModel { Graphs = { source } }.Clone().Graphs[0];
                string id = "rule.graph." + counter++; var hook = Hook(id, scope, inputs);
                graph.Id = graph.Hook = id; graph.Kind = hook.Kind; graph.ObjectId = null;
                return new(new CompiledBehaviorModel(new BehaviorModel { Graphs = { graph } }, new[] { hook }, named, ReadNamed, BindNamed), id, hook.Kind);
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
                        var into = Scope(spread.Target);
                        int intoField = Array.FindIndex(into.Fields, f => f.Name == e.To[(dot + 1)..] && f.Write != null);
                        if (intoField < 0) throw new InvalidDataException($"{rule.Name}: a(z) {e.To} nem írható készlet.");
                        bool downhill = e.Spread == "downhill";
                        int level = downhill ? Array.FindIndex(into.Fields, f => f.Name == e.Level) : -1;
                        int own = downhill ? Array.FindIndex(scope.Fields, f => f.Name == e.Level) : -1;
                        if (downhill && (level < 0 || own < 0)) throw new InvalidDataException($"{rule.Name}: a lejtés szerinti áramláshoz adj meg szintet ({e.Level}).");
                        flow = new(e.FlowName, from, -1, spread, into, intoField, downhill, level, own);
                    } else flow = new(e.FlowName, from, e.To.Length == 0 ? -1 : Writable(e.To), null, null, -1, false, -1, -1);
                    flow = flow with { Slot = LedgerSlot(e.FlowName.Length > 0 ? e.FlowName : "áramlás") };
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
        ledger = new double[ledgerNames.Count]; ledgerKnown = new bool[ledgerNames.Count];
    }

    /// <summary>Custom field values survive a rule book change for fields kept with the same name and scope.</summary>
    internal void Preserve(RuleEngine previous)
    {
        if (previous == null) return;
        foreach (var f in Fields)
            if (previous.Fields.Exists(p => p.Name == f.Name && p.Scope == f.Scope) && previous.values.TryGetValue(f.Name, out var v))
                values[f.Name] = (double[])v.Clone();
    }

    private double[] Values(GameWorld world, RuleFieldDefinition field)
    {
        var scope = Scope(field.Scope); int size = (scope.StateSize ?? scope.Size)(world);
        if (!values.TryGetValue(field.Name, out var array) || array.Length != size) {
            var previous = array; array = new double[size]; Array.Fill(array, field.Initial);
            if (previous != null) Array.Copy(previous, array, Math.Min(previous.Length, size)); values[field.Name] = array;
        }
        return array;
    }

    internal double ReadCustom(GameWorld world, string name, int id)
    {
        var f = Fields.Find(x => x.Name == name);
        if (f == null) return double.NaN;
        var scope = Scope(f.Scope);
        if (id < 0 || id >= scope.Size(world)) return double.NaN;
        id = scope.StateIndex(world, id);
        if (previewing) {
            int size = (scope.StateSize ?? scope.Size)(world);
            if (id < 0 || id >= size) return double.NaN;
            return values.TryGetValue(name, out var saved) && id < saved.Length ? saved[id] : f.Initial;
        }
        var v = Values(world, f);
        return id >= 0 && id < v.Length ? v[id] : double.NaN;
    }

    /// <summary>One whole step of <paramref name="years"/>; returns true when an action changed the network (routes need a refresh).</summary>
    internal bool Run(GameWorld world, double years, double time, string schedule = "step")
    {
        ForesTycoon.Engine.SimulationWork.RequireSynchronousContext();
        Begin(world, years, time, schedule);
        bool changed = false;
        while (StepPending) changed |= Continue(world, 256);
        return changed;
    }

    // ── A step spread over world ticks ───────────────────────────────────
    private IEnumerator<int> execution;
    internal bool NetworkChanged { get; private set; }
    private bool stepPending; private double stepYears, stepTime; private int stepRule, stepElement;
    private string stepSchedule = "step"; private int eventElement;
    /// <summary>A step has begun and not all of its work is done yet.</summary>
    internal bool StepPending => stepPending;

    /// <summary>Starts a step of <paramref name="years"/>; <see cref="Continue"/> does the work. The previous step must be finished.</summary>
    internal void Begin(GameWorld world, double years, double time, string schedule = "step", int element = 0)
    {
        if (!double.IsFinite(years) || years < 0 || !double.IsFinite(time) || Array.IndexOf(WorldRule.Schedules, schedule) < 0) throw new ArgumentException("Invalid rule interval or schedule.");
        if (stepPending) throw new InvalidOperationException("Az előző szabálylépés még fut.");
        execution?.Dispose(); execution = null; NetworkChanged = false;
        LastError = null; namedStep++;
        stepSchedule = schedule; eventElement = element;
        stepPending = true; stepYears = years; stepTime = time; stepRule = stepElement = 0;
    }

    /// <summary>
    /// Continues the step with at most <paramref name="budget"/> preparation/element work units, in its own order.
    /// Every scope can suspend. Returns true when an action
    /// changed the network. Nothing happens when no step is pending.
    /// </summary>
    internal bool Continue(GameWorld world, int budget)
    {
        if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        if (!stepPending) return false;
        execution ??= RunRulesWork(world, stepYears, stepTime).GetEnumerator();
        NetworkChanged = false;
        for (int i = 0; i < budget; i++) if (!AdvanceWorkUnit()) { execution.Dispose(); execution = null; break; }
        PublishLedger();
        return NetworkChanged;
    }

    internal IEnumerable<int> ContinueWork(GameWorld world)
    {
        if (!stepPending) yield break;
        execution ??= RunRulesWork(world, stepYears, stepTime).GetEnumerator();
        NetworkChanged = false;
        while (AdvanceWorkUnit()) { PublishLedger(); yield return 1; }
        execution.Dispose(); execution = null;
        PublishLedger();
    }

    private bool AdvanceWorkUnit()
    {
        if (Profile == null) return ForesTycoon.Engine.SimulationWork.MoveNext(execution);
        string id = stepRule < compiled.Length ? compiled[stepRule].Source.Id : null;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        bool more = ForesTycoon.Engine.SimulationWork.MoveNext(execution);
        if (id != null) Profile[id] = Profile.GetValueOrDefault(id) + System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return more;
    }

    private IEnumerable<int> PrepareValuesWork(GameWorld world, RuleFieldDefinition field)
    {
        var scope = Scope(field.Scope); int size = (scope.StateSize ?? scope.Size)(world);
        if (values.TryGetValue(field.Name, out var previous) && previous.Length == size) yield break;
        var array = new double[size];
        for (int first = 0; first < size; first += 1024) {
            int count = Math.Min(1024, size - first);
            Array.Fill(array, field.Initial, first, count);
            if (previous != null && first < previous.Length) Array.Copy(previous, first, array, first, Math.Min(count, previous.Length - first));
            yield return 1;
        }
        values[field.Name] = array;
    }

    private void PublishLedger()
    {
        if (!ledgerDirty) return;
        for (int k = 0; k < ledger.Length; k++) if (ledgerKnown[k]) flowTotals[ledgerNames[k]] = ledger[k];
        ledgerDirty = false;
    }

    private IEnumerable<int> RunRulesWork(GameWorld world, double years, double time)
    {
        for (int k = 0; k < ledger.Length; k++) ledgerKnown[k] = flowTotals.TryGetValue(ledgerNames[k], out ledger[k]);
        for (; stepRule < compiled.Length; stepRule++, stepElement = 0) {
            var rule = compiled[stepRule];
            // The metadata scan is bounded by the 256-rule book limit. A growth patch must not
            // suspend once for every unrelated water/month/passage rule in the same book.
            if (rule.Source.Schedule != stepSchedule) continue;
            yield return 1;
            if (!rule.Source.Enabled) continue;
            var scope = rule.Scope;
            if (stepSchedule is not ("passage" or "growth") && scope.PrepareElements != null) foreach (int work in scope.PrepareElements(world)) yield return work;
            var elements = stepSchedule == "passage" ? new[] { eventElement } : scope.PreparedElements?.Invoke(world) ?? scope.Elements(world);
            var customDefs = rule.CustomDefinitions;
            var custom = rule.CustomValues;
            for (int c = 0; c < custom.Length; c++) {
                foreach (int unit in PrepareValuesWork(world, customDefs[c])) yield return unit;
                custom[c] = values[customDefs[c].Name];
            }
            var inputs = rule.InputValues;
            int[] reads = rule.Reads; var readers = rule.Readers; var effects = rule.Effects;
            for (; stepElement < elements.Length; stepElement++) {
                yield return 1;
                int id = elements[stepElement];
                for (int r = 0; r < reads.Length; r++) inputs[reads[r]] = readers[r](world, id);
                for (int i = 0; i < custom.Length; i++) inputs[scope.Fields.Length + i] = custom[i][scope.StateIndex(world, id)];
                string error = null;
                if (rule.Condition != null && rule.Condition.Evaluate(years, time, inputs, out error) == 0) { if (error != null) LastError = error; continue; }
                if (error != null) { LastError = error; continue; }
                foreach (var effect in effects) {
                    if (!EffectAvailable(world, scope, id, effect)) continue;
                    double value = effect.Value == null ? 1 : effect.Value.Evaluate(years, time, inputs, out error);
                    if (error != null) { LastError = $"{rule.Source.Name}: {error}"; break; }
                    if (effect.Action != null) {
                        if (value == 0) continue;
                        NetworkChanged |= effect.Action.Apply(world, id);
                        break; // the element may be gone; its later effects do not run
                    }
                    if (effect.Flow != null) { Move(world, scope, id, inputs, custom, customDefs, effect.Flow, value); continue; }
                    double next = NextValue(effect.Source, inputs[effect.Field], value, years);
                    if (effect.Field < scope.Fields.Length) {
                        scope.Fields[effect.Field].Write(world, id, next);
                        inputs[effect.Field] = scope.Fields[effect.Field].Read(world, id); // the world may clamp it
                    } else {
                        int c = effect.Field - scope.Fields.Length;
                        next = Math.Clamp(next, customDefs[c].Min, customDefs[c].Max);
                        custom[c][scope.StateIndex(world, id)] = next; inputs[effect.Field] = next;
                    }
                }
            }
        }
        stepPending = false;
    }

    private static double NextValue(RuleEffect effect, double before, double value, double years) => effect.Mode switch {
        "add" => before + value,
        "approach" => before + Math.Clamp(value - before, -effect.ApproachPerYear * years, effect.ApproachPerYear * years),
        _ => value
    };

    private static bool EffectAvailable(GameWorld world, RuleScope scope, int id, CompiledEffect effect)
    {
        bool Has(int field) => field < 0 || field >= scope.Fields.Length || scope.Fields[field].Available?.Invoke(world, id) != false;
        return Has(effect.Field) && (effect.Flow == null || Has(effect.Flow.To));
    }

    /// <summary>The step under way, for a checkpoint; null when none is.</summary>
    internal RuleStepCheckpoint CaptureStep() => !stepPending ? null : new(stepYears, stepTime, stepRule, stepElement,
        namedSlots.OrderBy(p => p.Value).Where(p => namedStamps[p.Value] == namedStep).ToDictionary(p => p.Key, p => namedValues[p.Value], StringComparer.Ordinal));

    /// <summary>Takes up a step saved by <see cref="CaptureStep"/>: it continues where it stood, with the named values it had read.</summary>
    internal void RestoreStep(GameWorld world, RuleStepCheckpoint saved)
    {
        if (saved == null) return;
        CheckpointGuard.Require(double.IsFinite(saved.Years) && saved.Years >= 0 && double.IsFinite(saved.Time) && saved.Rule >= 0
            && saved.Rule <= compiled.Length && saved.Element >= 0 && saved.Named != null && saved.Named.Values.All(double.IsFinite), "rule step");
        stepPending = false;
        Begin(world, saved.Years, saved.Time);
        stepRule = saved.Rule; stepElement = saved.Element;
        foreach (var (key, value) in saved.Named) {
            CheckpointGuard.Require(namedSlots.ContainsKey(key), "rule step value");
            int slot = NamedSlot(key); namedValues[slot] = value; namedStamps[slot] = namedStep;
        }
    }

    /// <summary>When set, the milliseconds each rule took are added up here, by rule ID (benchmarks).</summary>
    internal Dictionary<string, double> Profile { get; set; }

    /// <summary>
    /// Moves up to <paramref name="amount"/> along a flow: never more than the source holds above its minimum, nor more than
    /// the receivers have room for. What leaves one stock arrives in the other, so a closed model conserves its quantity.
    /// </summary>
    private void Move(GameWorld world, RuleScope scope, int id, double[] inputs, double[][] custom, RuleFieldDefinition[] customDefs, CompiledFlow flow, double amount)
    {
        if (!(amount > 0)) return;
        var stock = new Stocks(world, scope, id, inputs, custom, customDefs);
        if (flow.From >= 0) amount = Math.Min(amount, Math.Max(0, stock.Read(flow.From) - Math.Max(0, stock.Min(flow.From))));
        double moved = 0;
        if (flow.Spread != null) {
            var members = flow.Spread.Members(world, id);
            var into = flow.SpreadScope.Fields[flow.SpreadField];
            double self = flow.Downhill ? scope.Fields[flow.OwnLevel].Read(world, id) : 0, total = 0;
            Span<double> weights = members.Length <= 16 ? stackalloc double[members.Length] : new double[members.Length];
            for (int m = 0; m < members.Length; m++) {
                weights[m] = flow.Downhill ? Math.Max(0, self - flow.SpreadScope.Fields[flow.SpreadLevel].Read(world, members[m])) : 1;
                total += weights[m];
            }
            if (total <= 0) return;
            for (int m = 0; m < members.Length; m++) {
                if (weights[m] <= 0) continue;
                double before = into.Read(world, members[m]);
                double share = Math.Min(amount * weights[m] / total, Math.Max(0, into.Max - before));
                into.Write(world, members[m], before + share);
                moved += into.Read(world, members[m]) - before;
            }
        } else if (flow.To >= 0) {
            double before = stock.Read(flow.To);
            stock.Write(flow.To, before + Math.Min(amount, Math.Max(0, stock.Max(flow.To) - before)));
            moved = stock.Read(flow.To) - before;
        } else moved = amount;
        if (moved <= 0) return;
        if (flow.From >= 0) stock.Write(flow.From, stock.Read(flow.From) - moved);
        ledgerKnown[flow.Slot] = true;
        ledger[flow.Slot] += moved;
        ledgerDirty = true;
    }

    /// <summary>The stocks of one element a flow moves between: its scope fields, then its custom fields.</summary>
    private readonly ref struct Stocks
    {
        private readonly GameWorld world; private readonly RuleScope scope; private readonly int id;
        private readonly double[] inputs; private readonly double[][] custom; private readonly RuleFieldDefinition[] customDefs;
        internal Stocks(GameWorld world, RuleScope scope, int id, double[] inputs, double[][] custom, RuleFieldDefinition[] customDefs)
        { this.world = world; this.scope = scope; this.id = id; this.inputs = inputs; this.custom = custom; this.customDefs = customDefs; }
        private int Own => scope.Fields.Length;
        internal double Read(int i) => i < Own ? scope.Fields[i].Read(world, id) : custom[i - Own][scope.StateIndex(world, id)];
        internal double Min(int i) => i < Own ? scope.Fields[i].Min : customDefs[i - Own].Min;
        internal double Max(int i) => i < Own ? scope.Fields[i].Max : customDefs[i - Own].Max;
        internal void Write(int i, double value)
        {
            if (i < Own) { scope.Fields[i].Write(world, id, value); inputs[i] = scope.Fields[i].Read(world, id); return; }
            value = Math.Clamp(value, customDefs[i - Own].Min, customDefs[i - Own].Max);
            custom[i - Own][scope.StateIndex(world, id)] = value; inputs[i] = value;
        }
    }

    /// <summary>Starter rules the author deleted; saved with the rules so they stay deleted.</summary>
    internal List<string> RemovedStarters { get; init; } = new();
    // The starter book is left out of a checkpoint only while its flow ledger is empty: the ledger must survive a save.
    internal RuleBookCheckpoint Capture() => IsDefault(Rules, Fields, RemovedStarters) && flowTotals.Count == 0 ? null
        : new(Clone(Rules), CloneFields(Fields), values.ToDictionary(p => p.Key, p => (double[])p.Value.Clone(), StringComparer.Ordinal),
            RemovedStarters.ToList(), flowTotals.Count == 0 ? null : new(flowTotals, StringComparer.Ordinal));

    internal void RestoreFlows(Dictionary<string, double> saved)
    {
        if (saved == null) return;
        foreach (var (name, total) in saved) { CheckpointGuard.Require(double.IsFinite(total), "flow ledger"); flowTotals[name] = total; }
    }

    internal void Restore(Dictionary<string, double[]> saved)
    {
        if (saved == null) return;
        foreach (var (name, v) in saved) {
            CheckpointGuard.Require(Fields.Exists(f => f.Name == name) && v != null && v.All(double.IsFinite), "rule field values");
            values[name] = (double[])v.Clone();
        }
    }

    internal static List<WorldRule> Clone(List<WorldRule> rules) => JsonSerializer.Deserialize<List<WorldRule>>(JsonSerializer.Serialize(rules));
    internal static List<RuleFieldDefinition> CloneFields(List<RuleFieldDefinition> fields) => JsonSerializer.Deserialize<List<RuleFieldDefinition>>(JsonSerializer.Serialize(fields));
}
