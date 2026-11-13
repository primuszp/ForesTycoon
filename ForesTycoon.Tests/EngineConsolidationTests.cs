using System.Text.Json;
namespace ForesTycoon.Tests;

public sealed class EngineConsolidationTests
{
    private sealed class Target : ISimulationFrameTarget
    {
        public int ContinueFrameCommands() => 0;
        public bool ContinueFrameTick(double seconds) => true;
        public bool ForegroundPending => false;
    }
    [Fact] public void InteractiveSchedulingDoesNotAllocatePerFrame()
    {
        var runner = new SimulationFrameRunner(); var target = new Target();
        for (int i=0;i<100;i++) runner.AdvanceWork(1d/60,target);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0;i<1000;i++) runner.AdvanceWork(1d/60,target);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-start);
    }
    [Fact] public void FrameSchedulerRejectsReentrancyAndRecoversAfterFailure()
    {
        var runner = new SimulationFrameRunner(); var target = new Target();
        Assert.Throws<InvalidOperationException>(()=>runner.AdvanceWork(.1,()=>{runner.AdvanceWork(.1,target);return 0;},_=>true));
        Assert.Equal(0UL,runner.Clock.Tick);
        Assert.Equal(3,runner.AdvanceWork(.1,target).Ticks);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidFrameCannotExecuteQueuedCommands(bool cooperative)
    {
        var runner = new SimulationFrameRunner(); int commands = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => {
            if (cooperative) runner.AdvanceWork(double.NaN, () => ++commands, _ => true);
            else runner.Advance(double.NaN, () => ++commands, _ => { });
        });
        Assert.Equal(0, commands); Assert.Equal(0UL, runner.Clock.Tick);
    }
    [Fact] public void ClockAdaptersShareTickAccountingAndRejectAnIncompleteModeSwitch()
    {
        var sync = new FixedStepClock(30, 5000); var sliced = new FixedStepClock(30, 5000);
        for (int i = 0; i < 120; i++) {
            sync.Advance(1d / 60, _ => { }); sliced.AdvanceWork(1d / 60, _ => true);
            Assert.Equal(sync.Tick, sliced.Tick); Assert.Equal(sync.PendingSimulationSeconds, sliced.PendingSimulationSeconds);
        }
        sliced.AdvanceWork(.1, _ => false, () => false); double pending = sliced.PendingSimulationSeconds;
        Assert.Throws<InvalidOperationException>(() => sliced.Advance(1, _ => throw new Exception("must not execute")));
        Assert.Equal(pending, sliced.PendingSimulationSeconds);
        Assert.True(sliced.WorkInProgress);
        var exhausted = new FixedStepClock(); exhausted.Reset(ulong.MaxValue);
        Assert.Throws<InvalidOperationException>(() => exhausted.AdvanceWork(1, _ => throw new Exception("must not execute")));
        Assert.Equal(ulong.MaxValue, exhausted.Tick);
    }
    [Fact] public void SynchronousAdapterRetainsItsExplicitLargeTickBudget()
    {
        var clock = new FixedStepClock(1, 5000); Assert.Equal(5000, clock.Advance(5000, _ => { }));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void DirectAndQueuedCommandsUseOneWorldImplementation(int operation)
    {
        using var direct = new SimulationWorld(TerrainSettings.Default.WithNodeSize(9, 42));
        using var queued = new SimulationWorld(TerrainSettings.Default.WithNodeSize(9, 42));
        var target = (IWorldCommandTarget)direct;
        switch (operation) {
            case 0: target.ExecuteHarvestForestArea(10,20); queued.QueueHarvestForestArea(10,20); break;
            case 1: target.ExecuteRoadPath(10,20,false,RoadPaving.Macadam); queued.QueueRoadPath(10,20,false,RoadPaving.Macadam); break;
            case 2: target.ExecutePlaceDepot(10); queued.QueuePlaceDepot(10); break;
            case 3: target.ExecuteSendVehicle(-1,10,-1,false); queued.QueueSendVehicle(-1,10,-1,false); break;
            case 4: target.ExecutePlantForestArea(10,20,ForestSpecies.Birch); queued.QueuePlantForestArea(10,20,ForestSpecies.Birch); break;
        }
        queued.ExecutePendingCommands();
        var directState = direct.CaptureCheckpoint(); var queuedState = queued.CaptureCheckpoint();
        Assert.Equal(0, directState.CommandCursor); Assert.Equal(1, queuedState.CommandCursor);
        // Direct compatibility calls intentionally do not append to the input journal.
        Assert.Equal(JsonSerializer.Serialize(directState),JsonSerializer.Serialize(queuedState with { CommandCursor = 0 }));
    }
    [Fact] public void SynchronousUpdateRejectsForegroundWorkInsteadOfSpinningForever()
    {
        using var world = new SimulationWorld(TerrainSettings.Default.WithNodeSize(9,42));
        world.RequestRegenerate(TerrainSettings.Default.WithNodeSize(9,43));
        Assert.True(world.GenerationPending);
        Assert.Throws<InvalidOperationException>(()=>world.Update(1d/30));
        world.CancelGeneration();
        world.QueuePlantForestArea(1,40,ForestSpecies.Birch); world.ContinueCommands(1);
        Assert.True(world.CommandPending);
        Assert.Throws<InvalidOperationException>(()=>world.Update(1d/30));
        while(world.CommandPending) world.ContinueCommands(64);
        world.Update(1d/30); Assert.Equal(1UL,world.SimulationTick);
    }
    [Fact] public void DirectCommandsCannotMutateAnUnfinishedTick()
    {
        using var world = new SimulationWorld(TerrainSettings.Default.WithNodeSize(9,42));
        Assert.False(world.ContinueUpdate(1d/30,1));
        Assert.Throws<InvalidOperationException>(()=>((IWorldCommandTarget)world).ExecuteHarvestForest(10));
        while(!world.ContinueUpdate(1d/30,64)) { }
        world.CaptureCheckpoint();
    }
}
