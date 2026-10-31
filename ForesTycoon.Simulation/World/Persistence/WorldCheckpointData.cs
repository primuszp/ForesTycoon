using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    internal sealed record RoadGradientCheckpoint(float X, float Y);
    internal sealed record RoadRouteCheckpoint(CheckpointPosition[] Centers, RoadGradientCheckpoint[] Gradients);
    internal sealed record VehicleCheckpoint(int Id, int[] Route, double Speed, float Capacity, bool Physics,
        RoadRouteCheckpoint RoadGeometry, double CurrentSpeed, double Position, double PreviousPosition, float Cargo,
        bool CargoStops, int[] SourceTiles, int Mill, bool Blocked, VehicleTransportState State, float TransferProgress,
        double FuelUsed = 0, double Distance = 0, bool Transit = false, bool TransitArrived = false, bool Broken = false, int LastPassageTile = -1,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        RigState Rig = null);
    internal sealed record VehiclesCheckpoint(int NextId, bool RoadPhysics, bool CargoStops, VehicleCheckpoint[] Vehicles,
        TruckPhysicsCheckpoint Physics = null);
    internal sealed record TruckPhysicsCheckpoint(float EmptyMass, float Power, float MaxTractiveForce, float DragArea,
        float Drivetrain, float TimberDensity, float FuelPerKilowattHour, float IdleFuelPerHour, float Braking,
        float RollingAsphalt, float RollingGravel, float RollingDirt, float RoughnessRolling, RigProfile Geometry = null)
    {
        internal static TruckPhysicsCheckpoint From(TruckSpec s) => new(s.EmptyMass, s.Power, s.MaxTractiveForce, s.DragArea,
            s.Drivetrain, s.TimberDensity, s.FuelPerKilowattHour, s.IdleFuelPerHour, s.Braking,
            s.RollingAsphalt, s.RollingGravel, s.RollingDirt, s.RoughnessRolling, s.Geometry);
        internal TruckSpec Restore()
        {
            void Check(float value, Tune key) {
                var range = GameTuning.Spec(key);
                CheckpointGuard.Require(float.IsFinite(value) && value >= (float)range.Min && value <= (float)range.Max, "sampled truck physics");
            }
            Check(EmptyMass, Tune.EmptyMass); Check(Power, Tune.Power); Check(MaxTractiveForce, Tune.MaxTractiveForce);
            Check(DragArea, Tune.DragArea); Check(Drivetrain, Tune.Drivetrain); Check(TimberDensity, Tune.TimberDensity);
            Check(FuelPerKilowattHour, Tune.FuelPerKilowattHour); Check(IdleFuelPerHour, Tune.IdleFuelPerHour); Check(Braking, Tune.Braking);
            Check(RollingAsphalt, Tune.RollingAsphalt); Check(RollingGravel, Tune.RollingGravel);
            Check(RollingDirt, Tune.RollingDirt); Check(RoughnessRolling, Tune.RoughnessRolling);
            Geometry?.Validate();
            return new(EmptyMass, Power, MaxTractiveForce, DragArea, Drivetrain, TimberDensity, FuelPerKilowattHour, IdleFuelPerHour, Braking) {
                RollingAsphalt = RollingAsphalt, RollingGravel = RollingGravel, RollingDirt = RollingDirt, RoughnessRolling = RoughnessRolling,
                Geometry = Geometry?.Copy() };
        }
    }
    internal sealed record AnimalCheckpoint(int Id, int Tile, int TargetTile, CheckpointPosition Position,
        CheckpointPosition PreviousPosition, CheckpointPosition Target, float Yaw, float PreviousYaw, float Blend,
        float Hunger, float WanderNeed, double WalkTime, double Age, uint Seed,
        double? PreviousWalkTime = null, double? PreviousAge = null, string Kind = "deer", Dictionary<string, double> Fields = null,
        double DistanceMoved = 0);
    internal sealed record ForageCheckpoint(int Tile, float Depletion);
    internal sealed record WildlifeHabitatCheckpoint(int Tile, CheckpointPosition Position, uint Rank);
    internal sealed record AnimalTypeCheckpoint(string Kind, int DesiredPopulation);
    internal sealed record WildlifeCheckpoint(AnimalCheckpoint[] Animals, ForageCheckpoint[] Forage,
        ulong ForestRevision, ulong SurfaceRevision, int NextAnimalId = 1,
        WildlifeHabitatCheckpoint[] Habitat = null, AnimalTypeCheckpoint[] Types = null);
    internal sealed record PileCheckpoint(int Tile, float Volume);
    internal sealed record HarvestCheckpoint(int[] Tiles, float InitialVolume, int Landing = -1, PileCheckpoint[] Piles = null, float LandingStock = 0);
    internal sealed record ForestMachineCheckpoint(int Id, ForestMachineKind Kind, int Site, int[] Path, double Position, double PreviousPosition,
        ForestMachineState State, ForestMachineState Goal, float Cargo, double WorkTime, int Home = -1, bool HomeRequested = false,
        int Source = -1, int Target = -1, int Destination = -1, double CargoValue = 0, double FuelUsed = 0, double UnitPrice = 0,
        UpkeepCheckpoint Upkeep = null, bool HasWork = false, float LogTransferVolume = 0, double LogTransferValue = 0, double LogCycleDuration = 0,
        double LogGripPhase = ForwarderLoading.Grip, double LogReleasePhase = ForwarderLoading.Release,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        RigState Rig = null);
    internal sealed record UpkeepCheckpoint(float Wear, bool Broken, float RepairLeft, uint Seed, int Breakdowns);
    internal sealed record DepotCheckpoint(int TileId, int[] Footprint, CheckpointPosition Position);
    internal sealed record TruckCheckpoint(int Id, int Home, TruckPhase Phase, int Vehicle, int Source, int Destination, bool HomeRequested,
        int Target = -1, double CargoValue = 0, double FuelCharged = 0, UpkeepCheckpoint Upkeep = null);
    internal sealed record StackCheckpoint(int Id, int Tile, float Volume, double Value);
    internal sealed record MillCheckpoint(int TileId, int[] Footprint, CheckpointPosition Position, float Received, float Stock, float Processed);
    internal sealed record LogisticsCheckpoint(HarvestCheckpoint[] Sites, MillCheckpoint[] Mills, string Status,
        ForestMachineCheckpoint[] Machines = null, int NextMachineId = 1,
        DepotCheckpoint[] Depots = null, TruckCheckpoint[] Trucks = null, int NextTruckId = 1,
        StackCheckpoint[] Stacks = null, int NextStackId = 1, double Income = 0, double RunningCosts = 0);
    internal sealed record WorldCheckpointData(int Version, ulong Tick, int CommandCursor, int PendingCommands,
        TerrainCheckpoint Terrain, EcologyCheckpoint Ecology, WildlifeCheckpoint Wildlife, LogisticsCheckpoint Logistics,
        VehiclesCheckpoint Vehicles, float AvailableTimber, float DeliveredTimber, EffectCheckpoint[] Effects,
        ForestryActionResult LastAction, ForestryAreaSummary LastArea, double Expenses = 0,
        Dictionary<string, double> Tuning = null, double RoadWeatherSeconds = 0, BehaviorModel Behaviors = null,
        BehaviorControllerSnapshot[] BehaviorStates = null, DynamicsModel Dynamics = null,
        Dictionary<string, double> DynamicsValues = null, string RuleProjectName = null, AlgorithmGraphReference Algorithms = null,
        // Null: the built-in rule book with no custom fields, so older saves and the opening reference stay unchanged.
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        RuleBookCheckpoint RuleBook = null,
        // Null between rule steps; set while a step is spread over ticks (SimulationWorld.WeatherRoads).
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        RuleStepCheckpoint RuleStep = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        bool PendingForestRateRefresh = false,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        ForesTycoon.Rules.SpeciesDefinition[] Species = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        WorldScenario Scene = null);
}
