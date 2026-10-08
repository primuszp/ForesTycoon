using System;

namespace ForesTycoon
{
    internal sealed record RoadGradientCheckpoint(float X, float Y);
    internal sealed record RoadRouteCheckpoint(CheckpointPosition[] Centers, RoadGradientCheckpoint[] Gradients);
    internal sealed record VehicleCheckpoint(int Id, int[] Route, double Speed, float Capacity, bool Physics,
        RoadRouteCheckpoint RoadGeometry, double CurrentSpeed, double Position, double PreviousPosition, float Cargo,
        bool CargoStops, int[] SourceTiles, int Mill, bool Blocked, VehicleTransportState State, float TransferProgress);
    internal sealed record VehiclesCheckpoint(int NextId, bool RoadPhysics, bool CargoStops, VehicleCheckpoint[] Vehicles);
    internal sealed record AnimalCheckpoint(int Id, int Tile, int TargetTile, CheckpointPosition Position,
        CheckpointPosition PreviousPosition, CheckpointPosition Target, float Yaw, float PreviousYaw, float Blend,
        float Hunger, float WanderNeed, double WalkTime, double Age, uint Seed);
    internal sealed record ForageCheckpoint(int Tile, float Depletion);
    internal sealed record WildlifeCheckpoint(AnimalCheckpoint[] Animals, ForageCheckpoint[] Forage,
        ulong ForestRevision, ulong SurfaceRevision);
    internal sealed record HarvestCheckpoint(int[] Tiles, float InitialVolume);
    internal sealed record MillCheckpoint(int TileId, int[] Footprint, CheckpointPosition Position, float Received, float Stock, float Processed);
    internal sealed record LogisticsCheckpoint(HarvestCheckpoint[] Sites, MillCheckpoint[] Mills, string Status);
    internal sealed record WorldCheckpointData(int Version, ulong Tick, int CommandCursor, int PendingCommands,
        TerrainCheckpoint Terrain, EcologyCheckpoint Ecology, WildlifeCheckpoint Wildlife, LogisticsCheckpoint Logistics,
        VehiclesCheckpoint Vehicles, float AvailableTimber, float DeliveredTimber, EffectCheckpoint[] Effects,
        ForestryActionResult LastAction, ForestryAreaSummary LastArea);
}
