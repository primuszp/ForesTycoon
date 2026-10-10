using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForesTycoon.Rules
{
    public enum RuleOperation { TrafficWear, SurfaceFactor, RoadDamage, Constant, Add, Multiply, Minimum, Maximum }
    public enum RuleUnit { Scalar, ConditionLoss }

    public sealed class RuleNode
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public RuleOperation Operation { get; set; }
        public double Value { get; set; } = 1;
        public string A { get; set; } = "";
        public string B { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
    }

    /// <summary>Authoring document. Runtime compiles a private snapshot, never this mutable graph.</summary>
    public sealed class RuleModel
    {
        public int Version { get; set; } = 1;
        public string Name { get; set; } = "Útkopás";
        public string Binding { get; set; } = "road.trafficWear";
        public string Output { get; set; } = "wear";
        public List<RuleNode> Nodes { get; set; } = new();
        public static RuleModel Default() => new() { Nodes = new()
        {
            new() { Id = "traffic", Name = "Áthaladási terhelés", Operation = RuleOperation.TrafficWear, X = 20, Y = 30 },
            new() { Id = "surface", Name = "Burkolati szorzó", Operation = RuleOperation.SurfaceFactor, X = 20, Y = 150 },
            new() { Id = "base", Name = "Burkolati kopás", Operation = RuleOperation.Multiply, A = "traffic", B = "surface", X = 260, Y = 70 },
            new() { Id = "scale", Name = "Kopási szorzó", Operation = RuleOperation.Constant, Value = 1, X = 260, Y = 210 },
            new() { Id = "wear", Name = "Útállapot csökkenése", Operation = RuleOperation.Multiply, A = "base", B = "scale", X = 500, Y = 90 }
        }};

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() } };
        public string ToJson() => JsonSerializer.Serialize(this, Options);
        public static RuleModel FromJson(string json) => JsonSerializer.Deserialize<RuleModel>(json, Options)
            ?? throw new InvalidDataException("Üres szabálymodell.");
        public RuleModel Clone() => new()
        {
            Version = Version, Name = Name, Binding = Binding, Output = Output,
            Nodes = Nodes?.Select(n => n == null ? null : new RuleNode
            {
                Id = n.Id, Name = n.Name, Operation = n.Operation, Value = n.Value,
                A = n.A, B = n.B, X = n.X, Y = n.Y
            }).ToList()
        };
        public static bool Binary(RuleOperation operation) => operation >= RuleOperation.Add;
    }

    public readonly record struct RoadRuleInputs(double TrafficWear, double SurfaceFactor, double RoadDamage);

    /// <summary>Acyclic equations inside one event; feedback passes through world state between events.</summary>
    public sealed class CompiledRoadRule
    {
        private readonly RuleModel model;
        private readonly RuleNode[] ordered;
        private readonly int[] left, right;
        private readonly double[] values;
        private readonly int output;
        public RuleModel Document => model.Clone();
        public string Name => model.Name;

        public CompiledRoadRule(RuleModel source)
        {
            ArgumentNullException.ThrowIfNull(source);
            model = source.Clone();
            if (model.Version != 1 || model.Binding != "road.trafficWear") Fail("Ismeretlen modellverzió vagy világkötés.");
            if (string.IsNullOrWhiteSpace(model.Name) || model.Nodes == null || model.Nodes.Count == 0 || model.Nodes.Count > 128)
                Fail("A modell neve és 1–128 csomópont szükséges.");
            var nodes = new Dictionary<string, RuleNode>(StringComparer.Ordinal);
            foreach (var n in model.Nodes)
            {
                if (n == null || string.IsNullOrWhiteSpace(n.Id) || string.IsNullOrWhiteSpace(n.Name) || !Enum.IsDefined(n.Operation)
                    || !double.IsFinite(n.Value) || n.Value < 0 || !float.IsFinite(n.X) || !float.IsFinite(n.Y))
                    Fail("Érvénytelen csomópont, érték vagy pozíció.");
                if (!nodes.TryAdd(n.Id, n)) Fail($"Ismétlődő azonosító: {n.Id}");
            }
            var state = new Dictionary<string, int>();
            var units = new Dictionary<string, RuleUnit>();
            var list = new List<RuleNode>();
            RuleUnit Visit(string id)
            {
                if (id == null || !nodes.TryGetValue(id, out var n)) { Fail($"Hiányzó bemenet: {id}"); return default; }
                if (state.TryGetValue(id, out int mark))
                {
                    if (mark == 1) Fail($"Azonnali képletkör: {n.Name}. A visszacsatolás világállapoton keresztül történjen.");
                    return units[id];
                }
                state[id] = 1;
                RuleUnit unit = n.Operation == RuleOperation.TrafficWear ? RuleUnit.ConditionLoss : RuleUnit.Scalar;
                if (RuleModel.Binary(n.Operation))
                {
                    var a = Visit(n.A); var b = Visit(n.B);
                    if (n.Operation == RuleOperation.Multiply)
                    {
                        if (a == RuleUnit.ConditionLoss && b == RuleUnit.ConditionLoss) Fail($"Hibás mértékegység: {n.Name}.");
                        unit = a == RuleUnit.ConditionLoss || b == RuleUnit.ConditionLoss ? RuleUnit.ConditionLoss : RuleUnit.Scalar;
                    }
                    else { if (a != b) Fail($"Eltérő mértékegységű bemenetek: {n.Name}."); unit = a; }
                }
                units[id] = unit; state[id] = 2; list.Add(n); return unit;
            }
            // Validate disconnected nodes too: an incomplete draft must never be installed.
            foreach (var n in model.Nodes) Visit(n.Id);
            if (Visit(model.Output) != RuleUnit.ConditionLoss) Fail("A kimenet egysége útállapot-veszteség legyen.");
            ordered = list.ToArray(); values = new double[ordered.Length];
            var indices = ordered.Select((n, i) => (n.Id, i)).ToDictionary(p => p.Id, p => p.i);
            left = new int[ordered.Length]; right = new int[ordered.Length];
            for (int i = 0; i < ordered.Length; i++)
                if (RuleModel.Binary(ordered[i].Operation)) { left[i] = indices[ordered[i].A]; right[i] = indices[ordered[i].B]; }
            output = indices[model.Output];
        }

        public float Evaluate(RoadRuleInputs input)
        {
            if (!double.IsFinite(input.TrafficWear) || input.TrafficWear < 0 || input.TrafficWear > 1 ||
                !double.IsFinite(input.SurfaceFactor) || input.SurfaceFactor < 0 || input.SurfaceFactor > 1 ||
                !double.IsFinite(input.RoadDamage) || input.RoadDamage < 0 || input.RoadDamage > 1)
                throw new ArgumentOutOfRangeException(nameof(input));
            for (int i = 0; i < ordered.Length; i++)
            {
                var n = ordered[i]; double a = values[left[i]], b = values[right[i]];
                values[i] = n.Operation switch
                {
                    RuleOperation.TrafficWear => input.TrafficWear,
                    RuleOperation.SurfaceFactor => input.SurfaceFactor,
                    RuleOperation.RoadDamage => input.RoadDamage,
                    RuleOperation.Constant => n.Value,
                    RuleOperation.Add => a + b,
                    RuleOperation.Multiply => a * b,
                    RuleOperation.Minimum => Math.Min(a, b),
                    RuleOperation.Maximum => Math.Max(a, b),
                    _ => throw new InvalidOperationException()
                };
                if (!double.IsFinite(values[i])) Fail($"Számítási túlcsordulás: {n.Name}.");
            }
            return (float)Math.Clamp(values[output], 0, 1);
        }

        // Nonnegative operators are monotone; unit upper bounds prove bounded-input arithmetic stays finite.
        public void ValidateRange() => Evaluate(new(1, 1, 1));
        private static void Fail(string message) => throw new InvalidDataException(message);
    }
}
