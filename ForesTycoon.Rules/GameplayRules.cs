using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>Authorable decisions. The host measures inputs, applies the result and keeps stocks conserved.</summary>
public static class GameplayRules
{
    public static readonly IReadOnlyDictionary<string, string[]> CatalogHooks = new Dictionary<string, string[]> {
        ["forest.initial"] = new[] { "forest.initialAllow", "forest.initialSpecies", "forest.initialAge", "forest.initialHealth", "forest.initialBiomass" },
        ["forest.plant"] = new[] { "forest.plantAllow", "forest.plantHealth" },
        ["forest.designate"] = new[] { "forest.designateAllow" }, ["forest.fell"] = new[] { "forest.fellAllow" },
        ["road.build"] = new[] { "economy.invest", "road.buildCost" }, ["road.repair"] = new[] { "economy.invest", "road.repairCost" },
        ["trail.build"] = new[] { "economy.invest", "trail.buildCost" }, ["timber.deliver"] = new[] { "economy.sale" },
        ["mill.place"] = new[] { "mill.placeAllow" }, ["economy.fuel"] = new[] { "economy.fuelCost" },
        ["economy.balance"] = new[] { "economy.balance", "economy.invest" },
        ["wildlife.habitat"] = new[] { "wildlife.habitat", "wildlife.population" },
        ["wildlife.forage"] = new[] { "wildlife.food" },
        ["wildlife.movement"] = new[] { "wildlife.stayScore", "wildlife.destinationScore" }
    };
    private static BehaviorHook H(string id, string name, string unit, double min, double max, params string[] inputs) =>
        new(id, name, "gameplay", unit, min, max, "", "", "A művelet végrehajtásakor, a mért bemenetekből", false, inputs);

    public static readonly BehaviorHook[] Hooks = {
        H("forest.initialAllow", "Kezdő erdő eloszlása", "0–1", 0, 1, "pattern.type", "pattern.distance", "noise.value", "tile.moisture", "tile.elevation", "noise.bucket3", "noise.bucket5"),
        H("forest.initialSpecies", "Kezdő erdő fajválasztása", "fajazonosító", 2, 17, "pattern.type", "pattern.first", "noise.value", "tile.moisture", "tile.elevation", "noise.bucket8", "noise.bucket10"),
        H("forest.initialAge", "Kezdő erdő kora", "év", 0, 1e4, "pattern.type", "species.matureAge", "noise.value"),
        H("forest.initialHealth", "Kezdő erdő egészsége", "0–1", 0, 1, "pattern.type", "noise.value", "tile.suitability"),
        H("forest.initialBiomass", "Kezdő erdő fatömege", "készlet", 0, 1e9, "species.maxBiomass", "forest.age", "species.matureAge", "forest.health"),
        H("economy.invest", "Beruházás engedélyezése", "0–1", 0, 1, "economy.balance", "economy.credit"),
        H("economy.balance", "Gazdasági eredmény", "eFt", -1e12, 1e12, "economy.income", "economy.expenses", "economy.runningCosts"),
        H("mission.reached", "Küldetés teljesülésének feltétele", "0–1", 0, 1, "goal.value", "goal.target", "goal.atMost", "world.year", "goal.deadline"),
        H("mission.failed", "Küldetés lejáratának feltétele", "0–1", 0, 1, "goal.value", "goal.target", "goal.atMost", "world.year", "goal.deadline"),
        H("mission.state", "Küldetés állapota és döntési elsőbbsége", "állapot", 0, 2, "decision.reached", "decision.failed", "goal.value", "goal.target", "goal.atMost", "world.year", "goal.deadline"),
        H("economy.sale", "Faanyag eladási bevétele", "eFt", 0, 1e12, "cargo.value", "market.factor", "cargo.volume"),
        H("economy.fuelCost", "Üzemanyag költsége", "eFt", 0, 1e12, "fuel.litres", "fuel.price"),
        H("road.buildCost", "Út építési költsége", "eFt", 0, 1e12, "tiles.count", "surface.price", "road.surface"),
        H("road.repairCost", "Út javítási költsége", "eFt", 0, 1e12, "surface.price", "road.damage", "repair.factor", "road.surface"),
        H("trail.buildCost", "Nyom építési költsége", "eFt", 0, 1e12, "tiles.count", "surface.price"),
        H("forest.plantAllow", "Telepítés engedélyezése", "0–1", 0, 1, "tile.suitability", "species.id"),
        H("forest.plantHealth", "Telepítés kezdő egészsége", "0–1", 0, 1, "tile.suitability", "species.id"),
        H("forest.designateAllow", "Kitermelés kijelölésének engedélye", "0–1", 0, 1, "forest.volume", "forest.age", "forest.maturity"),
        H("forest.fellAllow", "Fakivágás engedélyezése", "0–1", 0, 1, "tree.age", "tree.height", "tree.diameter", "species.id"),
        H("mill.placeAllow", "Fűrészmalom építésének engedélye", "0–1", 0, 1, "mill.count"),
        H("wildlife.habitat", "Vadak élőhelyének engedélye", "0–1", 0, 1, "tile.nearRoad", "forest.empty", "forest.neighbours", "forest.maturity"),
        H("wildlife.population", "Vadak legnagyobb kezdő egyedszáma", "db", 0, 1024),
        H("wildlife.habitatCapacity", "Élőhelyfelmérés mintakerete", "hely", 0, 1024),
        H("wildlife.walkable", "Állat számára engedélyezett felszín", "0–1", 0, 1, "tile.road", "tile.water", "tile.river", "tile.forestSupport", "tile.slope"),
        H("wildlife.neighbour", "Szomszédos erdő élőhelyértéke", "0–1", 0, 1, "forest.empty", "forest.maturity"),
        H("wildlife.habitatRank", "Élőhelyek rangsora", "rang", 0, uint.MaxValue, "noise.rank", "forest.empty", "forest.maturity"),
        H("wildlife.food", "Legelő táplálékkínálata", "0–1", 0, 1, "tile.depletion", "tile.moisture"),
        H("wildlife.stayScore", "Helyben maradás értéke", "pont", -1e6, 1e6, "food.amount", "animal.hunger", "animal.wander"),
        H("wildlife.destinationScore", "Szomszédos élőhely választása", "pont", -1e6, 1e6, "food.amount", "animal.hunger", "weather.rain", "forest.maturity", "noise.value", "direction.alignment"),
    };

    public static ResponseRuleDescription Description(string id)
    {
        var hook = Hooks.Single(h => h.Id == id);
        return new(hook.Name, "A kapcsolt játékművelet és az eredményét olvasó szabályok.", Math.Clamp(0, hook.Min, hook.Max),
            "Minden következő értékeléskor a választott állandó eredményt adja. A korábbi állapotváltozásokat megőrzi.",
            id == "wildlife.food" ? new[] { "gameplay:food.amount" } : Array.Empty<string>());
    }
}

/// <summary>No native-result input or fallback. An omitted decision is inactive (its bounded zero).</summary>
public sealed class GameplayProgram
{
    [ThreadStatic] private static GameplayProgram defaults;
    public static GameplayProgram Default => defaults ??= new(RuleProject.CreateDefault().Behaviors);
    private readonly Dictionary<string, (BehaviorHook Hook, Func<BehaviorInputs, double[], double> Evaluate)> programs;
    public string LastError { get; private set; }

    public GameplayProgram(BehaviorModel source, CompiledBehaviorModel shared = null, bool prepareExecutables = true)
    {
        var model = new BehaviorModel { Graphs = source.Graphs.Where(g => g.Kind == "gameplay").ToList() };
        if (model.Graphs.Any(g => g.Nodes.Any(n => !EcologyResponseProgram.AllowedOperation(n.Operation))))
            throw new InvalidDataException("A játékszabály csak mért bemeneteket és gráfműveleteket használhat.");
        var compiled = shared ?? new CompiledBehaviorModel(model, GameplayRules.Hooks);
        if (prepareExecutables) compiled.Prepare();
        programs = GameplayRules.Hooks.ToDictionary(h => h.Id, h => (h, compiled.Evaluator(h.Id, h.Kind)), StringComparer.Ordinal);
    }

    public double Evaluate(string id, params double[] inputs)
    {
        var (hook, evaluate) = programs[id];
        if (inputs.Length != hook.Inputs.Length) throw new ArgumentException("Hibás játékszabály-bemenetek: " + id);
        if (evaluate == null) return Math.Clamp(0, hook.Min, hook.Max);
        try { return evaluate(default, inputs); }
        catch (ArithmeticException error) { LastError = hook.Name + ": " + error.Message; return Math.Clamp(0, hook.Min, hook.Max); }
    }
}
