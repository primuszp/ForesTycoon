#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor;

internal static class AuthoringOperations
{
    internal static RuleTemplate Template(BehaviorGraph graph) => new() { Id = Guid.NewGuid().ToString("N"), Name = graph.Name,
        Graph = new BehaviorModel { Graphs = new() { graph } }.Clone().Graphs[0] };
    internal static RuleTemplate Template(BehaviorController controller) => new() { Id = Guid.NewGuid().ToString("N"), Name = controller.Name,
        Controller = new BehaviorModel { Controllers = new() { controller } }.Clone().Controllers[0] };
    internal static void Instantiate(BehaviorModel model, RuleTemplate template, ulong? objectId)
    {
        if (template.Graph is { } graph) {
            var copy = new BehaviorModel { Graphs = new() { graph } }.Clone().Graphs[0];
            copy.Id = Guid.NewGuid().ToString("N"); copy.ObjectId = objectId; copy.Name = template.Name;
            model.Graphs.RemoveAll(g => g.Hook == copy.Hook && g.Kind == copy.Kind && g.ObjectId == objectId); model.Graphs.Add(copy);
        } else if (template.Controller is { } controller) {
            var copy = new BehaviorModel { Controllers = new() { controller } }.Clone().Controllers[0];
            copy.Id = Guid.NewGuid().ToString("N"); copy.ObjectId = objectId; copy.Name = template.Name;
            model.Controllers.RemoveAll(c => c.Kind == copy.Kind && c.ObjectId == objectId); model.Controllers.Add(copy);
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
        if (graph.Nodes.Count > 246) throw new InvalidOperationException("A gráf megtelt.");
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

/// <summary>Detect external human/agent edits before overwriting the shared project or draft.</summary>
internal sealed class ProjectFileGuard
{
    private string? path, revision;
    internal static string? Revision(string file) => File.Exists(file) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) : null;
    internal void Observe(string file) { path = Path.GetFullPath(file); revision = Revision(path); }
    internal bool Changed(string file) => path != Path.GetFullPath(file) || Revision(file) != revision;
    internal void RequireUnchanged(string file) { if (Changed(file)) throw new IOException("A fájlt közben másik editor vagy AI módosította. Nyisd meg az új változatot, vagy ments másik projektútvonalra. Nem írtam felül: " + file); }
}

internal static class ProjectHistory
{
    internal static void Archive(string file)
    {
        if (!File.Exists(file)) return;
        string json = File.ReadAllText(file); string hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
        string saved = Path.Combine(Path.GetFullPath(file) + ".history", hash + ".json");
        if (!File.Exists(saved)) RuleProject.WriteAtomic(saved, json);
    }
}
