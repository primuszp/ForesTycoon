using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ForesTycoon;

partial class SimulationWorld
{
    private IEnumerator<int> generationWork;
    private object generationSpecies;
    private bool generationCommitted;
    private readonly List<IWorldCommand> generationInputs = new();
    internal bool GenerationPending => generationWork != null;
    internal bool GenerationCanCancel => GenerationPending && !generationCommitted;
    internal string GenerationStage { get; private set; } = "";
    internal string GenerationError { get; private set; }
    internal int LastForegroundWorkUnits { get; private set; }

    internal void RequestRegenerate(TerrainSettings settings, SoilLandscapeDefinition soils = null)
    {
        EnsureAvailable(allowFaulted: true);
        ArgumentNullException.ThrowIfNull(settings);
        AtTickBoundary(() => BeginGeneration(GenerateWorldWork(settings, soils ?? SoilLandscapeDefinition.Default, null)));
    }

    internal void RequestScenario(WorldScenario scenario, RuleProject definition = null)
    {
        EnsureAvailable(allowFaulted: true);
        ArgumentNullException.ThrowIfNull(scenario);
        AtTickBoundary(() => BeginGeneration(PrepareScenarioWork(scenario, definition)));
    }

    private IEnumerable<int> PrepareScenarioWork(WorldScenario scenario, RuleProject definition)
    {
        GenerationStage = "Jelenet ellenőrzése";
        var preparing = Task.Run(() =>
        {
            var project = definition?.Clone() ?? RuleProjectDocument;
            project.World = RuleProject.FromJson(new RuleProject { World = scenario }.ToJson()).World;
            ValidateRuleProject(project); RequireAlgorithmRuntime(project.Algorithms);
            return (Project: project, Soils: SoilModel(project.World));
        });
        while (!preparing.IsCompleted) yield return 64;
        var prepared = preparing.GetAwaiter().GetResult();
        foreach (int unit in GenerateWorldWork(ScenarioSettings(prepared.Project.World), prepared.Soils, prepared.Project)) yield return unit;
    }

    private void BeginGeneration(IEnumerable<int> work)
    {
        generationWork?.Dispose();
        generationInputs.Clear(); generationCommitted = false;
        generationWork = work.GetEnumerator();
        generationSpecies = worldSpecies;
        GenerationError = null; GenerationStage = "Pálya előkészítése";
    }

    internal void CancelGeneration()
    {
        VerifyPresentationAccess();
        if (generationCommitted && generationWork != null) return; // Only resource retirement remains after publication.
        generationWork?.Dispose(); generationWork = null; GenerationStage = ""; generationInputs.Clear();
        PublishBoundaryActions();
    }

    internal bool ContinueGeneration(int budget = 64)
        => WithSpecies(() => ContinueGenerationCore(budget));

    private bool ContinueGenerationCore(int budget)
    {
        EnsureAvailable(allowFaulted: true);
        if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        if (generationWork == null) return true;
        VerifyPresentationAccess();
        object liveSpecies = ForestSpeciesBook.CaptureContext();
        bool published = false;
        try
        {
            ForestSpeciesBook.RestoreContext(generationSpecies);
            LastForegroundWorkUnits = 0;
            while (LastForegroundWorkUnits < budget)
            {
                if (!SimulationWork.MoveNext(generationWork))
                {
                    generationWork.Dispose(); generationWork = null;
                    GenerationStage = ""; published = true;
                    return true;
                }
                LastForegroundWorkUnits += Math.Max(1, generationWork.Current);
            }
            generationSpecies = ForestSpeciesBook.CaptureContext();
            return false;
        }
        catch (Exception error)
        {
            generationWork.Dispose(); generationWork = null;
            GenerationError = error.Message; GenerationStage = ""; generationInputs.Clear();
            return true;
        }
        finally
        {
            if (!published && !generationCommitted) ForestSpeciesBook.RestoreContext(liveSpecies);
            if (generationWork == null) PublishBoundaryActions();
        }
    }

    private IEnumerable<int> GenerateWorldWork(TerrainSettings settings, SoilLandscapeDefinition soils, RuleProject project)
    {
        WorldSaveData snapshot = null;
        if (project?.World.InitialState != null)
        {
            GenerationStage = "Kiindulójelenet beolvasása";
            var reading = Task.Run(() => SimulationLoader.ReadInitialState(project.World));
            while (!reading.IsCompleted) yield return 64;
            snapshot = reading.GetAwaiter().GetResult();
            settings = snapshot.Terrain.ToSettings(); soils = snapshot.ReplaySoilModel;
        }
        double yearSeconds = snapshot?.ReplayForestYearSeconds ?? EcologyTime.DefaultGameSecondsPerYear;
        var climate = snapshot?.ReplayClimate ?? ClimateDefinition.Default;
        GenerationStage = "Domborzat és vízrajz";
        var terrainResult = new WorkResult<TerrainMap>();
        foreach (int unit in TerrainMap.CreateWork(settings, terrainResult)) yield return unit;
        GenerationStage = "Talaj, erdő és környezet";
        var preparingResponses = Task.Run(() => ForesTycoon.Rules.EcologyResponseProgram.FromModel(project?.Behaviors ?? WorldBehaviorPolicy.DefaultDocument()));
        while (!preparingResponses.IsCompleted) yield return 64;
        var responses = preparingResponses.GetAwaiter().GetResult();
        ForestSpeciesBook.Apply(SpeciesEdits(project?.World));
        // The forest is grown with the ecosystem, before the candidate world exists: give it the scene's zones and biomes now.
        var scene = project?.World ?? BuiltinZones; var generated = terrainResult.Value;
        generated.LandscapeClass = id => Landscape(scene, generated, id);
        var ecologyResult = new WorkResult<Ecosystem>();
        foreach (int unit in Ecosystem.CreateWork(terrainResult.Value, yearSeconds, soils, climate, ecologyResult, responses)) yield return unit;
        using var candidate = CreateCandidate(terrainResult.Value, yearSeconds, soils, climate, ecologyResult.Value, deferRules: true, scene: project?.World);
        GenerationStage = "Szabályok előkészítése";
        foreach (int unit in candidate.ApplyRuleProjectWork(DefaultRuleProject())) yield return unit;
        candidate.ApplyTuning(Tuning);
        if (project != null)
        {
            GenerationStage = "Jelenet szerkesztései";
            // A captured initial scene is restored before applying the editor's new paint.
            if (snapshot != null) foreach (int unit in candidate.RestoreCheckpointWork(snapshot.ReplayCheckpoint)) yield return unit;
            foreach (int unit in candidate.InitializeProjectSceneWork(project)) yield return unit;
        }
        GenerationStage = "Megjelenítés előkészítése";
        if (HasPresentation)
        {
            foreach (int unit in candidate.PreparePresentationWork()) yield return unit;
        }
        foreach (var command in generationInputs) candidate.Enqueue(command);
        generationInputs.Clear();
        Adopt(candidate, preserveBoundaryActions: true); generationCommitted = true;
        GenerationStage = "Régi pálya felszabadítása";
        foreach (int unit in candidate.DisposeWork()) yield return unit;
        LastRuleSample = "Még nem történt közúti áthaladás.";
        LastLoadReplayedTicks = 0;
    }
}
