using System;
using System.IO;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using System.Threading.Tasks;
using ForesTycoon.RuleCompiler;
using System.Text.Json;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    private RuleProject pendingProject;
    private bool projectInitialized, useLiveObjects = true;
    private string projectPath = RuleProject.DefaultPath, projectName = "ForesTycoon szabályrendszer", worldPath = SaveGamePath.Default;
    private RuleWorldCatalog liveObjects;
    private string liveStatus = "";
    private DateTime nextObjectRead;
    private readonly DynamicsEditorView dynamicsEditor = new();
    // Compiled engine algorithms are not edited here; a project's reference to them is kept as it was loaded.
    private AlgorithmGraphReference algorithmReference;
    private Task<AlgorithmBuildResult> algorithmBuild;
    private RuleProject publishingProject;
    private string publishingPath;
    private Task<RuleTrialResult> graphSimulation;
    private RuleTrialResult trialResult;
    private System.Threading.CancellationTokenSource trialCancellation;
    private System.Threading.CancellationTokenSource publishingCancellation;
    private string publishingAuthoringRevision;
    private RuleAuthoring authoring = new();
    private readonly ProjectFileGuard publishedGuard = new(), draftGuard = new();
    private string guardedPath;

    private void EnsureProject(GameWorld world)
    {
        if (!projectInitialized) {
            projectInitialized = true;
            if (pendingProject != null) {
                try { LoadProject(world, pendingProject); projectLoadError = null; }
                catch (Exception e) when (Expected(e)) { pendingProject = null; projectLoadError = e.Message; }
            }
            if (pendingProject == null) {
                projectName = world.RuleProjectDocument.Name;
                if (behaviors.Document.Graphs.Count == 0 && behaviors.Document.Controllers.Count == 0) behaviors.LoadDraft(world.BehaviorDocument);
                dynamicsEditor.Load(world.RuleProjectDocument.Dynamics);
            }
            behaviors.ApplyProject = () => ApplyProject(world, false);
            behaviors.StoreGraphTemplate = graph => { RememberAuthoring(); authoring.Templates.Add(AuthoringOperations.Template(graph)); message = "A gráf másolata a szabálytárban van; új kötés még nem jött létre."; };
            behaviors.StoreControllerTemplate = controller => { RememberAuthoring(); authoring.Templates.Add(AuthoringOperations.Template(controller)); message = "A munkaciklus másolata a szabálytárban van."; };
            currentRules.RuleTarget = rule => {
                var rules = RuleBookRules(world);
                int index = rules.FindIndex(r => r.Id == rule.Id || r.Template == rule.Id);
                if (index >= 0) return () => ShowRuleBook(index);
                var graph = behaviors.Document.Graphs.FirstOrDefault(g => !g.Hook.StartsWith("tuning.", StringComparison.Ordinal)
                    && WorldBehaviorPolicy.Hooks.Any(h => h.Id == g.Hook && h.Kind == g.Kind && RuleWorkshopModel.HookBelongsTo(h, rule)));
                return graph == null ? null : () => ShowCommonRule("behavior:" + graph.Id);
            };
        }
        if (guardedPath != Path.GetFullPath(projectPath)) { guardedPath = Path.GetFullPath(projectPath); publishedGuard.Observe(projectPath); draftGuard.Observe(RuleWorkshopModel.DraftPath(projectPath)); }
        var dynamics = dynamicsEditor.Draft;
        behaviors.Configure(WorldBehaviorPolicy.Hooks, DynamicsRuntime.Keys(dynamics, GameWorld.RuleWorldFields), key => PreviewValue(world, key));
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
                    if (DateTime.TryParse(catalog.PublishedUtc, out var published) && DateTime.UtcNow - published.ToUniversalTime() > TimeSpan.FromSeconds(15)) liveStatus += " · a játék kapcsolata szünetel";
                    if (catalog.Error != null) liveStatus += " · " + catalog.Error;
                } else { liveObjects = null; liveStatus = "Nincs élő játék-katalógus. A tesztvilág forrása külön választható."; }
            } catch (Exception e) { liveObjects = null; liveStatus = e.Message; }
        }
    }
    private void LoadProject(GameWorld world, RuleProject project)
    {
        project = EditorProjectLoader.Prepare(project);
        projectLoadError = null;
        var tuning = GameTuning.FromOverrides(project.Tuning);
        pendingProject = project.Clone(); projectName = project.Name;
        workshopVersions.Reset(); trialResult = null; publishedProjectRevision = null;
        authoring = project.Clone().Authoring ?? new();
        LoadRuleBook(project);
        scenario = project.World == null ? null : RuleProject.FromJson(new RuleProject { World = project.World }.ToJson()).World;
        scenarioBuilt = false; appliedScenario = null;
        if (scenario != null) seedEdit = scenario.Seed;
        authoringUndo.Clear(); authoringRedo.Clear();
        publishedGuard.Observe(projectPath); draftGuard.Observe(RuleWorkshopModel.DraftPath(projectPath)); guardedPath = Path.GetFullPath(projectPath);
        algorithmReference = project.Algorithms;
        dynamicsEditor.Load(project.Dynamics);
        behaviors.Configure(WorldBehaviorPolicy.Hooks, DynamicsRuntime.Keys(project.Dynamics, GameWorld.RuleWorldFields), key => PreviewValue(world, key));
        behaviors.LoadDraft(project.Behaviors);
        var catalog = world.DescribeCurrentRules();
        foreach (var rule in catalog.Rules)
            for (int i = 0; i < rule.Parameters.Count; i++)
                if (rule.Parameters[i].Tunable) rule.Parameters[i] = rule.Parameters[i] with { Value = tuning[GameTuning.Spec(rule.Parameters[i].Key).Key] };
        currentRules.Load(catalog);
    }
    internal RuleProject CollectProject(GameWorld world) => new() { Name = projectName,
        Tuning = currentRules.TuningOverrides(world), Behaviors = behaviors.Document, Dynamics = dynamicsEditor.Document,
        Algorithms = algorithmReference, Rules = RuleEngine.Clone(RuleBookRules(world)), RemovedStarters = RemovedStarters(world).ToList(), Fields = RuleEngine.CloneFields(RuleBookFields(world)),
        Authoring = JsonSerializer.Deserialize<RuleAuthoring>(JsonSerializer.Serialize(authoring)),
        World = scenario == null ? world.RuleProjectDocument.World : RuleProject.FromJson(new RuleProject { World = scenario }.ToJson()).World };
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
            return dynamicsEditor.Draft.Stocks.Find(s => s.Id == key[6..])?.Initial ?? 0;
        }
        return world.ReadRuleValue(key);
    }
    private void ApplyProject(GameWorld world, bool publish)
    {
        if (publish) publishedGuard.RequireUnchanged(projectPath);
        var project = CollectProject(world); project.Algorithms = null;
        project = GameWorld.NormalizeRuleProject(project);
        if (project.Algorithms != null && project.Algorithms.Revision != AlgorithmBuild.CurrentRevision) {
            if (!publish) throw new InvalidOperationException("Az algoritmusgráfokat a Mentés és futtatás fordítja. A beépített tesztvilág az editor saját algoritmusverzióját futtatja; a teljes gráfprojektet a CLI szimuláció ellenőrzi.");
            if (algorithmBuild != null) throw new InvalidOperationException("A fordítás már folyamatban van.");
            publishingProject = project; publishingPath = Path.GetFullPath(projectPath);
            publishingAuthoringRevision = AuthoringRevision(world);
            publishingCancellation = new(); var cancellation = publishingCancellation.Token;
            algorithmBuild = Task.Run(() => AlgorithmBuild.CompileAsync(project.Algorithms, cancellation));
            message = "Az algoritmusgráfok ellenőrzése és fordítása folyamatban. Az érvényes projekt fordítás után kerül a játékhoz.";
            return;
        }
        if (publish) { ProjectHistory.Archive(projectPath); RuleProject.WriteAtomic(projectPath, project.ToJson()); publishedGuard.Observe(projectPath); }
        world.QueueRuleProject(project);
        if (publish) { workshopVersions.MarkPublished(AuthoringRevision(world)); publishedProjectRevision = project.ContentRevision(); }
        message = publish ? "Projekt mentve; a futó játék automatikusan átveszi. A tesztvilágban is alkalmazva." : "A teljes szabályprojekt alkalmazása naplózva a tesztvilágban.";
    }
    private void RefreshProjectOperations()
    {
        if (graphSimulation?.IsCompleted == true) {
            try {
                trialResult = graphSimulation.GetAwaiter().GetResult();
                workshopVersions.CompleteTrial(trialResult.DraftRevision);
                message = "Összehasonlító próba kész. Az eredmények a Próba lépésben láthatók.";
            } catch (OperationCanceledException) { message = "A próba megszakítva; a munkapéldány megmaradt."; }
            catch (Exception e) { message = e.Message; }
            graphSimulation = null; trialCancellation?.Dispose(); trialCancellation = null;
        }
        if (algorithmBuild?.IsCompleted == true) {
            try {
                var runtime = algorithmBuild.GetAwaiter().GetResult();
                publishingCancellation?.Token.ThrowIfCancellationRequested();
                publishedGuard.RequireUnchanged(publishingPath);
                ProjectHistory.Archive(publishingPath);
                RuleProject.WriteAtomic(publishingPath, publishingProject.ToJson());
                publishedGuard.Observe(publishingPath);
                workshopVersions.MarkPublished(publishingAuthoringRevision);
                publishedProjectRevision = publishingProject.ContentRevision();
                message = "Az algoritmusgráfok lefordultak és a projekt megjelent a játék számára. A játék ment és átáll az új algoritmusokra. " + runtime.Revision[..12];
            } catch (OperationCanceledException) { message = "A fordítás / közzététel megszakítva. A munkapéldány megmaradt, a játék projektje nem változott."; }
            catch (Exception e) { message = e.Message; }
            algorithmBuild = null; publishingProject = null; publishingCancellation?.Dispose(); publishingCancellation = null;
        }
    }
    internal void StopBackgroundOperations(GameWorld world)
    {
        trialCancellation?.Cancel();
        publishingCancellation?.Cancel();
        treeRenderer?.Dispose(); treeRenderer = null;
        if (world == null || !projectInitialized || !workshopVersions.Unsaved) return;
        try { WorkshopDraftStore.Save(RecoveryStem(), CollectProject(world)); }
        catch (Exception e) { Console.Error.WriteLine("A helyreállítási munkapéldány mentése nem sikerült: " + e.Message); }
    }
    internal void ShowDynamics() {
        var flow = dynamicsEditor.Draft.Flows.FirstOrDefault(); var stock = dynamicsEditor.Draft.Stocks.FirstOrDefault();
        ShowCommonRule(flow != null ? "flow:" + flow.Id : stock != null ? "stock:" + stock.Id : "library");
    }
    internal void UseWorldFile(string file) { worldPath = Path.GetFullPath(file); useLiveObjects = false; }
}
