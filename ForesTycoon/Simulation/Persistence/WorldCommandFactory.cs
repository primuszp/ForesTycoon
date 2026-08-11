using System;

namespace ForesTycoon
{
    static class WorldCommandFactory
    {
        public static IWorldCommand Create(WorldCommandRecord record) => record.Kind switch
        {
            WorldCommandKind.EditElevation => new EditElevationCommand(record.A, record.B, record.C, record.D),
            WorldCommandKind.RoadPath => new RoadPathCommand(record.A, record.B, record.Flag),
            WorldCommandKind.SpawnVehicle => new SpawnVehicleCommand(),
            WorldCommandKind.PlantForest => new PlantForestCommand(record.A, (ForestSpecies)record.B),
            WorldCommandKind.HarvestForest => new HarvestForestCommand(record.A),
            _ => throw new InvalidOperationException($"Unknown world command kind: {record.Kind}.")
        };
    }
}
