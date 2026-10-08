using System;

namespace ForesTycoon
{
    static class WorldCommandFactory
    {
        public static IWorldCommand Create(WorldCommandRecord record) => record.Kind switch
        {
            WorldCommandKind.EditElevation => new EditElevationCommand(record.A, record.B, record.C, record.D, record.Flag),
            WorldCommandKind.RoadPath => new RoadPathCommand(record.A, record.B, record.Flag),
            WorldCommandKind.PlaceSawmill => new PlaceSawmillCommand(record.A),
            WorldCommandKind.SpawnVehicle => new SpawnVehicleCommand(),
            WorldCommandKind.SetWeather => new SetWeatherCommand((WeatherPreset)record.A,record.B,record.C),
            WorldCommandKind.PlantForest => new PlantForestCommand(record.A, (ForestSpecies)record.B),
            WorldCommandKind.HarvestForest => new HarvestForestCommand(record.A),
            WorldCommandKind.PlantForestArea =>
                new PlantForestAreaCommand(record.A, record.B, (ForestSpecies)record.C),
            WorldCommandKind.HarvestForestArea => new HarvestForestAreaCommand(record.A, record.B),
            _ => throw new InvalidOperationException($"Unknown world command kind: {record.Kind}.")
        };
    }
}
