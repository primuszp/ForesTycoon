using System;
using System.IO;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>Authored curves lower to the same bounded arithmetic as influence curves.</summary>
public static class BehaviorCurves
{
    public const string Input = "curveInput";
    public static string Default => new RuleCurve { Input = Input }.ToFormula();

    public static RuleCurve Read(string definition)
    {
        if (definition == null || definition.Length > 8192 || !RuleCurve.TryParse(definition, out var curve)
            || curve.Input != Input || !string.IsNullOrWhiteSpace(curve.Tail))
            throw new InvalidDataException("Érvénytelen gráfgörbe. Válassz görbecsaládot és pontokat a görbeszerkesztőben.");
        curve.Validate();
        return curve;
    }

    internal static BehaviorGraph Expand(BehaviorGraph original)
    {
        if (original.Nodes == null || original.Nodes.Count is < 1 or > 256)
            throw new InvalidDataException("1–256 csomópont szükséges.");
        if (!original.Nodes.Any(n => n?.Operation == BehaviorOperation.Curve)) return original;
        // Check the authored IDs before replacing any nodes; expansion must not conceal a duplicate.
        if (original.Nodes.Any(n => n == null) || original.Nodes.Select(n => n.Id).Distinct(StringComparer.Ordinal).Count() != original.Nodes.Count)
            throw new InvalidDataException("Érvénytelen vagy ismétlődő csomópont.");
        var graph = new BehaviorModel { Graphs = { original } }.Clone().Graphs[0];
        var ids = graph.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var node in graph.Nodes.Where(n => n.Operation == BehaviorOperation.Curve).ToArray()) {
            if (string.IsNullOrWhiteSpace(node.Id) || string.IsNullOrWhiteSpace(node.Name)
                || !double.IsFinite(node.Value) || !float.IsFinite(node.X) || !float.IsFinite(node.Y))
                throw new InvalidDataException("Érvénytelen görbecsomópont.");
            var curve = Read(node.Key);
            var lowered = RuleFormula.Compile(curve.ToFormula(), new BehaviorGraph(),
                new[] { new FormulaName(Input, BehaviorOperation.Input, Input) });
            string prefix = node.Id + ".curve.";
            while (lowered.Nodes.Any(n => ids.Contains(prefix + n.Id))) prefix += "_";
            var mapped = lowered.Nodes.ToDictionary(item => item.Id,
                item => item.Operation == BehaviorOperation.Input ? node.A : item.Id == lowered.Output ? node.Id : prefix + item.Id);
            string Map(string id) => mapped[id];
            graph.Nodes.Remove(node);
            foreach (var item in lowered.Nodes.Where(n => n.Operation != BehaviorOperation.Input)) {
                string id = Map(item.Id);
                int inputs = BehaviorModel.Inputs(item.Operation);
                if (inputs > 0) item.A = Map(item.A);
                if (inputs > 1) item.B = Map(item.B);
                if (inputs > 2) item.C = Map(item.C);
                item.Id = id; ids.Add(id); graph.Nodes.Add(item);
            }
        }
        if (graph.Nodes.Count > 256) throw new InvalidDataException("A görbékkel kibontott gráf legfeljebb 256 műveletből állhat.");
        return graph;
    }
}
