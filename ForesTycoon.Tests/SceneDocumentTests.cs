using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class SceneDocumentTests
{
    private static RuleProject Project() => RuleProject.FromJson(WorldLoader.OpeningProject().ToJson());
    private static string State(GameWorld world) => JsonSerializer.Serialize(world.CaptureCheckpoint());

    [Fact]
    public void PortableOpeningProjectPreservesTheReferenceSceneExactly()
    {
        var project = Project();
        using var loaded = WorldLoader.CreateProject(project);
        var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "OpeningReference.json")))!;
        var actual = OpeningSceneReference.Fingerprints(loaded);
        foreach (var pair in expected)
            if (pair.Key != "CommandCursor") Assert.Equal(pair.Value, actual[pair.Key]);
        Assert.Equal(1, loaded.CaptureCheckpoint().CommandCursor);
        Assert.NotNull(loaded.RuleProjectDocument.World.InitialState);
    }

    [Fact]
    public void ForestPaintingPreservesEveryUntouchedTreePatchAndRestartsDeterministically()
    {
        var project = Project();
        using var original = WorldLoader.CreateProject(project);
        var patches = original.CaptureCheckpoint().Ecology.Forest.Trees.Patches;
        int tile = patches.First(p => p.Trees.Length > 0).TileId;
        project.World.ForestPaint[tile] = "Oak";
        using var edited = WorldLoader.CreateProject(project);
        var after = edited.CaptureCheckpoint().Ecology.Forest.Trees.Patches.ToDictionary(p => p.TileId);
        Assert.Equal(ForestSpecies.Oak, edited.TileSpecies(tile));
        foreach (var patch in patches.Where(p => p.TileId != tile))
            Assert.Equal(JsonSerializer.Serialize(patch), JsonSerializer.Serialize(after[patch.TileId]));
        string initial = State(edited);
        edited.Update(1d / 30);
        edited.StartScenario(project.World, project);
        Assert.Equal(initial, State(edited));
        using var save = new MemoryStream(); edited.Save(save); save.Position = 0;
        using var restored = WorldLoader.CreateOpening(false); restored.Load(save);
        Assert.Equal(initial, State(restored));
        edited.Update(1d / 30); restored.Update(1d / 30);
        Assert.Equal(State(edited), State(restored));
    }

    [Fact]
    public void TerrainPaintCanBeRemovedToRecoverTheExactBaseline()
    {
        var project = Project();
        using var world = WorldLoader.CreateProject(project);
        string baseline = State(world);
        int target = world.Map.NodeHeight(0) == 0 ? 1 : 0;
        project.World.HeightPaint[0] = target;
        world.StartScenario(project.World, project);
        Assert.Equal(target, world.Map.NodeHeight(0));
        project.World.HeightPaint.Clear();
        world.StartScenario(project.World, project);
        Assert.Equal(baseline, State(world));
    }

    [Fact]
    public void InvalidSnapshotAndInvalidSoilChangesLeaveTheLiveWorldIntact()
    {
        var project = Project();
        using var world = WorldLoader.CreateProject(project);
        string baseline = State(world);
        var broken = project.Clone(); broken.World.InitialState = "world-gzip-v1:invalid";
        Assert.Throws<InvalidDataException>(() => world.StartScenario(broken.World, broken));
        Assert.Equal(baseline, State(world));
        project.World.SoilPaint[0] = "missing-soil";
        Assert.Throws<InvalidDataException>(() => world.StartScenario(project.World, project));
        Assert.Equal(baseline, State(world));
    }

    [Fact]
    public void OpeningImportDoesNotInheritSpeciesFromAnEarlierEditorTrial()
    {
        var expected = Project();
        var edited = expected.Clone();
        foreach (var species in edited.World.Species) species.MaximumBiomass *= 2;
        using var trial = WorldLoader.CreateProject(edited);
        Assert.Equal(expected.World.InitialState, WorldLoader.OpeningProject().World.InitialState);
    }

    [Fact]
    public void RecaptureDoesNotNestEarlierSceneDocumentsOrMutateTheWorld()
    {
        var project = Project();
        using var world = WorldLoader.CreateProject(project);
        string baseline = State(world);
        var captured = WorldLoader.CaptureScenario(world);
        Assert.Equal(project.World.InitialState, captured.InitialState);
        Assert.Equal(baseline, State(world));
    }
}
