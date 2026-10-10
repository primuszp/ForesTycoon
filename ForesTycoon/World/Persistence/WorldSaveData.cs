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
        PlaceSawmill,
        RoadRepair,
        SkidTrailPath,
        PlaceDepot,
        SendVehicle,
        SendHome,
        StackSite,
        SetRuleModel,
        SetTuning,
        SetBehaviors
    }

    readonly record struct WorldCommandRecord(
        ulong Tick,
        WorldCommandKind Kind,
        int A,
        int B,
        int C,
        int D,
        bool Flag,
        string RuleJson = null);

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
        public const int CurrentVersion = 16;
        // Bump whenever native simulation semantics change. Editable graphs and tuning
        // are stored separately in the checkpoint/journal; this identifies their runtime.
        public const string CurrentRuntimeRulesVersion = "forestycoon-simulation/2026-10-10.6";
        public string RuntimeRulesVersion { get; init; } = CurrentRuntimeRulesVersion;
        public WorldSaveData() { }
        // Missing JSON must not silently inherit today's runtime identifier.
        [System.Text.Json.Serialization.JsonConstructor]
        public WorldSaveData(string runtimeRulesVersion) => RuntimeRulesVersion = runtimeRulesVersion;
        public int Version { get; init; } = CurrentVersion;
        public double TickRate { get; init; } = 30.0;
        public double ForestYearSeconds { get; init; } = EnvironmentSystem.SecondsPerForestYear;
        public SoilModelData SoilModel { get; init; }
        public ClimateDefinition Climate { get; init; }
        internal ClimateDefinition ReplayClimate => Version < 9 ? ClimateDefinition.Legacy :
            Climate ?? throw new InvalidOperationException("Save has no climate model.");
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
            if (Version != 4 && Version != 5 && Version != 6 && Version != 7 && Version != 8 && Version != 9 && Version != 10 && Version != 11 && Version != 12 && Version != 13 && Version != 14 && Version != 15 && Version != CurrentVersion)
                throw new NotSupportedException($"Save version {Version} is not supported; expected {CurrentVersion}.");
            bool migratedRuntime = (Version == 12 && RuntimeRulesVersion == "forestycoon-simulation/2026-10-10.1")
                || (Version == 13 && RuntimeRulesVersion is "forestycoon-simulation/2026-10-10.2" or "forestycoon-simulation/2026-10-10.3")
                || (Version == 14 && RuntimeRulesVersion == "forestycoon-simulation/2026-10-10.4")
                || (Version == 15 && RuntimeRulesVersion == "forestycoon-simulation/2026-10-10.5");
            if (Version >= 12 && !migratedRuntime && !string.Equals(RuntimeRulesVersion, CurrentRuntimeRulesVersion, StringComparison.Ordinal))
                throw new NotSupportedException($"Simulation rules runtime '{RuntimeRulesVersion ?? "missing"}' is not supported; expected '{CurrentRuntimeRulesVersion}'.");
            if (!EnvironmentSystem.IsValidForestYearSeconds(ReplayForestYearSeconds))
                throw new InvalidOperationException("Save forest year duration must be between 120 and 1200 seconds.");
            if (!double.IsFinite(TickRate) || TickRate <= 0.0)
                throw new InvalidOperationException("Save tick rate must be positive.");
            if (Terrain == null) throw new InvalidOperationException("Save has no terrain settings.");
            if (Commands == null) throw new InvalidOperationException("Save has no command journal.");
            _ = ReplaySoilModel;
            _ = ReplayClimate;
            if (Checkpoint != null) {
                if (Version >= 13) CheckpointGuard.Require(Checkpoint.Ecology?.Environment?.Snow != null, "seasonal snow checkpoint");
                if (Checkpoint.Rules != null) { var rules = new CompiledRoadRule(Checkpoint.Rules); rules.ValidateRange(); }
                if (Version >= 10 && Checkpoint.Rules == null) throw new InvalidOperationException("Save has no rule model.");
                if (Checkpoint.Tuning != null) _ = GameTuning.FromOverrides(Checkpoint.Tuning);
                if (Version >= 14 && Checkpoint.Behaviors == null) throw new InvalidOperationException("Save has no behavior model.");
                if (Checkpoint.Behaviors != null) _ = new WorldBehaviorPolicy(Checkpoint.Behaviors);
                if (Version >= 15 && Checkpoint.BehaviorStates == null) throw new InvalidOperationException("Save has no controller state.");
                CheckpointGuard.Require(Version >= 8 && Checkpoint.Version is 1 or 2 &&
                    (Version < 11 || Checkpoint.Version == 2), "world snapshot version");
                CheckpointGuard.Require(double.IsFinite(Checkpoint.RoadWeatherSeconds) && Checkpoint.RoadWeatherSeconds >= 0 &&
                    Checkpoint.RoadWeatherSeconds < 0.5, "road weather remainder");
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
