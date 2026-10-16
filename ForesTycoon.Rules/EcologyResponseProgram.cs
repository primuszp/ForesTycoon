using System;
using System.IO;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>Pure, bounded response graphs, shared by generation, surveys and the live simulation.</summary>
public sealed class EcologyResponseProgram
{
    public static readonly BehaviorHook[] Hooks = {
        new("forest.site", "Termőhelyi alkalmasság", "site", "0–1", 0, 1, "", "", "Ültetés, fajajánló és fa-erőforrás frissítése", false,
            new[] { "tile.moisture", "tile.elevation", "tile.fertility", "species.preferredElevation", "species.elevationTolerance" }),
        new("water.stress", "Fafaj vízválasza", "site", "0–1", 0, 1, "", "", "Pillanatnyi és havi fa-erőforrás frissítése", false,
            new[] { "tile.waterSupply", "tile.waterlogging", "species.drySensitivity" }),
        new("water.drought", "Aszály követése", "site", "0–1", 0, 1, "", "", "Szeletelt környezeti lépés, csempénként", false,
            new[] { "tile.waterSupply", "tile.drought", "step.seconds" }),
        new("water.waterlogging", "Pangóvíz követése", "site", "0–1", 0, 1, "", "", "Szeletelt környezeti lépés, csempénként", false,
            new[] { "tile.rawWaterlogging", "tile.waterlogging", "step.seconds" })
    };
    public static bool AllowedOperation(BehaviorOperation op) => op is BehaviorOperation.Constant or BehaviorOperation.Input
        or BehaviorOperation.Add or BehaviorOperation.Subtract or BehaviorOperation.Multiply or BehaviorOperation.Divide
        or BehaviorOperation.Minimum or BehaviorOperation.Maximum or BehaviorOperation.Greater or BehaviorOperation.Less
        or BehaviorOperation.Select or BehaviorOperation.Absolute or BehaviorOperation.Negate or BehaviorOperation.Exp or BehaviorOperation.Power;
    public static BehaviorGraph DefaultGraph(BehaviorHook hook)
    {
        string expression = hook.Id switch {
            "forest.site" => "tile.fertility * (0.45 + 0.55 * korlát(1 - abs(tile.elevation - species.preferredElevation) / max(species.elevationTolerance; 0.000001); 0; 1))",
            "water.stress" => "hatvány(korlát(tile.waterSupply; 0; 1); species.drySensitivity) * (1 - tile.waterlogging * 0.65)",
            "water.drought" => "tile.drought + (1 - tile.waterSupply - tile.drought) * (1 - exp(-step.seconds / 90))",
            "water.waterlogging" => "tile.waterlogging + (tile.rawWaterlogging - tile.waterlogging) * (1 - exp(-step.seconds / 90))",
            _ => throw new ArgumentException("Unknown ecology response.", nameof(hook))
        };
        return RuleFormula.Compile(expression, new() { Id = "default.site." + hook.Id, Name = hook.Name, Hook = hook.Id, Kind = hook.Kind },
            hook.Inputs.Select(f => new FormulaName(f, BehaviorOperation.Input, f)).ToArray());
    }
    [ThreadStatic] private static EcologyResponseProgram defaults;
    public static EcologyResponseProgram Default => defaults ??= new(new() { Graphs = Hooks.Select(DefaultGraph).ToList() });
    /// <summary>Compiles only the pure query graphs; unrelated world/stock bindings need no world during generation.</summary>
    public static EcologyResponseProgram FromModel(BehaviorModel source)
    {
        var model = new BehaviorModel { Graphs = source.Graphs.Where(g => Hooks.Any(h => h.Id == g.Hook && h.Kind == g.Kind)).ToList() };
        foreach (var hook in Hooks)
            if (!model.Graphs.Any(g => g.Hook == hook.Id && g.Kind == hook.Kind)) model.Graphs.Add(DefaultGraph(hook));
        return new(model);
    }
    private readonly Func<BehaviorInputs, double[], double>[] evaluate;
    private readonly double[][] inputs = Hooks.Select(h => new double[h.Inputs.Length]).ToArray();
    public string LastError { get; private set; }
    public EcologyResponseProgram(BehaviorModel source)
    {
        var graphs = source.Graphs.Where(g => Hooks.Any(h => h.Id == g.Hook && h.Kind == g.Kind)).ToList();
        if (graphs.Count != Hooks.Length) throw new InvalidDataException("Mind a négy termőhelyi és vízválaszgráf szükséges. A hatás kikapcsolásához köss állandót a kimenetre.");
        foreach (var g in graphs)
            if (g.Nodes.Any(n => !AllowedOperation(n.Operation)))
                throw new InvalidDataException("A termőhelyi gráf csak a felkínált csempe-, fafaj- és lépésmezőket olvashatja; nincs beépített számítás vagy másik világállapot.");
        var program = new CompiledBehaviorModel(new() { Graphs = graphs }, Hooks);
        evaluate = Hooks.Select(h => program.Evaluator(h.Id, h.Kind) ?? throw new InvalidDataException("Hiányzó termőhelyi gráf: " + h.Id)).ToArray();
    }
    private double Evaluate(int index)
    {
        try { return evaluate[index](default, inputs[index]); }
        catch (ArithmeticException error) {
            LastError = Hooks[index].Name + ": " + error.Message + " A hibás válasz 0; javítsd a kijelzett gráfot.";
            return 0;
        }
    }
    public double Fitness(double moisture, double elevation, double fertility, double preferredElevation, double elevationTolerance)
    {
        var v = inputs[0]; v[0] = moisture; v[1] = elevation; v[2] = fertility; v[3] = preferredElevation; v[4] = elevationTolerance;
        return Evaluate(0);
    }
    public double WaterFactor(double supply, double waterlogging, double sensitivity)
    {
        var v = inputs[1]; v[0] = supply; v[1] = waterlogging; v[2] = sensitivity; return Evaluate(1);
    }
    public double Drought(double supply, double previous, double seconds)
    {
        var v = inputs[2]; v[0] = supply; v[1] = previous; v[2] = seconds; return Evaluate(2);
    }
    public double Waterlogging(double raw, double previous, double seconds)
    {
        var v = inputs[3]; v[0] = raw; v[1] = previous; v[2] = seconds; return Evaluate(3);
    }
}
