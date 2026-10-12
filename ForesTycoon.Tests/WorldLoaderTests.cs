using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class WorldLoaderTests
{
    [Fact]
    public void OpeningMatchesTheRecordedPreLoaderScene()
    {
        using var world = WorldLoader.CreateOpening(false);
        var expected = JsonSerializer.Deserialize<SortedDictionary<string, string>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "OpeningReference.json")))!;
        var actual = OpeningSceneReference.Fingerprints(world);
        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected) Assert.Equal(pair.Value, actual[pair.Key]);
        Assert.False(world.HasPresentation);
    }

    [Fact]
    public void ProjectLoaderMatchesScenarioRestartIncludingPaintAndRules()
    {
        var project = GameWorld.DefaultRuleProject();
        project.World = GameWorld.DefaultScenario(11, 16);
        using var legacy = new GameWorld(GameWorld.ScenarioSettings(project.World), enableRendering: false);
        int dry = Enumerable.Range(0, 256).First(t => t > 70 && legacy.TileSpecies(t) != ForestSpecies.None);
        project.World.ForestPaint[dry] = "Oak";
        project.World.HeightPaint[0] = 0;
        project.World.SoilPaint[dry] = project.World.SoilTypes.Last().Id;
        project.World.Springs[dry] = 2;
        legacy.QueueRuleProject(project); legacy.ExecutePendingCommands();
        legacy.StartScenario(project.World);
        using var loaded = WorldLoader.CreateProject(project);
        Assert.Equal(JsonSerializer.Serialize(legacy.CaptureCheckpoint()), JsonSerializer.Serialize(loaded.CaptureCheckpoint()));
        Assert.Equal(0, loaded.Map.NodeHeight(0));
        Assert.Equal(ForestSpecies.Oak, loaded.TileSpecies(dry));
        Assert.Equal(project.World.SoilTypes.Count - 1, loaded.SoilIndex(dry));
        string original = loaded.RuleProjectDocument.ToJson();
        project.World.ForestPaint.Clear();
        Assert.Equal(original, loaded.RuleProjectDocument.ToJson());
        for (int i = 0; i < 30; i++) { legacy.Update(1d / 30); loaded.Update(1d / 30); }
        Assert.Equal(JsonSerializer.Serialize(legacy.CaptureCheckpoint()), JsonSerializer.Serialize(loaded.CaptureCheckpoint()));
    }

    [Fact]
    public void RecordedOpeningSaveCanStillBeLoaded()
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "OpeningReference.world.json.gz"));
        using var zip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var restored = WorldLoader.CreateOpening(false);
        restored.Load(zip, allowAlgorithmMigration: true);
        var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "OpeningReference.json")))!;
        var actual = OpeningSceneReference.Fingerprints(restored);
        foreach (var pair in expected) Assert.Equal(pair.Value, actual[pair.Key]);
    }

    [Fact]
    public void OpeningSaveRoundTripPreservesState()
    {
        using var original = WorldLoader.CreateOpening(false);
        using var stream = new MemoryStream(); original.Save(stream); stream.Position = 0;
        using var restored = WorldLoader.CreateOpening(false); restored.Load(stream);
        Assert.Equal(OpeningSceneReference.Fingerprints(original).ToArray(), OpeningSceneReference.Fingerprints(restored).ToArray());
    }
}
