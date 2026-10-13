using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ForesTycoon;

/// <summary>A field of a scope: read for every element, writable when <see cref="Write"/> is set.</summary>
internal sealed record RuleScopeField(string Name, string Help, Func<GameWorld, int, double> Read, Action<GameWorld, int, double> Write = null);
/// <summary>A named action of a scope; returns true when it changed the world's structure (routes must be refreshed).</summary>
internal sealed record RuleScopeAction(string Name, string Help, Func<GameWorld, int, bool> Apply);
/// <summary>An entity set a rule runs on: its elements, fields and actions. Custom fields are kept per element ID.</summary>
internal sealed record RuleScope(string Id, string Name, string Element, Func<GameWorld, int[]> Elements, Func<GameWorld, int> Size,
    RuleScopeField[] Fields, RuleScopeAction[] Actions)
{
    /// <summary>The discovered object kind (<see cref="GameWorld.RuleObjects"/>) behind the scope; null for hand-written scopes.</summary>
    public string Entity { get; init; }
}

internal sealed record RuleBookCheckpoint(List<WorldRule> Rules, List<RuleFieldDefinition> Fields, Dictionary<string, double[]> Values);

/// <summary>
/// Runs the rule book: every enabled rule, in order, on every element of its scope. Per element the condition is
/// evaluated, then the effects in order; a later effect sees the earlier writes of the same element. Expressions are the
/// editor's formula language, compiled to the bounded behavior graphs (no IO, no C#). Deterministic: fixed element order.
/// </summary>
internal sealed class RuleEngine
{
    // ── Scopes: the building blocks a rule can address ──────────────────
    // Declared before the scopes that read it (static initialization runs in text order).
    private static readonly (string Name, string Help)[] TileFieldNames = {
        ("gyökérzónavíz", "Gyökérzóna vize (mm)."), ("felszíni_víz", "Felszíni víz (mm)."), ("mélyvíz", "Mélyvíz (mm)."),
        ("többletvíz", "Szabadföldi vízkapacitás feletti víz (mm)."), ("vízellátottság", "Felvehető víz aránya (0–1)."), ("pangóvíz", "Pangóvíz aránya (0–1)."),
        ("talaj.beszivárgás", "Beszivárgás (mm/óra)."), ("talaj.telítettség", "Teljes víztartás (mm)."), ("talaj.vízkapacitás", "Szabadföldi vízkapacitás (mm)."),
        ("talaj.hervadáspont", "Hervadáspont (mm)."), ("talaj.drénezés", "Drénezés (mm/óra)."), ("talaj.teherbírás", "Teherbírás (0–1)."),
        ("lombborítás", "Lombborítás (0–1)."), ("burkolt", "1 út vagy épület, különben 0."), ("átnedvesedés", "Átnedvesedés (0–1).") };
    internal static readonly RuleScope[] Scopes = new RuleScope[] {
        new("world", "Világ", "a világ", _ => new[] { 0 }, _ => 1, Array.Empty<RuleScopeField>(), Array.Empty<RuleScopeAction>()),
        new("tile", "Minden csempe", "csempe", w => Enumerable.Range(0, w.Map.Tiles.Count).ToArray(), w => w.Map.Tiles.Count,
            TileFieldNames.Select(n => new RuleScopeField(n.Name, n.Help, (w, id) => w.TileRuleInputs(id).TryGetValue(n.Name, out double v) ? v : 0)).ToArray(),
            Array.Empty<RuleScopeAction>()),
        new("trail", "Minden közelítőnyom", "nyom", w => w.Map.SkidTrailTiles.ToArray(), w => w.Map.Tiles.Count,
            new RuleScopeField[] {
                new("nyom.kopás", "Nyomvályú mélysége (0 friss … 1 mély, sáros).", (w, id) => w.Map.GetSkidTrailWear(id),
                    (w, id, v) => w.Map.SetSkidTrailState(id, (float)v, w.Map.GetSkidTrailIdle(id))),
                new("nyom.tétlenség", "Erdőévek az utolsó áthaladás óta.", (w, id) => w.Map.GetSkidTrailIdle(id),
                    (w, id, v) => w.Map.SetSkidTrailState(id, w.Map.GetSkidTrailWear(id), (float)v)) },
            new RuleScopeAction[] { new("nyom.megszüntetése", "A nyom benő és eltűnik a térképről.", (w, id) => w.Map.RemoveSkidTrail(id)) }),
    }.Concat(GameWorld.DiscoveredScopes()).ToArray();
    internal static RuleScope Scope(string id) => Array.Find(Scopes, s => s.Id == id);


    // ── Built-in rules, moved out of C# one by one ───────────────────────
    internal static List<WorldRule> BuiltIn() => new() {
        new() { Id = "trail.age", BuiltIn = true, Name = "Nyom regenerációja és benövése", Scope = "trail", Schedule = "step",
            Description = "A nyomvályúk erdőévenként 0,4-del simulnak. A használaton kívüli, sima nyom a benövési idő után eltűnik.",
            Effects = {
                new() { Target = "nyom.kopás", Mode = "set", Formula = "max(0; nyom.kopás - 0,4 * lépés)" },
                new() { Target = "nyom.tétlenség", Mode = "add", Formula = "lépés" },
                new() { Target = "nyom.megszüntetése", Mode = "action",
                    Formula = "ha(nyom.tétlenség < hangolás.SkidTrailOvergrowYears; 0; ha(nyom.kopás > 0,001; 0; 1))" } } },
        new() { Id = "forest.stumps", BuiltIn = true, Name = "Tönkök lebomlása", Scope = "stump", Schedule = "step",
            Description = "A tönk hat erdőév alatt elkorhad és eltűnik. Korábban C# kód volt (havi ellenőrzés).",
            Effects = { new() { Target = "remove", Mode = "action", Formula = "curve.step(age; 6; 0; 1)" } } },
        // A sample of the object model: a tree rule reading its own tile through a relation. Off by default, so the
        // shipped game behaves as before; switch it on in the editor to see dry ground weaken the trees on it.
        new() { Id = "tree.drought", BuiltIn = true, Enabled = false, Name = "Aszálykár a fákon", Scope = "tree", Schedule = "step",
            Description = "Kiszáradt talajon a fa egészsége romlik; az árnyéktűrő fajok jobban bírják. A havi növekedés ezt a csökkent egészséget látja.",
            Effects = {
                new() { Target = "health", Mode = "add",
                    Formula = "curve.linear(tile.waterSupply; 0; -0,3; 0,2; 0) * (1 - 0,5 * shadeTolerance) * lépés" } } },
    };

    /// <summary>Built-in rules a project does not list yet are added at their place in the built-in order.</summary>
    internal static void AddMissingBuiltIns(List<WorldRule> rules)
    {
        foreach (var rule in BuiltIn())
            if (!rules.Exists(r => r.Id == rule.Id)) rules.Add(rule);
    }

    internal static bool IsDefault(List<WorldRule> rules, List<RuleFieldDefinition> fields) =>
        fields.Count == 0 && JsonSerializer.Serialize(rules) == JsonSerializer.Serialize(BuiltIn());

    // ── Compilation ──────────────────────────────────────────────────────
    private sealed class Expression
    {
        private readonly CompiledBehaviorModel model;
        private readonly string hook, kind;
        internal Expression(CompiledBehaviorModel model, string hook, string kind) { this.model = model; this.hook = hook; this.kind = kind; }
        internal double Evaluate(double years, double time, double[] inputs, out string error)
        {
            model.ClearError();
            double value = model.Evaluate(hook, kind, 0, new BehaviorInputs(0, years, time, 0, 0), inputs);
            error = model.LastError;
            return value;
        }
    }
    private sealed record CompiledEffect(RuleEffect Source, Expression Value, int Field, RuleScopeAction Action);
    /// <summary><paramref name="Used"/>: the inputs the rule mentions; only these are read per element (a tree rule touches thousands).</summary>
    private sealed record CompiledRule(WorldRule Source, RuleScope Scope, string[] Inputs, bool[] Used, Expression Condition, CompiledEffect[] Effects);

    private readonly CompiledRule[] compiled;
    private readonly Dictionary<string, double[]> values = new(StringComparer.Ordinal);
    internal List<WorldRule> Rules { get; }
    internal List<RuleFieldDefinition> Fields { get; }
    internal string LastError { get; private set; }

    /// <summary>The names a rule's formulas may use on a scope: fields, custom fields, step, time, world values, stocks, tuning.</summary>
    internal static IReadOnlyList<FormulaName> Names(string scope, IEnumerable<RuleFieldDefinition> fields, IEnumerable<string> stocks)
    {
        var s = Scope(scope) ?? throw new InvalidDataException("Ismeretlen hatókör: " + scope);
        var inputs = InputNames(s, fields);
        var names = RuleFormula.Names(Hook("rule", s, inputs), stocks).Where(n => n.Operation is not (BehaviorOperation.Native or BehaviorOperation.State or BehaviorOperation.Amount)).ToList();
        names.RemoveAll(n => n.Operation == BehaviorOperation.Delta || n.Operation == BehaviorOperation.Time);
        names.Insert(0, new("lépés", BehaviorOperation.Delta, Help: "Az eltelt idő ebben a lépésben (erdőév)."));
        names.Insert(1, new("idő", BehaviorOperation.Time, Help: "Eltelt idő a játék kezdete óta (erdőév)."));
        for (int i = 0; i < names.Count; i++)
            if (names[i].Operation == BehaviorOperation.Input)
                names[i] = names[i] with { Help = s.Fields.FirstOrDefault(f => f.Name == names[i].Name)?.Help
                    ?? fields.FirstOrDefault(f => f.Name == names[i].Name)?.Description ?? "" };
        foreach (var spec in GameTuning.Specs) names.Add(new("hangolás." + spec.Id, BehaviorOperation.NamedValue, "tuning." + spec.Id, $"{spec.Name} ({spec.Unit})."));
        return names;
    }

    private static string[] InputNames(RuleScope scope, IEnumerable<RuleFieldDefinition> fields) =>
        scope.Fields.Select(f => f.Name).Concat(fields.Where(f => f.Scope == scope.Id).Select(f => f.Name)).ToArray();

    // The world-kind hook strips per-element inputs, so rules use their own kind per scope.
    private static BehaviorHook Hook(string id, RuleScope scope, string[] inputs) =>
        new(id, "Szabály", "rule." + scope.Id, "", -1e12, 1e12, "Nem használt", "Nem használt", "Szabálykönyv", false, inputs);

    internal static IReadOnlySet<string> NamedKeys(IEnumerable<string> stockKeys) =>
        new HashSet<string>(GameWorld.RuleWorldFields.Concat(stockKeys).Concat(GameTuning.Specs.Select(s => "tuning." + s.Id)), StringComparer.Ordinal);

    internal RuleEngine(List<WorldRule> rules, List<RuleFieldDefinition> fields, IEnumerable<string> stockIds, Func<string, double> readValue)
    {
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
                    return new(new CompiledBehaviorModel(new BehaviorModel { Graphs = { graph } }, new[] { hook }, named, readValue), id, hook.Kind);
                } catch (Exception e) when (e is FormatException or InvalidDataException) {
                    throw new InvalidDataException($"{rule.Name} › {part}: {e.Message}", e);
                }
            }
            var condition = string.IsNullOrWhiteSpace(rule.Condition) ? null : Compile(rule.Condition, "feltétel");
            var effects = rule.Effects.Select((e, i) => {
                if (e.Mode == "action") {
                    var action = Array.Find(scope.Actions, a => a.Name == e.Target) ?? throw new InvalidDataException($"{rule.Name}: a(z) {scope.Name} hatókörben nincs ilyen művelet: {e.Target}.");
                    return new CompiledEffect(e, string.IsNullOrWhiteSpace(e.Formula) ? null : Compile(e.Formula, $"{i + 1}. hatás"), -1, action);
                }
                int field = Array.IndexOf(inputs, e.Target);
                bool writable = field >= 0 && (field >= scope.Fields.Length || scope.Fields[field].Write != null);
                if (!writable) throw new InvalidDataException($"{rule.Name}: a(z) {e.Target} nem írható mező ebben a hatókörben.");
                return new CompiledEffect(e, Compile(e.Formula, $"{i + 1}. hatás"), field, null);
            }).ToArray();
            string text = rule.Condition + " " + string.Join(" ", rule.Effects.Select(e => e.Target + " " + e.Formula));
            list.Add(new(rule, scope, inputs, inputs.Select(n => text.Contains(n, StringComparison.Ordinal)).ToArray(), condition, effects));
        }
        compiled = list.ToArray();
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
        int size = Scope(field.Scope).Size(world);
        if (!values.TryGetValue(field.Name, out var array) || array.Length != size) {
            array = new double[size]; Array.Fill(array, field.Initial); values[field.Name] = array;
        }
        return array;
    }

    internal double ReadCustom(GameWorld world, string name, int id)
    {
        var f = Fields.Find(x => x.Name == name);
        if (f == null) return double.NaN;
        var v = Values(world, f);
        return id >= 0 && id < v.Length ? v[id] : double.NaN;
    }

    /// <summary>One step of <paramref name="years"/>; returns true when an action changed the network (routes need a refresh).</summary>
    internal bool Run(GameWorld world, double years, double time)
    {
        bool changed = false;
        foreach (var rule in compiled) {
            if (!rule.Source.Enabled) continue;
            var scope = rule.Scope;
            var custom = rule.Inputs.Skip(scope.Fields.Length).Select(n => Values(world, Fields.Find(f => f.Name == n))).ToArray();
            var customDefs = rule.Inputs.Skip(scope.Fields.Length).Select(n => Fields.Find(f => f.Name == n)).ToArray();
            var inputs = new double[rule.Inputs.Length];
            foreach (int id in scope.Elements(world)) {
                for (int i = 0; i < scope.Fields.Length; i++) if (rule.Used[i]) inputs[i] = scope.Fields[i].Read(world, id);
                for (int i = 0; i < custom.Length; i++) inputs[scope.Fields.Length + i] = custom[i][id];
                string error = null;
                if (rule.Condition != null && rule.Condition.Evaluate(years, time, inputs, out error) == 0) { if (error != null) LastError = error; continue; }
                if (error != null) { LastError = error; continue; }
                foreach (var effect in rule.Effects) {
                    double value = effect.Value == null ? 1 : effect.Value.Evaluate(years, time, inputs, out error);
                    if (error != null) { LastError = $"{rule.Source.Name}: {error}"; break; }
                    if (effect.Action != null) {
                        if (value == 0) continue;
                        changed |= effect.Action.Apply(world, id);
                        break; // the element may be gone; its later effects do not run
                    }
                    double next = effect.Source.Mode == "add" ? inputs[effect.Field] + value : value;
                    if (effect.Field < scope.Fields.Length) {
                        scope.Fields[effect.Field].Write(world, id, next);
                        inputs[effect.Field] = scope.Fields[effect.Field].Read(world, id); // the world may clamp it
                    } else {
                        int c = effect.Field - scope.Fields.Length;
                        next = Math.Clamp(next, customDefs[c].Min, customDefs[c].Max);
                        custom[c][id] = next; inputs[effect.Field] = next;
                    }
                }
            }
        }
        return changed;
    }

    internal RuleBookCheckpoint Capture() => IsDefault(Rules, Fields) ? null
        : new(Clone(Rules), CloneFields(Fields), values.ToDictionary(p => p.Key, p => (double[])p.Value.Clone(), StringComparer.Ordinal));

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
