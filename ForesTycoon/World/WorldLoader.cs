using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

    internal const string OpeningSceneFile = "Content/Scenes/opening.project.json";

    /// <summary>
    /// The opening scene document the game starts from: the project's source copy when running from a checkout (so editor
    /// saves are played without a rebuild), otherwise the copy shipped next to the executable; null if neither exists.
    /// </summary>
    internal static string OpeningScenePath()
    {
        for (var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent) {
            string source = System.IO.Path.Combine(dir.FullName, "ForesTycoon", OpeningSceneFile);
            if (System.IO.File.Exists(source)) return source;
        }
        string shipped = System.IO.Path.Combine(AppContext.BaseDirectory, OpeningSceneFile);
        return System.IO.File.Exists(shipped) ? shipped : null;
    }

    /// <summary>The game's start: the authored opening scene, or the generated opening when no scene document exists.</summary>
    internal static GameWorld CreateStart(string sceneProject = null, bool enableRendering = true)
    {
        sceneProject ??= OpeningScenePath();
        return sceneProject == null ? CreateOpening(enableRendering)
            : CreateProject(RuleProject.FromJson(System.IO.File.ReadAllText(sceneProject)), enableRendering);
    }

    internal static IEnumerable<int> CreateStartWork(WorkResult<GameWorld> result, string sceneProject = null, bool enableRendering = true)
    {
        sceneProject ??= OpeningScenePath();
        RuleProject project = null;
        if (sceneProject != null) {
            string path = sceneProject;
            var reading = Task.Run(() => RuleProject.FromJson(System.IO.File.ReadAllText(path)));
            while (!reading.IsCompleted) yield return 64;
            project = reading.GetAwaiter().GetResult();
        }
        // Fixed 16x16 bootstrap only. Every operation that scales with the requested map uses the work pipeline.
        var world = new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
        bool transferred = false;
        try {
            if (enableRendering) world.AttachRendering();
            if (project?.World != null) world.RequestScenario(project.World, project);
            else world.RequestRegenerate(TerrainSettings.Default);
            while (world.GenerationPending) { world.ContinueGeneration(16); yield return 16; }
            if (world.GenerationError != null) throw new System.IO.InvalidDataException(world.GenerationError);
            if (project != null && project.World == null) {
                world.QueueRuleProject(project);
                do { world.ContinueCommands(16); yield return 16; } while (world.CommandPending);
            }
            result.Value = world; transferred = true;
        } finally { if (!transferred) world.Dispose(); }
    }

    internal static GameWorld CreateProject(RuleProject project, bool enableRendering = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        var copy = project.Clone();
        GameWorld.ValidateRuleProject(copy);
        var world = copy.World == null ? CreateOpening(false) : copy.World.InitialState != null ?
            new GameWorld(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false) :
            new GameWorld(GameWorld.ScenarioSettings(copy.World), soils: GameWorld.SoilModel(copy.World), enableRendering: false, responseRules: ForesTycoon.Rules.EcologyResponseProgram.FromModel(copy.Behaviors));
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
