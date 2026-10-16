using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class ForegroundWorkTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static string State(GameWorld world) => JsonSerializer.Serialize(world.CaptureCheckpoint());
    private static void FinishGeneration(GameWorld world, int budget)
    {
        int slices = 0; var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (world.GenerationPending)
        {
            world.ContinueGeneration(budget);
            // Worker compilation/serialization waits are yielded work, not simulation progress.
            // Let those workers run; the interactive host naturally gives them the rest of the frame.
            if (++slices % 256 == 0) {
                Thread.Yield();
                Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(30), world.GenerationStage);
            }
        }
        Assert.Null(world.GenerationError);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    public void GenerationKeepsOldWorldVisibleAndPublishesTheExactSynchronousResult(int budget)
    {
        using var actual = World(); using var expected = World();
        var settings = TerrainSettings.Default.WithNodeSize(33, 137).WithForestPattern(ForestPattern.LargeMixed);
        expected.Regenerate(settings);
        var previous = actual.Map;
        actual.RequestRegenerate(settings);
        Assert.False(actual.ContinueGeneration(1));
        Assert.Same(previous, actual.Map);
        Assert.Throws<InvalidOperationException>(() => actual.CaptureCheckpoint());
        FinishGeneration(actual, budget);
        Assert.NotSame(previous, actual.Map);
        Assert.Equal(State(expected), State(actual));
    }

    [Fact]
    public void CancellationPreservesWorldAndQueuedInput()
    {
        using var world = World(); using var expected = World();
        world.RequestRegenerate(TerrainSettings.Default.WithNodeSize(65, 19));
        world.ContinueGeneration(500);
        world.QueueWeather(WeatherPreset.Storm, 31, 20);
        expected.QueueWeather(WeatherPreset.Storm, 31, 20);
        world.CancelGeneration();
        Assert.Equal(State(expected), State(world));
        world.ExecutePendingCommands(); expected.ExecutePendingCommands();
        Assert.Equal(State(expected), State(world));
    }

    [Fact]
    public void GenerationDefersSaveAndTransfersInputToThePublishedWorld()
    {
        using var world = World(); using var save = new MemoryStream();
        world.RequestRegenerate(TerrainSettings.Default.WithNodeSize(33, 19));
        world.ContinueGeneration(10);
        world.QueueWeather(WeatherPreset.Rain, 29, 20);
        bool saved = false;
        world.AtTickBoundary(() => { world.Save(save); saved = true; });
        Assert.False(saved);
        FinishGeneration(world, 29);
        Assert.True(saved);
        save.Position = 0;
        using var restored = World(); restored.Load(save);
        Assert.Equal(State(world), State(restored));
        world.ExecutePendingCommands(); restored.ExecutePendingCommands();
        Assert.Equal(WeatherPreset.Rain, world.Environment.Preset);
        Assert.Equal(State(world), State(restored));
    }

    [Fact]
    public void LargeEditKeepsCommandsInOrderAndDefersSnapshotsUntilComplete()
    {
        using var world = World(); using var expected = World();
        foreach (var target in new[] { world, expected })
        {
            target.QueueElevationEdit(144, 1, 9, 3);
            target.QueueWeather(WeatherPreset.Snow, 25, 30);
        }
        expected.ExecutePendingCommands();
        Assert.Equal(0, world.ContinueCommands(1));
        Assert.True(world.CommandPending);
        Assert.False(world.ContinueUpdate(1d / 30, 1));
        Assert.Equal((ulong)0, world.SimulationTick);
        using var save = new MemoryStream();
        world.AtTickBoundary(() => world.Save(save));
        Assert.Throws<InvalidOperationException>(() => world.CaptureCheckpoint());
        while (world.CommandPending) world.ContinueCommands(7);
        Assert.True(save.Length > 0);
        Assert.Equal(State(expected), State(world));
        save.Position = 0;
        using var restored = World(); restored.Load(save);
        Assert.Equal(State(world), State(restored));
    }

    [Fact]
    public void PausedHostFinishesLargeEditsWithoutStartingSimulation()
    {
        using var world = World();
        world.QueueElevationEdit(144, 1, 8, 2);
        var runner = new SimulationFrameRunner(maximumWorkMilliseconds: .1);
        runner.Clock.IsPaused = true;
        int frames = 0;
        do
        {
            runner.AdvanceWork(1d / 60, () => world.ContinueCommands(3), dt => world.ContinueUpdate(dt), () => world.ForegroundPending);
            Assert.True(++frames < 100000);
        } while (world.ForegroundPending);
        Assert.True(frames > 1);
        Assert.Equal((ulong)0, world.SimulationTick);
        Assert.Equal((ulong)0, runner.Clock.Tick);
    }

    [Fact]
    public void CapturedScenarioRestorationAndPaintAreSlicedAndExact()
    {
        using var source = World();
        source.Update(.1);
        var scenario = WorldLoader.CaptureScenario(source);
        scenario.HeightPaint[144] = 5;
        scenario.ForestPaint[20] = "Beech";
        using var expected = World(); using var actual = World();
        expected.StartScenario(scenario);
        actual.RequestScenario(scenario);
        FinishGeneration(actual, 19);
        Assert.Equal(State(expected), State(actual));
        for (int i = 0; i < 20; i++) { expected.Update(1d / 30); actual.Update(1d / 30); }
        Assert.Equal(State(expected), State(actual));
    }

    [Fact]
    public void InputArrivingDuringAnEditIsSavedAsPendingAndReplaysFromTheSeed()
    {
        using var world = World();
        world.QueueElevationEdit(144, 1, 7, 2);
        world.ContinueCommands(1);
        world.QueueWeather(WeatherPreset.Storm, 30, 20);
        using var snapshot = new MemoryStream();
        world.AtTickBoundary(() => world.Save(snapshot));
        while (world.CommandPending) world.ContinueCommands(5);
        snapshot.Position = 0;
        var pending = WorldSaveSerializer.Read(snapshot);
        Assert.Equal(1, pending.Checkpoint.CommandCursor);
        Assert.Equal(1, pending.Checkpoint.PendingCommands);
        world.ExecutePendingCommands();
        for (int i = 0; i < 20; i++) world.Update(1d / 30);
        using var saved = new MemoryStream(); world.Save(saved); saved.Position = 0;
        var data = WorldSaveSerializer.Read(saved);
        using var journal = new MemoryStream();
        WorldSaveSerializer.Write(journal, new WorldSaveData
        {
            Terrain = data.Terrain,
            Tick = data.Tick,
            TickRate = data.TickRate,
            ForestYearSeconds = data.ForestYearSeconds,
            SoilModel = data.SoilModel,
            Climate = data.Climate,
            Commands = data.Commands
        });
        journal.Position = 0;
        using var replayed = World(); replayed.Load(journal);
        Assert.Equal(State(world), State(replayed));
    }

    [Fact]
    public async Task MeshWorkerSpeciesContextIsIndependentOfTheLiveBook()
    {
        float builtin = ForestSpeciesProfile.For(ForestSpecies.Beech).ShadeTolerance;
        try
        {
            ForestSpeciesBook.Apply(new Dictionary<ForestSpecies, ForestSpeciesEdit>
            {
                [ForestSpecies.Beech] = new(ShadeTolerance: .123f)
            });
            object context = ForestSpeciesBook.CaptureContext();
            ForestSpeciesBook.Apply(null);
            float observed = await Task.Run(() => ForestSpeciesBook.WithContext(context,
                () => ForestSpeciesProfile.For(ForestSpecies.Beech).ShadeTolerance));
            Assert.Equal(.123f, observed);
            Assert.Equal(builtin, ForestSpeciesProfile.For(ForestSpecies.Beech).ShadeTolerance);
        }
        finally { ForestSpeciesBook.Apply(null); }
    }

    [Fact]
    public void InteractiveRegenerationCanRecoverAFaultedWorld()
    {
        using var world = World();
        var systems = (WorldSystemCollection)typeof(GameWorld).GetField("systems",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(world)!;
        systems.Add(new FailingSystem());
        Assert.Throws<IOException>(() => world.Update(1d / 30));
        Assert.True(world.IsFaulted);
        world.RequestRegenerate(TerrainSettings.Default.WithNodeSize(17, 18));
        while (world.GenerationPending) world.ContinueCommands(11);
        Assert.Null(world.GenerationError);
        Assert.False(world.IsFaulted);
        world.Update(1d / 30);
        Assert.Equal((ulong)1, world.SimulationTick);
    }

    [Fact]
    public void ReadingCanopyDuringAnEditDoesNotPublishOrCompleteTheVegetationRaster()
    {
        using var world = World();
        world.QueueElevationEdit(144, 1, 7, 2);
        world.ContinueCommands(1);
        string before = JsonSerializer.Serialize(world.Environment.Capture());
        for (int id = 0; id < world.Map.Tiles.Count; id++)
            Assert.True(world.Environment.HydrologyInputs(id).InterceptionCapacity >= 0);
        Assert.Equal(before, JsonSerializer.Serialize(world.Environment.Capture()));
        Assert.True(world.CommandPending);
    }

    private sealed class FailingSystem : IWorldSystem
    {
        public void Update(double seconds) => throw new IOException("Injected update failure");
        public IEnumerable<int> UpdateWork(double seconds) { Update(seconds); yield return 1; }
        public void Clear() { }
    }
}
