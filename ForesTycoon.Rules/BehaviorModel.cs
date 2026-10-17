using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForesTycoon.Rules
{
    public enum BehaviorOperation { Constant, Native, Delta, Time, State, Amount, Add, Subtract, Multiply, Divide, Minimum, Maximum, Greater, Less, Select, Absolute, Negate,
        Capacity, Available, From, To, Surface, NamedValue,
        // A named input the hook declares (for example a soil parameter of the tile); Key holds its name.
        Input, Exp, Power, Curve }
    public sealed class BehaviorNode
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public BehaviorOperation Operation { get; set; }
        public double Value { get; set; }
        public string Key { get; set; } = "";
        public string A { get; set; } = "";
        public string B { get; set; } = "";
        public string C { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
    }
    public sealed class BehaviorGraph
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "Viselkedés";
        public string Hook { get; set; } = "";
        public string Kind { get; set; } = "";
        // Null binds the type. An object binding replaces, rather than stacks with, the type graph.
        public ulong? ObjectId { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Disabled { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? DisabledValue { get; set; }
        public string Output { get; set; } = "native";
        public List<BehaviorNode> Nodes { get; set; } = new() {
            new() { Id = "native", Name = "Beépített eredmény", Operation = BehaviorOperation.Native, X = 30, Y = 60 }
        };
    }
    public sealed class BehaviorModel
    {
        public string Schema { get; set; } = "forest-behaviors";
        public int Version { get; set; } = 3;
        public List<BehaviorGraph> Graphs { get; set; } = new();
        public List<BehaviorController> Controllers { get; set; } = new();
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter() } };
        public string ToJson() => JsonSerializer.Serialize(this, Options);
        public static BehaviorModel FromJson(string json) => JsonSerializer.Deserialize<BehaviorModel>(json, Options) ?? throw new InvalidDataException("Üres viselkedésmodell.");
        public BehaviorModel Clone() => FromJson(ToJson());
        public static int Inputs(BehaviorOperation op) => op switch {
            BehaviorOperation.Select => 3, BehaviorOperation.Absolute or BehaviorOperation.Negate or BehaviorOperation.Exp or BehaviorOperation.Curve => 1,
            BehaviorOperation.Power => 2,
            >= BehaviorOperation.Add and <= BehaviorOperation.Less => 2, _ => 0
        };
    }
    /// <param name="Inputs">Names of the extra values the game passes with every evaluation, in order.</param>
    /// <param name="Formula">The rule as the game computes it, written as a formula; null when it is not yet expressed.</param>
    public sealed record BehaviorHook(string Id, string Name, string Kind, string Unit, double Min, double Max,
        string State, string Amount, string Schedule, bool SupportsObjects = true, string[] Inputs = null, string Formula = null);
    public readonly record struct BehaviorInputs(double Native, double Delta, double Time, double State, double Amount,
        double Capacity = 0, double Available = 0, double From = 0, double To = 0, double Surface = 0);

    /// <summary>Pure bounded expressions. No IO, clock, random state, or arbitrary C# execution.</summary>
    public sealed class CompiledBehaviorModel
    {
        private readonly BehaviorModel document;
        private readonly Dictionary<(string Hook, string Kind, ulong? Id), Program> programs = new();
        private readonly HashSet<(string Hook, string Kind)> overriding = new();
        /// <summary>True when some graph of the hook computes more than the built-in result.</summary>
        public bool Overrides(string hook, string kind) => overriding.Contains((hook, kind));
        public BehaviorModel Document => document.Clone();
        public bool HasGraphs => document.Graphs.Count > 0;
        public string[] NamedKeys => programs.Values.SelectMany(p => p.NamedKeys).Distinct(StringComparer.Ordinal).ToArray();
        public bool CanSharePreparedEvaluators => programs.Values.All(p => p.Prepared);
        public CompiledBehaviorControllers Controllers { get; }
        /// <summary>Prepare delegates during project loading without reading any world values.</summary>
        public void Prepare() { foreach (var program in programs.Values) program.Prepare(); }
        public string LastError { get; private set; }
        public void ClearError() => LastError = null;
        /// <param name="bindValue">Optional: resolves a named value's key once, to a reader used for every evaluation
        /// instead of <paramref name="readValue"/> (which is still required to validate the names).</param>
        public CompiledBehaviorModel(BehaviorModel source, IEnumerable<BehaviorHook> available,
            IReadOnlySet<string> namedValues = null, Func<string, double> readValue = null, Func<string, Func<double>> bindValue = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            document = source.Clone();
            if (document.Schema != "forest-behaviors" || document.Version is not (1 or 2 or 3) || document.Graphs == null || document.Graphs.Count > 512)
                throw new InvalidDataException("Ismeretlen vagy túl nagy viselkedésmodell.");
            Controllers = new CompiledBehaviorControllers(document);
            var hooks = available.ToDictionary(h => (h.Id, h.Kind));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var graph in document.Graphs)
            {
                if (graph == null || string.IsNullOrWhiteSpace(graph.Id) || !ids.Add(graph.Id) || string.IsNullOrWhiteSpace(graph.Name)
                    || !hooks.TryGetValue((graph.Hook, graph.Kind), out var hook)) throw new InvalidDataException("Hibás gráfazonosító vagy nem támogatott világkötés.");
                if (!hook.SupportsObjects && graph.ObjectId.HasValue) throw new InvalidDataException("Ez a paraméter a teljes világra vonatkozik.");
                if (!programs.TryAdd((graph.Hook, graph.Kind, graph.ObjectId), new Program(graph, hook, namedValues, readValue, bindValue)))
                    throw new InvalidDataException("Egy eseményhez és célhoz csak egy viselkedés köthető.");
                if (graph.Nodes.Find(n => n.Id == graph.Output)?.Operation != BehaviorOperation.Native) overriding.Add((graph.Hook, graph.Kind));
            }
        }
        /// <summary>
        /// The evaluator of one world-wide graph, looked up once: for callers that evaluate it for thousands of elements.
        /// It throws <see cref="ArithmeticException"/> where <see cref="Evaluate"/> records the error; null when no graph is bound.
        /// </summary>
        public Func<BehaviorInputs, double[], double> Evaluator(string hook, string kind) =>
            programs.TryGetValue((hook, kind, null), out var program) ? (input, extras) => program.Evaluate(input, extras) : null;
        public double Evaluate(string hook, string kind, ulong id, in BehaviorInputs inputs, double[] extras = null)
        {
            if (!programs.TryGetValue((hook, kind, id), out var program) && !programs.TryGetValue((hook, kind, null), out program)) return inputs.Native;
            try { return program.Evaluate(inputs, extras); }
            catch (ArithmeticException e) {
                if (LogisticsRules.IsKind(kind)) {
                    double safe = hook == "route.cost" ? 0 : program.ErrorValue;
                    LastError = $"{hook} / {kind} #{id}: {e.Message} Biztonsági fix eredmény: {safe}. Útvonalhibánál a szakasz tiltott.";
                    return safe;
                }
                LastError = $"{hook} / {kind} #{id}: {e.Message} A beépített eredmény maradt érvényben.";
                return inputs.Native;
            }
        }
        private sealed class Program
        {
            internal bool Prepared => disabled || fast != null;
            internal IEnumerable<string> NamedKeys => nodes.Where(n => n.Operation == BehaviorOperation.NamedValue).Select(n => n.Key);
            internal double ErrorValue => Math.Max(0, hook.Min);
            private readonly bool disabled;
            private readonly double disabledValue;
            private readonly BehaviorNode[] nodes;
            private readonly int[] a, b, c, inputIndex;
            private double[] extras;
            private readonly double[] values;
            private readonly bool[] evaluated;
            private readonly int output;
            private readonly BehaviorHook hook;
            private readonly Func<string, double> readValue;
            /// <summary>The reader of each named-value node, resolved once (null for other nodes).</summary>
            private readonly Func<double>[] named;
            internal Program(BehaviorGraph graph, BehaviorHook hook, IReadOnlySet<string> namedValues, Func<string, double> readValue,
                Func<string, Func<double>> bindValue = null)
            {
                this.hook = hook;
                graph = BehaviorCurves.Expand(graph);
                disabled = graph.Disabled;
                if (graph.DisabledValue.HasValue && (!double.IsFinite(graph.DisabledValue.Value) || graph.DisabledValue < hook.Min || graph.DisabledValue > hook.Max))
                    throw new InvalidDataException("A kikapcsolási eredmény legyen véges és a szabály értéktartományán belül.");
                if (disabled && (!EcologyResponseProgram.IsPureKind(hook.Kind) || !graph.DisabledValue.HasValue))
                    throw new InvalidDataException("Értéket számoló szabály kikapcsolásához adj meg egyértelmű helyettesítő értéket.");
                disabledValue = graph.DisabledValue ?? 0;
                this.readValue = readValue;
                if (graph.Nodes == null || graph.Nodes.Count is < 1 or > 256) throw new InvalidDataException("1–256 csomópont szükséges.");
                var byId = new Dictionary<string, BehaviorNode>(StringComparer.Ordinal);
                foreach (var n in graph.Nodes)
                    if (n == null || string.IsNullOrWhiteSpace(n.Id) || string.IsNullOrWhiteSpace(n.Name) || !Enum.IsDefined(n.Operation)
                        || !double.IsFinite(n.Value) || !float.IsFinite(n.X) || !float.IsFinite(n.Y) || !byId.TryAdd(n.Id, n))
                        throw new InvalidDataException("Érvénytelen vagy ismétlődő csomópont.");
                if (LogisticsRules.IsKind(hook.Kind) && graph.Nodes.Any(n => !LogisticsRules.AllowedOperation(n.Operation)))
                    throw new InvalidDataException("A logisztikai szabály csak mért bemenetekből és gráfműveletekből számolhat.");
                foreach (var n in graph.Nodes.Where(n => n.Operation == BehaviorOperation.Input))
                    if (hook.Inputs == null || Array.IndexOf(hook.Inputs, n.Key) < 0)
                        throw new InvalidDataException("Ennek a szabálynak nincs ilyen bemenete: " + n.Key);
                foreach (var n in graph.Nodes.Where(n => n.Operation == BehaviorOperation.NamedValue))
                    if (namedValues == null || !namedValues.Contains(n.Key) || readValue == null)
                        throw new InvalidDataException("Ismeretlen készlet vagy világmező: " + n.Key);
                if (hook.Kind == "world" && graph.Nodes.Any(n => n.Operation is >= BehaviorOperation.Delta and <= BehaviorOperation.Amount or >= BehaviorOperation.Capacity and <= BehaviorOperation.Surface))
                    throw new InvalidDataException("A világparaméter gráfjában csak az alapérték és állandók használhatók bemenetként.");
                var marks = new Dictionary<string, int>(); var ordered = new List<BehaviorNode>();
                void Visit(string id) {
                    if (id == null || !byId.TryGetValue(id, out var n)) throw new InvalidDataException("Hiányzó bemenet: " + id);
                    if (marks.TryGetValue(id, out int mark)) { if (mark == 1) throw new InvalidDataException("Kör a képletben: " + n.Name); return; }
                    marks[id] = 1;
                    int count = BehaviorModel.Inputs(n.Operation);
                    if (count > 0) Visit(n.A); if (count > 1) Visit(n.B); if (count > 2) Visit(n.C);
                    marks[id] = 2; ordered.Add(n);
                }
                foreach (var n in graph.Nodes) Visit(n.Id);
                Visit(graph.Output);
                nodes = ordered.ToArray(); values = new double[nodes.Length]; evaluated = new bool[nodes.Length]; a = new int[nodes.Length]; b = new int[nodes.Length]; c = new int[nodes.Length];
                inputIndex = nodes.Select(n => n.Operation == BehaviorOperation.Input ? Array.IndexOf(hook.Inputs, n.Key) : -1).ToArray();
                named = nodes.Select(n => n.Operation != BehaviorOperation.NamedValue ? null
                    : bindValue?.Invoke(n.Key) ?? (() => readValue(n.Key))).ToArray();
                var indices = nodes.Select((n, i) => (n.Id, i)).ToDictionary(p => p.Id, p => p.i);
                for (int i = 0; i < nodes.Length; i++) {
                    int count = BehaviorModel.Inputs(nodes[i].Operation);
                    if (count > 0) a[i] = indices[nodes[i].A]; if (count > 1) b[i] = indices[nodes[i].B]; if (count > 2) c[i] = indices[nodes[i].C];
                }
                output = indices[graph.Output];
            }
            internal void Prepare()
            {
                if (disabled || compileTried) return;
                compileTried = true; fast = Compile();
                if (fast != null) System.Runtime.CompilerServices.RuntimeHelpers.PrepareDelegate(fast);
            }
            internal double Evaluate(in BehaviorInputs input, double[] extraValues = null)
            {
                if (disabled) return disabledValue;
                // Tiered: a graph is interpreted until it proves hot, then compiled once (loading a project stays quick).
                if (fast == null && !compileTried && ++evaluations >= CompileAfter) { compileTried = true; fast = Compile(); }
                if (fast != null) return Math.Clamp(fast(input, extraValues), hook.Min, hook.Max);
                extras = extraValues;
                Array.Clear(evaluated);
                return Math.Clamp(EvaluateNode(output, input), hook.Min, hook.Max);
            }

            // ── The graph compiled to a delegate ──────────────────────────────
            // Rules run on every element of a scope (thousands of tiles), so a hot graph is compiled once into a .NET
            // expression instead of being walked node by node. The semantics stay the interpreter's: only the selected
            // branch of a choice runs, a division by zero and a non-finite node fail with the same message. Shared
            // operations are emitted once; immutable input reads and single-use subtrees are inlined.
            private Func<BehaviorInputs, double[], double> fast;
            private int evaluations; private bool compileTried;
            private const int CompileAfter = 256;
            private static double Finite(double value, string name) =>
                double.IsFinite(value) ? value : throw new ArithmeticException("Nem véges eredmény: " + name);
            private static double Input(double[] extras, int index, string name) =>
                extras != null && index >= 0 && index < extras.Length ? extras[index] : throw new ArithmeticException("Hiányzó bemenet: " + name);
            private static double Divide(double left, double right) => right == 0 ? throw new ArithmeticException("Nullával osztás.") : left / right;

            private Func<BehaviorInputs, double[], double> Compile()
            {
                // Fold only proven constant subgraphs, using the interpreter itself. Invalid constant
                // branches stay lazy runtime errors; preparation never reads inputs or world values.
                var constants = new double?[nodes.Length];
                var selected = new int[nodes.Length]; Array.Fill(selected, -1);
                Array.Clear(evaluated);
                for (int i = 0; i < nodes.Length; i++) {
                    int count = BehaviorModel.Inputs(nodes[i].Operation);
                    bool constant = nodes[i].Operation == BehaviorOperation.Constant;
                    if (nodes[i].Operation == BehaviorOperation.Select && constants[a[i]].HasValue) {
                        selected[i] = constants[a[i]].Value != 0 ? b[i] : c[i];
                        constant = constants[selected[i]].HasValue;
                    } else if (count > 0) constant = constants[a[i]].HasValue
                        && (count < 2 || constants[b[i]].HasValue) && (count < 3 || constants[c[i]].HasValue);
                    if (constant) {
                        try { constants[i] = EvaluateNode(i, default); }
                        catch (ArithmeticException) { } // Preserve the original node and its error, if selected at runtime.
                    }
                }
                if (constants[output].HasValue) { double value = constants[output].Value; return (_, _) => value; }
                var alive = new bool[nodes.Length]; var incoming = new int[nodes.Length];
                void Mark(int index) {
                    if (alive[index]) return;
                    alive[index] = true;
                    if (constants[index].HasValue) return;
                    void Edge(int child) { incoming[child]++; Mark(child); }
                    if (selected[index] >= 0) { Edge(selected[index]); return; }
                    int count = BehaviorModel.Inputs(nodes[index].Operation);
                    if (count > 0) Edge(a[index]); if (count > 1) Edge(b[index]); if (count > 2) Edge(c[index]);
                }
                Mark(output);
                var inline = new bool[nodes.Length];
                for (int i = 0; i < nodes.Length; i++) {
                    int count = BehaviorModel.Inputs(nodes[i].Operation);
                    bool immutableInput = count == 0 && nodes[i].Operation != BehaviorOperation.NamedValue;
                    inline[i] = constants[i].HasValue || immutableInput || incoming[i] <= 1 &&
                        (selected[i] >= 0 ? inline[selected[i]] : (count < 1 || inline[a[i]]) && (count < 2 || inline[b[i]]) && (count < 3 || inline[c[i]]));
                }
                var input = System.Linq.Expressions.Expression.Parameter(typeof(BehaviorInputs), "input");
                var extrasParameter = System.Linq.Expressions.Expression.Parameter(typeof(double[]), "extras");
                var self = typeof(Program);
                var result = System.Linq.Expressions.Expression.Label(typeof(double), "result");
                var labels = nodes.Select((_, i) => System.Linq.Expressions.Expression.Label("node" + i)).ToArray();
                var returns = nodes.Select((_, i) => System.Linq.Expressions.Expression.Label("return" + i)).ToArray();
                var outputs = nodes.Select((_, i) => System.Linq.Expressions.Expression.Variable(typeof(double), "value" + i)).ToArray();
                var ready = nodes.Select((_, i) => System.Linq.Expressions.Expression.Variable(typeof(bool), "ready" + i)).ToArray();
                var continuation = nodes.Select((_, i) => System.Linq.Expressions.Expression.Variable(typeof(int), "caller" + i)).ToArray();
                var sites = nodes.Select(_ => new List<System.Linq.Expressions.LabelTarget>()).ToArray();
                var code = new List<System.Linq.Expressions.Expression> { System.Linq.Expressions.Expression.Goto(labels[output]) };
                System.Linq.Expressions.Expression Value(int index) => constants[index].HasValue
                    ? System.Linq.Expressions.Expression.Constant(constants[index].Value) : outputs[index];
                System.Linq.Expressions.Expression Field(string name) => System.Linq.Expressions.Expression.Property(input, name);
                System.Linq.Expressions.Expression Constant(double v) => System.Linq.Expressions.Expression.Constant(v);
                System.Linq.Expressions.Expression Call(string method, params System.Linq.Expressions.Expression[] args) =>
                    System.Linq.Expressions.Expression.Call(typeof(Math).GetMethod(method, args.Select(x => x.Type).ToArray()), args);
                System.Linq.Expressions.Expression Bool(System.Linq.Expressions.Expression test) =>
                    System.Linq.Expressions.Expression.Condition(test, Constant(1), Constant(0));
                System.Linq.Expressions.Expression Operation(int index, System.Linq.Expressions.Expression left, System.Linq.Expressions.Expression right) {
                    var n = nodes[index];
                    return n.Operation switch {
                            BehaviorOperation.Constant => Constant(n.Value), BehaviorOperation.Native => Field(nameof(BehaviorInputs.Native)),
                            BehaviorOperation.Delta => Field(nameof(BehaviorInputs.Delta)), BehaviorOperation.Time => Field(nameof(BehaviorInputs.Time)),
                            BehaviorOperation.State => Field(nameof(BehaviorInputs.State)), BehaviorOperation.Amount => Field(nameof(BehaviorInputs.Amount)),
                            BehaviorOperation.Capacity => Field(nameof(BehaviorInputs.Capacity)), BehaviorOperation.Available => Field(nameof(BehaviorInputs.Available)),
                            BehaviorOperation.From => Field(nameof(BehaviorInputs.From)), BehaviorOperation.To => Field(nameof(BehaviorInputs.To)),
                            BehaviorOperation.Surface => Field(nameof(BehaviorInputs.Surface)),
                            BehaviorOperation.NamedValue => System.Linq.Expressions.Expression.Invoke(System.Linq.Expressions.Expression.Constant(named[index])),
                            BehaviorOperation.Input => System.Linq.Expressions.Expression.Call(self.GetMethod(nameof(Input), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static),
                                extrasParameter, System.Linq.Expressions.Expression.Constant(inputIndex[index]), System.Linq.Expressions.Expression.Constant(n.Key)),
                            BehaviorOperation.Exp => Call(nameof(Math.Exp), left), BehaviorOperation.Power => Call(nameof(Math.Pow), left, right),
                            BehaviorOperation.Add => System.Linq.Expressions.Expression.Add(left, right), BehaviorOperation.Subtract => System.Linq.Expressions.Expression.Subtract(left, right),
                            BehaviorOperation.Multiply => System.Linq.Expressions.Expression.Multiply(left, right),
                            BehaviorOperation.Divide => System.Linq.Expressions.Expression.Call(self.GetMethod(nameof(Divide), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static), left, right),
                            BehaviorOperation.Minimum => Call(nameof(Math.Min), left, right), BehaviorOperation.Maximum => Call(nameof(Math.Max), left, right),
                            BehaviorOperation.Greater => Bool(System.Linq.Expressions.Expression.GreaterThan(left, right)),
                            BehaviorOperation.Less => Bool(System.Linq.Expressions.Expression.LessThan(left, right)),
                            BehaviorOperation.Absolute => Call(nameof(Math.Abs), left), BehaviorOperation.Negate => System.Linq.Expressions.Expression.Negate(left),
                            _ => throw new InvalidDataException("Ismeretlen művelet.")
                        };
                }
                System.Linq.Expressions.Expression Checked(System.Linq.Expressions.Expression value, int index) => System.Linq.Expressions.Expression.Call(
                    self.GetMethod(nameof(Finite), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static), value,
                    System.Linq.Expressions.Expression.Constant(nodes[index].Name));
                System.Linq.Expressions.Expression Inline(int index) {
                    if (constants[index].HasValue) return Constant(constants[index].Value);
                    if (selected[index] >= 0) return Inline(selected[index]);
                    int count = BehaviorModel.Inputs(nodes[index].Operation);
                    var left = count > 0 ? Inline(a[index]) : null;
                    if (nodes[index].Operation == BehaviorOperation.Select) return Checked(System.Linq.Expressions.Expression.Condition(
                        System.Linq.Expressions.Expression.NotEqual(left, Constant(0)), Inline(b[index]), Inline(c[index])), index);
                    return Checked(Operation(index, left, count > 1 ? Inline(b[index]) : null), index);
                }
                if (inline[output]) return System.Linq.Expressions.Expression.Lambda<Func<BehaviorInputs, double[], double>>(
                    Inline(output), input, extrasParameter).Compile();
                void Require(int child) {
                    if (constants[child].HasValue) return;
                    if (inline[child]) { code.Add(System.Linq.Expressions.Expression.Assign(outputs[child], Inline(child))); return; }
                    var resume = System.Linq.Expressions.Expression.Label("resume" + child + "_" + sites[child].Count);
                    if (incoming[child] > 1) code.Add(System.Linq.Expressions.Expression.Assign(continuation[child], System.Linq.Expressions.Expression.Constant(sites[child].Count)));
                    sites[child].Add(resume);
                    code.Add(System.Linq.Expressions.Expression.Goto(labels[child])); code.Add(System.Linq.Expressions.Expression.Label(resume));
                }
                for (int i = 0; i < nodes.Length; i++) {
                    if (!alive[i] || constants[i].HasValue || inline[i]) continue;
                    var n = nodes[i];
                    code.Add(System.Linq.Expressions.Expression.Label(labels[i]));
                    if (incoming[i] > 1) code.Add(System.Linq.Expressions.Expression.IfThen(ready[i], System.Linq.Expressions.Expression.Goto(returns[i])));
                    System.Linq.Expressions.Expression value;
                    if (constants[i].HasValue) value = Constant(constants[i].Value);
                    else if (selected[i] >= 0) { Require(selected[i]); value = Value(selected[i]); }
                    else if (n.Operation == BehaviorOperation.Select) {
                        Require(a[i]);
                        var yes = System.Linq.Expressions.Expression.Label("yes" + i);
                        var no = System.Linq.Expressions.Expression.Label("no" + i);
                        var joined = System.Linq.Expressions.Expression.Label("joined" + i);
                        code.Add(System.Linq.Expressions.Expression.IfThenElse(System.Linq.Expressions.Expression.NotEqual(Value(a[i]), Constant(0)),
                            System.Linq.Expressions.Expression.Goto(yes), System.Linq.Expressions.Expression.Goto(no)));
                        code.Add(System.Linq.Expressions.Expression.Label(yes)); Require(b[i]);
                        code.Add(System.Linq.Expressions.Expression.Assign(outputs[i], Value(b[i])));
                        code.Add(System.Linq.Expressions.Expression.Goto(joined));
                        code.Add(System.Linq.Expressions.Expression.Label(no)); Require(c[i]);
                        code.Add(System.Linq.Expressions.Expression.Assign(outputs[i], Value(c[i])));
                        code.Add(System.Linq.Expressions.Expression.Label(joined)); value = outputs[i];
                    } else {
                        int count = BehaviorModel.Inputs(n.Operation);
                        if (count > 0) Require(a[i]); if (count > 1) Require(b[i]);
                        var left = count > 0 ? Value(a[i]) : null; var right = count > 1 ? Value(b[i]) : null;
                        value = Operation(i, left, right);
                    }
                    code.Add(System.Linq.Expressions.Expression.Assign(outputs[i], System.Linq.Expressions.Expression.Call(
                        self.GetMethod(nameof(Finite), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static), value,
                        System.Linq.Expressions.Expression.Constant(n.Name))));
                    if (incoming[i] > 1) code.Add(System.Linq.Expressions.Expression.Assign(ready[i], System.Linq.Expressions.Expression.Constant(true)));
                    code.Add(System.Linq.Expressions.Expression.Goto(returns[i]));
                }
                // Each node body exists once. Local continuations preserve lazy, ordered DAG evaluation without
                // recursive delegate calls, expression-tree expansion, or per-evaluation allocations.
                for (int i = 0; i < nodes.Length; i++) {
                    if (!alive[i] || constants[i].HasValue || inline[i]) continue;
                    code.Add(System.Linq.Expressions.Expression.Label(returns[i]));
                    if (i == output) code.Add(System.Linq.Expressions.Expression.Goto(result, outputs[i]));
                    else if (sites[i].Count == 1) code.Add(System.Linq.Expressions.Expression.Goto(sites[i][0]));
                    else code.Add(System.Linq.Expressions.Expression.Switch(continuation[i],
                        System.Linq.Expressions.Expression.Throw(System.Linq.Expressions.Expression.New(typeof(InvalidOperationException))),
                        sites[i].Select((site, index) => System.Linq.Expressions.Expression.SwitchCase(System.Linq.Expressions.Expression.Goto(site),
                            System.Linq.Expressions.Expression.Constant(index))).ToArray()));
                }
                code.Add(System.Linq.Expressions.Expression.Label(result, Constant(0)));
                var variables = outputs.Where((_, i) => alive[i] && !constants[i].HasValue)
                    .Concat(ready.Where((_, i) => alive[i] && incoming[i] > 1 && !constants[i].HasValue && !inline[i]))
                    .Concat(continuation.Where((_, i) => alive[i] && incoming[i] > 1 && !constants[i].HasValue && !inline[i]));
                return System.Linq.Expressions.Expression.Lambda<Func<BehaviorInputs, double[], double>>(
                    System.Linq.Expressions.Expression.Block(variables, code), input, extrasParameter).Compile();
            }

            private double EvaluateNode(int i, in BehaviorInputs input)
            {
                    if (evaluated[i]) return values[i];
                    var operation = nodes[i].Operation;
                    int count = BehaviorModel.Inputs(operation);
                    double left = count > 0 ? EvaluateNode(a[i], input) : 0;
                    // Only the selected branch executes. Unused and disconnected nodes cannot fail a valid result.
                    double right = count > 1 && operation != BehaviorOperation.Select ? EvaluateNode(b[i], input) : 0;
                    values[i] = nodes[i].Operation switch {
                        BehaviorOperation.Constant => nodes[i].Value, BehaviorOperation.Native => input.Native,
                        BehaviorOperation.Delta => input.Delta, BehaviorOperation.Time => input.Time, BehaviorOperation.State => input.State, BehaviorOperation.Amount => input.Amount,
                        BehaviorOperation.Capacity => input.Capacity, BehaviorOperation.Available => input.Available,
                        BehaviorOperation.From => input.From, BehaviorOperation.To => input.To, BehaviorOperation.Surface => input.Surface,
                        BehaviorOperation.NamedValue => named[i](),
                        BehaviorOperation.Input => extras != null && inputIndex[i] < extras.Length ? extras[inputIndex[i]] : throw new ArithmeticException("Hiányzó bemenet: " + nodes[i].Key),
                        BehaviorOperation.Exp => Math.Exp(left), BehaviorOperation.Power => Math.Pow(left, right),
                        BehaviorOperation.Add => left + right, BehaviorOperation.Subtract => left - right, BehaviorOperation.Multiply => left * right,
                        BehaviorOperation.Divide => right == 0 ? throw new ArithmeticException("Nullával osztás.") : left / right,
                        BehaviorOperation.Minimum => Math.Min(left, right), BehaviorOperation.Maximum => Math.Max(left, right),
                        BehaviorOperation.Greater => left > right ? 1 : 0, BehaviorOperation.Less => left < right ? 1 : 0,
                        BehaviorOperation.Select => EvaluateNode(left != 0 ? b[i] : c[i], input), BehaviorOperation.Absolute => Math.Abs(left), BehaviorOperation.Negate => -left,
                        _ => throw new ArithmeticException("Ismeretlen művelet.")
                    };
                    if (!double.IsFinite(values[i])) throw new ArithmeticException("Nem véges eredmény: " + nodes[i].Name);
                    evaluated[i] = true;
                    return values[i];
            }
        }
    }
}
