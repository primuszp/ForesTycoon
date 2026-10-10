using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

[CollectionDefinition("Effect allocation", DisableParallelization = true)]
public sealed class EffectAllocationCollection { }

[Collection("Effect allocation")]
public class WorldEffectSystemTests
{
    [Fact]
    public void BurstKeepsNewestEffectsInFixedCapacityAndWrapsThroughUpdateAndRestore()
    {
        var effects = new WorldEffectSystem();
        for (int i = 0; i < WorldEffectSystem.MaxActiveEffects + 100; i++)
            effects.Spawn(WorldEffectKind.RoadChanged, new(i, 0, 0), i % 2 == 0 ? .1 : 1);
        Assert.Equal(WorldEffectSystem.MaxActiveEffects, effects.Count);
        Assert.Equal(100, effects.DroppedEffects); Assert.Equal(100, effects.Active[0].Position.X);
        Assert.InRange(effects.CpuPayloadBytes, 1, 1024 * 1024);
        effects.Update(.2);
        Assert.Equal(WorldEffectSystem.MaxActiveEffects / 2, effects.Count);
        Assert.Equal(101, effects.Active[0].Position.X);
        Assert.Equal(WorldEffectSystem.MaxActiveEffects + 99, effects.Active[^1].Position.X);
        var clone = new WorldEffectSystem(); clone.Restore(effects.Capture());
        Assert.Equal(effects.Capture(), clone.Capture());
        effects.Clear(); Assert.Equal(0, effects.Count); Assert.Equal(0, effects.DroppedEffects);
    }

    [Fact]
    public void FullRingSpawnDoesNotAllocateAndInvalidSpawnPreservesFeedback()
    {
        _ = Enum.GetNames<WorldEffectKind>(); // Include lazy names metadata before the allocation measurement.
        var effects = new WorldEffectSystem();
        for (int i = 0; i < WorldEffectSystem.MaxActiveEffects + 100; i++) effects.Spawn(WorldEffectKind.TreePlanted, Vector3.Zero);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) effects.Spawn(WorldEffectKind.TreePlanted, Vector3.Zero);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - allocated);
        var before = effects.Capture(); long dropped = effects.DroppedEffects;
        Assert.Throws<ArgumentOutOfRangeException>(() => effects.Spawn(WorldEffectKind.TreePlanted, new(float.NaN, 0, 0)));
        Assert.Equal(before, effects.Capture()); Assert.Equal(dropped, effects.DroppedEffects);
    }

    [Fact]
    public void HistoricalOverflowRestoreValidatesAllEntriesAndRetainsNewestFeedback()
    {
        var state = Enumerable.Range(0, WorldEffectSystem.MaxActiveEffects + 10)
            .Select(i => new EffectCheckpoint(WorldEffectKind.TreePlanted, new(i, 0, 0), 1, .2, .1)).ToArray();
        var effects = new WorldEffectSystem(); effects.Restore(state);
        Assert.Equal(10, effects.DroppedEffects); Assert.Equal(10, effects.Active[0].Position.X);
        var before = effects.Capture(); state[0] = state[0] with { Age = -1 };
        Assert.Throws<InvalidDataException>(() => effects.Restore(state)); Assert.Equal(before, effects.Capture());
    }

    [Fact]
    public void Update_AdvancesAndExpiresTransientEffects()
    {
        WorldEffectSystem effects = new WorldEffectSystem();
        effects.Spawn(WorldEffectKind.RoadChanged, new Vector3(1, 2, 3), lifetimeSeconds: 0.5);

        effects.Update(0.2);
        Assert.Single(effects.Active);
        Assert.Equal(0.4f, effects.Active[0].Progress, 3);

        effects.Update(0.3);
        Assert.Empty(effects.Active);
    }

    [Fact]
    public void Update_RejectsNegativeSimulationTime()
    {
        WorldEffectSystem effects = new WorldEffectSystem();

        Assert.Throws<ArgumentOutOfRangeException>(() => effects.Update(-0.01));
    }
}
