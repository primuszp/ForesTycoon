using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace ForesTycoon;

internal static partial class WorldLoader
{
    private const string SnapshotPrefix = "world-gzip-v1:";

    /// <summary>Capture a portable scene without embedding its command history or previous scene documents.</summary>
    internal static WorldScenario CaptureScenario(GameWorld world)
    {
        if (world.Map.Settings.TileColumns != world.Map.Settings.TileRows)
            throw new InvalidOperationException("A szerkeszthető jelenet térképe négyzetes legyen.");
        using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0;
        var source = WorldSaveSerializer.Read(saved);
        if (source.Checkpoint.PendingCommands != 0)
            throw new InvalidOperationException("A jelenet rögzítése előtt hajtsd végre a függő parancsokat.");
        var initial = new WorldSaveData {
            TickRate = source.TickRate, ForestYearSeconds = source.ForestYearSeconds,
            SoilModel = source.SoilModel, Climate = source.Climate, Tick = source.Tick, Terrain = source.Terrain,
            Checkpoint = source.Checkpoint with { CommandCursor = 0, PendingCommands = 0, Algorithms = null }
        };
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Optimal, leaveOpen: true)) WorldSaveSerializer.Write(gzip, initial);
        var scenario = GameWorld.DefaultScenario(source.Terrain.Seed, source.Terrain.NodeColumns - 1);
        scenario.Name = "Rögzített jelenet";
        scenario.InitialState = SnapshotPrefix + Convert.ToBase64String(packed.ToArray());
        scenario.SoilTypes = GameWorld.EditableSoils(world.Soils.Definition.Catalog);
        var previous = world.RuleProjectDocument.World;
        if (previous != null) { scenario.Species = previous.Species; scenario.Springs = previous.Springs; }
        scenario.Validate();
        return scenario;
    }

    internal static RuleProject OpeningProject()
    {
        using var world = CreateOpening(false);
        var project = world.RuleProjectDocument;
        project.World = CaptureScenario(world);
        project.World.Name = "Nyitójelenet";
        return project;
    }

    internal static void RestoreInitialState(GameWorld world, WorldScenario scenario)
    {
        var saved = ReadInitialState(scenario);
        using var stream = new MemoryStream(); WorldSaveSerializer.Write(stream, saved); stream.Position = 0;
        world.Load(stream);
    }

    // Pure parsing/remapping. Safe on a worker; never reads or changes the live world or species context.
    internal static WorldSaveData ReadInitialState(WorldScenario scenario)
    {
        scenario.Validate();
        using var unpacked = new MemoryStream();
        try {
            using var packed = new MemoryStream(Convert.FromBase64String(scenario.InitialState[SnapshotPrefix.Length..]));
            using var gzip = new GZipStream(packed, CompressionMode.Decompress);
            var buffer = new byte[81920]; int count;
            while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0) {
                if (unpacked.Length + count > 128 * 1024 * 1024) throw new InvalidDataException("A kiinduló jelenet túl nagy.");
                unpacked.Write(buffer, 0, count);
            }
        } catch (FormatException error) { throw new InvalidDataException("Sérült kiinduló jelenet.", error); }
        unpacked.Position = 0;
        var saved = WorldSaveSerializer.Read(unpacked);
        if (saved.Checkpoint == null || saved.Checkpoint.Tick != saved.Tick || saved.Checkpoint.PendingCommands != 0 || saved.Commands.Count != 0)
            throw new InvalidDataException("A kiinduló jelenet teljes, parancselőzmény nélküli állapotmentés legyen.");
        if (saved.Terrain.Seed != scenario.Seed || saved.Terrain.NodeColumns != scenario.Size + 1 || saved.Terrain.NodeRows != scenario.Size + 1)
            throw new InvalidDataException("A rögzített jelenet mérete és seedje nem változtatható; ehhez új generált pályát kell létrehozni.");
        saved.ValidateReplay();
        return PrepareSceneSnapshot(saved, scenario);
    }
}
