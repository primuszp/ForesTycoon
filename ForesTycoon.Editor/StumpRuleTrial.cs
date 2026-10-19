using System;
using System.IO;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor;

/// <summary>A controlled sandbox with four real stumps, run by the game's rule engine.</summary>
internal static class StumpRuleTrial
{
    internal sealed record Case(string Name, double Moisture, double Diameter, double OneYearProgress, double? RemovedAtYears);
    internal sealed record Result(Case[] Cases, double StepYears, double DurationYears);
    private static readonly (string Name, float Moisture, float Diameter)[] Cases = {
        ("Szárazabb · kisebb", .2f, .15f), ("Nedvesebb · kisebb", .8f, .15f),
        ("Szárazabb · nagyobb", .2f, .60f), ("Nedvesebb · nagyobb", .8f, .60f)
    };

    internal static GameWorld CreateWorld(RuleProject source, string ruleId)
    {
        var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        try {
            // A controlled trial must not depend on the authored world's natural density or species mix.
            var habitat = (IForestHabitat)world.Map;
            var trialTiles = Enumerable.Range(0, world.Map.Tiles.Count).Where(habitat.CanSupportForest).Take(4).ToArray();
            if (trialTiles.Length != 4) throw new InvalidDataException("Nincs négy erdősíthető cella a próbavilágban.");
            world.Forest.Clear();
            world.Forest.PaintInitialForest(trialTiles.ToDictionary(id => id, _ => ForestSpecies.Oak));
            foreach (int tile in trialTiles) world.Forest.Harvest(tile, out _);
            using var stream = new MemoryStream(); world.Save(stream); stream.Position = 0;
            var data = WorldSaveSerializer.Read(stream);
            var patches = data.Checkpoint.Ecology.Forest.Trees.Patches;
            var tiles = patches.Where(p => p.Stumps.Length > 0).Take(4).Select(p => p.TileId).ToArray();
            if (tiles.Length != 4) throw new InvalidDataException("A próbához négy tönköt tartalmazó cella szükséges.");
            for (int i = 0; i < patches.Length; i++) {
                var patch = patches[i]; int c = Array.IndexOf(tiles, patch.TileId);
                if (c < 0) { patches[i] = patch with { Stumps = Array.Empty<ForestTreeStump>() }; continue; }
                var stump = patch.Stumps[0];
                stump = stump with { DecayProgress = 0, Felled = stump.Felled with {
                    Dimensions = stump.Felled.Dimensions with { Diameter = Cases[c].Diameter }
                } };
                patches[i] = patch with { Stumps = new[] { stump } };
                data.Checkpoint.Terrain.Moisture[patch.TileId] = Cases[c].Moisture;
            }
            stream.SetLength(0); WorldSaveSerializer.Write(stream, data); stream.Position = 0; world.Load(stream);
            var project = source.Clone(); project.World = null; project.Algorithms = null;
            foreach (var rule in project.Rules) rule.Enabled = rule.Id == ruleId;
            if (!project.Rules.Any(r => r.Id == ruleId && r.Scope == "stump")) throw new InvalidDataException("Válassz tönkszabályt a próbához.");
            world.QueueRuleProject(project); world.ExecutePendingCommands();
            return world;
        } catch { world.Dispose(); throw; }
    }

    internal static Result Run(RuleProject source, string ruleId)
    {
        using var world = CreateWorld(source, ruleId);
        var scope = RuleEngine.Scope("stump");
        var moisture = scope.Fields.Single(f => f.Name == "tile.moisture");
        var diameter = scope.Fields.Single(f => f.Name == "diameter");
        var progress = scope.Fields.Single(f => f.Name == "decayProgress");
        var oneYear = new double[4]; var removed = new double?[4];
        double secondsPerYear = world.CaptureCheckpoint().Ecology.ForestYearSeconds;
        const double step = .05, duration = 40;
        for (int tick = 1; tick <= (int)(duration / step); tick++) {
            world.RuleBook.Run(world, step, tick * step * secondsPerYear);
            if (world.RuleBook.LastError != null) throw new InvalidDataException(world.RuleBook.LastError);
            var ids = scope.Elements(world);
            for (int c = 0; c < Cases.Length; c++) {
                int id = ids.FirstOrDefault(id => Math.Abs(moisture.Read(world, id) - Cases[c].Moisture) < 1e-6
                    && Math.Abs(diameter.Read(world, id) - Cases[c].Diameter) < 1e-6, -1);
                if (id < 0) removed[c] ??= tick * step;
                if (tick == 20) oneYear[c] = id < 0 ? 1 : progress.Read(world, id);
            }
            if (tick >= 20 && removed.All(t => t.HasValue)) break;
        }
        return new(Cases.Select((c, i) => new Case(c.Name, c.Moisture, c.Diameter, oneYear[i], removed[i])).ToArray(), step, duration);
    }
}
