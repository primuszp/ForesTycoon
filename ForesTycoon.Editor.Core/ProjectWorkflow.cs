#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ForesTycoon.Rules;
using ForesTycoon.RuleCompiler;

namespace ForesTycoon.Editor.Core;

internal enum ProjectOperationKind { PreviewApplied, DraftSaved, SceneSaveStarted, SceneSaved, SceneSaveFailed,
    PublishStarted, Published, PublishFailed, PublishCancelled, SceneOpened, RecoverySaved }
internal sealed record ProjectOperation(ProjectOperationKind Kind, string? Path = null, string? DocumentRevision = null,
    string? RuntimeRevision = null, string? BuildRevision = null, Exception? Error = null, bool SceneSaveStarted = false, Exception? PreviewError = null);
internal sealed record EditorProjectSource(RuleProject? Project = null, BehaviorModel? Behaviors = null, GameRuleCatalog? Catalog = null);
internal sealed record PublicationOptions(bool SaveScene = true, bool VerifyAlgorithms = false, string? ExpectedFileRevision = null);

/// <summary>Host-thread orchestration with owned snapshots for background work. Polling and shutdown need no UI.</summary>
internal sealed class ProjectWorkflow : IDisposable
{
    private sealed record Publication(RuleProject Project, string Path, string ScenePath, string DocumentRevision,
        IProjectWorkflowRuntime Runtime, Action<RuleProject>? ValidateScene);
    private readonly ProjectPersistence persistence;
    private readonly IProjectCompiler compiler;
    private Task<AlgorithmBuildResult>? compilation;
    private CancellationTokenSource? cancellation;
    private Publication? publication;
    private bool stopped;
    private string projectPath = "", scenePath = "";
    internal WorkshopVersions Versions { get; } = new();
    internal string? PublishedProjectRevision { get; private set; }
    internal bool IsPublishing => compilation != null;
    internal bool IsSavingScene => persistence.SceneSave != null;
    internal Task<AlgorithmBuildResult>? Compilation => compilation;
    internal Task<string>? SceneSave => persistence.SceneSave;

    internal string ProjectPath
    {
        get => projectPath;
        set {
            EnsureIdle(); string path = System.IO.Path.GetFullPath(value);
            if (SamePath(projectPath, path)) return;
            persistence.Observe(path); persistence.Observe(ProjectPaths.DraftPath(path)); projectPath = path;
        }
    }
    internal string ScenePath
    {
        get => scenePath;
        set { EnsureIdle(); string path = Path.GetFullPath(value); if (SamePath(scenePath, path)) return; persistence.Observe(path); scenePath = path; }
    }

    internal ProjectWorkflow(string projectPath, string scenePath, IProjectCompiler? compiler = null, ProjectPersistence? persistence = null)
    {
        this.persistence = persistence ?? new(); this.compiler = compiler ?? new AlgorithmProjectCompiler();
        ProjectPath = projectPath; ScenePath = scenePath;
    }

    internal void EnsureIdle()
    {
        ThrowIfStopped(); persistence.EnsureSceneIdle();
        if (IsPublishing) throw new InvalidOperationException("A közzététel befejezése előtt nem indítható új mentés vagy megnyitás.");
    }
    internal void ResetDocument(string revision)
    { EnsureIdle(); Versions.Reset(); Versions.Observe(revision); PublishedProjectRevision = null; }

    internal EditorProjectSource ReadSource(string file)
    {
        EnsureIdle();
        using var json = JsonDocument.Parse(persistence.ReadText(file));
        if (!json.RootElement.TryGetProperty("schema", out var schema)) throw new InvalidDataException("Hiányzó projektséma.");
        string text = json.RootElement.GetRawText();
        return schema.GetString() switch {
            "forest-rule-project" => new(Project: RuleProject.FromJson(text)),
            "forest-behaviors" => new(Behaviors: BehaviorModel.FromJson(text)),
            "forest-current-rules" => new(Catalog: GameRuleCatalog.FromJson(text)),
            _ => throw new InvalidDataException("Nyiss meg egy teljes szabályprojektet; a külön útkopási modell megszűnt.")
        };
    }

    internal ProjectOperation OpenScene(string file, IProjectWorkflowRuntime runtime, Action<RuleProject> activate)
    {
        EnsureIdle(); string path = Path.GetFullPath(file);
        var source = ReadSource(path);
        var project = runtime.PrepareDocument(source.Project ?? throw new InvalidDataException("A fájl nem tartalmaz szabályprojektet."));
        if (project.World == null) throw new InvalidDataException("A fájl nem tartalmaz jelenetet.");
        runtime.ValidatePreview(EditorDocument.ForSandbox(project));
        activate(project); scenePath = path;
        return new(ProjectOperationKind.SceneOpened, path);
    }

    internal ProjectOperation ApplyPreview(RuleProject project, IProjectWorkflowRuntime runtime)
    {
        ThrowIfStopped();
        runtime.ApplyPreview(runtime.Normalize(EditorDocument.ForSandbox(project)));
        return new(ProjectOperationKind.PreviewApplied);
    }

    internal ProjectOperation SaveDraft(RuleProject project)
    { EnsureIdle(); return SaveDraftSnapshot(project.Clone()); }

    private ProjectOperation SaveDraftSnapshot(RuleProject snapshot)
    {
        string revision = EditorDocument.RevisionOf(snapshot);
        string path = persistence.Save(ProjectPaths.DraftPath(projectPath), snapshot);
        Versions.Observe(revision); Versions.MarkSaved(revision);
        return new(ProjectOperationKind.DraftSaved, path, revision);
    }

    internal ProjectOperation SaveAll(RuleProject project, IProjectWorkflowRuntime runtime)
    {
        EnsureIdle(); var snapshot = project.Clone();
        var draft = SaveDraftSnapshot(snapshot);
        return snapshot.World == null ? draft : BeginSceneSave(snapshot, runtime);
    }

    internal ProjectOperation BeginSceneSave(RuleProject project, IProjectWorkflowRuntime runtime, string? destination = null)
    {
        EnsureIdle();
        return BeginSceneSave(project, destination ?? scenePath, runtime.CaptureSceneValidation());
    }

    private ProjectOperation BeginSceneSave(RuleProject project, string path, Action<RuleProject> validate)
    {
        persistence.BeginSceneSave(path, project, validate);
        return new(ProjectOperationKind.SceneSaveStarted, Path.GetFullPath(path));
    }

    internal ProjectOperation Publish(RuleProject project, IProjectWorkflowRuntime runtime, PublicationOptions? options = null)
    {
        EnsureIdle(); options ??= new();
        if (options.ExpectedFileRevision != null && (ProjectFileGuard.Revision(projectPath) ?? "missing") != options.ExpectedFileRevision)
            throw new IOException("A célprojekt verziója eltér az elvárttól; nem írtam felül.");
        persistence.RequireUnchanged(projectPath);
        var snapshot = project.Clone(); string revision = EditorDocument.RevisionOf(snapshot);
        var validated = runtime.Normalize(snapshot);
        var intent = new Publication(validated, projectPath, scenePath, revision, runtime,
            !options.SaveScene || validated.World == null ? null : runtime.CaptureSceneValidation());
        if (validated.Algorithms != null && (options.VerifyAlgorithms || compiler.RequiresCompilation(validated.Algorithms))) {
            cancellation = new(); var token = cancellation.Token;
            publication = intent;
            compilation = Task.Run(() => compiler.CompileAsync(intent.Project.Algorithms!, token), token);
            return new(ProjectOperationKind.PublishStarted, intent.Path, revision);
        }
        return CommitPublication(intent, null);
    }

    private ProjectOperation CommitPublication(Publication intent, string? buildRevision)
    {
        persistence.Save(intent.Path, intent.Project);
        Versions.MarkPublished(intent.DocumentRevision); PublishedProjectRevision = intent.Project.ContentRevision();
        // A compiled publication is handed over to the game. Its independently editable sandbox stays on its current draft.
        Exception? previewError = null;
        if (buildRevision == null) {
            try { intent.Runtime.ApplyPreview(EditorDocument.ForSandbox(intent.Project)); }
            catch (Exception error) { previewError = error; }
        }
        Exception? sceneError = null; bool saving = false;
        if (intent.ValidateScene != null) {
            try { BeginSceneSave(intent.Project, intent.ScenePath, intent.ValidateScene); saving = true; }
            catch (Exception error) { sceneError = error; }
        }
        return new(ProjectOperationKind.Published, intent.Path, intent.DocumentRevision, PublishedProjectRevision,
            buildRevision, sceneError, saving, previewError);
    }

    internal IReadOnlyList<ProjectOperation> Poll()
    {
        if (compilation?.IsCompleted != true && persistence.SceneSave?.IsCompleted != true) return Array.Empty<ProjectOperation>();
        var results = new List<ProjectOperation>();
        if (compilation?.IsCompleted == true) {
            try {
                var build = compilation.GetAwaiter().GetResult(); cancellation!.Token.ThrowIfCancellationRequested();
                results.Add(CommitPublication(publication!, build.Revision));
            } catch (OperationCanceledException) { results.Add(new(ProjectOperationKind.PublishCancelled)); }
            catch (Exception error) { results.Add(new(ProjectOperationKind.PublishFailed, Error: error)); }
            finally { ClearPublication(); }
        }
        if (persistence.SceneSave?.IsCompleted == true) results.Add(CompleteSceneSave());
        return results;
    }

    private ProjectOperation CompleteSceneSave()
    {
        try { scenePath = persistence.CompleteSceneSave()!; return new(ProjectOperationKind.SceneSaved, scenePath); }
        catch (Exception error) { return new(ProjectOperationKind.SceneSaveFailed, Error: error); }
    }

    /// <summary>Cancel publication, observe its result, and finish any already accepted scene write before closing the host.</summary>
    internal IReadOnlyList<ProjectOperation> Stop()
    {
        stopped = true;
        var results = new List<ProjectOperation>(); cancellation?.Cancel();
        if (compilation != null) {
            try { _ = compilation.GetAwaiter().GetResult(); results.Add(new(ProjectOperationKind.PublishCancelled)); }
            catch (OperationCanceledException) { results.Add(new(ProjectOperationKind.PublishCancelled)); }
            catch (Exception error) { results.Add(new(ProjectOperationKind.PublishFailed, Error: error)); }
            finally { ClearPublication(); }
        }
        if (persistence.SceneSave != null) results.Add(CompleteSceneSave());
        return results;
    }

    internal ProjectOperation SaveRecovery(RuleProject project)
    {
        string stem = projectPath + ".recovery-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string path = persistence.Save(ProjectPaths.DraftPath(stem), project.Clone());
        return new(ProjectOperationKind.RecoverySaved, path);
    }

    private void ClearPublication()
    { compilation = null; publication = null; cancellation?.Dispose(); cancellation = null; }
    private void ThrowIfStopped() { if (stopped) throw new ObjectDisposedException(nameof(ProjectWorkflow)); }
    private static bool SamePath(string a, string b) => string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    public void Dispose() => Stop();
}
