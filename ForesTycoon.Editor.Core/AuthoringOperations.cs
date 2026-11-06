#nullable enable
using System;
using System.IO;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor.Core;

internal static class AuthoringOperations
{
    internal const int GraphNodeLimit = 256;
    internal const int InfluenceNodeCount = 10;
    internal static RuleTemplate Template(BehaviorGraph graph) => new() { Id = Guid.NewGuid().ToString("N"), Name = graph.Name,
        Graph = new BehaviorModel { Graphs = new() { graph } }.Clone().Graphs[0] };
    internal static RuleTemplate Template(BehaviorController controller) => new() { Id = Guid.NewGuid().ToString("N"), Name = controller.Name,
        Controller = new BehaviorModel { Controllers = new() { controller } }.Clone().Controllers[0] };
    internal static void Instantiate(BehaviorModel model, RuleTemplate template, ulong? objectId)
        => InstallInstance(model, PrepareInstance(template, objectId));

    internal static RuleTemplate PrepareInstance(RuleTemplate template, ulong? objectId)
    {
        if (template.Graph is { } graph) {
            var copy = new BehaviorModel { Graphs = new() { graph } }.Clone().Graphs[0];
            copy.Id = Guid.NewGuid().ToString("N"); copy.ObjectId = objectId; copy.Name = template.Name;
            return new() { Name = template.Name, Graph = copy };
        } else if (template.Controller is { } controller) {
            var copy = new BehaviorModel { Controllers = new() { controller } }.Clone().Controllers[0];
            copy.Id = Guid.NewGuid().ToString("N"); copy.ObjectId = objectId; copy.Name = template.Name;
            return new() { Name = template.Name, Controller = copy };
        } else throw new InvalidDataException("A sablonhoz nincs gráf.");
    }

    internal static void InstallInstance(BehaviorModel model, RuleTemplate instance)
    {
        if (instance.Graph is { } graph) {
            model.Graphs.RemoveAll(g => g.Hook == graph.Hook && g.Kind == graph.Kind && g.ObjectId == graph.ObjectId);
            model.Graphs.Add(graph);
        } else if (instance.Controller is { } controller) {
            model.Controllers.RemoveAll(c => c.Kind == controller.Kind && c.ObjectId == controller.ObjectId);
            model.Controllers.Add(controller);
        } else throw new InvalidDataException("A sablonhoz nincs gráf.");
    }
    internal static string[] StockConsumers(RuleProject document, string stock) =>
        document.Behaviors.Graphs.Where(g => g.Nodes.Any(n => n.Operation == BehaviorOperation.NamedValue && n.Key == "stock." + stock)).Select(g => g.Name)
            .Concat(document.Dynamics.Flows.Where(f => f.From == stock || f.To == stock || f.Rate.Nodes.Any(n => n.Operation == BehaviorOperation.NamedValue && n.Key == "stock." + stock)).Select(f => f.Name))
            .Distinct().ToArray();
    /// <summary>
    /// Makes a quantity influence a graph's result: result × max(0, 1 + strength × (quantity − reference)).
    /// The constants stay editable nodes. Returns the strength node's ID.
    /// </summary>
    internal static string AddInfluence(BehaviorGraph graph, string key, string name, double reference, double strength)
    {
        var output = graph.Nodes.Find(n => n.Id == graph.Output) ?? throw new InvalidOperationException("A gráfnak nincs kimenete.");
        if (graph.Nodes.Count > GraphNodeLimit - InfluenceNodeCount) throw new InvalidOperationException("A gráf megtelt.");
        float x = output.X + 250, y = output.Y + 120;
        BehaviorNode Add(string label, BehaviorOperation op, double value = 0, string a = "", string b = "", string k = "", float dx = 0, float dy = 0)
        {
            var n = new BehaviorNode { Id = Guid.NewGuid().ToString("N"), Name = label, Operation = op, Value = value, A = a, B = b, Key = k, X = x + dx, Y = y + dy };
            graph.Nodes.Add(n); return n;
        }
        var quantity = Add(name, BehaviorOperation.NamedValue, k: key, dx: -250, dy: 120);
        var baseline = Add(name + " · viszonyítás", BehaviorOperation.Constant, reference, dx: -250, dy: 220);
        var difference = Add("Eltérés", BehaviorOperation.Subtract, a: quantity.Id, b: baseline.Id, dy: 160);
        var gain = Add("Erősség", BehaviorOperation.Constant, strength, dy: 260);
        var effect = Add("Hatás", BehaviorOperation.Multiply, a: difference.Id, b: gain.Id, dx: 230, dy: 200);
        var one = Add("Egy", BehaviorOperation.Constant, 1, dx: 230, dy: 300);
        var factor = Add("Szorzó", BehaviorOperation.Add, a: one.Id, b: effect.Id, dx: 460, dy: 240);
        var zero = Add("Nulla", BehaviorOperation.Constant, 0, dx: 460, dy: 340);
        var bounded = Add("Nem negatív szorzó", BehaviorOperation.Maximum, a: factor.Id, b: zero.Id, dx: 690, dy: 280);
        var result = Add(name + " hatásával", BehaviorOperation.Multiply, a: output.Id, b: bounded.Id, dx: 920, dy: 0);
        graph.Output = result.Id;
        return gain.Id;
    }
}
