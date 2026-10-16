using System;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>All new trees get their physical dimensions from these authored response graphs.</summary>
public static class TreeInitialSizeRules
{
    public static readonly string[] Inputs = { "tree.age", "tree.random", "tree.scale", "tree.planted", "species.matureAge",
        "species.matureHeight", "species.matureDiameter", "species.crownRatio", "initial.height", "initial.diameter" };
    public static readonly BehaviorHook[] Hooks = {
        Hook("forest.initialHeight", "Fák kezdő magassága", 1000, Inputs.Take(8).ToArray()),
        Hook("forest.initialDiameter", "Fák kezdő törzsátmérője", 100, Inputs.Take(9).ToArray()),
        Hook("forest.initialCrown", "Fák kezdő koronasugara", 1000, Inputs)
    };
    private static BehaviorHook Hook(string id, string name, double max, string[] inputs) => new(id, name, "initialTree", "m", 0, max, "", "",
        "Minden új fa létrehozásakor: generálás, erdőfestés, ültetés és természetes újulat. A korona már a magasság- és átmérőgráf eredményét olvashatja. A meglévő fák méretét ez nem írja át.", false, inputs);
    public static BehaviorGraph DefaultGraph(BehaviorHook hook)
    {
        const string growth = "(1 - exp(-max(0; tree.age) / max(0.000001; species.matureAge) * 1.8))";
        const string variation = "(0.88 + tree.random * 0.24) * tree.scale";
        string expression = hook.Id switch {
            "forest.initialHeight" => $"(min(0.8; 0.15 * species.matureHeight) + (species.matureHeight - min(0.8; 0.15 * species.matureHeight)) * {growth}) * {variation}",
            "forest.initialDiameter" => $"(min(0.012; 0.1 * species.matureDiameter) + species.matureDiameter * {growth}) * {variation}",
            "forest.initialCrown" => "max(0.15; initial.height * species.crownRatio)",
            _ => throw new ArgumentException("Unknown initial size graph.", nameof(hook))
        };
        var graph = RuleFormula.Compile(expression, new() { Id = "default.initialTree." + hook.Id, Name = hook.Name, Hook = hook.Id, Kind = hook.Kind },
            hook.Inputs.Select(f => new FormulaName(f, BehaviorOperation.Input, f)).ToArray());
        graph.Nodes.Single(n => n.Id == graph.Output).Name = hook.Name;
        return graph;
    }
}
