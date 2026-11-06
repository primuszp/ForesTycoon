using System.Reflection;
using ForesTycoon.Editor;

namespace ForesTycoon.Tests;

[Collection("SpeciesBook")]
public sealed class EditorArchitectureTests
{
    [Fact]
    public void UndoCapturesAnEditMadeBetweenHistorySamplesAndRedoRestoresIt()
    {
        using var world = FoundationPersistenceTests.FlatWorld();
        var editor = Open(world);
        editor.SetTuningForTest("DieselPrice", 1.20); Invoke(editor, "RecordHistory", world);
        editor.SetTuningForTest("DieselPrice", 1.21);
        editor.Undo(world); Assert.Equal(1.20, Price(editor));
        Assert.Equal(1.20, Parameter(Field<CurrentRulesView>(editor, "currentRules"), "DieselPrice"));
        editor.Redo(world); Assert.Equal(1.21, Price(editor));
    }

    [Fact]
    public void TheFirstEditCanBeUndoneBeforeAnyPeriodicHistorySample()
    {
        using var world = FoundationPersistenceTests.FlatWorld();
        var editor = Open(world); double original = Price(editor);
        editor.SetTuningForTest("DieselPrice", 1.21);
        editor.Undo(world); Assert.Equal(original, Price(editor));
        editor.Redo(world); Assert.Equal(1.21, Price(editor));
    }

    [Fact]
    public void RedoDoesNotOverwriteANewUnrecordedEditAfterUndo()
    {
        using var world = FoundationPersistenceTests.FlatWorld();
        var editor = Open(world);
        editor.SetTuningForTest("DieselPrice", 1.20); Invoke(editor, "RecordHistory", world);
        editor.SetTuningForTest("DieselPrice", 1.21); editor.Undo(world);
        editor.SetTuningForTest("DieselPrice", 1.22); editor.Redo(world);
        Assert.Equal(1.22, Price(editor)); Assert.False(editor.CanRedo);
        editor.Undo(world); Assert.Equal(1.20, Price(editor));
        editor.Redo(world); Assert.Equal(1.22, Price(editor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectingAGraphNodeOrControllerAfterUndoKeepsRedo(bool controller)
    {
        var state = new BehaviorEditorState();
        state.LoadDraft(controller ? new BehaviorModel { Controllers = new() { new() { Id = "controller", Kind = "truck", InitialState = "state",
            States = new() { new() { Id = "state", Name = "Original" } } } } }
            : new BehaviorModel { Graphs = new() { Constant(1) } });
        var view = new BehaviorEditorView(state);
        state.Remember();
        if (controller) state.Draft.Controllers[0].States[0].Name = "Changed";
        else state.Draft.Graphs[0].Nodes[0].Value = 2;
        Assert.True(view.Undo());
        if (controller) view.SelectControllerById("controller"); else view.SelectGraph("formula");
        // A redundant checkpoint must also be harmless, including callers that have not yet adopted drag transactions.
        state.Remember();
        Assert.True(view.Redo());
        if (controller) Assert.Equal("Changed", state.Draft.Controllers[0].States[0].Name);
        else Assert.Equal(2, state.Draft.Graphs[0].Nodes[0].Value);
    }

    [Fact]
    public void ARealLocalEditAfterUndoBranchesHistoryEvenWhenRedoIsPressedImmediately()
    {
        var state = new BehaviorEditorState(); state.LoadDraft(new() { Graphs = new() { Constant(1) } });
        state.Remember(); state.Draft.Graphs[0].Nodes[0].Value = 2;
        Assert.True(state.Undo()); state.Remember(); state.Draft.Graphs[0].Nodes[0].Value = 3;
        Assert.False(state.Redo()); Assert.Equal(3, state.Draft.Graphs[0].Nodes[0].Value);
        Assert.True(state.Undo()); Assert.Equal(1, state.Draft.Graphs[0].Nodes[0].Value);
        Assert.True(state.Redo()); Assert.Equal(3, state.Draft.Graphs[0].Nodes[0].Value);
    }

    [Fact]
    public void RestoringAVisualStarterRemovesItsPreviousBlockingConditionGraph()
    {
        var document = new EditorDocument();
        var starter = RuleEngine.StarterRules().Single(r => r.Id == "traffic.wear");
        var rule = document.RuleBook.AddStarter(starter);
        document.RuleBook.SetCalculation(rule, null, Constant(0));
        Assert.True(document.RuleBook.RestoreVisualRule(rule));
        Assert.Equal(starter.Condition, rule.Condition); Assert.Null(rule.ConditionGraph);
        _ = new RuleEngine(document.Rules, document.Fields, Array.Empty<string>(), _ => 0);
    }

    [Fact]
    public void SavedSceneIdentityIncludesAuthoringButTheRuntimeIdentityStaysTheSame()
    {
        var project = GameWorld.DefaultRuleProject(); var original = SceneKey(project);
        string runtime = project.ContentRevision();
        project.Authoring.Templates.Add(AuthoringOperations.Template(Constant(2)));
        Assert.NotEqual(original, SceneKey(project)); Assert.Equal(runtime, project.ContentRevision());
        project.Authoring.Templates.Clear(); Assert.Equal(original, SceneKey(project));
        project.Authoring = null!; Assert.Equal(original, SceneKey(project));
    }

    [Fact]
    public void DocumentCapturesItsOwnedTuningWithoutAnyViewAndIsolatesLoadsAndSnapshots()
    {
        var source = GameWorld.DefaultRuleProject(); source.World = GameWorld.DefaultScenario(42, 16); source.Tuning["DieselPrice"] = 1.20;
        var document = new EditorDocument(); document.Load(source);
        source.Tuning["DieselPrice"] = 1.21;
        var captured = document.Capture(); Assert.Equal(1.20, captured.Tuning["DieselPrice"]);
        captured.Tuning["DieselPrice"] = 1.22; captured.World!.Name = "Changed outside";
        Assert.Equal(1.20, document.Tuning.Get("DieselPrice")); Assert.NotEqual("Changed outside", document.World!.Name);
        string before = document.Revision(); document.Tuning.Set("DieselPrice", 1.23);
        Assert.NotEqual(before, document.Revision());
        Assert.Equal(1.23, document.Capture().Tuning["DieselPrice"]);
    }

    [Fact]
    public void TuningRejectsInvalidValuesAtomicallyAndDefaultsDoNotCreateOverrides()
    {
        var document = new EditorDocument(); document.Tuning.Set("DieselPrice", 1.20);
        string before = document.Revision(); long version = document.Version;
        Assert.Throws<ArgumentException>(() => document.Tuning.Set("DieselPrice", double.NaN));
        Assert.Throws<ArgumentException>(() => document.Tuning.Replace(new Dictionary<string, double> { ["DieselPrice"] = 1.21, ["unknown"] = 1 }));
        Assert.Equal(1.20, document.Tuning.Get("DieselPrice"));
        Assert.Equal(version, document.Version); Assert.Equal(before, document.Revision());
        document.Tuning.Set("DieselPrice", GameTuning.Default[Tune.DieselPrice]);
        Assert.Empty(document.Tuning.Capture());
    }

    [Fact]
    public void CatalogAndGeometryEditsUseTheDocumentValuesAndCatalogReloadCannotOverwriteThem()
    {
        var document = new EditorDocument(); document.Tuning.Set("DieselPrice", 1.20);
        var view = new CurrentRulesView(document.Tuning); view.Load(CurrentGameRules.Build());
        Assert.Equal(1.20, Parameter(view, "DieselPrice"));
        view.SetGeometry("TruckWheelbase", 5);
        Assert.Equal(5, document.Tuning.Get("TruckWheelbase"));
        document.Tuning.Set("DieselPrice", 1.21); Assert.Equal(1.21, Parameter(view, "DieselPrice"));
        view.Load(CurrentGameRules.Build());
        Assert.Equal(1.21, Parameter(view, "DieselPrice")); Assert.Equal(5, view.GetGeometry("TruckWheelbase"));
        var captured = view.TuningOverrides(null); captured["DieselPrice"] = 1.22;
        Assert.Equal(1.21, document.Tuning.Get("DieselPrice"));
    }

    [Fact]
    public void OpeningAProjectOwnsAllRuntimeFragmentsAndReadingThemDoesNotInvalidateTheDocument()
    {
        using var world = FoundationPersistenceTests.FlatWorld();
        var editor = Open(world); var document = Field<EditorDocument>(editor, "document");
        var snapshot = document.Capture(); long version = document.Version;
        for (int i = 0; i < 3; i++) {
            Assert.Same(document.World, Invoke(editor, "Scenario", world));
            Assert.Equal(document.Rules.Select(rule => rule.Id), ((IReadOnlyList<RuleView>)Invoke(Field<RuleBookView>(Field<RuleWorkspaceView>(editor, "ruleWorkspace"), "book"), "RuleBookRules", world)!).Select(rule => rule.Id));
            Assert.Equal(document.Fields.Select(field => field.Name), ((IReadOnlyList<RuleFieldView>)Invoke(Field<RuleBookView>(Field<RuleWorkspaceView>(editor, "ruleWorkspace"), "book"), "RuleBookFields", world)!).Select(field => field.Name));
            Assert.Equal(snapshot.ToJson(), editor.CollectProject().ToJson());
        }
        Assert.Equal(version, document.Version);
        snapshot.Rules.Clear(); snapshot.World!.Name = "Foreign";
        Assert.NotEmpty(document.Rules); Assert.NotEqual("Foreign", document.World!.Name);
    }

    [Fact]
    public void StandaloneCatalogImportsTuningAndProjectsSubsequentEdits()
    {
        var catalog = CurrentGameRules.Build(tuning: GameTuning.FromOverrides(new Dictionary<string, double> { ["DieselPrice"] = 1.20 }));
        var view = new CurrentRulesView(); view.Load(catalog);
        Assert.Equal(1.20, view.TuningOverrides(null)["DieselPrice"]);
        view.SetTuning(new Dictionary<string, double> { ["DieselPrice"] = 1.21 });
        Assert.Equal(1.21, Parameter(view, "DieselPrice"));
        Assert.Equal(1.21, view.TuningOverrides(null)["DieselPrice"]);
    }

    [Fact]
    public void LegacyCatalogOpeningKeepsImportedTuningWhenTheCompleteDocumentIsInitialized()
    {
        string file = Path.Combine(Path.GetTempPath(), "editor-catalog-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            var catalog = CurrentGameRules.Build(tuning: GameTuning.FromOverrides(new Dictionary<string, double> { ["DieselPrice"] = 1.21 }));
            File.WriteAllText(file, catalog.ToJson());
            using var world = FoundationPersistenceTests.FlatWorld();
            var editor = new RuleEditorView(file); Invoke(editor, "EnsureProject", world);
            Assert.Equal(1.21, Price(editor)); Assert.NotNull(editor.CollectProject().World);
            Assert.NotEmpty(editor.CollectProject().Rules); Assert.NotEmpty(editor.CollectProject().Behaviors.Graphs);
            Assert.Equal(1.21, Parameter(Field<CurrentRulesView>(editor, "currentRules"), "DieselPrice"));
            Assert.Equal(GameTuning.Default[Tune.DieselPrice], world.Tuning[Tune.DieselPrice]);
        } finally { File.Delete(file); }
    }

    [Fact]
    public void LoadingARulesOnlyProjectCapturesTheFallbackWorldOnceAndKeepsItsOwnTuning()
    {
        using var world = FoundationPersistenceTests.FlatWorld();
        var editor = Open(world); var project = GameWorld.DefaultRuleProject();
        project.World = null; project.Tuning["DieselPrice"] = 1.22;
        Invoke(editor, "LoadProject", world, project);
        var document = Field<EditorDocument>(editor, "document");
        Assert.NotNull(document.World); Assert.Equal(1.22, Price(editor));
        var captured = editor.CollectProject(); captured.World!.Name = "Foreign";
        Assert.NotEqual("Foreign", editor.CollectProject().World!.Name);
        Assert.Equal(1.22, Parameter(Field<CurrentRulesView>(editor, "currentRules"), "DieselPrice"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallingBackToTheRuntimeDocumentKeepsTheOriginalOpenError(bool unsupportedVersion)
    {
        string file = Path.Combine(Path.GetTempPath(), "editor-invalid-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            if (unsupportedVersion) {
                var project = GameWorld.DefaultRuleProject(); project.Version = RuleProject.CurrentVersion + 100;
                File.WriteAllText(file, project.ToJson());
            } else File.WriteAllText(file, "invalid json");
            using var world = FoundationPersistenceTests.FlatWorld();
            var editor = new RuleEditorView(file); Invoke(editor, "EnsureProject", world);
            Assert.False(string.IsNullOrWhiteSpace(editor.ProjectLoadError));
            Assert.NotNull(editor.CollectProject().World); Assert.NotEmpty(editor.CollectProject().Rules);
        } finally { File.Delete(file); }
    }

    private static RuleEditorView Open(GameWorld world)
    { var editor = new RuleEditorView(); Invoke(editor, "EnsureProject", world); return editor; }
    private static double Price(RuleEditorView editor) => GameTuning.FromOverrides(editor.CollectProject().Tuning)[Tune.DieselPrice];
    private static double Parameter(CurrentRulesView view, string key) => view.Catalog.Rules.SelectMany(r => r.Parameters).First(p => p.Key == key).Value;
    private static string SceneKey(RuleProject project) => (string)typeof(RuleEditorView)
        .GetMethod("SceneKey", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { project })!;
    private static object? Invoke(object target, string name, params object[] args) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static BehaviorGraph Constant(double value) => new() { Id = "formula", Hook = "formula", Kind = "world", Output = "value",
        Nodes = new() { new() { Id = "value", Operation = BehaviorOperation.Constant, Value = value } } };
}
