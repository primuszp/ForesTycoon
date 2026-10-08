using System;
using System.Collections.Generic;

namespace ForesTycoon
{
    enum WorldCommandKind
    {
        EditElevation,
        RoadPath,
        SpawnVehicle,
        PlantForest,
        HarvestForest,
        PlantForestArea,
        HarvestForestArea,
        SetWeather,
        PlaceSawmill
    }

    readonly record struct WorldCommandRecord(
        ulong Tick,
        WorldCommandKind Kind,
        int A,
        int B,
        int C,
        int D,
        bool Flag);

    sealed class TerrainSettingsData
    {
        public int NodeColumns { get; init; }
        public int NodeRows { get; init; }
        public int TileWidth { get; init; }
        public int TileHeight { get; init; }
        public int HeightScale { get; init; }
        public float MinimumWaterDepth { get; init; }
        public float RiverWaterHeight { get; init; }
        public float SeaLevel { get; init; }
        public int Seed { get; init; }
        public int MaxHeight { get; init; }
        public ForestPattern ForestPattern { get; init; }

        public static TerrainSettingsData From(TerrainSettings settings) => new TerrainSettingsData
        {
            NodeColumns = settings.NodeColumns,
            NodeRows = settings.NodeRows,
            TileWidth = settings.TileWidth,
            TileHeight = settings.TileHeight,
            HeightScale = settings.HeightScale,
            MinimumWaterDepth = settings.MinimumWaterDepth,
            RiverWaterHeight = settings.RiverWaterHeight,
            SeaLevel = settings.SeaLevel,
            Seed = settings.Seed,
            MaxHeight = settings.MaxHeight,
            ForestPattern = settings.ForestPattern
        };

        public TerrainSettings ToSettings() => new TerrainSettings(
            NodeColumns, NodeRows, TileWidth, TileHeight, HeightScale,
            MinimumWaterDepth, RiverWaterHeight, SeaLevel, Seed, MaxHeight, ForestPattern);
    }

    sealed class WorldSaveData
    {
        public const int CurrentVersion = 8;
        public int Version { get; init; } = CurrentVersion;
        public double TickRate { get; init; } = 30.0;
        public double ForestYearSeconds { get; init; } = EnvironmentSystem.SecondsPerForestYear;
        public SoilModelData SoilModel { get; init; }
        public WorldCheckpointData Checkpoint { get; init; }
        internal SoilLandscapeDefinition ReplaySoilModel => Version < 7 ? SoilLandscapeDefinition.Legacy :
            (SoilModel ?? throw new InvalidOperationException("Save has no soil model.")).ToDefinition();
        internal double ReplayForestYearSeconds => Version == 4 ? EnvironmentSystem.SecondsPerForestYear : ForestYearSeconds;
        internal bool ReplayLegacyTerrainEdits => Version < 6;
        // In v6 the previously unused elevation Flag identifies historical v4/v5 edit semantics.
        internal WorldCommandRecord ReplayCommand(WorldCommandRecord record) =>
            ReplayLegacyTerrainEdits && record.Kind == WorldCommandKind.EditElevation ? record with { Flag = true } : record;
        public ulong Tick { get; init; }
        public TerrainSettingsData Terrain { get; init; } = new TerrainSettingsData();
        public List<WorldCommandRecord> Commands { get; init; } = new List<WorldCommandRecord>();

        public void Validate()
        {
            if (Version != 4 && Version != 5 && Version != 6 && Version != 7 && Version != CurrentVersion)
                throw new NotSupportedException($"Save version {Version} is not supported; expected {CurrentVersion}.");
            if (!EnvironmentSystem.IsValidForestYearSeconds(ReplayForestYearSeconds))
                throw new InvalidOperationException("Save forest year duration must be between 120 and 1200 seconds.");
            if (!double.IsFinite(TickRate) || TickRate <= 0.0)
                throw new InvalidOperationException("Save tick rate must be positive.");
            if (Terrain == null) throw new InvalidOperationException("Save has no terrain settings.");
            if (Commands == null) throw new InvalidOperationException("Save has no command journal.");
            _ = ReplaySoilModel;
            if (Checkpoint != null) {
                CheckpointGuard.Require(Version >= 8 && Checkpoint.Version == 1, "world snapshot version");
                CheckpointGuard.Require(Checkpoint.Tick <= Tick && Checkpoint.CommandCursor >= 0 && Checkpoint.PendingCommands >= 0 &&
                    (long)Checkpoint.CommandCursor + Checkpoint.PendingCommands <= Commands.Count, "world snapshot cursor");
                CheckpointGuard.Require(Checkpoint.Terrain != null && Checkpoint.Ecology != null && Checkpoint.Wildlife != null &&
                    Checkpoint.Logistics != null && Checkpoint.Vehicles != null && Checkpoint.Effects != null, "world snapshot systems");
            }
        }

        internal void ValidateReplay()
        {
            Validate();
            if (!double.IsFinite(1.0 / TickRate)) throw new InvalidOperationException("Save tick duration must be finite.");
            ulong previousTick = 0;
            for (int i = 0; i < Commands.Count; i++)
            {
                var record = Commands[i];
                if (record.Tick > Tick || (i > 0 && record.Tick < previousTick))
                    throw new System.IO.InvalidDataException("Save commands are not in deterministic tick order.");
                WorldCommandFactory.Create(record);
                previousTick = record.Tick;
            }
        }
    }
}
