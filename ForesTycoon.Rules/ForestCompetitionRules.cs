using System;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>Project-owned pair contributions and resource responses. Geometry and ordered reduction belong to the host.</summary>
public static class ForestCompetitionRules
{
    public static readonly string[] PairInputs = { "tree.height", "tree.crownRadius", "tree.diameter", "tree.health", "tree.shadeTolerance",
        "neighbor.height", "neighbor.crownRadius", "neighbor.diameter", "neighbor.health", "neighbor.shadeTolerance", "pair.distance", "pair.distanceSquared" };
    public static readonly string[] LoadInputs = { "competition.shade", "competition.crownLoad", "environment.radiation", "resources.water" };
    public static readonly string[] ResourceInputs = { "resources.light", "resources.water", "resources.space", "species.shadeTolerance", "resources.lightResponse" };
    public static readonly BehaviorHook[] Hooks = {
        new("forest.shading", "Szomszéd árnyékoló hatása", "competitionPair", "terhelés", 0, 1e6, "", "", "Szeletelten, minden különböző fapárra a két szomszédsági gyűrűben", false, PairInputs),
        new("forest.crowding", "Szomszéd térfoglaló hatása", "competitionPair", "terhelés", 0, 1e6, "", "", "Szeletelten, minden különböző fapárra a két szomszédsági gyűrűben", false, PairInputs),
        new("forest.light", "A fához jutó fény", "competition", "0–1", 0, 1, "", "", "A szomszédok gráfos hatásainak sorrendtartó összegzése után", false, LoadInputs),
        new("forest.space", "A fa szabad növekedési tere", "competition", "0–1", 0, 1, "", "", "A szomszédok gráfos hatásainak sorrendtartó összegzése után", false, LoadInputs),
        new("forest.lightResponse", "Fafaj fényválasza", "resource", "0–1", 0, 1, "", "", "Növekedés, stressz és erdődiagnózis", false, ResourceInputs.Take(4).ToArray()),
        new("forest.resourceLimit", "A fa erőforráskorlátja", "resource", "0–1", 0, 1, "", "", "Egészség, stressz és erdődiagnózis", false, ResourceInputs)
    };
    public static BehaviorGraph DefaultGraph(BehaviorHook hook)
    {
        const string reach = "(tree.crownRadius + neighbor.crownRadius)";
        string overlap = $"hatvány(max(0; 1 - pair.distanceSquared / max(0.01; {reach} * {reach})); 2)";
        string expression = hook.Id switch {
            "forest.shading" => overlap + " * 2 * korlát(0.5 + (neighbor.height - tree.height) / max(0.8; tree.height) * 2; 0; 1) * neighbor.health",
            "forest.crowding" => overlap + " * korlát(neighbor.crownRadius * neighbor.crownRadius / max(0.0225; tree.crownRadius * tree.crownRadius); 0.001; 4)",
            "forest.light" => "korlát(environment.radiation; 0; 1) * exp(-0.65 * competition.shade)",
            "forest.space" => "1 / (1 + competition.crownLoad * 0.55)",
            "forest.lightResponse" => "hatvány(korlát(resources.light; 0; 1); 1 - species.shadeTolerance * 0.65)",
            "forest.resourceLimit" => "min(resources.space; min(resources.water; resources.lightResponse))",
            _ => throw new ArgumentException("Unknown forest competition response.", nameof(hook))
        };
        // The range decision is part of the authored graph, including its lazy branch.
        // The host must never infer a distance limit from a different/custom graph.
        if (hook.Kind == "competitionPair")
            expression = $"ha(pair.distanceSquared < max(0.01; {reach} * {reach}); {expression}; 0)";
        var graph = RuleFormula.Compile(expression, new() { Id = "default." + hook.Kind + "." + hook.Id, Name = hook.Name, Hook = hook.Id, Kind = hook.Kind },
            hook.Inputs.Select(f => new FormulaName(f, BehaviorOperation.Input, f)).ToArray());
        graph.Nodes.Single(n => n.Id == graph.Output).Name = hook.Name;
        if (hook.Kind == "competitionPair") {
            var output = graph.Nodes.Single(n => n.Id == graph.Output);
            graph.Nodes.Single(n => n.Id == output.A).Name = "A koronák hatótávján belül";
        }
        return graph;
    }
}
