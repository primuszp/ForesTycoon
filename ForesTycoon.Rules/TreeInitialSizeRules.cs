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
}
