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
}

public sealed class VehicleRuleProgram
{
    [ThreadStatic] private static VehicleRuleProgram defaults;
    public static VehicleRuleProgram Default => defaults ??= new(RuleProject.CreateDefault().Behaviors);
    private readonly Func<BehaviorInputs, double[], double>[] evaluate;
    private readonly double[][] inputs = VehicleResponseRules.Hooks.Select(h => new double[h.Inputs.Length]).ToArray();
    public string LastError { get; private set; }
    public VehicleRuleProgram(BehaviorModel source, CompiledBehaviorModel sharedProgram = null)
    {
        var model = new BehaviorModel { Graphs = source.Graphs.Where(g => g.Kind == "vehicleResponse").ToList() };
        foreach (var hook in VehicleResponseRules.Hooks)
            if (!model.Graphs.Any(g => g.Hook == hook.Id)) model.Graphs.Add(RuleProject.DefaultGraph(hook));
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
