using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ForesTycoon;

internal static partial class SimulationLoader
{
    private static WorldSaveData PrepareSceneSnapshot(WorldSaveData saved, WorldScenario scenario)
    {
        var original = saved.ReplaySoilModel.Catalog;
        // Keep the exact original catalog (including implicit built-in values) in the unedited scene.
        bool sameTypes = JsonSerializer.Serialize(scenario.SoilTypes) == JsonSerializer.Serialize(SimulationWorld.EditableSoils(original));
        if (scenario.SoilPaint.Count == 0 && sameTypes) return saved;

        var catalog = SimulationWorld.SoilModel(scenario).Catalog;
        var indices = scenario.SoilTypes.Select((s, i) => (s.Id, Index: (byte)i)).ToDictionary(p => p.Id, p => p.Index);
        var source = saved.Checkpoint.Ecology;
        var raster = new byte[source.SoilProfiles.Length];
        var overrides = new Dictionary<int, byte>();
        for (int tile = 0; tile < raster.Length; tile++) {
            string id = scenario.SoilPaint.TryGetValue(tile, out var painted) ? painted : original[source.SoilProfiles[tile]].Id;
            if (!indices.TryGetValue(id, out byte index))
                throw new InvalidDataException("A térképen használt talajtípus nem törölhető: " + id);
            raster[tile] = index;
            // Pin every original tile. Catalog order and generator preferences cannot move existing soil patches.
            overrides[tile] = index;
        }
        if (sameTypes && raster.SequenceEqual(source.SoilProfiles)) return saved;

        var water = source.Environment;
        var fields = water.Fields.Select(a => (double[])a.Clone()).ToArray();
        for (int tile = 0; tile < raster.Length; tile++) {
            double excess = Math.Max(0, fields[2][tile] - catalog[raster[tile]].Properties.Saturation);
            fields[2][tile] -= excess;
            fields[1][tile] += excess;
        }
        var ecology = source with {
            SoilHash = catalog.Hash, SoilProfiles = raster,
            Environment = water with { Fields = fields, InitialWaterBeforeSoilEdit = water.InitialWater, Revision = water.Revision + 1 },
            // Predictions prepared under the old soil cannot be used at the next monthly boundary.
            Forest = source.Forest with { Preparation = new ForestMonthlyPreparation().Capture() }
        };
        var replacement = new WorldSaveData {
            Version = saved.Version, RuntimeRulesVersion = saved.RuntimeRulesVersion,
            TickRate = saved.TickRate, ForestYearSeconds = saved.ForestYearSeconds,
            SoilModel = SoilModelData.From(new SoilLandscapeDefinition(1, catalog, overrides)),
            Climate = saved.Climate, Tick = saved.Tick, Terrain = saved.Terrain,
            Checkpoint = saved.Checkpoint with { Ecology = ecology }
        };
        return replacement;
    }
}
