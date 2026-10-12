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
    private readonly AlgorithmEditorView algorithms = new();
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
    private int graphTestTicks = 300;
    private bool graphTestSavedWorld;

    private void EnsureProject(GameWorld world)
    {
        if (!projectInitialized) {
            projectInitialized = true;
            if (pendingProject != null) LoadProject(world, pendingProject);
            else {
                projectName = world.RuleProjectDocument.Name;
                if (behaviors.Document.Graphs.Count == 0 && behaviors.Document.Controllers.Count == 0) behaviors.LoadDraft(world.BehaviorDocument);
                dynamicsEditor.Load(world.RuleProjectDocument.Dynamics);
            }
            behaviors.ApplyProject = () => ApplyProject(world, false);
            behaviors.StoreGraphTemplate = graph => { RememberAuthoring(); authoring.Templates.Add(AuthoringOperations.Template(graph)); message = "A gráf másolata a szabálytárban van; új kötés még nem jött létre."; };
            behaviors.StoreControllerTemplate = controller => { RememberAuthoring(); authoring.Templates.Add(AuthoringOperations.Template(controller)); message = "A munkaciklus másolata a szabálytárban van."; };
            currentRules.EditAlgorithm = source => { algorithms.SelectSource(source); view = View.Algorithms; workshopPhase = WorkshopPhase.Edit; workshopTool = WorkshopTool.Algorithms; };
            if (pendingProject == null && AlgorithmBuild.FindWorkspace() is string workspace) algorithms.ImportWorkspace(workspace);
        }
        algorithms.Refresh();
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
        ValidateDraft(project);
        var tuning = GameTuning.FromOverrides(project.Tuning);
        pendingProject = project.Clone(); projectName = project.Name; draft = project.Road.Clone(); selected = draft.Output;
        projectPathEdit = projectPath;
        workshopVersions.Reset(); bindingSelectedFor = null; trialResult = null; publishedProjectRevision = null;
        authoring = project.Clone().Authoring ?? new();
        scenario = project.World == null ? null : RuleProject.FromJson(new RuleProject { World = project.World }.ToJson()).World;
        scenarioBuilt = false; appliedScenario = null;
        if (scenario != null) seedEdit = scenario.Seed;
        authoringUndo.Clear(); authoringRedo.Clear();
        publishedGuard.Observe(projectPath); draftGuard.Observe(RuleWorkshopModel.DraftPath(projectPath)); guardedPath = Path.GetFullPath(projectPath);
        algorithms.Load(project.Algorithms);
        dynamicsEditor.Load(project.Dynamics);
        behaviors.Configure(WorldBehaviorPolicy.Hooks, DynamicsRuntime.Keys(project.Dynamics, GameWorld.RuleWorldFields), key => PreviewValue(world, key));
        behaviors.LoadDraft(project.Behaviors);
        var catalog = world.DescribeCurrentRules();
        foreach (var rule in catalog.Rules)
            for (int i = 0; i < rule.Parameters.Count; i++)
                if (rule.Parameters[i].Tunable) rule.Parameters[i] = rule.Parameters[i] with { Value = tuning[GameTuning.Spec(rule.Parameters[i].Key).Key] };
        currentRules.Load(catalog); currentRules.SetRoadTrafficModel(world, draft); fitRequested = true;
    }
    private static void ValidateDraft(RuleProject project)
    {
        if (project == null || project.Schema != "forest-rule-project" || project.Version != 1 || project.Name == null
            || project.Tuning == null || project.Road?.Nodes == null || project.Road.Nodes.Count > 128
            || project.Behaviors?.Graphs == null || project.Behaviors.Graphs.Count > 512 || project.Behaviors.Controllers == null
            || project.Dynamics?.Stocks == null || project.Dynamics.Stocks.Count > 256 || project.Dynamics.Flows == null || project.Dynamics.Flows.Count > 512
            || project.Authoring != null && (project.Authoring.Entries == null || project.Authoring.Templates == null || project.Authoring.Entries.Count > 2048 || project.Authoring.Templates.Count > 1024
                || project.Authoring.Entries.Any(e => e.Value == null || e.Value.Notes == null) || project.Authoring.Templates.Any(t => t == null || t.Name == null || t.Id == null || t.Graph == null && t.Controller == null)))
            throw new InvalidDataException("Hiányos vagy túl nagy szerkesztési projekt.");
        if (project.Road.Nodes.Any(n => n == null || n.Id == null || n.Name == null || !Enum.IsDefined(n.Operation) || !float.IsFinite(n.X) || !float.IsFinite(n.Y)))
            throw new InvalidDataException("Hibás útkopási csomópont.");
        static bool InvalidNodes(BehaviorGraph graph) => graph.Nodes == null || graph.Nodes.Count > 256
            || graph.Nodes.Any(n => n == null || n.Id == null || n.Name == null || !Enum.IsDefined(n.Operation) || !float.IsFinite(n.X) || !float.IsFinite(n.Y));
        if (project.Behaviors.Graphs.Any(g => g == null || g.Id == null || g.Name == null || InvalidNodes(g)
            || !WorldBehaviorPolicy.Hooks.Any(h => h.Id == g.Hook && h.Kind == g.Kind))
            || project.Dynamics.Stocks.Any(s => s == null || s.Id == null || s.Name == null || s.Unit == null)
            || project.Dynamics.Flows.Any(f => f == null || f.Id == null || f.Name == null || f.From == null || f.To == null
                || f.Rate == null || InvalidNodes(f.Rate) || f.Rate.Hook != "flow." + f.Id || f.Rate.Kind != "dynamics")
            || project.Behaviors.Controllers.Any(c => c == null || c.Name == null || c.States == null || c.Kind is not ("forwarder" or "processor" or "truck")
                || c.States.Any(s => s == null || s.Name == null || !Enum.IsDefined(s.Action) || s.Transitions == null || !float.IsFinite(s.X) || !float.IsFinite(s.Y))))
            throw new InvalidDataException("Hibás szerkesztési objektum vagy nem támogatott kötés.");
        // Missing ports, expression cycles and incomplete outputs can be reopened and repaired.
        // The runtime compiler still rejects them at publish/apply time.
    }
    internal RuleProject CollectProject(GameWorld world) => new() { Name = projectName,
        Road = (draft ?? currentRules.RoadTrafficModel ?? world.RuleDocument).Clone(),
        Tuning = currentRules.TuningOverrides(world), Behaviors = behaviors.Document, Dynamics = dynamicsEditor.Document,
        Algorithms = algorithms.Reference, Authoring = JsonSerializer.Deserialize<RuleAuthoring>(JsonSerializer.Serialize(authoring)),
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
        if (world == null || !projectInitialized || !workshopVersions.Unsaved) return;
        try { WorkshopDraftStore.Save(RecoveryStem(), CollectProject(world), algorithms.Document, algorithms.Workspace); }
        catch (Exception e) { Console.Error.WriteLine("A helyreállítási munkapéldány mentése nem sikerült: " + e.Message); }
    }
    private void SimulateGraphs(GameWorld world)
    {
        var project = CollectProject(world); project.Algorithms = algorithms.Store(projectPath);
        project = GameWorld.NormalizeRuleProject(project);
        string folder = Path.Combine(AlgorithmBuild.CacheRoot, "editor-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string save = Path.Combine(folder, "world.json");
        if (graphTestSavedWorld) File.Copy(Path.GetFullPath(worldPath), save);
        else {
            world.ExecutePendingCommands(); using var stream = new MemoryStream(); world.Save(stream);
            RuleProject.WriteAtomic(save, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        }
        int ticks = graphTestTicks;
        using var input = File.OpenRead(save); var data = WorldSaveSerializer.Read(input);
        var checkpoint = data.Checkpoint ?? throw new InvalidDataException("A próbához állapotpillanatképes mentés szükséges. Olvasd be a világot, majd mentsd újra.");
        if (checkpoint.PendingCommands != 0) throw new InvalidDataException("A mentésben függő parancsok vannak. Olvasd be a tesztvilágba és léptesd a parancsokat a próba előtt.");
        var baseline = new RuleProject { Name = checkpoint.RuleProjectName ?? "Mentés szabályai", Road = checkpoint.Rules ?? RuleModel.Default(),
            Tuning = checkpoint.Tuning ?? new(), Behaviors = checkpoint.Behaviors ?? WorldBehaviorPolicy.DefaultDocument(),
            Dynamics = checkpoint.Dynamics ?? new(), Algorithms = checkpoint.Algorithms };
        string revision = AuthoringRevision(world), source = graphTestSavedWorld ? Path.GetFullPath(worldPath) : $"Editor tesztvilága · tick {world.SimulationTick}";
        trialCancellation = new(); var cancellation = trialCancellation.Token;
        graphSimulation = Task.Run(() => RuleTrialRunner.Run(baseline, project, save, folder, revision, source, ticks, cancellation));
        message = "Azonos mentésből fut az alap- és a szerkesztett változat; a játék szabályai nem változnak.";
    }
    private string objectSearch = "";
    private int objectKind;
    private ulong? objectId;
    private string objectLabel;
    private void ObjectsView(GameWorld world)
    {
        if (useLiveObjects && liveObjects == null) {
            ImGui.TextWrapped("Nincs élő objektumkatalógus a játékból. Indítsd el a játékot, vagy válaszd külön az editor tesztvilágát.");
            if (ImGui.Button("Az editor tesztvilágának objektumait használom")) useLiveObjects = false;
            return;
        }
        var kinds = ObjectKinds();
        objectKind = Math.Clamp(objectKind, 0, kinds.Length - 1);
        if (ImGui.BeginCombo("Objektumtípus", RuleWorkshopModel.KindName(kinds[objectKind]))) {
            for (int i = 0; i < kinds.Length; i++) if (ImGui.Selectable(RuleWorkshopModel.KindName(kinds[i]), objectKind == i)) { objectKind = i; objectId = null; }
            ImGui.EndCombo();
        }
        ImGui.SameLine(); ImGui.SetNextItemWidth(350); ImGui.InputText("Keresés", ref objectSearch, 120);
        ImGui.TextWrapped("Válassz játékobjektumot, majd folyamatot. Az egyedi szabály a típus szabályának másolatából indul, és annak helyére lép ennél az objektumnál.");
        ImGui.BeginChild("objects-list", new System.Numerics.Vector2(420, 0), ImGuiChildFlags.Borders);
        var targets = behaviors.ObjectSource(kinds[objectKind]);
        foreach (var target in targets.Where(t => objectSearch.Length == 0 || (t.Label + " " + t.Id).Contains(objectSearch, StringComparison.OrdinalIgnoreCase)).Take(200))
            if (ImGui.Selectable(target.Label + "##" + target.Id, objectId == target.Id)) { objectId = target.Id; objectLabel = target.Label; }
        ImGui.TextWrapped("Legfeljebb 200 találat látható; az összes objektum kereshető név vagy azonosító szerint.");
        ImGui.EndChild(); ImGui.SameLine();
        ImGui.BeginChild("objects-rules", new System.Numerics.Vector2(0, 0), ImGuiChildFlags.Borders);
        if (objectId.HasValue) {
            ImGui.TextWrapped(objectLabel); ImGui.Separator(); var rules = behaviors.Document;
            string kind = kinds[objectKind];
            if (kind is "truck" or "forwarder" or "processor") {
                var control = rules.Controllers.Find(c => c.Kind == kind && c.ObjectId == objectId) ?? rules.Controllers.Find(c => c.Kind == kind && c.ObjectId == null);
                ImGui.TextWrapped("Munkaciklus: " + (control == null ? "Automatikus alapvezérlés" : control.Name + (control.ObjectId.HasValue ? " · egyedi kötés" : " · típusszabály")));
                if (ImGui.Button(control?.ObjectId.HasValue == true ? "Egyedi munkaciklus szerkesztése" : "Egyedi munkaciklus létrehozása")) {
                    behaviors.SelectObjectController(kind, objectId.Value); workshopTool = WorkshopTool.ObjectRule; workshopPhase = WorkshopPhase.Edit; objectRuleTitle = objectLabel + " munkaciklusa";
                }
                if (control?.ObjectId.HasValue == true && ImGui.Button("Vissza a típus munkaciklusához")) behaviors.RestoreController(kind, objectId);
                ImGui.Separator();
            }
            var source = kinds[objectKind] switch {
                "wildlife" => new RuleSource("ForesTycoon/World/WildlifeSystem.cs", "WildlifeSystem.Update"),
                "depot" => new RuleSource("ForesTycoon/World/Cargo/ForestryLogistics.Fleet.cs", "ForestryLogistics.UpdateTrucks"),
                "stack" => new RuleSource("ForesTycoon/World/Cargo/ForestryLogistics.Stacks.cs", "TimberStack.Take"), _ => null
            };
            if (source != null) {
                ImGui.TextWrapped("A teljes eljárás gráfja szerkeszti az objektum állapotát. Egyedi viselkedéshez az ID-ra tett feltételt használd az eljárás megfelelő blokkjában: " + objectId);
                if (ImGui.Button("Teljes algoritmusgráf")) { algorithms.SelectSource(source); view = View.Algorithms; workshopPhase = WorkshopPhase.Edit; workshopTool = WorkshopTool.Algorithms; }
                ImGui.Separator();
            }
            foreach (var hook in WorldBehaviorPolicy.Hooks.Where(h => h.Kind == kinds[objectKind])) {
                var effective = rules.Graphs.Find(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == objectId)
                    ?? rules.Graphs.Find(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null);
                ImGui.PushID(hook.Id); ImGui.TextWrapped(hook.Name + " · " + hook.Schedule);
                ImGui.TextWrapped(effective == null ? "Alapszabály" : effective.Name + (effective.ObjectId.HasValue ? " · egyedi kötés" : " · típusszintű kötés"));
                if (ImGui.Button(!hook.SupportsObjects ? "Típusszintű gráf szerkesztése" : effective?.ObjectId.HasValue == true ? "Egyedi gráf szerkesztése" : "Egyedi felülírás létrehozása és szerkesztése")) {
                    behaviors.SelectObjectHook(hook, objectId.Value); view = View.Behaviors; workshopPhase = WorkshopPhase.Edit;
                    workshopTool = WorkshopTool.ObjectRule; objectRuleTitle = hook.Name; objectRuleScope = objectLabel;
                }
                if (effective?.ObjectId.HasValue == true && ImGui.Button("Vissza a típusszabályhoz")) behaviors.RestoreDefault(hook.Id, hook.Kind, objectId);
                ImGui.Separator(); ImGui.PopID();
            }
        } else ImGui.TextWrapped("Az objektumlista a futó játékból vagy a betöltött játékmentésből származik. A típusszintű szabályok az újonnan létrejövő objektumokra is érvényesek.");
        ImGui.EndChild();
    }
    private string[] ObjectKinds() => (useLiveObjects && liveObjects != null ? liveObjects.Objects.Select(o => o.Kind) : GameWorld.RuleObjectKinds).Concat(GameWorld.RuleObjectKinds).Distinct().ToArray();
    internal void ShowDynamics() { thematicDynamics = false; view = View.Dynamics; workshopTool = WorkshopTool.Dynamics; workshopPhase = WorkshopPhase.Edit; }
    internal void ShowObjects() { view = View.Objects; workshopTool = WorkshopTool.Objects; workshopPhase = WorkshopPhase.Understand; }
    internal void ShowAlgorithms() { view = View.Algorithms; workshopTool = WorkshopTool.Algorithms; workshopPhase = WorkshopPhase.Edit; }
    internal void UseWorldFile(string file) { worldPath = Path.GetFullPath(file); useLiveObjects = false; graphTestSavedWorld = true; }
}
