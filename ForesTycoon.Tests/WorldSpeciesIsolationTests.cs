using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class WorldSpeciesIsolationTests
{
    private static RuleProject Project(double growth)
    {
        var project = GameWorld.DefaultRuleProject();
        project.Algorithms = null;
        project.World = GameWorld.DefaultScenario(42, 16);
        foreach (var species in project.World.Species) species.AnnualGrowthRate = growth;
        return project;
    }

    [Fact]
    public void SimulationUsesItsOwnSpeciesAfterAnotherWorldIsCreated()
    {
        var slow = Project(.01);
        using var reference = WorldLoader.CreateProject(slow);
        using var actual = WorldLoader.CreateProject(slow);
        for (int i = 0; i < 90; i++) reference.Update(1);
        string expected = JsonSerializer.Serialize(reference.CaptureCheckpoint());

        using var other = WorldLoader.CreateProject(Project(.2));
        for (int i = 0; i < 90; i++) { actual.Update(1); other.Update(1); }
        Assert.Equal(expected, JsonSerializer.Serialize(actual.CaptureCheckpoint()));
    }

    [Fact]
    public void InterleavedSceneGenerationUsesEachScenesSpecies()
    {
        var slow = Project(.01); var fast = Project(.2);
        using var expected = WorldLoader.CreateProject(slow);
        using var actual = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        using var other = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        actual.RequestScenario(slow.World, slow); other.RequestScenario(fast.World, fast);
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (actual.GenerationPending || other.GenerationPending) {
            if (actual.GenerationPending) actual.ContinueGeneration(64);
            if (other.GenerationPending) other.ContinueGeneration(64);
            Thread.Yield(); Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(60));
        }
        Assert.Null(actual.GenerationError); Assert.Null(other.GenerationError);
        Assert.Equal(JsonSerializer.Serialize(expected.CaptureCheckpoint()), JsonSerializer.Serialize(actual.CaptureCheckpoint()));
        Assert.Equal(.01f, actual.WithSpecies(() => ForestSpeciesProfile.For(ForestSpecies.Oak).AnnualGrowthRate), 5);
        Assert.Equal(.2f, other.WithSpecies(() => ForestSpeciesProfile.For(ForestSpecies.Oak).AnnualGrowthRate), 5);
    }

    [Fact]
    public void SynchronousSceneGenerationUsesAuthoredSpeciesFromTheStart()
    {
        var project = Project(.01);
        using var expected = WorldLoader.CreateProject(project);
        using var actual = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        actual.StartScenario(project.World, project);
        Assert.Equal(JsonSerializer.Serialize(expected.CaptureCheckpoint()), JsonSerializer.Serialize(actual.CaptureCheckpoint()));
    }

    [Fact]
    public void CheckpointRestoresSpeciesAndContinuesIndependentlyOfAnotherWorld()
    {
        using var original = WorldLoader.CreateProject(Project(.01));
        for (int i = 0; i < 30; i++) original.Update(1);
        using var save = new MemoryStream(); original.Save(save); save.Position = 0;
        using var restored = WorldLoader.CreateProject(Project(.2));
        restored.Load(save);
        Assert.Equal(.01f, restored.WithSpecies(() => ForestSpeciesProfile.For(ForestSpecies.Oak).AnnualGrowthRate), 5);
        using var other = WorldLoader.CreateProject(Project(.2));
        for (int i = 0; i < 90; i++) { original.Update(1); restored.Update(1); other.Update(1); }
        Assert.Equal(JsonSerializer.Serialize(original.CaptureCheckpoint()), JsonSerializer.Serialize(restored.CaptureCheckpoint()));
    }
}
