using System;

namespace ForesTycoon;

internal static partial class SimulationLoader
{
    internal static SimulationWorld CreateProject(RuleProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var copy = SimulationWorld.NormalizeRuleProject(project);
        SimulationWorld.ValidateRuleProject(copy);
        var world = copy.World == null ? new SimulationWorld(TerrainSettings.Default) : copy.World.InitialState != null ?
            new SimulationWorld(TerrainSettings.Default.WithNodeSize(17, 42)) :
            new SimulationWorld(SimulationWorld.ScenarioSettings(copy.World), soils: SimulationWorld.SoilModel(copy.World), responseRules: ForesTycoon.Rules.EcologyResponseProgram.FromModel(copy.Behaviors), scene: copy.World);
        try
        {
            if (copy.World?.InitialState != null) RestoreInitialState(world, copy.World);
            world.InitializeProjectScene(copy);
            return world;
        }
        catch { world.Dispose(); throw; }
    }
}
