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
