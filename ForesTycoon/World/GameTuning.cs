using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon
{
    /// <summary>Every tunable number of the native rules. The order is internal only; files and saves use the names.</summary>
    internal enum Tune
    {
        VehicleTimeScale,
        ProcessorCapacity, FellingRate, ProcessorUnloadRate, FuelFelling, HarvesterSpeed,
        ForwarderCapacity, ForwarderLoadRate, ForwarderUnloadRate, ForwarderSpeed, ForwarderLoadedSpeed,
        FuelDriving, FuelDrivingLoaded, FuelCrane, DieselPrice,
        WearPerSecond, BreakdownHazard, WearFuelPenalty, WearPacePenalty,
        RepairSeconds, FieldRepairRelief, RepairBaseCost, RepairWearCost, ServicePerSecond, ServiceCostPerWear,
        AsphaltBuild, MacadamBuild, RoadRepairShare, TrailCost,
        ReferenceWear, ReferenceMass, AsphaltFactor, MacadamFactor, TrailLoadMultiplier, SkidTrailOvergrowYears,
        EmptyMass, TimberDensity, Power, MaxTractiveForce, Drivetrain, Braking, DragArea,
        RollingAsphalt, RollingGravel, RollingDirt, RoughnessRolling, FuelPerKilowattHour, IdleFuelPerHour,
    }

    /// <summary>One tunable: which rule it belongs to, how it is shown and the range the game accepts.</summary>
    internal sealed record TuningSpec(Tune Key, string Rule, string Name, string Unit, double Default, double Min, double Max)
    {
        internal string Id => Key.ToString();
    }

    /// <summary>
    /// The world's tunable numbers. Immutable: a change is a new instance, applied through a journaled command, so a
    /// replay and a checkpoint see exactly the values the game ran with. Unlisted names keep their defaults.
    /// </summary>
    internal sealed class GameTuning
    {
        internal static readonly TuningSpec[] Specs =
        {
            new(Tune.VehicleTimeScale, "time.world", "Járműidő szorzó", "×", GameWorld.VehicleTimeScale, 1, 20),

            new(Tune.ProcessorCapacity, "machine.processor", "Rakománykapacitás", "m³", ForestMachine.ProcessorCapacity, 0.5, 20),
            new(Tune.FellingRate, "machine.processor", "Kitermelési ráta", "m³/jármű-s", ForestryLogistics.FellingRate, 0.05, 20),
            new(Tune.ProcessorUnloadRate, "machine.processor", "Lerakodási ráta", "m³/jármű-s", ForestryLogistics.ProcessorUnloadRate, 0.05, 20),
            new(Tune.FuelFelling, "machine.processor", "Vágási üzemanyag", "l/jármű-s", ForestryLogistics.FuelFelling, 0, 10),
            new(Tune.HarvesterSpeed, "machine.processor", "Haladási sebesség", "csempe/jármű-s", ForestryLogistics.HarvesterSpeed, 0.02, 5),

            new(Tune.ForwarderCapacity, "machine.forwarder", "Rakománykapacitás", "m³", ForestMachine.ForwarderCapacity, 1, 60),
            new(Tune.ForwarderLoadRate, "machine.forwarder", "Rakodási ráta", "m³/jármű-s", ForestryLogistics.ForwarderLoadRate, 0.05, 30),
            new(Tune.ForwarderUnloadRate, "machine.forwarder", "Lerakodási ráta", "m³/jármű-s", ForestryLogistics.ForwarderUnloadRate, 0.05, 30),
            new(Tune.ForwarderSpeed, "machine.forwarder", "Üres sebesség", "csempe/jármű-s", ForestryLogistics.ForwarderSpeed, 0.02, 5),
            new(Tune.ForwarderLoadedSpeed, "machine.forwarder", "Rakott sebesség", "csempe/jármű-s", ForestryLogistics.ForwarderLoadedSpeed, 0.02, 5),

            new(Tune.FuelDriving, "machine.movement", "Üres menet üzemanyaga", "l/jármű-s", ForestryLogistics.FuelDriving, 0, 10),
            new(Tune.FuelDrivingLoaded, "machine.movement", "Rakott menet üzemanyaga", "l/jármű-s", ForestryLogistics.FuelDrivingLoaded, 0, 10),
            new(Tune.FuelCrane, "machine.movement", "Darumunka üzemanyaga", "l/jármű-s", ForestryLogistics.FuelCrane, 0, 10),
            new(Tune.DieselPrice, "economy.fuel", "Dízelár", "eFt/l", ForestryLogistics.DieselPrice, 0, 20),

            new(Tune.WearPerSecond, "upkeep.wear", "Kopási ráta", "1/jármű-s", VehicleUpkeep.WearPerSecond, 0, 0.02),
            new(Tune.BreakdownHazard, "upkeep.wear", "Meghibásodási hajlam", "1/jármű-s teljes kopásnál", VehicleUpkeep.BreakdownHazard, 0, 0.5),
            new(Tune.WearFuelPenalty, "upkeep.effects", "Többletfogyasztás teljes kopásnál", "×", VehicleUpkeep.WearFuelPenalty, 0, 5),
            new(Tune.WearPacePenalty, "upkeep.effects", "Lassulás teljes kopásnál", "arány", VehicleUpkeep.WearPacePenalty, 0, 0.9),
            new(Tune.RepairSeconds, "upkeep.repair", "Javítási idő", "jármű-s", VehicleUpkeep.RepairSeconds, 1, 1200),
            new(Tune.FieldRepairRelief, "upkeep.repair", "Kopásenyhítés", "kopás", VehicleUpkeep.FieldRepairRelief, 0, 1),
            new(Tune.RepairBaseCost, "upkeep.repair", "Alapköltség", "eFt", VehicleUpkeep.RepairBaseCost, 0, 10000),
            new(Tune.RepairWearCost, "upkeep.repair", "Kopásköltség", "eFt/teljes kopás", VehicleUpkeep.RepairWearCost, 0, 10000),
            new(Tune.ServicePerSecond, "upkeep.service", "Karbantartási ráta", "1/jármű-s", VehicleUpkeep.ServicePerSecond, 0, 1),
            new(Tune.ServiceCostPerWear, "upkeep.service", "Kopásegység ára", "eFt", VehicleUpkeep.ServiceCostPerWear, 0, 10000),

            new(Tune.AsphaltBuild, "road.build", "Aszfalt", "eFt/csempe", RoadCosts.AsphaltBuild, 0, 100000),
            new(Tune.MacadamBuild, "road.build", "Makadám", "eFt/csempe", RoadCosts.MacadamBuild, 0, 100000),
            new(Tune.RoadRepairShare, "road.repair", "Javítás az építési ár arányában", "× teljes kár", RoadCosts.RepairShare, 0, 5),
            new(Tune.TrailCost, "trail.build", "Nyomkijelölés", "eFt/csempe", RoadCosts.Trail, 0, 10000),

            new(Tune.ReferenceWear, "road.trafficWear", "Referencia áthaladási kopás", "állapotveszteség/áthaladás", RoadTrafficParameters.ReferenceWear, 0, 0.2),
            new(Tune.ReferenceMass, "road.trafficWear", "Referencia össztömeg", "kg", RoadTrafficParameters.ReferenceMass, 1000, 200000),
            new(Tune.AsphaltFactor, "road.trafficWear", "Aszfalt szorzó", "1", RoadTrafficParameters.AsphaltFactor, 0, 10),
            new(Tune.MacadamFactor, "road.trafficWear", "Makadám szorzó", "1", RoadTrafficParameters.MacadamFactor, 0, 10),
            new(Tune.TrailLoadMultiplier, "trail.traffic", "Nyomterhelési szorzó", "× útkopás", RoadTrafficParameters.TrailLoadMultiplier, 0, 500),
            new(Tune.SkidTrailOvergrowYears, "trail.age", "Benövési idő", "erdőév", TerrainMap.DefaultSkidTrailOvergrowYears, 0.1, 50),

            new(Tune.EmptyMass, "vehicle.mass", "Üres tömeg", "kg", TruckSpec.Default.EmptyMass, 2000, 60000),
            new(Tune.TimberDensity, "vehicle.mass", "Fasűrűség", "kg/m³", TruckSpec.Default.TimberDensity, 300, 1300),
            new(Tune.Power, "vehicle.motion", "Motorteljesítmény", "W", TruckSpec.Default.Power, 50000, 1000000),
            new(Tune.MaxTractiveForce, "vehicle.motion", "Legnagyobb vonóerő", "N", TruckSpec.Default.MaxTractiveForce, 10000, 600000),
            new(Tune.Drivetrain, "vehicle.motion", "Hajtáslánc hatásfoka", "1", TruckSpec.Default.Drivetrain, 0.3, 1),
            new(Tune.Braking, "vehicle.motion", "Fékező lassulás", "m/s²", TruckSpec.Default.Braking, 0.3, 10),
            new(Tune.DragArea, "vehicle.resistance", "Légellenállási felület", "m²", TruckSpec.Default.DragArea, 0, 30),
            new(Tune.RollingAsphalt, "vehicle.resistance", "Aszfalt Crr", "1", VehicleDynamics.DefaultRollingAsphalt, 0, 0.3),
            new(Tune.RollingGravel, "vehicle.resistance", "Makadám Crr", "1", VehicleDynamics.DefaultRollingGravel, 0, 0.3),
            new(Tune.RollingDirt, "vehicle.resistance", "Föld Crr", "1", VehicleDynamics.DefaultRollingDirt, 0, 0.5),
            new(Tune.RoughnessRolling, "vehicle.resistance", "Egyenetlenség hatása", "× Crr teljes kárnál", VehicleDynamics.DefaultRoughnessRolling, 0, 10),
            new(Tune.FuelPerKilowattHour, "vehicle.fuel", "Fajlagos fogyasztás", "l/kWh", TruckSpec.Default.FuelPerKilowattHour, 0.05, 1),
            new(Tune.IdleFuelPerHour, "vehicle.fuel", "Alapjárat", "l/h", TruckSpec.Default.IdleFuelPerHour, 0, 30),
        };

        private static readonly Dictionary<string, TuningSpec> byId = Specs.ToDictionary(s => s.Id, StringComparer.Ordinal);
        internal static readonly GameTuning Default = new(Specs.Select(s => s.Default).ToArray());

        static GameTuning()
        {
            // Every key has exactly one spec, in enum order, so the array can be indexed by the key.
            for (int i = 0; i < Specs.Length; i++)
                if ((int)Specs[i].Key != i) throw new InvalidOperationException("Tuning specs out of order: " + Specs[i].Key);
            if (Specs.Length != Enum.GetValues<Tune>().Length) throw new InvalidOperationException("Tuning spec missing.");
        }

        private readonly double[] values;
        private GameTuning(double[] values) { this.values = values; Truck = BuildTruck(); }

        internal double this[Tune key] => values[(int)key];
        internal float F(Tune key) => (float)values[(int)key];
        internal static TuningSpec Spec(string id) => byId.TryGetValue(id, out var spec) ? spec : null;
        internal static TuningSpec Spec(Tune key) => Specs[(int)key];

        /// <summary>The truck physics built from the tunables, shared by every vehicle of the world.</summary>
        internal TruckSpec Truck { get; }
        private TruckSpec BuildTruck() => new(F(Tune.EmptyMass), F(Tune.Power), F(Tune.MaxTractiveForce), F(Tune.DragArea), F(Tune.Drivetrain),
            F(Tune.TimberDensity), F(Tune.FuelPerKilowattHour), F(Tune.IdleFuelPerHour), F(Tune.Braking))
        {
            RollingAsphalt = F(Tune.RollingAsphalt), RollingGravel = F(Tune.RollingGravel), RollingDirt = F(Tune.RollingDirt),
            RoughnessRolling = F(Tune.RoughnessRolling)
        };

        internal bool IsDefault => Specs.All(s => values[(int)s.Key] == s.Default);

        /// <summary>Only the values that differ from the defaults, by name — what files and saves store.</summary>
        internal Dictionary<string, double> ToOverrides() =>
            Specs.Where(s => values[(int)s.Key] != s.Default).ToDictionary(s => s.Id, s => values[(int)s.Key], StringComparer.Ordinal);

        /// <summary>Defaults with the given values; unknown names, non-finite or out-of-range values are rejected.</summary>
        internal static GameTuning FromOverrides(IReadOnlyDictionary<string, double> overrides)
        {
            if (overrides == null || overrides.Count == 0) return Default;
            var next = (double[])Default.values.Clone();
            foreach (var (id, value) in overrides)
            {
                var spec = Spec(id) ?? throw new ArgumentException($"Ismeretlen hangolási paraméter: {id}.");
                if (!double.IsFinite(value) || value < spec.Min || value > spec.Max)
                    throw new ArgumentException($"{spec.Name} ({id}) a {spec.Min:G6}–{spec.Max:G6} tartományon kívül esik: {value:G6}.");
                next[(int)spec.Key] = value;
            }
            return new GameTuning(next);
        }

        internal string ToJson() => System.Text.Json.JsonSerializer.Serialize(new SortedDictionary<string, double>(ToOverrides(), StringComparer.Ordinal));
        internal static GameTuning FromJson(string json) =>
            FromOverrides(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(json ?? "{}")
                ?? throw new ArgumentException("Üres hangolási dokumentum."));
    }
}
