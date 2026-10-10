using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForesTycoon.Rules
{
    public enum BehaviorOperation { Constant, Native, Delta, Time, State, Amount, Add, Subtract, Multiply, Divide, Minimum, Maximum, Greater, Less, Select, Absolute, Negate,
        Capacity, Available, From, To, Surface }
    public sealed class BehaviorNode
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public BehaviorOperation Operation { get; set; }
        public double Value { get; set; }
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
            BehaviorOperation.Select => 3, BehaviorOperation.Absolute or BehaviorOperation.Negate => 1,
            >= BehaviorOperation.Add and <= BehaviorOperation.Less => 2, _ => 0
        };
    }
    public sealed record BehaviorHook(string Id, string Name, string Kind, string Unit, double Min, double Max,
        string State, string Amount, string Schedule, bool SupportsObjects = true);
    public readonly record struct BehaviorInputs(double Native, double Delta, double Time, double State, double Amount,
        double Capacity = 0, double Available = 0, double From = 0, double To = 0, double Surface = 0);

    /// <summary>Pure bounded expressions. No IO, clock, random state, or arbitrary C# execution.</summary>
    public sealed class CompiledBehaviorModel
    {
        private readonly BehaviorModel document;
        private readonly Dictionary<(string Hook, string Kind, ulong? Id), Program> programs = new();
        public BehaviorModel Document => document.Clone();
        public bool HasGraphs => document.Graphs.Count > 0;
        public string LastError { get; private set; }
        public CompiledBehaviorModel(BehaviorModel source, IEnumerable<BehaviorHook> available)
        {
            ArgumentNullException.ThrowIfNull(source);
            document = source.Clone();
            if (document.Schema != "forest-behaviors" || document.Version is not (1 or 2 or 3) || document.Graphs == null || document.Graphs.Count > 512)
                throw new InvalidDataException("Ismeretlen vagy túl nagy viselkedésmodell.");
            _ = new CompiledBehaviorControllers(document);
            var hooks = available.ToDictionary(h => (h.Id, h.Kind));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var graph in document.Graphs)
            {
                if (graph == null || string.IsNullOrWhiteSpace(graph.Id) || !ids.Add(graph.Id) || string.IsNullOrWhiteSpace(graph.Name)
                    || !hooks.TryGetValue((graph.Hook, graph.Kind), out var hook)) throw new InvalidDataException("Hibás gráfazonosító vagy nem támogatott világkötés.");
                if (!hook.SupportsObjects && graph.ObjectId.HasValue) throw new InvalidDataException("Ez a paraméter a teljes világra vonatkozik.");
                if (!programs.TryAdd((graph.Hook, graph.Kind, graph.ObjectId), new Program(graph, hook)))
                    throw new InvalidDataException("Egy eseményhez és célhoz csak egy viselkedés köthető.");
            }
        }
        public double Evaluate(string hook, string kind, ulong id, in BehaviorInputs inputs)
        {
            if (!programs.TryGetValue((hook, kind, id), out var program) && !programs.TryGetValue((hook, kind, null), out program)) return inputs.Native;
            try { return program.Evaluate(inputs); }
            catch (ArithmeticException e) {
                LastError = $"{hook} / {kind} #{id}: {e.Message} A beépített eredmény maradt érvényben.";
                return inputs.Native;
            }
        }
        private sealed class Program
        {
            private readonly BehaviorNode[] nodes;
            private readonly int[] a, b, c;
            private readonly double[] values;
            private readonly bool[] evaluated;
            private readonly int output;
            private readonly BehaviorHook hook;
            internal Program(BehaviorGraph graph, BehaviorHook hook)
            {
                this.hook = hook;
                if (graph.Nodes == null || graph.Nodes.Count is < 1 or > 256) throw new InvalidDataException("1–256 csomópont szükséges.");
                var byId = new Dictionary<string, BehaviorNode>(StringComparer.Ordinal);
                foreach (var n in graph.Nodes)
                    if (n == null || string.IsNullOrWhiteSpace(n.Id) || string.IsNullOrWhiteSpace(n.Name) || !Enum.IsDefined(n.Operation)
                        || !double.IsFinite(n.Value) || !float.IsFinite(n.X) || !float.IsFinite(n.Y) || !byId.TryAdd(n.Id, n))
                        throw new InvalidDataException("Érvénytelen vagy ismétlődő csomópont.");
                if (hook.Kind == "world" && graph.Nodes.Any(n => n.Operation is >= BehaviorOperation.Delta and <= BehaviorOperation.Amount or >= BehaviorOperation.Capacity))
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
                var indices = nodes.Select((n, i) => (n.Id, i)).ToDictionary(p => p.Id, p => p.i);
                for (int i = 0; i < nodes.Length; i++) {
                    int count = BehaviorModel.Inputs(nodes[i].Operation);
                    if (count > 0) a[i] = indices[nodes[i].A]; if (count > 1) b[i] = indices[nodes[i].B]; if (count > 2) c[i] = indices[nodes[i].C];
                }
                output = indices[graph.Output];
            }
            internal double Evaluate(in BehaviorInputs input)
            {
                Array.Clear(evaluated);
                return Math.Clamp(EvaluateNode(output, input), hook.Min, hook.Max);
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
