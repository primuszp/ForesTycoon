using System.Reflection;
using ForesTycoon.Editor;
using ForesTycoon.RuleCompiler;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class ProjectWorkflowTests
{
    [Fact]
    public void PreviewNormalizesASandboxCopyAndDoesNotWriteOrChangeTheAuthoredAlgorithms()
    {
        using var files = new Files(); using var flow = files.Workflow(); var runtime = new Runtime(); var source = Project();
        var result = flow.ApplyPreview(source, runtime);
        Assert.Equal(ProjectOperationKind.PreviewApplied, result.Kind);
        Assert.Null(Assert.Single(runtime.Previews).Algorithms); Assert.NotNull(source.Algorithms);
        Assert.Equal(new[] { "normalize", "apply" }, runtime.Calls); Assert.Empty(Directory.GetFiles(files.Root));
        runtime.NormalizeError = new InvalidDataException("invalid");
        Assert.Throws<InvalidDataException>(() => flow.ApplyPreview(source, runtime)); Assert.Single(runtime.Previews);
    }

    [Fact]
    public void DraftsCanContainRepairableGraphsAndMarkOnlyTheirCapturedRevisionAsSaved()
    {
        using var files = new Files(); using var flow = files.Workflow(); var source = Project();
        source.Behaviors.Graphs.Add(new() { Id = "repair", Output = "missing" });
        var result = flow.SaveDraft(source); string revision = EditorDocument.RevisionOf(source);
        source.Name = "newer"; flow.Versions.Observe(EditorDocument.RevisionOf(source));
        Assert.Equal(ProjectOperationKind.DraftSaved, result.Kind); Assert.Equal(revision, flow.Versions.Saved);
        Assert.True(flow.Versions.Unsaved); Assert.Equal("draft", RuleProject.FromJson(File.ReadAllText(result.Path!)).Name);
        Assert.False(File.Exists(flow.ProjectPath)); Assert.False(File.Exists(flow.ScenePath));
    }

    [Fact]
    public async Task SaveAllUsesTheSameSnapshotForDraftAndSceneWhileTheUserKeepsEditing()
    {
        using var files = new Files(); using var flow = files.Workflow(); using var release = new ManualResetEventSlim();
        var runtime = new Runtime { SceneValidation = _ => Wait(release) }; var source = Project(); string revision = EditorDocument.RevisionOf(source);
        try {
            Assert.Equal(ProjectOperationKind.SceneSaveStarted, flow.SaveAll(source, runtime).Kind);
            source.Name = "newer"; flow.Versions.Observe(EditorDocument.RevisionOf(source));
            release.Set(); await flow.SceneSave!;
            Assert.Equal(ProjectOperationKind.SceneSaved, Assert.Single(flow.Poll()).Kind);
            Assert.Equal("draft", RuleProject.FromJson(File.ReadAllText(flow.ScenePath)).Name);
            Assert.Equal("draft", RuleProject.FromJson(File.ReadAllText(RuleWorkshopModel.DraftPath(flow.ProjectPath))).Name);
            Assert.Equal(revision, flow.Versions.Saved); Assert.True(flow.Versions.Unsaved);
        } finally { release.Set(); }
    }

    [Fact]
    public async Task FailedSaveAsKeepsTheActivePathAndReleasesTheBusyStateAfterPolling()
    {
        using var files = new Files(); using var flow = files.Workflow(); string original = flow.ScenePath;
        var runtime = new Runtime { SceneValidation = _ => throw new InvalidDataException("invalid scene") };
        flow.BeginSceneSave(Project(), runtime, files.PathFor("rejected.json"));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await flow.SceneSave!);
        Assert.True(flow.IsSavingScene); Assert.Equal(original, flow.ScenePath);
        Assert.Equal(ProjectOperationKind.SceneSaveFailed, Assert.Single(flow.Poll()).Kind);
        Assert.False(flow.IsSavingScene); Assert.Equal(original, flow.ScenePath); Assert.False(File.Exists(files.PathFor("rejected.json")));
    }

    [Fact]
    public async Task AcceptedSceneSaveBlocksOverlappingOperationsAndPathChanges()
    {
        using var files = new Files(); using var flow = files.Workflow(); using var release = new ManualResetEventSlim();
        var runtime = new Runtime { SceneValidation = _ => Wait(release) }; var source = Project();
        try {
            flow.BeginSceneSave(source, runtime);
            Assert.Throws<InvalidOperationException>(() => flow.Publish(source, runtime));
            Assert.Throws<InvalidOperationException>(() => flow.SaveAll(source, runtime));
            Assert.Throws<InvalidOperationException>(() => flow.ReadSource(files.PathFor("other.json")));
            Assert.Throws<InvalidOperationException>(() => flow.ProjectPath = files.PathFor("other.json"));
            Assert.Throws<InvalidOperationException>(() => flow.ScenePath = files.PathFor("other-scene.json"));
            release.Set(); await flow.SceneSave!; flow.Poll(); Assert.False(flow.IsSavingScene);
        } finally { release.Set(); }
    }

    [Fact]
    public async Task CurrentAlgorithmsPublishWithoutCompilationAndRemainInBothSavedFiles()
    {
        using var files = new Files(); var compiler = new Compiler { Required = false }; using var flow = files.Workflow(compiler);
        var runtime = new Runtime(); var source = Project();
        var result = flow.Publish(source, runtime);
        Assert.Equal(ProjectOperationKind.Published, result.Kind); Assert.False(compiler.Started.Task.IsCompleted);
        Assert.True(result.SceneSaveStarted); Assert.Null(Assert.Single(runtime.Previews).Algorithms);
        await flow.SceneSave!; flow.Poll();
        Assert.Equal("custom", RuleProject.FromJson(File.ReadAllText(flow.ProjectPath)).Algorithms.Revision);
        Assert.Equal("custom", RuleProject.FromJson(File.ReadAllText(flow.ScenePath)).Algorithms.Revision);
        Assert.Equal(EditorDocument.RevisionOf(source), flow.Versions.Published);
    }

    [Fact]
    public async Task CompilationPublishesItsCapturedDocumentAndValidationContext()
    {
        using var files = new Files(); var compiler = new Compiler(); using var flow = files.Workflow(compiler);
        int captured = 0; var runtime = new Runtime { SceneValidation = _ => captured++ }; var source = Project();
        string revision = EditorDocument.RevisionOf(source);
        Assert.Equal(ProjectOperationKind.PublishStarted, flow.Publish(source, runtime).Kind);
        await compiler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(File.Exists(flow.ProjectPath));
        Assert.Throws<InvalidOperationException>(() => flow.ScenePath = files.PathFor("redirect.json"));
        source.Name = "newer"; source.Algorithms.BundlePath = "newer";
        flow.Versions.Observe(EditorDocument.RevisionOf(source)); runtime.SceneValidation = _ => throw new InvalidOperationException("wrong context");
        compiler.Complete(); await flow.Compilation!; var results = flow.Poll();
        Assert.Contains(results, r => r.Kind == ProjectOperationKind.Published && r.DocumentRevision == revision);
        if (flow.SceneSave != null) { await flow.SceneSave; flow.Poll(); }
        Assert.Equal(1, captured); Assert.Empty(runtime.Previews); Assert.False(flow.IsPublishing);
        var saved = RuleProject.FromJson(File.ReadAllText(flow.ProjectPath));
        Assert.Equal("draft", saved.Name); Assert.Equal("bundle.json", saved.Algorithms.BundlePath);
        Assert.Equal(revision, flow.Versions.Published); Assert.NotEqual(flow.Versions.Current, flow.Versions.Published);
    }

    [Fact]
    public async Task CompilationFailureDoesNotReplacePublishedDataOrStartASceneSave()
    {
        using var files = new Files(); File.WriteAllText(files.PathFor("published.json"), "original");
        var compiler = new Compiler(); using var flow = files.Workflow(compiler); var source = Project();
        flow.Publish(source, new Runtime()); compiler.Fail(new InvalidDataException("compile failed"));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await flow.Compilation!);
        Assert.Equal(ProjectOperationKind.PublishFailed, Assert.Single(flow.Poll()).Kind);
        Assert.Equal("original", File.ReadAllText(flow.ProjectPath)); Assert.Null(flow.Versions.Published);
        Assert.Null(flow.SceneSave); Assert.False(flow.IsPublishing); Assert.Equal("custom", source.Algorithms.Revision);
    }

    [Fact]
    public async Task ExternalEditsDuringCompilationAreProtectedAtPublicationTime()
    {
        using var files = new Files(); var compiler = new Compiler(); using var flow = files.Workflow(compiler);
        flow.Publish(Project(), new Runtime()); File.WriteAllText(flow.ProjectPath, "external");
        compiler.Complete(); await flow.Compilation!;
        var result = Assert.Single(flow.Poll()); Assert.Equal(ProjectOperationKind.PublishFailed, result.Kind); Assert.IsType<IOException>(result.Error);
        Assert.Equal("external", File.ReadAllText(flow.ProjectPath)); Assert.Null(flow.Versions.Published); Assert.Null(flow.SceneSave);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopNeverPublishesAnUnconsumedCompilation(bool completed)
    {
        using var files = new Files(); var compiler = new Compiler(); using var flow = files.Workflow(compiler);
        flow.Publish(Project(), new Runtime()); await compiler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (completed) { compiler.Complete(); await flow.Compilation!; }
        Assert.Equal(ProjectOperationKind.PublishCancelled, Assert.Single(flow.Stop()).Kind);
        Assert.False(flow.IsPublishing); Assert.False(File.Exists(flow.ProjectPath)); Assert.Empty(flow.Poll()); Assert.Empty(flow.Stop());
        Assert.Throws<ObjectDisposedException>(() => flow.Publish(Project(), new Runtime()));
    }

    [Fact]
    public async Task StopFinishesAnAcceptedSaveAsAndUpdatesTheActivePath()
    {
        using var files = new Files(); using var flow = files.Workflow(); using var release = new ManualResetEventSlim();
        string destination = files.PathFor("new-scene.json");
        try {
            flow.BeginSceneSave(Project(), new Runtime { SceneValidation = _ => Wait(release) }, destination);
            var stop = Task.Run(flow.Stop); release.Set(); var results = await stop;
            Assert.Equal(ProjectOperationKind.SceneSaved, Assert.Single(results).Kind);
            Assert.Equal(destination, flow.ScenePath); Assert.False(flow.IsSavingScene); Assert.True(File.Exists(destination));
        } finally { release.Set(); }
    }

    [Fact]
    public void SceneOpeningValidatesASandboxBeforeActivatingTheAuthoredProject()
    {
        using var files = new Files(); using var flow = files.Workflow(); var source = Project(); var runtime = new Runtime();
        string path = files.PathFor("open.json"); File.WriteAllText(path, source.ToJson());
        var result = flow.OpenScene(path, runtime, p => {
            Assert.Equal(new[] { "prepare", "validate" }, runtime.Calls); Assert.NotNull(p.Algorithms);
        });
        Assert.Equal(ProjectOperationKind.SceneOpened, result.Kind); Assert.Equal(path, flow.ScenePath); Assert.Null(runtime.Validated!.Algorithms);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("world")]
    [InlineData("validation")]
    [InlineData("activation")]
    public void FailedSceneOpeningDoesNotChangeTheActivePath(string failure)
    {
        using var files = new Files(); using var flow = files.Workflow(); string original = flow.ScenePath;
        string path = files.PathFor("open.json"); var source = Project(); if (failure == "world") source.World = null;
        File.WriteAllText(path, failure == "json" ? "invalid" : source.ToJson());
        var runtime = new Runtime { ValidateError = failure == "validation" ? new InvalidDataException("invalid") : null };
        bool activated = false;
        Assert.ThrowsAny<Exception>(() => flow.OpenScene(path, runtime, _ => { activated = true; throw new InvalidOperationException("activation"); }));
        Assert.Equal(failure == "activation", activated); Assert.Equal(original, flow.ScenePath); Assert.False(flow.IsPublishing); Assert.False(flow.IsSavingScene);
    }

    [Fact]
    public void RecoveryAfterStopKeepsTheLatestDraftWithoutChangingThePublishedProject()
    {
        using var files = new Files(); using var flow = files.Workflow(); flow.Stop();
        var source = Project(); source.Name = "latest"; var result = flow.SaveRecovery(source);
        Assert.Equal(ProjectOperationKind.RecoverySaved, result.Kind); Assert.Contains(".recovery-", result.Path!);
        Assert.Equal("latest", RuleProject.FromJson(File.ReadAllText(result.Path!)).Name);
        Assert.False(File.Exists(flow.ProjectPath)); Assert.False(File.Exists(flow.ScenePath)); Assert.Null(flow.Versions.Saved);
    }

    [Fact]
    public void IdlePollingDoesNotAllocateAResultListOnEveryFrame()
    {
        using var files = new Files(); using var flow = files.Workflow(); flow.Poll();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        int count = 0;
        for (int i = 0; i < 1000; i++) count += flow.Poll().Count;
        long used = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(0, count); Assert.True(used < 4096);
    }

    [Fact]
    public async Task APublishedFileRemainsPublishedWhenOnlyThePreviewApplicationFails()
    {
        using var files = new Files(); using var flow = files.Workflow(new Compiler { Required = false });
        var source = Project(); var result = flow.Publish(source, new Runtime { ApplyError = new InvalidOperationException("preview") });
        Assert.Equal(ProjectOperationKind.Published, result.Kind); Assert.NotNull(result.PreviewError);
        Assert.Equal(EditorDocument.RevisionOf(source), flow.Versions.Published); Assert.True(File.Exists(flow.ProjectPath));
        await flow.SceneSave!; Assert.Equal(ProjectOperationKind.SceneSaved, Assert.Single(flow.Poll()).Kind);
    }

    [Fact]
    public async Task SceneValidationFailureAfterPublicationReportsTheSeparateSceneFailure()
    {
        using var files = new Files(); using var flow = files.Workflow(new Compiler { Required = false });
        var result = flow.Publish(Project(), new Runtime { SceneValidation = _ => throw new InvalidDataException("scene") });
        Assert.Equal(ProjectOperationKind.Published, result.Kind); Assert.Null(result.Error);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await flow.SceneSave!);
        Assert.Equal(ProjectOperationKind.SceneSaveFailed, Assert.Single(flow.Poll()).Kind);
        Assert.NotNull(flow.Versions.Published); Assert.True(File.Exists(flow.ProjectPath)); Assert.False(File.Exists(flow.ScenePath));
    }

    [Fact]
    public async Task AClosedEditorConsumesSaveCompletionWithoutAnImGuiFrame()
    {
        using var files = new Files(); using var world = FoundationPersistenceTests.FlatWorld();
        var editor = new RuleEditorView(); Invoke(editor, "EnsureProject", world);
        var flow = (ProjectWorkflow)typeof(RuleEditorView).GetField("workflow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
        string destination = files.PathFor("hidden-save.json"); Invoke(editor, "SaveScene", world, destination, null);
        await flow.SceneSave!; bool open = false; editor.Draw(world, ref open);
        Assert.Equal(destination, flow.ScenePath); Assert.False(flow.IsSavingScene);
    }

    [Fact]
    public void ClosingBeforeTheFirstDrawStillRecoversEditsMadeSinceOpening()
    {
        using var files = new Files(); using var world = FoundationPersistenceTests.FlatWorld();
        var editor = new RuleEditorView();
        var flow = (ProjectWorkflow)typeof(RuleEditorView).GetField("workflow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
        flow.ProjectPath = files.PathFor("published.json"); flow.ScenePath = files.PathFor("scene.json");
        Invoke(editor, "EnsureProject", world); editor.SetTuningForTest("DieselPrice", 1.21);
        editor.StopBackgroundOperations(world);
        string recovery = Assert.Single(Directory.GetFiles(files.Root, "*.recovery-*.draft.json"));
        Assert.Equal(1.21, RuleProject.FromJson(File.ReadAllText(recovery)).Tuning["DieselPrice"]);
        Assert.False(File.Exists(flow.ProjectPath)); Assert.False(File.Exists(flow.ScenePath));
    }

    private static RuleProject Project() => new() { Name = "draft", World = new() { Name = "scene" },
        Algorithms = new() { Revision = "custom", BundlePath = "bundle.json", WorkspaceRoot = "workspace" } };
    private static void Wait(ManualResetEventSlim release)
    { if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); }
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private sealed class Runtime : IProjectWorkflowRuntime
    {
        internal readonly List<string> Calls = new();
        internal readonly List<RuleProject> Previews = new();
        internal Action<RuleProject> SceneValidation = _ => { };
        internal Exception? NormalizeError, ValidateError, ApplyError;
        internal RuleProject? Validated;
        public RuleProject Normalize(RuleProject project) { Calls.Add("normalize"); if (NormalizeError != null) throw NormalizeError; return project.Clone(); }
        public RuleProject PrepareDocument(RuleProject project) { Calls.Add("prepare"); return project.Clone(); }
        public void ValidatePreview(RuleProject project) { Calls.Add("validate"); Validated = project; if (ValidateError != null) throw ValidateError; }
        public void ApplyPreview(RuleProject project) { Calls.Add("apply"); if (ApplyError != null) throw ApplyError; Previews.Add(project.Clone()); }
        public Action<RuleProject> CaptureSceneValidation() => SceneValidation;
    }
    private sealed class Compiler : IProjectCompiler
    {
        internal bool Required = true;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<AlgorithmBuildResult> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool RequiresCompilation(AlgorithmGraphReference reference) => Required;
        public Task<AlgorithmBuildResult> CompileAsync(AlgorithmGraphReference reference, CancellationToken cancellation)
        { Started.TrySetResult(); return result.Task.WaitAsync(cancellation); }
        internal void Complete() => result.TrySetResult(new(new string('a', 64), "runtime.dll", "compiled"));
        internal void Fail(Exception error) => result.TrySetException(error);
    }
    private sealed class Files : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "project-workflow-" + Guid.NewGuid().ToString("N"));
        internal Files() => Directory.CreateDirectory(Root);
        internal string PathFor(string name) => Path.Combine(Root, name);
        internal ProjectWorkflow Workflow(IProjectCompiler? compiler = null) => new(PathFor("published.json"), PathFor("scene.json"), compiler);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
