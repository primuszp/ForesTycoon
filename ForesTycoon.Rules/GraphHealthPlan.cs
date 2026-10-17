using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>Recognizes an authored static query or a static coefficient times one health input. No gameplay formula.</summary>
public sealed class GraphHealthPlan
{
    private readonly Func<BehaviorInputs, double[], double> gate, coefficient;
    private readonly double min, max;
    private string healthName, resultName;
    public int HealthInput { get; }
    private GraphHealthPlan(Func<BehaviorInputs, double[], double> gate, Func<BehaviorInputs, double[], double> coefficient,
        int healthInput, double min, double max) { this.gate = gate; this.coefficient = coefficient; HealthInput = healthInput; this.min = min; this.max = max; }

    public static bool ReadsHealth(BehaviorGraph source)
    {
        var nodes = source.Nodes.ToDictionary(n => n.Id); var visited = new HashSet<string>();
        bool Visit(string id) {
            if (!visited.Add(id)) return false;
            var n = nodes[id]; int arity = BehaviorModel.Inputs(n.Operation);
            return n.Operation == BehaviorOperation.Input && n.Key is "tree.health" or "neighbor.health"
                || arity > 0 && Visit(n.A) || arity > 1 && Visit(n.B) || arity > 2 && Visit(n.C);
        }
        return !source.Disabled && Visit(source.Output);
    }

    public static GraphHealthPlan TryCreate(BehaviorGraph source, BehaviorHook hook)
    {
        var graph = BehaviorCurves.Expand(new BehaviorModel { Graphs = new() { source } }.Clone().Graphs[0]);
        if (graph.Disabled) return new(null, (_, _) => graph.DisabledValue!.Value, -1, hook.Min, hook.Max);
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        bool Static(string id) {
            var part = new BehaviorGraph { Output = id, Nodes = graph.Nodes };
            return !ReadsHealth(part);
        }
        Func<BehaviorInputs, double[], double> Compile(string id) {
            var part = new BehaviorGraph { Id = "health.prepare", Hook = "health.prepare", Kind = "queryPreparation", Output = id, Nodes = graph.Nodes };
            var definition = new BehaviorHook(part.Hook, hook.Name, part.Kind, hook.Unit, -double.MaxValue, double.MaxValue, "", "", "", false, hook.Inputs);
            var program = new CompiledBehaviorModel(new() { Graphs = new() { part } }, new[] { definition });
            program.Prepare(); return program.Evaluator(part.Hook, part.Kind);
        }
        string root = graph.Output; Func<BehaviorInputs, double[], double> gate = null;
        if (Static(root)) return new(null, Compile(root), -1, hook.Min, hook.Max);
        var output = nodes[root];
        if (output.Operation == BehaviorOperation.Select && Static(output.A) && nodes[output.C].Operation == BehaviorOperation.Constant && nodes[output.C].Value == 0) {
            gate = Compile(output.A); root = output.B;
        }
        int Health(string id) => nodes[id].Operation != BehaviorOperation.Input ? -1 : nodes[id].Key switch { "tree.health" => 3, "neighbor.health" => 8, _ => -1 };
        if (Health(root) is int direct && direct >= 0) return Named(new(gate, (_, _) => 1, direct, hook.Min, hook.Max), root, root);
        var node = nodes[root];
        if (node.Operation != BehaviorOperation.Multiply) return null;
        if (Health(node.A) is int left && left >= 0 && Static(node.B)) return Named(new(gate, Compile(node.B), left, hook.Min, hook.Max), node.A, root);
        if (Health(node.B) is int right && right >= 0 && Static(node.A)) return Named(new(gate, Compile(node.A), right, hook.Min, hook.Max), node.B, root);
        return null;
        GraphHealthPlan Named(GraphHealthPlan plan, string health, string result) {
            plan.healthName = nodes[health].Name; plan.resultName = nodes[result].Name; return plan;
        }
    }

    // NaN is only an inactive lazy branch marker, never a computed coefficient.
    public double Prepare(double[] inputs) => gate != null && gate(default, inputs) == 0 ? double.NaN : coefficient(default, inputs);
    public double Evaluate(double value, double ownHealth, double otherHealth)
    {
        if (double.IsNaN(value)) return 0;
        if (HealthInput >= 0) {
            double health = HealthInput == 3 ? ownHealth : otherHealth;
            if (!double.IsFinite(health)) throw new ArithmeticException("Nem véges eredmény: " + healthName);
            value *= health;
        }
        if (!double.IsFinite(value)) throw new ArithmeticException("Nem véges eredmény: " + resultName);
        return Math.Clamp(value, min, max);
    }
}
