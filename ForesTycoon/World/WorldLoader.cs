using System;

namespace ForesTycoon;

/// <summary>Builds initial world state for the game and editor; presentation is attached last.</summary>
internal static partial class WorldLoader
{
    // Keep the established generator and its exact built-in soil values for the opening scene.
    internal static GameWorld CreateOpening(bool enableRendering = true)
    {
        // Species overrides are process-wide today. A previous editor trial must not seed the reference forest.
        ForestSpeciesBook.Apply(null);
        return new(TerrainSettings.Default, enableRendering: enableRendering);
    }

    internal static GameWorld CreateProject(RuleProject project, bool enableRendering = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        var copy = project.Clone();
        GameWorld.ValidateRuleProject(copy);
        var world = copy.World == null ? CreateOpening(false) : copy.World.InitialState != null ?
            new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false) :
            new GameWorld(GameWorld.ScenarioSettings(copy.World), soils: GameWorld.SoilModel(copy.World), enableRendering: false);
        try
        {
            if (copy.World?.InitialState != null) RestoreInitialState(world, copy.World);
            world.InitializeProjectScene(copy);
            if (enableRendering) world.AttachRendering();
            return world;
        }
        catch { world.Dispose(); throw; }
    }
}
