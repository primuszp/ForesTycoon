using System;
using System.Collections.Generic;

namespace ForesTycoon.Rules;

/// <summary>Conservative bounds used only to prove a lazy zero branch. Unknown means run the original graph.</summary>
public readonly record struct GraphInputRange(double Min, double Max)
{
    public static GraphInputRange Exact(double value) => new(value, value);
    public bool Valid => double.IsFinite(Min) && double.IsFinite(Max) && Min <= Max;
    public static GraphInputRange Unknown => new(double.NaN, double.NaN);
}

/// <summary>No biological formula lives here: the condition is taken from the authored graph.</summary>
public sealed class GraphZeroGate
{
    private readonly record struct Instruction(BehaviorOperation Operation, int A, int B, int Input, double Value);
    private readonly Instruction[] instructions;
    private readonly GraphInputRange[] values;

    private GraphZeroGate(Instruction[] instructions) { this.instructions = instructions; values = new GraphInputRange[instructions.Length]; }
    internal bool SameCondition(GraphZeroGate other) => other != null && instructions.AsSpan().SequenceEqual(other.instructions);

    public static GraphZeroGate TryCreate(BehaviorGraph graph, IReadOnlyList<string> inputs)
    {
        if (graph?.Nodes == null || graph.Nodes.Count > 256 || inputs == null) return null;
        if (graph.Disabled) return graph.DisabledValue == 0 ? new(Array.Empty<Instruction>()) : null;
        var nodes = new Dictionary<string, BehaviorNode>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes) if (node == null || string.IsNullOrWhiteSpace(node.Id) || !nodes.TryAdd(node.Id, node)) return null;
        if (!nodes.TryGetValue(graph.Output, out var output)) return null;
        if (output.Operation == BehaviorOperation.Constant) return output.Value == 0 ? new(Array.Empty<Instruction>()) : null;
        if (output.Operation != BehaviorOperation.Select || !nodes.TryGetValue(output.C, out var zero)
            || zero.Operation != BehaviorOperation.Constant || zero.Value != 0) return null;
        var code = new List<Instruction>(); var visited = new Dictionary<string, int>(); var active = new HashSet<string>();
        int Visit(string id) {
            if (visited.TryGetValue(id, out int known)) return known;
            if (!active.Add(id) || !nodes.TryGetValue(id, out var node)) return -1;
            int a = -1, b = -1, input = -1;
            switch (node.Operation) {
                case BehaviorOperation.Constant: if (!double.IsFinite(node.Value)) return -1; break;
                case BehaviorOperation.Input:
                    for (int i = 0; i < inputs.Count; i++) if (inputs[i] == node.Key) { input = i; break; }
                    if (input < 0) return -1; break;
                case BehaviorOperation.Add: case BehaviorOperation.Subtract: case BehaviorOperation.Multiply:
                case BehaviorOperation.Minimum: case BehaviorOperation.Maximum: case BehaviorOperation.Less: case BehaviorOperation.Greater:
                    a = Visit(node.A); b = Visit(node.B); if (a < 0 || b < 0) return -1; break;
                case BehaviorOperation.Absolute: case BehaviorOperation.Negate:
                    a = Visit(node.A); if (a < 0) return -1; break;
                default: return -1;
            }
            active.Remove(id); int index = code.Count;
            code.Add(new(node.Operation, a, b, input, node.Value)); return visited[id] = index;
        }
        return Visit(output.A) < 0 ? null : new(code.ToArray());
    }

    /// <summary>False proves every finite evaluation selects zero; true also covers anything we cannot prove.</summary>
    public bool MayBeNonZero(ReadOnlySpan<GraphInputRange> inputs)
    {
        if (instructions.Length == 0) return false;
        for (int i = 0; i < instructions.Length; i++) {
            var node = instructions[i];
            var a = node.A < 0 ? default : values[node.A]; var b = node.B < 0 ? default : values[node.B];
            GraphInputRange result;
            switch (node.Operation) {
                case BehaviorOperation.Constant: result = GraphInputRange.Exact(node.Value); break;
                case BehaviorOperation.Input: result = node.Input < inputs.Length ? inputs[node.Input] : GraphInputRange.Unknown; break;
                case BehaviorOperation.Add: result = Rounded(a.Min + b.Min, a.Max + b.Max); break;
                case BehaviorOperation.Subtract: result = Rounded(a.Min - b.Max, a.Max - b.Min); break;
                case BehaviorOperation.Multiply:
                    double p = a.Min * b.Min, q = a.Min * b.Max, r = a.Max * b.Min, s = a.Max * b.Max;
                    result = Rounded(Math.Min(Math.Min(p, q), Math.Min(r, s)), Math.Max(Math.Max(p, q), Math.Max(r, s))); break;
                case BehaviorOperation.Minimum: result = new(Math.Min(a.Min, b.Min), Math.Min(a.Max, b.Max)); break;
                case BehaviorOperation.Maximum: result = new(Math.Max(a.Min, b.Min), Math.Max(a.Max, b.Max)); break;
                case BehaviorOperation.Less: result = a.Min >= b.Max ? GraphInputRange.Exact(0) : a.Max < b.Min ? GraphInputRange.Exact(1) : new(0, 1); break;
                case BehaviorOperation.Greater: result = a.Max <= b.Min ? GraphInputRange.Exact(0) : a.Min > b.Max ? GraphInputRange.Exact(1) : new(0, 1); break;
                case BehaviorOperation.Negate: result = new(-a.Max, -a.Min); break;
                case BehaviorOperation.Absolute: result = new(a.Min <= 0 && a.Max >= 0 ? 0 : Math.Min(Math.Abs(a.Min), Math.Abs(a.Max)), Math.Max(Math.Abs(a.Min), Math.Abs(a.Max))); break;
                default: return true;
            }
            // Never mask missing inputs, overflow or an unsupported/invalid intermediate.
            if (!result.Valid) return true;
            values[i] = result;
        }
        return values[^1].Min != 0 || values[^1].Max != 0;
    }

    private static GraphInputRange Rounded(double min, double max) => new(Math.BitDecrement(min), Math.BitIncrement(max));
}
