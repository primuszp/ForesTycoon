using System;
using System.IO;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor;

/// <summary>Reopens repairable editor drafts using the same version migrations as the game.</summary>
internal static class EditorProjectLoader
{
    internal static RuleProject Prepare(RuleProject project)
    {
        if (project == null || project.Schema != "forest-rule-project" || project.Version < 1 || project.Version > RuleProject.CurrentVersion || project.Name == null
            || project.Tuning == null
            || project.Behaviors?.Graphs == null || project.Behaviors.Graphs.Count > 512 || project.Behaviors.Controllers == null
            || project.Dynamics?.Stocks == null || project.Dynamics.Stocks.Count > 256 || project.Dynamics.Flows == null || project.Dynamics.Flows.Count > 512
            || project.Authoring != null && (project.Authoring.Entries == null || project.Authoring.Templates == null || project.Authoring.Entries.Count > 2048 || project.Authoring.Templates.Count > 1024
                || project.Authoring.Entries.Any(e => e.Value == null || e.Value.Notes == null) || project.Authoring.Templates.Any(t => t == null || t.Name == null || t.Id == null || t.Graph == null && t.Controller == null)))
            throw new InvalidDataException("Hiányos vagy túl nagy szerkesztési projekt.");
        static bool InvalidNodes(BehaviorGraph graph) => graph.Nodes == null || graph.Nodes.Count > 256
            || graph.Nodes.Any(n => n == null || n.Id == null || n.Name == null || !Enum.IsDefined(n.Operation) || !float.IsFinite(n.X) || !float.IsFinite(n.Y));
        if (project.Behaviors.Graphs.Any(g => g == null || g.Id == null || g.Name == null || InvalidNodes(g))
            || project.Dynamics.Stocks.Any(s => s == null || s.Id == null || s.Name == null || s.Unit == null)
            || project.Dynamics.Flows.Any(f => f == null || f.Id == null || f.Name == null || f.From == null || f.To == null
                || f.Rate == null || InvalidNodes(f.Rate) || f.Rate.Hook != "flow." + f.Id || f.Rate.Kind != "dynamics")
            || project.Behaviors.Controllers.Any(c => c == null || c.Name == null || c.States == null || c.Kind is not ("forwarder" or "processor" or "truck")
                || c.States.Any(s => s == null || s.Name == null || !Enum.IsDefined(s.Action) || s.Transitions == null || !float.IsFinite(s.X) || !float.IsFinite(s.Y))))
            throw new InvalidDataException("Hibás szerkesztési objektum vagy nem támogatott kötés.");
        // Migrate a copy before checking current hooks; reopening a draft must not compile it.
        project = project.Clone();
        project.Rules ??= new(); project.Fields ??= new(); project.RemovedStarters ??= new();
        if (project.Version == 1) RuleEngine.AddMissingStarters(project.Rules, project.RemovedStarters);
        RuleProjectMigrations.UpgradeProject(project);
        foreach (var graph in project.Behaviors.Graphs)
            if (!WorldBehaviorPolicy.Hooks.Any(h => h.Id == graph.Hook && h.Kind == graph.Kind))
                throw new InvalidDataException($"Nem támogatott kötés: {graph.Hook} ({graph.Kind}). Gráf: {graph.Name} [{graph.Id}].");
        // Missing ports, expression cycles and incomplete outputs can be reopened and repaired.
        // The runtime compiler still rejects them at publish/apply time.
        return project;
    }
}
