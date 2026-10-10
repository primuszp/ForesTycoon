using System;

namespace ForesTycoon
{
    static class WorldCommandFactory
    {
        public static IWorldCommand Create(WorldCommandRecord record) => record.Kind switch
        {
            WorldCommandKind.SetRuleModel => new SetRuleModelCommand(record.RuleJson),
            WorldCommandKind.SetTuning => new SetTuningCommand(record.RuleJson),
            WorldCommandKind.EditElevation => new EditElevationCommand(record.A, record.B, record.C, record.D, record.Flag),
            // C carries the surface; old saves have 0, the asphalt every earlier road was built with.
            WorldCommandKind.RoadPath => new RoadPathCommand(record.A, record.B, record.Flag,
                Enum.IsDefined((RoadPaving)record.C) ? (RoadPaving)record.C
                    : throw new InvalidOperationException($"Unknown road surface: {record.C}.")),
            WorldCommandKind.RoadRepair => new RoadRepairCommand(record.A, record.B),
            WorldCommandKind.SkidTrailPath => new SkidTrailPathCommand(record.A, record.B, record.Flag),
            WorldCommandKind.PlaceDepot => new PlaceDepotCommand(record.A),
            WorldCommandKind.SendVehicle => new SendVehicleCommand(record.A, record.B, record.C - 1, record.Flag),
            WorldCommandKind.StackSite => new StackSiteCommand(record.A, record.Flag),
            WorldCommandKind.SendHome => new SendHomeCommand(record.A, record.Flag),
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
