using System;
using System.Linq;

namespace ForesTycoon.Ecology
{
    internal sealed record WeatherCheckpoint(uint Random, double Time, double EventStart, double EventEnd,
        WeatherPreset Preset, double PeakRain, double EventRain, double TotalRain, double PreviousCloud);
    internal sealed record RuntimeCheckpoint(double Time, double PendingSeconds, ulong CompletedSteps);
    internal sealed record EnvironmentCheckpoint(double[][] Fields, double Remainder, double MonthSeconds,
        double RadiationIntegral, double Evaporated, double Transpired, double Outflow, double InitialWater,
        ulong Revision, WeatherCheckpoint Weather, ForestHydrologyInputs[] Vegetation, ulong VegetationRevision,
        double? RainReceived = null);
    internal sealed record TreePatchCheckpoint(int TileId, ForestTree[] Trees, ForestTreeStump[] Stumps,
        ForestDeadTree[] DeadTrees, float Depot, ulong Revision, ulong TopologyRevision);
    internal sealed record TreeStoreCheckpoint(TreePatchCheckpoint[] Patches, ulong NextId, ulong TopologyRevision,
        ulong Generation, int[] Slots, int[] FreeSlots);
    internal sealed record PlantationCheckpoint(int TileId, ForestPlantation Plantation);
    internal sealed record ForestCheckpoint(ulong Month, double AccumulatedSeconds, ulong Revision, ulong EditRevision,
        double CurrentYearGrowth, double LastYearGrowth, int NextPlantationId, ulong PlantationRevision,
        ForestStand[] Stands, TreeStoreCheckpoint Trees, PlantationCheckpoint[] Plantations,
        CompetitionCheckpoint Competition, PreparationCheckpoint Preparation);
    internal sealed record EcologyCheckpoint(int Version, double ForestYearSeconds, string SoilHash, byte[] SoilProfiles,
        RuntimeCheckpoint Clock, ForestCheckpoint Forest, EnvironmentCheckpoint Environment, ClimateDefinition Climate = null);

    internal sealed partial class EnvironmentSystem
    {
        private double[][] StateFields => new[] { canopy, surface, soil, deep, drought, wet, wetIntegral,
            demandIntegral, uptakeIntegral, uptakeRate, demandRate };
        internal EnvironmentCheckpoint Capture() => new(StateFields.Select(a => (double[])a.Clone()).ToArray(),
            remainder, monthSeconds, radiationIntegral, Evaporated, Transpired, Outflow, InitialWater, Revision, weather.Capture(),
            (ForestHydrologyInputs[])vegetation.Clone(), vegetationRevision, RainReceived);

        internal void Restore(EnvironmentCheckpoint state)
        {
            ArgumentNullException.ThrowIfNull(state);
            var fields = StateFields;
            CheckpointGuard.Length(state.Fields, fields.Length, "water fields");
            for (int f = 0; f < fields.Length; f++)
            {
                CheckpointGuard.Length(state.Fields[f], CellCount, "water raster length");
                foreach (double value in state.Fields[f]) CheckpointGuard.NonNegative(value, "water raster value");
            }
            for (int id = 0; id < CellCount; id++)
            {
                CheckpointGuard.Require(state.Fields[2][id] <= soils[id].Saturation + 1e-9, "soil saturation");
                CheckpointGuard.Unit(state.Fields[4][id], "drought"); CheckpointGuard.Unit(state.Fields[5][id], "waterlogging");
            }
            CheckpointGuard.Require(state.Remainder >= -1e-9 && state.Remainder < StepSeconds && double.IsFinite(state.Remainder), "water remainder");
            foreach (double v in new[] { state.MonthSeconds, state.RadiationIntegral, state.Evaporated, state.Transpired,
                state.Outflow, state.InitialWater }) CheckpointGuard.NonNegative(v, "water scalar");
            CheckpointGuard.Require(state.InitialWater == InitialWater, "initial water/model mismatch");
            CheckpointGuard.Length(state.Vegetation, CellCount, "vegetation water inputs");
            foreach (var input in state.Vegetation)
            {
                CheckpointGuard.Unit(input.Cover, "vegetation cover");
                CheckpointGuard.NonNegative(input.InterceptionCapacity, "interception capacity");
                CheckpointGuard.NonNegative(input.LeafAreaIndex, "leaf area index");
            }
            weather.Restore(state.Weather);
            CheckpointGuard.Require(Climate.Uniform || state.RainReceived.HasValue, "regional rain ledger");
            regionalRainReceived = state.RainReceived ?? weather.TotalRain * CellCount;
            CheckpointGuard.NonNegative(regionalRainReceived, "received precipitation");
            if (Climate.Uniform) CheckpointGuard.Require(regionalRainReceived == weather.TotalRain * CellCount, "uniform precipitation ledger");
            for (int i = 0; i < fields.Length; i++) state.Fields[i].CopyTo(fields[i], 0);
            remainder = state.Remainder; monthSeconds = state.MonthSeconds; radiationIntegral = state.RadiationIntegral;
            Evaporated = state.Evaporated; Transpired = state.Transpired; Outflow = state.Outflow; Revision = state.Revision;
            state.Vegetation.CopyTo(vegetation, 0); vegetationRevision = state.VegetationRevision;
            surfaceFlux.Cancel(); RefreshRouting(); Summarize();
            double scale = Math.Max(1, InitialWater + RainReceived);
            CheckpointGuard.Require(Math.Abs(BalanceError) <= scale * 1e-9, "water balance");
        }
    }

    internal sealed partial class WeatherSystem
    {
        internal WeatherCheckpoint Capture() => new(random, Time, EventStart, EventEnd, Preset, PeakRain, EventRain, TotalRain, previousCloud);
        internal void Restore(WeatherCheckpoint s)
        {
            ArgumentNullException.ThrowIfNull(s);
            CheckpointGuard.Require(s.Random != 0 && Enum.IsDefined(s.Preset) && s.Preset != WeatherPreset.Snow, "weather RNG/preset");
            foreach (double v in new[] { s.Time, s.EventStart, s.EventEnd, s.PeakRain, s.EventRain, s.TotalRain })
                CheckpointGuard.NonNegative(v, "weather scalar");
            CheckpointGuard.Require(s.EventStart <= s.Time && s.Time <= s.EventEnd + 1e-8 && s.EventEnd > s.EventStart &&
                s.EventRain <= s.TotalRain, "weather interval");
            CheckpointGuard.Unit(s.PreviousCloud, "cloud transition");
            random = s.Random; Time = s.Time; EventStart = s.EventStart; EventEnd = s.EventEnd;
            Preset = s.Preset; PeakRain = s.PeakRain; EventRain = s.EventRain; TotalRain = s.TotalRain; previousCloud = s.PreviousCloud;
        }
    }
}
