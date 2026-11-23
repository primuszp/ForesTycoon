using System;

namespace ForesTycoon;

internal static partial class SimulationLoader
{
    internal static SimulationWorld CreateProject(RuleProject project) => CreateProject(project, settings => new SimulationWorld(settings));

    internal static TWorld CreateProject<TWorld>(RuleProject project, Func<TerrainSettings, TWorld> createWorld) where TWorld : SimulationWorld
    {
        ArgumentNullException.ThrowIfNull(project);
        bool hasScene = project.World != null;
        var world = createWorld(hasScene ? TerrainSettings.Default.WithNodeSize(17, 42) : TerrainSettings.Default);
        try
        {
            if (hasScene) world.StartScenario(project.World, project);
            else world.InitializeProjectScene(SimulationWorld.NormalizeRuleProject(project));
            return world;
        }
        catch { world.Dispose(); throw; }
    }
}
