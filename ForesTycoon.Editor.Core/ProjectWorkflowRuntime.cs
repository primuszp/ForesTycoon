#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using ForesTycoon.Rules;
using ForesTycoon.RuleCompiler;

namespace ForesTycoon.Editor.Core;

internal interface IProjectWorkflowRuntime
{
    RuleProject Normalize(RuleProject project);
    RuleProject PrepareDocument(RuleProject project);
    void ValidatePreview(RuleProject project);
    void ApplyPreview(RuleProject project);
    Action<RuleProject> CaptureSceneValidation();
}

internal interface IProjectCompiler
{
    bool RequiresCompilation(AlgorithmGraphReference reference);
    Task<AlgorithmBuildResult> CompileAsync(AlgorithmGraphReference reference, CancellationToken cancellation);
}

internal sealed class AlgorithmProjectCompiler : IProjectCompiler
{
    public bool RequiresCompilation(AlgorithmGraphReference reference) => reference.Revision != AlgorithmBuild.CurrentRevision;
    public Task<AlgorithmBuildResult> CompileAsync(AlgorithmGraphReference reference, CancellationToken cancellation)
        => AlgorithmBuild.CompileAsync(reference, cancellation);
}
