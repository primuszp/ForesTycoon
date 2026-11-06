#nullable enable
using System;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor.Core;

/// <summary>CPU-only project validation for CLI callers and headless hosts.</summary>
internal sealed class SimulationProjectWorkflowRuntime(SimulationWorld? world = null) : IProjectWorkflowRuntime
{
    public RuleProject Normalize(RuleProject project) => SimulationWorld.NormalizeRuleProject(project);
    public RuleProject PrepareDocument(RuleProject project) => EditorProjectLoader.Prepare(project);
    public void ValidatePreview(RuleProject project) => SimulationWorld.ValidateRuleProject(project);
    public void ApplyPreview(RuleProject project) => world?.QueueRuleProject(project);
    public Action<RuleProject> CaptureSceneValidation()
    {
        object? species = world?.WithSpecies(ForestSpeciesBook.CaptureContext);
        return project => ForestSpeciesBook.WithContext(species, () => {
            using var validation = SimulationLoader.CreateProject(EditorDocument.ForSandbox(project));
            return 0;
        });
    }
}
