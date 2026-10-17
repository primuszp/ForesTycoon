using System.Text.Json;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public class ParallelFlowTests
{
    private static GameWorld World(bool planned)
    {
        var world = new GameWorld(TerrainSettings.Default.WithNodeSize(65, 42), enableRendering: false);
        world.Forest.Clear(); world.RuleBook.PlanLocalFlows = planned;
        world.Environment.ForceWeather(WeatherPreset.Rain, 30, 600);
        return world;
    }
    private static string State(GameWorld world) => JsonSerializer.Serialize(world.CaptureCheckpoint());

    [Fact]
    public void MembershipCachesInvalidateOnRemovalAndSameStoreRestore()
    {
        using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        var scope = RuleEngine.Scope("tree"); var store = world.Forest.IndividualTrees;
        SimulationWork.Complete(scope.PrepareElements(world)); int original = scope.PreparedElements(world).Length;
        Assert.Single(scope.PrepareElements(world));
        var saved = store.Capture(); var removed = store.Patches.First();
        store.RemoveTile(removed.Key);
        SimulationWork.Complete(scope.PrepareElements(world));
        Assert.Equal(original - removed.Value.Count, scope.PreparedElements(world).Length);
        store.Restore(saved, world.Map.Tiles.Count, world.Forest.ForestYear);
        SimulationWork.Complete(scope.PrepareElements(world));
        Assert.Equal(original, scope.PreparedElements(world).Length);
    }

    [Fact]
    public void PlannedOwnCellFlowsHaveTheExactOrderedKernelStateAndLedger()
    {
        using var planned = World(true); using var ordered = World(false);
        for (int tick = 0; tick < 65; tick++) {
            ordered.Update(1d / 30);
            while (!planned.ContinueUpdate(1d / 30, tick % 2 == 0 ? 3 : 64))
                Assert.InRange(planned.LastUpdateWorkUnits, 1, tick % 2 == 0 ? 3 : 64);
        }
        Assert.True(planned.RuleBook.PlannedFlowRows > 0);
        Assert.Equal(State(ordered), State(planned));
        using var save = new MemoryStream(); planned.Save(save); save.Position = 0;
        using var restored = World(true); restored.Load(save);
        for (int tick = 0; tick < 20; tick++) { planned.Update(1d / 30); restored.Update(1d / 30); }
        Assert.Equal(State(planned), State(restored));
    }

    [Fact]
    public void AStepSavedDuringPlanningRestoresBeforeItsUncommittedRows()
    {
        using var world = World(true); world.Update(1d / 30);
        world.RuleBook.Begin(world, .5 / EcologyTime.DefaultGameSecondsPerYear, world.Forest.ForestYear);
        while (world.RuleBook.PlannedFlowRows == 0) world.RuleBook.Continue(world, 64);
        Assert.True(world.RuleBook.StepPending);
        using var save = new MemoryStream(); world.Save(save); save.Position = 0;
        using var restored = World(true); restored.Load(save);
        while (world.RuleBook.StepPending) world.RuleBook.Continue(world, 13);
        while (restored.RuleBook.StepPending) restored.RuleBook.Continue(restored, 7);
        Assert.Equal(State(world), State(restored));
    }
}
