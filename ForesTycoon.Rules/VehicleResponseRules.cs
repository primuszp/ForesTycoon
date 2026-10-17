using System;
using System.Linq;

namespace ForesTycoon.Rules;

public enum VehicleResponse { Mass, Rolling, Resistance, Force, Fuel, TargetSpeed, Strain, Wear, Breakdown, FuelFactor, PaceFactor, RepairSeconds, RepairRelief, RepairCost, ServiceRelief, ServiceCost, RouteSpeed }

/// <summary>Pure vehicle decisions; integration, elapsed timers and seeded draws remain host operations.</summary>
public static class VehicleResponseRules
{
    private static BehaviorHook H(string id, string name, string unit, double min, double max, string schedule, params string[] inputs) =>
        new(id, name, "vehicleResponse", unit, min, max, "", "", schedule, false, inputs);
    public static readonly BehaviorHook[] Hooks = {
        H("vehicle.mass", "Jármű és rakomány tömege", "kg", 1, 1e9, "Járműfizikai al-lépésben", "spec.emptyMass", "spec.timberDensity", "vehicle.cargo"),
        H("vehicle.rolling", "Útfelület gördülési válasza", "", 0, 100, "Járműfizikai al-lépésben", "road.surface", "road.roughness", "spec.rollingAsphalt", "spec.rollingGravel", "spec.rollingDirt", "spec.roughnessRolling"),
        H("vehicle.resistance", "Jármű menetellenállása", "N", -1e10, 1e10, "Járműfizikai al-lépésben", "vehicle.rolling", "vehicle.mass", "vehicle.speed", "road.slopeSin", "road.slopeCos", "spec.dragArea", "physics.gravity", "physics.airDensity"),
        H("vehicle.force", "Rendelkezésre álló vonóerő", "N", 0, 1e9, "Járműfizikai al-lépésben", "spec.maxTractiveForce", "spec.power", "spec.drivetrain", "vehicle.speed"),
        H("vehicle.fuel", "Motor üzemanyag-fogyasztása", "l", 0, 1e9, "Járműfizikai al-lépés után, a tényleges motorteljesítményből", "vehicle.enginePower", "step.seconds", "spec.fuelPerKilowattHour", "spec.idleFuelPerHour"),
        H("vehicle.targetSpeed", "Útállapot és megállás sebességhatása", "csempe/s", 0, 1e5, "Járműfizikai al-lépésben", "route.targetSpeed", "road.damage", "spec.braking", "route.metresPerTile", "route.remaining"),
        H("upkeep.strain", "Rakomány és terep terhelése", "×", 0, 1000, "Gépenként vagy teherautónként, szeletelt munkalépésben", "vehicle.kind", "vehicle.cargoFill", "vehicle.felling", "road.rough"),
        H("upkeep.wear", "Munkavégzés kopása", "kopás", 0, 1, "Minden üzemelő gép/jármű munkalépésében", "spec.wearPerSecond", "vehicle.strain", "step.seconds", "vehicle.wear"),
        H("upkeep.breakdown", "Meghibásodási esély", "0–1", 0, 1, "Kopás után, egy mentett seedelt sorsolás előtt", "spec.breakdownHazard", "vehicle.wear", "vehicle.strain", "step.seconds"),
        H("upkeep.fuelFactor", "Kopás fogyasztási hatása", "×", 0, 100, "Az elfogyasztott üzemanyag elszámolásakor", "spec.wearFuelPenalty", "vehicle.wear"),
        H("upkeep.paceFactor", "Kopás munkatempó-hatása", "×", .001, 100, "Gépi munkalépés előtt", "spec.wearPacePenalty", "vehicle.wear"),
        H("upkeep.repairSeconds", "Helyszíni javítás ideje", "s", 0, 1200, "Meghibásodás pillanatában rögzítve", "spec.repairSeconds", "vehicle.wear", "vehicle.strain"),
        H("upkeep.repairRelief", "Helyszíni javítás kopásenyhítése", "kopás", 0, 1, "A helyszíni javítás végén", "spec.fieldRepairRelief", "vehicle.wear"),
        H("upkeep.repairCost", "Helyszíni javítás költsége", "eFt", 0, 1e12, "A helyszíni javítás végén", "spec.repairBaseCost", "spec.repairWearCost", "vehicle.wear"),
        H("upkeep.serviceRelief", "Telephelyi karbantartás kopásenyhítése", "kopás", 0, 1, "Telephelyen álló, nem hibás járművenként", "vehicle.wear", "spec.servicePerSecond", "step.seconds"),
        H("upkeep.serviceCost", "Telephelyi karbantartás költsége", "eFt", 0, 1e12, "A ténylegesen enyhített kopásból, karbantartási lépésenként", "service.relief", "spec.serviceCostPerWear"),
        H("vehicle.routeSpeed", "Lejtő és kanyar sebességválasza", "csempe/s", 0, 1e5, "Járműfizikai al-lépésben, a geometria mért bemeneteiből", "route.cruise", "road.grade", "vehicle.cargoFill", "road.corner")
    };
    public static BehaviorGraph DefaultGraph(BehaviorHook hook)
    {
        string formula = hook.Id switch {
            "vehicle.mass" => "spec.emptyMass + max(0; vehicle.cargo) * spec.timberDensity",
            "vehicle.rolling" => "ha(road.surface < 0.5; spec.rollingAsphalt; ha(road.surface < 1.5; spec.rollingGravel; spec.rollingDirt)) * (1 + spec.roughnessRolling * korlát(road.roughness; 0; 1))",
            "vehicle.resistance" => "vehicle.rolling * vehicle.mass * physics.gravity * road.slopeCos + vehicle.mass * physics.gravity * road.slopeSin + 0.5 * physics.airDensity * spec.dragArea * vehicle.speed * vehicle.speed",
            "vehicle.force" => "min(spec.maxTractiveForce; spec.power * spec.drivetrain / max(vehicle.speed; 1))",
            "vehicle.fuel" => "vehicle.enginePower / 1000 * (step.seconds / 3600) * spec.fuelPerKilowattHour + spec.idleFuelPerHour * (step.seconds / 3600)",
            "vehicle.targetSpeed" => "min(route.targetSpeed * (1 - 0.55 * road.damage); hatvány(max(0; 2 * (spec.braking / route.metresPerTile) * route.remaining); 0.5))",
            "vehicle.routeSpeed" => "min(ha(road.grade < 0; route.cruise * korlát(1 + road.grade * (1.2 + vehicle.cargoFill); 0.45; 1); route.cruise); ha(road.corner > 0.5; route.cruise * 0.48; route.cruise))",
            "upkeep.strain" => "ha(road.rough > 0.5; ha(vehicle.kind < 0.5; 2; 1.5); 1) * (1 + 0.5 * vehicle.cargoFill) * ha(vehicle.felling > 0.5; 1.3; 1)",
            "upkeep.wear" => "spec.wearPerSecond * vehicle.strain * step.seconds",
            "upkeep.breakdown" => "spec.breakdownHazard * vehicle.wear * vehicle.wear * vehicle.strain * step.seconds",
            "upkeep.fuelFactor" => "1 + spec.wearFuelPenalty * vehicle.wear",
            "upkeep.paceFactor" => "1 - spec.wearPacePenalty * vehicle.wear",
            "upkeep.repairSeconds" => "spec.repairSeconds",
            "upkeep.repairRelief" => "spec.fieldRepairRelief",
            "upkeep.repairCost" => "spec.repairBaseCost + spec.repairWearCost * vehicle.wear",
            "upkeep.serviceRelief" => "min(vehicle.wear; spec.servicePerSecond * step.seconds)",
            "upkeep.serviceCost" => "service.relief * spec.serviceCostPerWear",
            _ => throw new ArgumentException("Unknown vehicle response.", nameof(hook))
        };
        var graph = RuleFormula.Compile(formula, new() { Id = "default.vehicleResponse." + hook.Id, Name = hook.Name, Hook = hook.Id, Kind = hook.Kind },
            hook.Inputs.Select(input => new FormulaName(input, BehaviorOperation.Input, input)).ToArray());
        graph.Nodes.Single(n => n.Id == graph.Output).Name = hook.Name;
        return graph;
    }
}

public sealed class VehicleRuleProgram
{
    [ThreadStatic] private static VehicleRuleProgram defaults;
    public static VehicleRuleProgram Default => defaults ??= new(new());
    private readonly Func<BehaviorInputs, double[], double>[] evaluate;
    private readonly double[][] inputs = VehicleResponseRules.Hooks.Select(h => new double[h.Inputs.Length]).ToArray();
    public string LastError { get; private set; }
    public VehicleRuleProgram(BehaviorModel source, CompiledBehaviorModel sharedProgram = null)
    {
        var model = new BehaviorModel { Graphs = source.Graphs.Where(g => g.Kind == "vehicleResponse").ToList() };
        foreach (var hook in VehicleResponseRules.Hooks)
            if (!model.Graphs.Any(g => g.Hook == hook.Id)) model.Graphs.Add(VehicleResponseRules.DefaultGraph(hook));
        if (model.Graphs.Any(g => g.Nodes.Any(n => !EcologyResponseProgram.AllowedOperation(n.Operation))))
            throw new System.IO.InvalidDataException("A járműválasz csak mért bemenetekből és közös gráfműveletekből számolhat.");
        var program = sharedProgram ?? new CompiledBehaviorModel(model, VehicleResponseRules.Hooks);
        evaluate = VehicleResponseRules.Hooks.Select(h => program.Evaluator(h.Id, h.Kind)).ToArray();
    }
    public double Evaluate(VehicleResponse response, ReadOnlySpan<double> values)
    {
        int index = (int)response;
        if ((uint)index >= (uint)inputs.Length || values.Length != inputs[index].Length) throw new ArgumentException("Vehicle input mismatch.");
        values.CopyTo(inputs[index]);
        try { return evaluate[index](default, inputs[index]); }
        catch (ArithmeticException error) {
            LastError ??= VehicleResponseRules.Hooks[index].Name + ": " + error.Message;
            return Math.Max(0, VehicleResponseRules.Hooks[index].Min);
        }
    }
}
