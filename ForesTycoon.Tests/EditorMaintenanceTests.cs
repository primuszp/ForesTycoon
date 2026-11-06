using System.Diagnostics;
using System.Reflection;
using ForesTycoon.Editor;
using ForesTycoon.RuleCompiler;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class EditorMaintenanceTests
{
    [Fact]
    public async Task SceneSaveRejectsAnExternalEditMadeDuringValidation()
    {
        using var files = new TemporaryFiles();
        string file = files.PathFor("scene.json");
        File.WriteAllText(file, "original");
        var persistence = new ProjectPersistence(); persistence.Observe(file);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        try {
            persistence.BeginSceneSave(file, new RuleProject(), _ => {
                started.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            File.WriteAllText(file, "external edit");
            release.Set();
            await Assert.ThrowsAsync<IOException>(async () => await persistence.SceneSave!);
            Assert.Throws<IOException>(() => persistence.CompleteSceneSave());
            Assert.Null(persistence.SceneSave);
            Assert.Equal("external edit", File.ReadAllText(file));
        } finally { release.Set(); }
    }

    [Theory]
    [InlineData("project.json")]
    [InlineData("project.json.draft.json")]
    [InlineData("scene.json")]
    public void AllDocumentWritesProtectExternalChangesAndArchiveThePreviousContent(string name)
    {
        using var files = new TemporaryFiles();
        string file = files.PathFor(name);
        var persistence = new ProjectPersistence(); persistence.Observe(file);
        var project = new RuleProject { Name = "first", Algorithms = new() { Revision = new string('a', 64), BundlePath = files.PathFor("bundle.json"), WorkspaceRoot = Path.GetTempPath() } };
        persistence.Save(file, project);
        string original = File.ReadAllText(file);
        project.Name = "second"; persistence.Save(file, project);
        Assert.Equal(original, File.ReadAllText(Assert.Single(Directory.GetFiles(file + ".history"))));
        Assert.Equal(project.Algorithms.Revision, RuleProject.FromJson(File.ReadAllText(file)).Algorithms.Revision);
        File.WriteAllText(file, "external");
        Assert.Throws<IOException>(() => persistence.Save(file, project));
        Assert.Equal("external", File.ReadAllText(file));
    }

    [Fact]
    public async Task BusySceneSaveRejectsASecondDestinationAndCapturesTheAcceptedDocument()
    {
        using var files = new TemporaryFiles();
        var persistence = new ProjectPersistence();
        var project = new RuleProject { Name = "captured" };
        using var release = new ManualResetEventSlim();
        string first = files.PathFor("first.json"), second = files.PathFor("second.json");
        try {
            persistence.BeginSceneSave(first, project, _ => {
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            });
            project.Name = "newer edit";
            Assert.Throws<InvalidOperationException>(() => persistence.BeginSceneSave(second, project, _ => { }));
            Assert.False(File.Exists(second));
            release.Set(); await persistence.SceneSave!;
            Assert.Equal(first, persistence.CompleteSceneSave());
            Assert.Equal("captured", RuleProject.FromJson(File.ReadAllText(first)).Name);
            persistence.BeginSceneSave(second, project, _ => { }); await persistence.SceneSave!;
            Assert.Equal(second, persistence.CompleteSceneSave());
            Assert.Equal("newer edit", RuleProject.FromJson(File.ReadAllText(second)).Name);
        } finally { release.Set(); }
    }

    [Fact]
    public void FailedProjectApplicationKeepsTheErrorInsteadOfReportingSuccess()
    {
        using var world = FoundationPersistenceTests.FlatWorld();
        var editor = new RuleEditorView(); Invoke(editor, "EnsureProject", world);
        var project = editor.CollectProject();
        project.Behaviors.Graphs[0].Output = "missing-output";
        Invoke(editor, "LoadProject", world, project);
        var workspace = Field<RuleWorkspaceView>(editor, "ruleWorkspace");
        Invoke(Field<RuleBookView>(workspace, "book"), "ApplyRuleBook", world, "should-not-be-success");
        string message = Field<string>(editor, "message");
        Assert.NotEmpty(message);
        Assert.DoesNotContain("should-not-be-success", message);
        Assert.False(world.CommandPending);
    }

    [Fact]
    public async Task PublishAndSaveAsPreserveAlgorithmsAndChangeTheActivePathOnlyAfterSuccess()
    {
        using var files = new TemporaryFiles();
        using var world = FoundationPersistenceTests.FlatWorld();
        var editor = new RuleEditorView();
        Set(editor, "projectPath", files.PathFor("published.json"));
        Set(editor, "scenePath", files.PathFor("original-scene.json"));
        Invoke(editor, "EnsureProject", world);
        Invoke(editor, "LoadProject", world, editor.CollectProject());
        Invoke(editor, "ApplyProject", world, true);
        var workflow = Field<ProjectWorkflow>(editor, "workflow");
        await workflow.SceneSave!; Invoke(editor, "RefreshProjectOperations", world);
        Assert.Equal(AlgorithmBuild.CurrentReference?.Revision,
            RuleProject.FromJson(File.ReadAllText(files.PathFor("published.json"))).Algorithms?.Revision);
        Assert.Equal(AlgorithmBuild.CurrentReference?.Revision,
            RuleProject.FromJson(File.ReadAllText(files.PathFor("original-scene.json"))).Algorithms?.Revision);

        string next = files.PathFor("save-as.json");
        var custom = editor.CollectProject();
        custom.Algorithms = new() { Revision = new string('a', 64), BundlePath = files.PathFor("bundle.json"), WorkspaceRoot = Path.GetTempPath() };
        Invoke(editor, "SaveScene", world, next, custom);
        Assert.Equal(files.PathFor("original-scene.json"), Field<string>(editor, "scenePath"));
        await workflow.SceneSave!; Invoke(editor, "RefreshProjectOperations", world);
        Assert.Equal(next, Field<string>(editor, "scenePath"));
        Assert.Equal(custom.Algorithms.Revision, RuleProject.FromJson(File.ReadAllText(next)).Algorithms.Revision);

        string rejected = files.PathFor("rejected.json");
        var invalid = editor.CollectProject(); invalid.Behaviors.Graphs[0].Output = "missing-output";
        Invoke(editor, "SaveScene", world, rejected, invalid);
        await Assert.ThrowsAnyAsync<Exception>(async () => await workflow.SceneSave!);
        Invoke(editor, "RefreshProjectOperations", world);
        Assert.Equal(next, Field<string>(editor, "scenePath")); Assert.False(File.Exists(rejected));
    }

    [Fact]
    public async Task ACustomAlgorithmBundleMustCompileBeforePublication()
    {
        using var files = new TemporaryFiles();
        using var world = FoundationPersistenceTests.FlatWorld();
        var editor = new RuleEditorView();
        string published = files.PathFor("published.json");
        Set(editor, "projectPath", published);
        Invoke(editor, "EnsureProject", world);
        var project = editor.CollectProject();
        project.Algorithms = new() { Revision = new string('a', 64), BundlePath = files.PathFor("missing-bundle.json"), WorkspaceRoot = Path.GetTempPath() };
        Invoke(editor, "LoadProject", world, project);
        Invoke(editor, "ApplyProject", world, true);
        var build = Field<ProjectWorkflow>(editor, "workflow").Compilation!;
        Assert.NotNull(build);
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await build);
        Invoke(editor, "RefreshProjectOperations", world);
        Assert.False(File.Exists(published));
        Assert.Equal(project.Algorithms.Revision, editor.CollectProject().Algorithms.Revision);
    }

    [Fact]
    public void SandboxProjectionLeavesTheAuthoredAlgorithmBundleIntact()
    {
        var authored = RuleProject.CreateDefault();
        authored.Algorithms = new() { Revision = new string('a', 64), BundlePath = "custom.json" };
        var preview = EditorDocument.ForSandbox(authored);
        Assert.Null(preview.Algorithms);
        Assert.Equal(new string('a', 64), authored.Algorithms.Revision);
        preview.Behaviors.Graphs.Clear(); Assert.NotEmpty(authored.Behaviors.Graphs);
    }

    [Fact]
    public void CapturedDocumentsDoNotShareMutableObjectsWithTheSourceOrTheEditor()
    {
        var source = RuleProject.CreateDefault();
        source.Algorithms = new() { Revision = "original" };
        var document = new EditorDocument(); document.Load(source);
        source.Algorithms.Revision = "external";
        var captured = document.Capture();
        Assert.Equal("original", captured.Algorithms.Revision);
        captured.Algorithms.Revision = "changed"; captured.Rules.Clear();
        Assert.Equal("original", document.Algorithms!.Revision); Assert.NotEmpty(document.Rules!);
    }

    [Fact]
    public void TrialResultsSurviveTheSameRevisionButAreInvalidatedByAnotherDocumentRevision()
    {
        using var trials = new TrialService();
        var result = new WorldTrialResult(new double[1], new double[1], new double[1], new bool[1], "captured", 1, 1, "revision");
        trials.Show(result); trials.ValidateSource("revision");
        Assert.Same(result, trials.ResultFor(1, 1));
        trials.ValidateSource("different-revision");
        Assert.Null(trials.ResultFor(1, 1));
    }

    [Fact]
    public void IdleRevisionReadsDoNotRebuildTheDocumentAndAnEditChangesItsIdentity()
    {
        using var world = FoundationPersistenceTests.FlatWorld();
        var editor = new RuleEditorView(); Invoke(editor, "EnsureProject", world);
        string before = (string)Invoke(editor, "AuthoringRevision", world)!;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 30; i++) Assert.Equal(before, Invoke(editor, "AuthoringRevision", world));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 128 * 1024,
            "Idle frames must not clone, serialize and hash the full project again.");
        editor.SetTuningForTest("DieselPrice", 1.23);
        Assert.NotEqual(before, Invoke(editor, "AuthoringRevision", world));
    }

    [Fact]
    public void ProjectSwitchAndDifferentMapDimensionsCannotDisplayAnOldTrial()
    {
        using var world = FoundationPersistenceTests.FlatWorld();
        var editor = new RuleEditorView(); Invoke(editor, "EnsureProject", world);
        var trials = Field<TrialService>(editor, "trials");
        var result = new WorldTrialResult(new double[256], new double[256], new double[256], new bool[256], "old", 16, 16, "old-revision");
        editor.ShowWorldTrial(result);
        Assert.Same(result, trials.ResultFor(16, 16)); Assert.Null(trials.ResultFor(32, 32));
        var project = editor.CollectProject(); project.World = GameWorld.DefaultScenario(42, 32);
        Invoke(editor, "LoadProject", world, project);
        Assert.Null(trials.ResultFor(16, 16)); Assert.Null(trials.ResultFor(32, 32));
        Assert.False((result with { Water = new double[1] }).Fits(16, 16));
    }

    [Fact]
    public async Task InvalidatingARunningTrialDiscardsItsCompletion()
    {
        using var trials = new TrialService();
        var project = GameWorld.DefaultRuleProject(); project.World = GameWorld.DefaultScenario(42, 16);
        trials.Start(project, project, 0, 0, "old-revision");
        trials.Invalidate();
        var timeout = Stopwatch.StartNew();
        while (trials.IsBusy && timeout.Elapsed < TimeSpan.FromSeconds(10)) { trials.Poll(); await Task.Delay(10); }
        Assert.False(trials.IsBusy); Assert.Null(trials.Result); Assert.Null(trials.Error);
    }

    [Fact]
    public void HistoryKeepsItsBoundAndDropsRedoWhenEditingAfterUndo()
    {
        var history = new EditorHistory<string>(3);
        history.Record("a", "open"); history.Record("b", "edit"); history.Record("c", "edit"); history.Record("d", "edit");
        Assert.Equal(3, history.Count); Assert.Equal("b", history[0].State);
        history.MoveTo(1); Assert.True(history.CanUndo); Assert.True(history.CanRedo);
        Assert.False(history.Record("c", "unchanged")); Assert.True(history.CanRedo);
        history.Record("new", "branch"); Assert.False(history.CanRedo); Assert.Equal("new", history.Current!.State);
        history.Clear(); Assert.False(history.CanUndo); Assert.False(history.CanRedo); Assert.Null(history.Current);
    }

    private static object? Invoke(object instance, string method, params object[] args) => instance.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
    private static T Field<T>(object instance, string name)
    {
        var type = instance.GetType(); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return (T)(type.GetField(name, flags) is { } field ? field.GetValue(instance) : type.GetProperty(name, flags)!.GetValue(instance))!;
    }
    private static void Set(object instance, string name, object value)
    {
        var type = instance.GetType(); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        if (type.GetField(name, flags) is { } field) field.SetValue(instance, value);
        else type.GetProperty(name, flags)!.SetValue(instance, value);
    }

    private sealed class TemporaryFiles : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "editor-maintenance-" + Guid.NewGuid().ToString("N"));
        internal TemporaryFiles() => Directory.CreateDirectory(folder);
        internal string PathFor(string file) => Path.Combine(folder, file);
        public void Dispose() => Directory.Delete(folder, recursive: true);
    }
}
