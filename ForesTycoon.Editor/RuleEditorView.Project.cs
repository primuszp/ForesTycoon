using System;
using System.IO;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    private RuleProject pendingProject;
    private System.Collections.Generic.Dictionary<string, double> pendingTuning;
    private bool projectInitialized, useLiveObjects = true;
    private string projectPath { get => workflow.ProjectPath; set => workflow.ProjectPath = value; }
    private string worldPath = SaveGamePath.Default;
    private RuleWorldCatalog liveObjects;
    private string liveStatus = "";
    private DateTime nextObjectRead;
    private readonly DynamicsEditorView dynamicsEditor;
    private readonly ProjectWorkflow workflow;
    private bool publishing => workflow.IsPublishing;
    private bool savingScene => workflow.IsSavingScene;
    private static IProjectWorkflowRuntime Runtime(GameWorld world) => new SimulationProjectWorkflowRuntime(world);

    private void EnsureProject(GameWorld world)
    {
        if (!projectInitialized) {
            projectInitialized = true;
            if (pendingProject != null) {
                try { LoadProject(world, pendingProject); projectLoadError = null; }
                catch (Exception e) when (Expected(e)) { pendingProject = null; projectLoadError = e.Message; }
            }
            if (pendingProject == null) {
                var initial = world.RuleProjectDocument;
                if (document.Behaviors.Draft.Graphs.Count > 0 || document.Behaviors.Draft.Controllers.Count > 0)
                    initial.Behaviors = document.Behaviors.Capture();
                if (pendingTuning != null) initial.Tuning = pendingTuning;
                string loadError = projectLoadError;
                LoadProject(world, initial);
                projectLoadError = loadError;
            }
            behaviors.ApplyProject = () => ApplyProject(world, false);
            behaviors.StoreGraphTemplate = graph => { ruleWorkspace.StoreTemplate(graph); message = "A gráf másolata a szabálytárban van; új kötés még nem jött létre."; };
            behaviors.StoreControllerTemplate = controller => { ruleWorkspace.StoreTemplate(controller); message = "A munkaciklus másolata a szabálytárban van."; };
            currentRules.RuleTarget = rule => {
                var rules = document.Rules;
                int index = rules.FindIndex(r => r.Id == rule.Id || r.Template == rule.Id);
                if (index >= 0) return () => ShowRuleBook(index);
                var graph = behaviors.Document.Graphs.FirstOrDefault(g => !g.Hook.StartsWith("tuning.", StringComparison.Ordinal)
                    && WorldBehaviorPolicy.Hooks.Any(h => h.Id == g.Hook && h.Kind == g.Kind && RuleWorkshopModel.HookBelongsTo(h, rule)));
                return graph == null ? null : () => ShowCommonRule("behavior:" + graph.Id);
            };
        }
        behaviors.Configure(WorldBehaviorPolicy.Hooks, dynamicsEditor.NamedValues(), key => PreviewValue(world, key));
        behaviors.ObjectSource = kind => useLiveObjects
            ? liveObjects?.Objects.Where(o => o.Kind == kind).Select(o => (o.Id, o.Label)) ?? Enumerable.Empty<(ulong Id, string Label)>() : world.BehaviorTargets(kind);
        if (DateTime.UtcNow >= nextObjectRead) {
            nextObjectRead = DateTime.UtcNow.AddSeconds(1);
            try {
                if (File.Exists(projectPath + ".world.json")) {
                    var catalog = RuleWorldCatalog.FromJson(File.ReadAllText(projectPath + ".world.json"));
                    if (catalog?.Schema != "forest-rule-world" || catalog.Version != 1 || catalog.Objects == null)
                        throw new InvalidDataException("Érvénytelen játékobjektum-katalógus.");
                    liveObjects = catalog; liveStatus = $"{catalog.Source}: {catalog.Objects.Count} objektum · tick {catalog.Tick}";
                    if (DateTime.TryParse(catalog.PublishedUtc, out var published) && DateTime.UtcNow - published.ToUniversalTime() > TimeSpan.FromSeconds(15)) liveStatus += " · korábbi pillanatkép; frissítés kérhető";
                    if (catalog.Error != null) liveStatus += " · " + catalog.Error;
                } else { liveObjects = null; liveStatus = "Nincs élő játék-katalógus. A tesztvilág forrása külön választható."; }
            } catch (Exception e) { liveObjects = null; liveStatus = e.Message; }
        }
    }
    private void LoadProject(GameWorld world, RuleProject project)
    {
        workflow.EnsureIdle();
        project = EditorProjectLoader.Prepare(project);
        project.World ??= world.RuleProjectDocument.World ?? WorldLoader.CaptureScenario(world);
        // Legacy maps without species use the built-in catalog, resolved once when the document opens.
        if (project.World.Species.Count == 0) project.World.Species = GameWorld.DefaultScenario().Species;
        MarkDocumentChanged();
        trials.Invalidate();
        projectLoadError = null;
        pendingProject = project;
        document.Load(project);
        workflow.ResetDocument(document.Revision());
        ruleWorkspace.Reset(project);
        scenarioBuilt = false; appliedScenario = null;
        if (document.World != null) seedEdit = document.World.Seed;
        dynamicsEditor.ResetSelection();
        behaviors.Configure(WorldBehaviorPolicy.Hooks, DynamicsRuntime.Keys(project.Dynamics, GameWorld.RuleWorldFields), key => PreviewValue(world, key));
        behaviors.ResetSelection();
        currentRules.Load(world.DescribeCurrentRules());
        RecordHistory(world);
    }
    internal RuleProject CollectProject() => document.Capture();
    private double PreviewValue(GameWorld world, string key)
    {
        if (useLiveObjects) {
            if (liveObjects == null) return double.NaN;
            if (key.StartsWith("stock.") && liveObjects.Stocks.TryGetValue(key[6..], out double stock)) return stock;
            if (liveObjects.Fields.TryGetValue(key, out double field)) return field;
            return double.NaN;
        }
        if (key.StartsWith("stock.")) {
            if (world.DynamicsValues.TryGetValue(key[6..], out double value)) return value;
            return dynamicsEditor.Stocks.Find(s => s.Id == key[6..])?.Initial ?? 0;
        }
        return world.ReadRuleValue(key);
    }
    private void ApplyProject(GameWorld world, bool publish)
    {
        MarkDocumentChanged();
        if (publish) CompleteEdits();
        ApplyWorkflowResult(publish ? workflow.Publish(CollectProject(), Runtime(world)) : workflow.ApplyPreview(CollectProject(), Runtime(world)));
    }
    private void RefreshProjectOperations(GameWorld world)
    {
        if (trials.HasSource) trials.ValidateSource(AuthoringRevision(world));
        trials.Poll();
        foreach (var operation in workflow.Poll()) ApplyWorkflowResult(operation);
    }
    private void ApplyWorkflowResult(ProjectOperation operation)
    {
        statusFrame = -1000;
        message = operation.Kind switch {
            ProjectOperationKind.PreviewApplied => "A teljes szabályprojekt alkalmazása naplózva a tesztvilágban.",
            ProjectOperationKind.DraftSaved => "Munkapéldány mentve. A játékban futó szabályokat nem módosította. " + operation.Path,
            ProjectOperationKind.SceneSaveStarted => "A jelenet ellenőrzése és mentése folyamatban…",
            ProjectOperationKind.SceneSaved => "Jelenet és szabályok mentve: " + operation.Path,
            ProjectOperationKind.SceneSaveFailed => "A jelenet nem menthető: " + operation.Error?.Message,
            ProjectOperationKind.PublishStarted => "Az algoritmusgráfok ellenőrzése és fordítása folyamatban. Az érvényes projekt fordítás után kerül a játékhoz.",
            ProjectOperationKind.Published when operation.Error != null => "A projekt közzétéve. A jelenet mentése nem indítható: " + operation.Error.Message,
            ProjectOperationKind.Published when operation.PreviewError != null => "A projekt közzétéve. A tesztvilág alkalmazása nem sikerült: " + operation.PreviewError.Message,
            ProjectOperationKind.Published => "A projekt közzétéve; a futó játék átveszi. " + (operation.SceneSaveStarted ? "A jelenet mentése folyamatban." : ""),
            ProjectOperationKind.PublishCancelled => "A fordítás / közzététel megszakítva. A munkapéldány megmaradt.",
            ProjectOperationKind.PublishFailed => operation.Error?.Message ?? "A közzététel nem sikerült.",
            ProjectOperationKind.SceneOpened => "Jelenet megnyitva: " + operation.Path,
            _ => message
        };
    }
    internal void StopBackgroundOperations(GameWorld world)
    {
        CompleteEdits();
        foreach (var operation in workflow.Stop())
            if (operation.Error != null) Console.Error.WriteLine(operation.Error.Message);
        trials.Dispose();
        treeEditor.Dispose();
        if (world == null || !projectInitialized) return;
        document.Invalidate();
        RefreshWorkshopRevision(world);
        if (!workshopVersions.Unsaved) return;
        try { workflow.SaveRecovery(CollectProject()); }
        catch (Exception e) { Console.Error.WriteLine("A helyreállítási munkapéldány mentése nem sikerült: " + e.Message); }
    }
    internal void ShowDynamics() {
        var flow = dynamicsEditor.Flows.FirstOrDefault(); var stock = dynamicsEditor.Stocks.FirstOrDefault();
        ShowCommonRule(flow != null ? "flow:" + flow.Id : stock != null ? "stock:" + stock.Id : "library");
    }
    internal void UseWorldFile(string file) { worldPath = Path.GetFullPath(file); useLiveObjects = false; }
}
