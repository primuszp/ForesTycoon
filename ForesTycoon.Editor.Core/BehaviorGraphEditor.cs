#nullable enable
using System;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor.Core;

/// <summary>Graph authoring rules and their undo checkpoints. Screen selection and layout gestures belong to the view.</summary>
internal sealed class BehaviorGraphEditor(BehaviorEditorState owner)
{
    internal const int NodeLimit = AuthoringOperations.GraphNodeLimit;

    internal BehaviorGraph Ensure(BehaviorHook hook)
    {
        var existing = owner.Draft.Graphs.Find(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null);
        if (existing != null) return existing;
        var graph = WorldBehaviorPolicy.Hooks.Any(h => h.Id == hook.Id && h.Kind == hook.Kind)
            ? WorldBehaviorPolicy.DefaultGraph(hook) : WorldBehaviorPolicy.InactiveGraph(hook);
        owner.Remember(); owner.Draft.Graphs.Add(graph); return graph;
    }

    internal BehaviorGraph? RestoreDefault(string hook, string kind, ulong? objectId)
    {
        var replacement = EcologyResponseProgram.IsPureKind(kind) && !objectId.HasValue
            ? WorldBehaviorPolicy.DefaultGraph(owner.Hooks.Single(h => h.Id == hook && h.Kind == kind)) : null;
        owner.Remember();
        owner.Draft.Graphs.RemoveAll(g => g.Hook == hook && g.Kind == kind && g.ObjectId == objectId);
        if (replacement != null) owner.Draft.Graphs.Add(replacement);
        return replacement ?? owner.Draft.Graphs.FirstOrDefault(g => g.Hook == hook && g.Kind == kind && !g.ObjectId.HasValue);
    }

    internal void Edit(BehaviorGraph graph, Action<BehaviorGraph> edit)
    {
        Require(graph); owner.Remember(); edit(graph);
    }

    internal void EditNode(BehaviorGraph graph, BehaviorNode node, Action<BehaviorNode> edit)
    {
        Require(graph, node); owner.Remember(); edit(node);
    }

    internal void SetAvailability(string id, bool disabled, double? value)
    {
        var graph = owner.Draft.Graphs.Single(g => g.Id == id);
        Edit(graph, g => { g.Disabled = disabled; if (value.HasValue) g.DisabledValue = value.Value; });
    }

    internal bool CanDelete(BehaviorGraph graph) => !EcologyResponseProgram.IsPureKind(graph.Kind);
    internal void Delete(BehaviorGraph graph)
    {
        Require(graph);
        if (!CanDelete(graph)) throw new InvalidOperationException("Az összefüggést az alapgráf visszaállításával módosíthatod.");
        owner.Remember(); owner.Draft.Graphs.Remove(graph);
    }

    internal bool CanAddNode(BehaviorGraph graph, BehaviorOperation operation) => graph.Nodes.Count < NodeLimit && Enum.IsDefined(operation)
        && (!EcologyResponseProgram.IsPureKind(graph.Kind) || EcologyResponseProgram.AllowedOperation(operation)
            || LogisticsRules.IsKind(graph.Kind) && LogisticsRules.AllowedOperation(operation))
        && (graph.Kind != "world" || operation is not (>= BehaviorOperation.Delta and <= BehaviorOperation.Amount or >= BehaviorOperation.Capacity and <= BehaviorOperation.Surface));

    internal BehaviorNode AddNode(BehaviorGraph graph, BehaviorOperation operation, string name)
    {
        Require(graph);
        if (!CanAddNode(graph, operation)) throw new InvalidOperationException("A csomópont nem adható ehhez a gráfhoz, vagy a gráf megtelt.");
        var node = new BehaviorNode { Id = Guid.NewGuid().ToString("N")[..8], Name = name, Operation = operation, Value = 1,
            A = graph.Output, B = graph.Output, C = graph.Output, Key = operation == BehaviorOperation.Curve ? BehaviorCurves.Default : "",
            X = 260, Y = 40 + graph.Nodes.Count * 30 };
        owner.Remember(); graph.Nodes.Add(node); return node;
    }

    internal void DeleteNode(BehaviorGraph graph, BehaviorNode node)
    {
        Require(graph, node);
        // Missing ports and outputs remain visible, repairable draft data; do not silently rewrite dependent formulas.
        owner.Remember(); graph.Nodes.Remove(node);
    }

    internal void SetOutput(BehaviorGraph graph, string nodeId)
    {
        Require(graph);
        if (!graph.Nodes.Any(n => n.Id == nodeId)) throw new ArgumentException("Hiányzó kimeneti csomópont.", nameof(nodeId));
        Edit(graph, g => g.Output = nodeId);
    }

    internal void SetInput(BehaviorGraph graph, BehaviorNode node, int port, string source)
    {
        Require(graph, node);
        if (port < 0 || port >= BehaviorModel.Inputs(node.Operation)) throw new ArgumentOutOfRangeException(nameof(port));
        if (source.Length > 0 && !graph.Nodes.Any(n => n.Id == source)) throw new ArgumentException("Hiányzó bemeneti csomópont.", nameof(source));
        owner.Remember();
        if (port == 0) node.A = source; else if (port == 1) node.B = source; else node.C = source;
    }

    internal string? AddInfluence(BehaviorGraph graph, string key, string name, double reference, double strength)
    {
        Require(graph);
        if (graph.Nodes.Count > NodeLimit - AuthoringOperations.InfluenceNodeCount || !graph.Nodes.Any(n => n.Id == graph.Output)) return null;
        owner.Remember(); return AuthoringOperations.AddInfluence(graph, key, name, reference, strength);
    }

    internal BehaviorNode? AddOutputCurve(BehaviorGraph graph)
    {
        Require(graph);
        if (graph.Nodes.Count >= NodeLimit || !graph.Nodes.Any(n => n.Id == graph.Output)) return null;
        var node = new BehaviorNode { Id = "curve." + Guid.NewGuid().ToString("N")[..8], Name = "Válaszgörbe",
            Operation = BehaviorOperation.Curve, Key = BehaviorCurves.Default, A = graph.Output, X = 260, Y = 40 };
        owner.Remember(); graph.Nodes.Add(node); graph.Output = node.Id; return node;
    }

    internal BehaviorNode? AddOutputMultiplier(BehaviorGraph graph)
    {
        Require(graph);
        var output = graph.Nodes.Find(n => n.Id == graph.Output);
        if (output == null || graph.Nodes.Count > NodeLimit - 2) return null;
        var scale = new BehaviorNode { Id = Guid.NewGuid().ToString("N"), Name = "Szorzó · ezt az értéket állítsd", Operation = BehaviorOperation.Constant, Value = 1, X = output.X, Y = output.Y + 140 };
        var product = new BehaviorNode { Id = Guid.NewGuid().ToString("N"), Name = "Szorzott eredmény", Operation = BehaviorOperation.Multiply, A = output.Id, B = scale.Id, X = output.X + 250, Y = output.Y + 60 };
        owner.Remember(); graph.Nodes.Add(scale); graph.Nodes.Add(product); graph.Output = product.Id; return scale;
    }

    private void Require(BehaviorGraph graph)
    {
        if (!owner.Draft.Graphs.Contains(graph)) throw new InvalidOperationException("A gráf már nem az aktuális munkapéldány része.");
    }
    private void Require(BehaviorGraph graph, BehaviorNode node)
    {
        Require(graph);
        if (!graph.Nodes.Contains(node)) throw new InvalidOperationException("A csomópont már nem az aktuális gráf része.");
    }
}
