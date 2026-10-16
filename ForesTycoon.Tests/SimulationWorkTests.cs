using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class SimulationWorkTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), forestYearSeconds: 120, enableRendering: false);
    private static string State(GameWorld world) => JsonSerializer.Serialize(world.CaptureCheckpoint());

    [Fact]
    public void MidTickSaveWaitsForBoundaryAndRetainsPendingCommandsAndReplay()
    {
        using var world = World();
        using var saved = new MemoryStream();
        Assert.False(world.ContinueUpdate(1d / 30, 1));
        Assert.Throws<InvalidOperationException>(() => world.CaptureCheckpoint());
        bool snapshotTaken = false;
        world.AtTickBoundary(() => { world.Save(saved); snapshotTaken = true; });
        world.QueueWeather(WeatherPreset.Storm, 32, 20);
        Assert.Equal(0, world.ExecutePendingCommands());
        Assert.False(snapshotTaken);
        Assert.Equal(0, saved.Length);
        while (!world.ContinueUpdate(1d / 30, 3)) Assert.False(snapshotTaken);
        Assert.True(snapshotTaken);
        Assert.Equal((ulong)1, world.SimulationTick);
        saved.Position = 0;
        using var restored = World(); restored.Load(saved);
        Assert.Equal(State(world), State(restored));
        Assert.Equal(1, world.ExecutePendingCommands());
        Assert.Equal(1, restored.ExecutePendingCommands());
        for (int tick = 0; tick < 90; tick++) {
            world.Update(1d / 30);
            while (!restored.ContinueUpdate(1d / 30, 11)) { }
        }
        Assert.Equal(State(world), State(restored));
    }

    [Fact]
    public void ProjectReplacementWaitsForOldWorkAndSurvivesSave()
    {
        using var world = World();
        for (int i = 0; i < 14; i++) world.Update(1d / 30);
        var project = world.RuleProjectDocument;
        project.Name = "Replacement at boundary";
        Assert.False(world.ContinueUpdate(1d / 30, 1));
        world.QueueRuleProject(project);
        Assert.Equal(0, world.ExecutePendingCommands());
        while (!world.ContinueUpdate(1d / 30, 5)) { }
        Assert.Equal(1, world.ExecutePendingCommands());
        Assert.Equal(project.Name, world.RuleProjectDocument.Name);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = World(); restored.Load(save);
        for (int i = 0; i < 20; i++) {
            while (!world.ContinueUpdate(1d / 30, 2)) { }
            restored.Update(1d / 30);
        }
        Assert.Equal(State(world), State(restored));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void FrameBudgetKeepsDebtAndPauseFinishesOnlyThePendingTick(bool paused, double speed)
    {
        var clock = new FixedStepClock(10, 8);
        int units = 0;
        bool Continue(double dt) { Assert.Equal(.1, dt); return ++units == 3; }
        Assert.Equal(0, clock.AdvanceWork(.25, Continue, () => false));
        Assert.True(clock.WorkInProgress);
        Assert.Equal(0, clock.DroppedSimulationSeconds);
        clock.IsPaused = paused; clock.Speed = speed;
        Assert.Equal(0, clock.AdvanceWork(100, Continue, () => false));
        Assert.Equal(1, clock.AdvanceWork(100, Continue, () => false));
        Assert.False(clock.WorkInProgress);
        Assert.Equal(.15, clock.PendingSimulationSeconds, 10);
        Assert.Equal(0, clock.AdvanceWork(100, _ => throw new Exception()));
        clock.IsPaused = false; clock.Speed = 1;
        Assert.Equal(1, clock.AdvanceWork(0, _ => true));
        Assert.Equal(.05, clock.PendingSimulationSeconds, 10);
    }

    [Fact]
    public void ManualStepWhilePausedUsesTheSameBudgetWithoutAddingAnotherTick()
    {
        var clock = new FixedStepClock { IsPaused = true };
        clock.RequestTick();
        int units = 0;
        while (clock.Tick == 0) clock.AdvanceWork(100, _ => ++units == 5, () => false);
        Assert.Equal(5, units);
        Assert.Equal(0, clock.AdvanceWork(100, _ => throw new Exception()));
        Assert.Equal(0, clock.PendingSimulationSeconds, 10);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    public void SlicesPreserveMonthBoundariesEnvironmentAndExactCheckpoints(int budget)
    {
        using var expected = World(); using var actual = World();
        foreach (var world in new[] { expected, actual }) {
            world.QueueWeather(WeatherPreset.Snow, 30, 120); world.ExecutePendingCommands();
        }
        for (int tick = 0; tick < 310; tick++) {
            expected.Update(1d / 30);
            while (!actual.ContinueUpdate(1d / 30, budget)) { }
        }
        Assert.Equal(State(expected), State(actual));
        Assert.InRange(Math.Abs(actual.Environment.BalanceError), 0, 1e-5);
    }

    [Fact]
    public void DijkstraCanSuspendPerNodeAndKeepsTheExactPathAndCost()
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
        map.BuildRoadTilePath(34, 210, RoadPaving.Macadam);
        int[] expected = map.FindNetworkPath(34, 210, out float cost);
        var result = new WorkResult<(int[] Path, float Cost)>();
        using var work = map.FindNetworkPathWork(34, 210, result).GetEnumerator();
        Assert.True(work.MoveNext());
        Assert.Empty(result.Value.Path);
        int units = 1;
        while (work.MoveNext()) units++;
        Assert.True(units > expected.Length);
        Assert.Equal(expected, result.Value.Path);
        Assert.Equal(cost, result.Value.Cost);
    }

    [Fact]
    public void DeferredForestRateRefreshIsSavedBeforeItStartsAndContinuesExactly()
    {
        using var world = World();
        var project = world.RuleProjectDocument;
        project.Rules.Single(r => r.Id == "forest.growth").Enabled = false;
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        Assert.True(world.CaptureCheckpoint().PendingForestRateRefresh);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = World(); restored.Load(save);
        Assert.Equal(State(world), State(restored));
        world.Update(1d / 30);
        int slices = 0;
        while (!restored.ContinueUpdate(1d / 30, 1)) slices++;
        Assert.True(slices > 5);
        Assert.False(restored.CaptureCheckpoint().PendingForestRateRefresh);
        Assert.Equal(State(world), State(restored));
    }

    [Fact]
    public void SnapshotCallbackFailureDoesNotFaultTheCompletedSimulation()
    {
        using var world = World();
        Assert.False(world.ContinueUpdate(1d / 30, 1));
        world.AtTickBoundary(() => throw new IOException("Storage unavailable"));
        Assert.Throws<IOException>(() => { while (!world.ContinueUpdate(1d / 30, 7)) { } });
        Assert.False(world.IsFaulted);
        Assert.False(world.UpdatePending);
        Assert.Equal((ulong)1, world.SimulationTick);
        world.Update(1d / 30);
        Assert.Equal((ulong)2, world.SimulationTick);
    }

    [Fact]
    public void TreeFieldsInitializeInSlicesAfterTheObjectListIsPrepared()
    {
        using var world = World();
        var project = world.RuleProjectDocument;
        project.Fields.Add(new() { Name = "saját.memory", Scope = "tree", Initial = 3, Min = 0, Max = 10 });
        project.Rules.Add(new() {
            Id = "tree-memory", Name = "Tree memory", Scope = "tree",
            Effects = new() { new() { Target = "saját.memory", Mode = "add", Formula = "1" } }
        });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        for (int tick = 0; tick < 16; tick++) while (!world.ContinueUpdate(1d / 30, 1)) { }
        Assert.Null(world.RuleBook.LastError);
        Assert.True(world.ForestTreeCount > 0);
        Assert.Equal(4, world.RuleBook.ReadCustom(world, "saját.memory", 0));
    }
}
