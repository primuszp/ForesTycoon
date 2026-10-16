using System;
using System.IO;
using System.Linq;

namespace ForesTycoon.Rules;

/// <summary>Pure, bounded response graphs, shared by generation, surveys and the live simulation.</summary>
public sealed class EcologyResponseProgram
{
    private static readonly BehaviorHook[] SiteHooks = {
        new("forest.site", "Termőhelyi alkalmasság", "site", "0–1", 0, 1, "", "", "Ültetés, fajajánló és fa-erőforrás frissítése", false,
            new[] { "tile.moisture", "tile.elevation", "tile.fertility", "species.preferredElevation", "species.elevationTolerance" }),
        new("water.stress", "Fafaj vízválasza", "site", "0–1", 0, 1, "", "", "Pillanatnyi és havi fa-erőforrás frissítése", false,
            new[] { "tile.waterSupply", "tile.waterlogging", "species.drySensitivity" }),
        new("water.drought", "Aszály követése", "site", "0–1", 0, 1, "", "", "Szeletelt környezeti lépés, csempénként", false,
            new[] { "tile.waterSupply", "tile.drought", "step.seconds" }),
        new("water.waterlogging", "Pangóvíz követése", "site", "0–1", 0, 1, "", "", "Szeletelt környezeti lépés, csempénként", false,
            new[] { "tile.rawWaterlogging", "tile.waterlogging", "step.seconds" })
    };
    public static readonly BehaviorHook[] Hooks = SiteHooks.Concat(ForestCompetitionRules.Hooks).Concat(ForestRegenerationRules.Hooks).Concat(TreeInitialSizeRules.Hooks).ToArray();
    private static readonly int InitialSizeIndex = Array.FindIndex(Hooks, h => h.Id == "forest.initialHeight" && h.Kind == "initialTree");
    public static bool IsPureKind(string kind) => kind is "site" or "competitionPair" or "competition" or "resource" or "regeneration" or "initialTree";
    public static bool AllowedOperation(BehaviorOperation op) => op is BehaviorOperation.Constant or BehaviorOperation.Input
        or BehaviorOperation.Add or BehaviorOperation.Subtract or BehaviorOperation.Multiply or BehaviorOperation.Divide
        or BehaviorOperation.Minimum or BehaviorOperation.Maximum or BehaviorOperation.Greater or BehaviorOperation.Less
        or BehaviorOperation.Select or BehaviorOperation.Absolute or BehaviorOperation.Negate or BehaviorOperation.Exp or BehaviorOperation.Power or BehaviorOperation.Curve;
    public static BehaviorGraph DefaultGraph(BehaviorHook hook)
    {
        if (hook.Kind == "initialTree") return TreeInitialSizeRules.DefaultGraph(hook);
        if (hook.Kind == "regeneration") return ForestRegenerationRules.DefaultGraph(hook);
        if (hook.Kind != "site") return ForestCompetitionRules.DefaultGraph(hook);
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
    private readonly GraphZeroGate shadeGate, crowdingGate;
    private readonly GraphInputRange[] pairBounds = new GraphInputRange[ForestCompetitionRules.PairInputs.Length];
    public bool CanBoundPairs => shadeGate != null && crowdingGate != null;
    private readonly double[][] inputs = Hooks.Select(h => new double[h.Inputs.Length]).ToArray();
    public string LastError { get; private set; }
    public EcologyResponseProgram(BehaviorModel source)
    {
        var graphs = source.Graphs.Where(g => Hooks.Any(h => h.Id == g.Hook && h.Kind == g.Kind)).ToList();
        if (graphs.Count != Hooks.Length) throw new InvalidDataException("Minden ökológiai válaszgráf szükséges. A hatás kikapcsolásához köss állandót a kimenetre.");
        foreach (var g in graphs)
            if (g.Nodes.Any(n => !AllowedOperation(n.Operation)))
                throw new InvalidDataException("Az ökológiai gráf csak a felkínált objektummezőket olvashatja; nincs beépített számítás vagy másik világállapot.");
        var program = new CompiledBehaviorModel(new() { Graphs = graphs }, Hooks);
        evaluate = Hooks.Select(h => program.Evaluator(h.Id, h.Kind) ?? throw new InvalidDataException("Hiányzó ökológiai gráf: " + h.Id)).ToArray();
        shadeGate = GraphZeroGate.TryCreate(graphs.Single(g => g.Hook == "forest.shading"), ForestCompetitionRules.PairInputs);
        crowdingGate = GraphZeroGate.TryCreate(graphs.Single(g => g.Hook == "forest.crowding"), ForestCompetitionRules.PairInputs);
        if (shadeGate?.SameCondition(crowdingGate) == true) crowdingGate = shadeGate;
    }
    private double Evaluate(int index, double[] values = null)
    {
        try { return evaluate[index](default, values ?? inputs[index]); }
        catch (ArithmeticException error) {
            LastError ??= Hooks[index].Name + ": " + error.Message + " A hibás válasz 0; javítsd a kijelzett gráfot.";
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
    public (double Shade, double CrownLoad) Pair(double height, double crown, double diameter, double health, double shadeTolerance,
        double otherHeight, double otherCrown, double otherDiameter, double otherHealth, double otherShadeTolerance, double distanceSquared)
    {
        var v = inputs[4];
        v[0] = height; v[1] = crown; v[2] = diameter; v[3] = health; v[4] = shadeTolerance;
        v[5] = otherHeight; v[6] = otherCrown; v[7] = otherDiameter; v[8] = otherHealth; v[9] = otherShadeTolerance;
        v[10] = Math.Sqrt(distanceSquared); v[11] = distanceSquared;
        return (Evaluate(4), Evaluate(5, v));
    }
    public bool PairMayContribute(double height, double crown, double diameter, double health, double shadeTolerance,
        ReadOnlySpan<GraphInputRange> neighbor, double minimumDistanceSquared, double maximumDistanceSquared)
    {
        if (!CanBoundPairs) return true;
        var v = pairBounds;
        v[0] = GraphInputRange.Exact(height); v[1] = GraphInputRange.Exact(crown); v[2] = GraphInputRange.Exact(diameter);
        v[3] = GraphInputRange.Exact(health); v[4] = GraphInputRange.Exact(shadeTolerance);
        for (int i = 0; i < 5; i++) v[5 + i] = neighbor[i];
        v[10] = new(Math.Sqrt(minimumDistanceSquared), Math.Sqrt(maximumDistanceSquared));
        v[11] = new(minimumDistanceSquared, maximumDistanceSquared);
        return shadeGate.MayBeNonZero(v) || !ReferenceEquals(shadeGate, crowdingGate) && crowdingGate.MayBeNonZero(v);
    }
    public (double Light, double Space) Competition(double shade, double crownLoad, double radiation, double water)
    {
        var v = inputs[6]; v[0] = shade; v[1] = crownLoad; v[2] = radiation; v[3] = water;
        return (Evaluate(6), Evaluate(7, v));
    }
    public double LightResponse(double light, double water, double space, double shadeTolerance)
    {
        var v = inputs[8]; v[0] = light; v[1] = water; v[2] = space; v[3] = shadeTolerance;
        return Evaluate(8);
    }
    public double ResourceLimit(double light, double water, double space, double shadeTolerance)
    {
        var v = inputs[9]; v[0] = light; v[1] = water; v[2] = space; v[3] = shadeTolerance;
        v[4] = LightResponse(light, water, space, shadeTolerance); return Evaluate(9);
    }

    public double Regeneration(string hook, in ForestRegenerationInputs query)
    {
        int index = -1;
        for (int i = 0; i < Hooks.Length; i++)
            if (Hooks[i].Id == hook && Hooks[i].Kind == "regeneration") { index = i; break; }
        if (index < 0) throw new ArgumentException("Unknown regeneration response.", nameof(hook));
        var v = inputs[index];
        if (hook == "forest.seedCrowding") { v[0] = query.NeighborBiomass; v[1] = query.Moisture; return Evaluate(index); }
        v[0] = query.Maturity;
        v[1] = query.Health;
        v[2] = query.Biomass;
        v[3] = query.Moisture;
        v[4] = query.Suitability;
        v[5] = query.Water;
        v[6] = query.Crowding;
        v[7] = query.ShadeTolerance;
        v[8] = query.MaximumBiomass;
        v[9] = query.Supply;
        v[10] = query.Years;
        v[11] = query.Random;
        v[12] = query.NeighborBiomass;
        v[13] = query.Distance;
        v[14] = query.Age;
        v[15] = query.MatureAge;
        v[16] = query.Fertility;
        v[17] = query.Elevation;
        v[18] = query.Radiation;
        return Evaluate(index);
    }
    public (double Height, double Diameter, double CrownRadius) InitialSize(double age, double random, double scale, bool planted,
        double matureAge, double matureHeight, double matureDiameter, double crownRatio)
    {
        // Hook order is fixed: height, then diameter, then crown; no recursive graph dependency.
        int index = InitialSizeIndex;
        var h = inputs[index]; h[0] = age; h[1] = random; h[2] = scale; h[3] = planted ? 1 : 0;
        h[4] = matureAge; h[5] = matureHeight; h[6] = matureDiameter; h[7] = crownRatio;
        double height = Evaluate(index);
        var d = inputs[index + 1]; Array.Copy(h, d, h.Length); d[8] = height;
        double diameter = Evaluate(index + 1);
        var c = inputs[index + 2]; Array.Copy(d, c, d.Length); c[9] = diameter;
        return (height, diameter, Evaluate(index + 2));
    }

}
