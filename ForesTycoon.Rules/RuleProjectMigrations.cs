using System;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>One-time conversion of the old wildlife query overrides into editable object state equations.</summary>
public static class RuleProjectMigrations
{
    public static void UpgradeProject(RuleProject project)
    {
        UpgradeObjectSimulation(project);
        if (project.Version >= 4) return;
        var defaults = RuleProject.CreateDefault();
        foreach (var graph in defaults.Behaviors.Graphs.Where(g => g.Hook.StartsWith("control.") || g.Hook == "supply.active" ||
            g.Hook is "wildlife.walkable" or "wildlife.neighbour" or "wildlife.habitatRank" or "wildlife.habitatCapacity" or "mission.state"))
            if (!project.Behaviors.Graphs.Any(g => g.Hook == graph.Hook && g.Kind == graph.Kind && !g.ObjectId.HasValue)) project.Behaviors.Graphs.Add(graph);
        foreach (var rule in defaults.Rules.Where(r => r.Schedule == "population" || r.Id == "wildlife.initialize"))
            if (!project.Rules.Any(r => r.Id == rule.Id)) {
                if (rule.Id == "wildlife.initialize") project.Rules.Insert(0, rule);
                else project.Rules.Add(rule);
            }
        var body = project.Rules.FirstOrDefault(r => r.Id == "wildlife.body");
        if (body != null) foreach (var effect in defaults.Rules.Single(r => r.Id == "wildlife.body").Effects
            .Where(e => e.Target is "noiseTimeScale" or "noiseTileScale" or "rainScale"))
            if (!body.Effects.Any(e => e.Target == effect.Target)) body.Effects.Add(effect);
        project.Version = 4;
    }
    public static readonly string[] RetiredWildlifeQueries = { "wildlife.forageRecovery", "wildlife.hungerRate", "wildlife.eatingRate", "wildlife.depletionRate", "wildlife.wanderRate", "wildlife.walkSpeed", "wildlife.wanderRelief" };
    public static void UpgradeObjectSimulation(RuleProject project)
    {
        if (project.Version >= 3) return;
        foreach (var graph in RuleProject.CreateDefault().Behaviors.Graphs.Where(g => g.Hook is "machine.workflow" or "truck.workflow"))
            if (!project.Behaviors.Graphs.Any(g => g.Hook == graph.Hook && g.Kind == graph.Kind && !g.ObjectId.HasValue)) project.Behaviors.Graphs.Add(graph);
        foreach (var rule in RuleProject.CreateDefault().Rules.Where(r => r.Schedule == "wildlife"))
            if (!project.Rules.Any(r => r.Id == rule.Id)) project.Rules.Add(rule);
        Move("wildlife.forageRecovery", "wildlife.forageRecovery", "depletion", -1);
        Move("wildlife.hungerRate", "wildlife.ageAndHunger", "hunger", 1);
        Move("wildlife.eatingRate", "wildlife.grazing", "hunger", -1);
        Move("wildlife.depletionRate", "wildlife.grazing", "tile.forageDepletion", 1);
        Move("wildlife.wanderRate", "wildlife.grazing", "wanderNeed", 1);
        Move("wildlife.walkSpeed", "wildlife.body", "movementSpeed", 0);
        Move("wildlife.wanderRelief", "wildlife.wanderRelief", "wanderNeed", -2);
        project.Behaviors.Graphs.RemoveAll(g => g.Kind == "gameplay" && RetiredWildlifeQueries.Contains(g.Hook));
        project.Version = 3;

        void Move(string query, string ruleId, string target, int scale)
        {
            var old = project.Behaviors.Graphs.SingleOrDefault(g => g.Kind == "gameplay" && g.Hook == query && !g.ObjectId.HasValue);
            if (old == null) return;
            var graph = new BehaviorModel { Graphs = new() { old } }.Clone().Graphs[0];
            if (graph.Disabled) {
                graph.Nodes = new() { new() { Id = "fixed", Name = graph.Name, Operation = BehaviorOperation.Constant, Value = graph.DisabledValue ?? 0 } };
                graph.Output = "fixed"; graph.Disabled = false; graph.DisabledValue = null;
            }
            foreach (var node in graph.Nodes.Where(n => n.Operation == BehaviorOperation.Input))
                node.Key = node.Key switch { "food.amount" => "food", "animal.hunger" => "hunger", "body.walkSpeed" => "bodyWalkingSpeed", _ => node.Key };
            string NewId(string id) { while (graph.Nodes.Any(n => n.Id == id)) id += "_"; return id; }
            if (scale != 0) {
                string clock = NewId("migration.clock");
                graph.Nodes.Add(new() { Id = clock, Name = scale == -2 ? "Megtett út" : "Lépés hossza", Operation = scale == -2 ? BehaviorOperation.Input : BehaviorOperation.Delta, Key = scale == -2 ? "distanceMoved" : "" });
                string product = NewId("migration.change");
                graph.Nodes.Add(new() { Id = product, Name = "Állapotváltozás", Operation = BehaviorOperation.Multiply, A = graph.Output, B = clock }); graph.Output = product;
                if (scale < 0) {
                    string negative = NewId("migration.decrease");
                    graph.Nodes.Add(new() { Id = negative, Name = "Csökkenés", Operation = BehaviorOperation.Negate, A = graph.Output }); graph.Output = negative;
                }
            }
            var effect = project.Rules.Single(r => r.Id == ruleId).Effects.Single(e => e.Target == target);
            effect.Formula = ""; effect.Influence = null; effect.Computation = graph;
        }
    }
}
