using ForesTycoon.Engine;

namespace ForesTycoon.Editor.Core;

/// <summary>Advances the preview world independently of any graph editor or its selection.</summary>
internal sealed class SandboxSession
{
    private readonly SimulationFrameRunner runner = SimulationFrameRunner.Interactive();
    private TerrainMap map;
    internal SandboxSession() => runner.Clock.IsPaused = true;
    internal bool IsPaused => runner.Clock.IsPaused;
    internal void TogglePause() => runner.Clock.IsPaused = !runner.Clock.IsPaused;
    internal void RequestTime(double seconds) => runner.Clock.RequestTime(seconds);

    internal void Advance(SimulationWorld world, double elapsed)
    {
        Synchronize(world);
        runner.AdvanceWork(elapsed, () => world.ContinueCommands(), dt => world.ContinueUpdate(dt), () => world.ForegroundPending);
        Synchronize(world);
    }

    private void Synchronize(SimulationWorld world)
    {
        if (map == world.Map) return;
        map = world.Map; runner.Clock.Reset(world.SimulationTick);
    }
}
