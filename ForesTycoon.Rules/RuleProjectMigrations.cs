using System;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>One-time conversion of the old wildlife query overrides into editable object state equations.</summary>
public static class RuleProjectMigrations
{
    public static void UpgradeProject(RuleProject project)
    {
        UpgradeObjectSimulation(project);
        UpgradePopulationPolicies(project);
        if (project.Version < 5) {
            foreach (var graph in RuleProject.CreateDefault().Behaviors.Graphs.Where(g => g.Hook is "trail.drivingCondition" or "wildlife.timeScale"))
                if (!project.Behaviors.Graphs.Any(g => g.Hook == graph.Hook && g.Kind == graph.Kind && !g.ObjectId.HasValue)) project.Behaviors.Graphs.Add(graph);
            project.Version = 5;
        }
        if (project.Version >= 6) return;
        var defaults = RuleProject.CreateDefault();
        string[] keys = { "DirtBuild", "DirtWeather", "DirtRainWeather", "AsphaltBufferMetres", "MacadamBufferMetres", "DirtBufferMetres", "TrailBufferMetres" };
        foreach (string key in keys) {
            project.Tuning.TryAdd(key, defaults.Tuning[key]);
            foreach (var graph in defaults.Behaviors.Graphs.Where(g => g.Hook == "tuning." + key))
                if (!project.Behaviors.Graphs.Any(g => g.Hook == graph.Hook && g.Kind == graph.Kind && !g.ObjectId.HasValue)) project.Behaviors.Graphs.Add(graph);
        }
        foreach (string id in new[] { "road.weather", "traffic.wear" }) {
            var current = project.Rules.FirstOrDefault(r => r.Id == id);
            var target = current?.Effects.FirstOrDefault(e => e.Target == (id == "road.weather" ? "condition" : "road.condition"))?.Influence;
            if (target == null) continue;
            string surface = id == "road.weather" ? "surface" : "road.surface";
            if (target.Terms.Any(t => t.Conditions.Any(c => c.Field == surface && c.Value == 2))) continue;
            foreach (var term in defaults.Rules.Single(r => r.Id == id).Effects[0].Influence.Terms
                .Where(t => t.Conditions.Any(c => c.Field == surface && c.Value == 2))) target.Terms.Add(term);
        }
        project.Version = 6;
    }
    private static void UpgradePopulationPolicies(RuleProject project)
    {
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
    /// <summary>
    /// The retired separate road wear model ("road"). Its untouched form, traffic × surface × 1, is what the rule book's
    /// passage graph computes, so it is dropped; an edited one would change the game and is refused, never dropped silently.
    /// </summary>
    public static string RemoveRetiredRoadModel(string json)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;
        if (root == null || !root.TryGetPropertyValue("road", out var road)) return json;
        if (!IsNeutralRoadModel(road))
            throw new System.IO.InvalidDataException("A projekt megszűnt külön útkopási modellt tartalmaz (road), módosított értékekkel. A forgalmi kopást a közös szabálykönyv áthaladási gráfja szerkeszti; a módosítást ott kell újra megadni.");
        root.Remove("road");
        return root.ToJsonString();
    }
    private static bool IsNeutralRoadModel(System.Text.Json.Nodes.JsonNode road)
    {
        if (road?["nodes"] is not System.Text.Json.Nodes.JsonArray nodes || nodes.Count == 0) return false;
        foreach (var node in nodes) {
            string operation = node?["operation"]?.GetValue<string>();
            if (operation is "TrafficWear" or "SurfaceFactor" or "Multiply") continue;
            if (operation == "Constant" && node["value"]?.GetValue<double>() == 1) continue;
            return false;
        }
        return true;
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
