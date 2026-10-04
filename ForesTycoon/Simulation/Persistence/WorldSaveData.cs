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
        public const int CurrentVersion = 4;
        public int Version { get; init; } = CurrentVersion;
        public double TickRate { get; init; } = 30.0;
        public ulong Tick { get; init; }
        public TerrainSettingsData Terrain { get; init; } = new TerrainSettingsData();
        public List<WorldCommandRecord> Commands { get; init; } = new List<WorldCommandRecord>();

        public void Validate()
        {
            if (Version != CurrentVersion)
                throw new NotSupportedException($"Save version {Version} is not supported; expected {CurrentVersion}.");
            if (!double.IsFinite(TickRate) || TickRate <= 0.0)
                throw new InvalidOperationException("Save tick rate must be positive.");
            if (Terrain == null) throw new InvalidOperationException("Save has no terrain settings.");
            if (Commands == null) throw new InvalidOperationException("Save has no command journal.");
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
