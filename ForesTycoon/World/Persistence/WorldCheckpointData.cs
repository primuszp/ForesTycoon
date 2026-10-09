using System;

namespace ForesTycoon
{
    internal sealed record RoadGradientCheckpoint(float X, float Y);
    internal sealed record RoadRouteCheckpoint(CheckpointPosition[] Centers, RoadGradientCheckpoint[] Gradients);
    internal sealed record VehicleCheckpoint(int Id, int[] Route, double Speed, float Capacity, bool Physics,
        RoadRouteCheckpoint RoadGeometry, double CurrentSpeed, double Position, double PreviousPosition, float Cargo,
        bool CargoStops, int[] SourceTiles, int Mill, bool Blocked, VehicleTransportState State, float TransferProgress,
        double FuelUsed = 0, double Distance = 0, bool Transit = false, bool TransitArrived = false);
    internal sealed record VehiclesCheckpoint(int NextId, bool RoadPhysics, bool CargoStops, VehicleCheckpoint[] Vehicles);
    internal sealed record AnimalCheckpoint(int Id, int Tile, int TargetTile, CheckpointPosition Position,
        CheckpointPosition PreviousPosition, CheckpointPosition Target, float Yaw, float PreviousYaw, float Blend,
        float Hunger, float WanderNeed, double WalkTime, double Age, uint Seed);
    internal sealed record ForageCheckpoint(int Tile, float Depletion);
    internal sealed record WildlifeCheckpoint(AnimalCheckpoint[] Animals, ForageCheckpoint[] Forage,
        ulong ForestRevision, ulong SurfaceRevision);
    internal sealed record PileCheckpoint(int Tile, float Volume);
    internal sealed record HarvestCheckpoint(int[] Tiles, float InitialVolume, int Landing = -1, PileCheckpoint[] Piles = null, float LandingStock = 0);
    internal sealed record ForestMachineCheckpoint(int Id, ForestMachineKind Kind, int Site, int[] Path, double Position, double PreviousPosition,
        ForestMachineState State, ForestMachineState Goal, float Cargo, double WorkTime, int Home = -1, bool HomeRequested = false,
        int Source = -1, int Target = -1, int Destination = -1, double CargoValue = 0, double FuelUsed = 0, double UnitPrice = 0);
    internal sealed record DepotCheckpoint(int TileId, int[] Footprint, CheckpointPosition Position);
    internal sealed record TruckCheckpoint(int Id, int Home, TruckPhase Phase, int Vehicle, int Source, int Destination, bool HomeRequested,
        int Target = -1, double CargoValue = 0, double FuelCharged = 0);
    internal sealed record StackCheckpoint(int Id, int Tile, float Volume, double Value);
    internal sealed record MillCheckpoint(int TileId, int[] Footprint, CheckpointPosition Position, float Received, float Stock, float Processed);
    internal sealed record LogisticsCheckpoint(HarvestCheckpoint[] Sites, MillCheckpoint[] Mills, string Status,
        ForestMachineCheckpoint[] Machines = null, int NextMachineId = 1,
        DepotCheckpoint[] Depots = null, TruckCheckpoint[] Trucks = null, int NextTruckId = 1,
        StackCheckpoint[] Stacks = null, int NextStackId = 1, double Income = 0, double RunningCosts = 0);
    internal sealed record WorldCheckpointData(int Version, ulong Tick, int CommandCursor, int PendingCommands,
        TerrainCheckpoint Terrain, EcologyCheckpoint Ecology, WildlifeCheckpoint Wildlife, LogisticsCheckpoint Logistics,
        VehiclesCheckpoint Vehicles, float AvailableTimber, float DeliveredTimber, EffectCheckpoint[] Effects,
        ForestryActionResult LastAction, ForestryAreaSummary LastArea, double Expenses = 0);
}
