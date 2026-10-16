using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ForesTycoon.Rules;
using ImGuiNET;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    private bool thematicDynamics, templateSpecific;
    private bool requestCreatePopup, dismissAuthoringPopup;
    private string thematicItem, librarySearch = "", newRuleName = "Saját szabály", templateTarget = "", templateId,
        transferPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForesTycoon", "rules", "sablonok.json");
    private int newHook;
    private readonly Stack<string> authoringUndo = new(), authoringRedo = new();
    private int TopicOf(string id, int fallback) => authoring.Entries.TryGetValue(id, out var entry) ? Math.Clamp(entry.Topic, 0, 6) : fallback;
    private int FlowTopic(DynamicsFlow flow) => TopicOf("flow." + flow.Id, TopicOf("stock." + (flow.From.Length > 0 ? flow.From : flow.To), 5));
    private string RecoveryStem() => projectPath + ".recovery-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
    private void SwitchProject(GameWorld world, RuleProject project)
    {
        if (workshopVersions.Unsaved) {
            // An externally changed shared draft is kept intact; preserve this editor's work separately.
            if (draftGuard.Changed(RuleWorkshopModel.DraftPath(projectPath))) WorkshopDraftStore.Save(RecoveryStem(), CollectProject(world), algorithms.Document, algorithms.Workspace);
            else SaveProjectDraft(world);
        }
        LoadProject(world, project); workshopTool = WorkshopTool.Rules; workshopPhase = WorkshopPhase.Understand;
    }
    private void RememberAuthoring() { authoringUndo.Push(JsonSerializer.Serialize(authoring)); authoringRedo.Clear(); }
    private void AuthoringHistory(Stack<string> from, Stack<string> to) { if (from.Count == 0) return; to.Push(JsonSerializer.Serialize(authoring)); authoring = JsonSerializer.Deserialize<RuleAuthoring>(from.Pop()); }
    private void SetTopic(string id, int topic) { RememberAuthoring(); if (!authoring.Entries.TryGetValue(id, out var entry)) authoring.Entries[id] = entry = new(); entry.Topic = topic; }
    private void DrawPresentation(string id, int fallback)
    {
        if (!ImGui.CollapsingHeader("Saját jegyzet és témabesorolás##" + id)) return;
        int topic = TopicOf(id, fallback);
        ImGui.SetNextItemWidth(330);
        if (ImGui.Combo("Téma##" + id, ref topic, RuleWorkshopModel.Topics.Select(t => t.Name).ToArray(), 7)) { SetTopic(id, topic); workshopTopic = topic; }
        string notes = authoring.Entries.TryGetValue(id, out var entry) ? entry.Notes : "";
        if (ImGui.InputTextMultiline("Jegyzet##" + id, ref notes, 4000, new(-1, 80))) { RememberAuthoring(); if (entry == null) authoring.Entries[id] = entry = new() { Topic = topic }; entry.Notes = notes; }
        ImGui.TextWrapped("A besorolás és jegyzet a közös projekt része; a játék számítását nem változtatja meg.");
    }
    private bool MatchesItem(string name, string id) => workshopSearch.Length == 0 || (name + " " + id).Contains(workshopSearch, StringComparison.OrdinalIgnoreCase);
    private void DrawThematicItems(GameWorld world)
    {
        foreach (var stock in dynamicsEditor.Draft.Stocks.Where(s => (workshopSearch.Length > 0 || TopicOf("stock." + s.Id, 5) == workshopTopic) && MatchesItem(s.Name, s.Id)).ToArray())
            if (ImGui.Selectable("Készlet · " + stock.Name + "##stock" + stock.Id, workshopTool == WorkshopTool.Dynamics && thematicItem == "stock." + stock.Id)) OpenDynamic(stock.Id, false);
        foreach (var flow in dynamicsEditor.Draft.Flows.Where(f => (workshopSearch.Length > 0 || FlowTopic(f) == workshopTopic) && MatchesItem(f.Name, f.Id)).ToArray())
            if (ImGui.Selectable("Áramlás · " + flow.Name + "##flow" + flow.Id, workshopTool == WorkshopTool.Dynamics && thematicItem == "flow." + flow.Id)) OpenDynamic(flow.Id, true);
        foreach (var graph in behaviors.Document.Graphs.Where(g => authoring.Entries.ContainsKey("graph." + g.Id) && (workshopSearch.Length > 0 || TopicOf("graph." + g.Id, 5) == workshopTopic) && MatchesItem(g.Name, g.Id)).ToArray())
            if (ImGui.Selectable(graph.Name + "##custom" + graph.Id)) OpenLibraryGraph(graph);
        if (workshopSearch.Length > 0) {
            var found = GameWorld.RuleObjectKinds.SelectMany(kind => (behaviors.ObjectSource?.Invoke(kind) ?? Enumerable.Empty<(ulong Id, string Label)>()).Select(t => (Kind: kind, t.Id, t.Label)))
                .Where(t => MatchesItem(t.Label, t.Id.ToString())).Take(20).ToArray();
            if (found.Length > 0) EditorStyle.Section("Objektumtalálatok");
            foreach (var target in found) if (ImGui.Selectable(target.Label + "##search-object" + target.Kind + target.Id)) {
                var kinds = ObjectKinds(); objectKind = Array.IndexOf(kinds, target.Kind); objectId = target.Id; objectLabel = target.Label; objectSearch = ""; ShowObjects();
            }
        }
    }
    private void OpenDynamic(string id, bool flow)
    {
        thematicItem = (flow ? "flow." : "stock.") + id; thematicDynamics = true; workshopTopic = flow ? FlowTopic(dynamicsEditor.Draft.Flows.Single(f => f.Id == id)) : TopicOf(thematicItem, 5);
        dynamicsEditor.SelectItem(id, flow); workshopTool = WorkshopTool.Dynamics; workshopPhase = WorkshopPhase.Edit;
    }
    private void DrawThematicDynamics(GameWorld world)
    {
        if (thematicDynamics && thematicItem != null) DrawPresentation(thematicItem, workshopTopic);
        if (thematicDynamics && thematicItem?.StartsWith("stock.") == true) DrawStockBinding();
        var document = CollectProject(world);
        dynamicsEditor.Draw(world, key => PreviewValue(world, key), stock => AuthoringOperations.StockConsumers(document, stock),
            thematicDynamics ? id => TopicOf("stock." + id, 5) == workshopTopic : null,
            thematicDynamics ? id => FlowTopic(dynamicsEditor.Draft.Flows.Single(f => f.Id == id)) == workshopTopic : null);
    }
    private void DrawStockBinding()
    {
        if (!ImGui.CollapsingHeader("Ezt a készletet játékszabályhoz kapcsolom")) return;
        var stock = dynamicsEditor.Draft.Stocks.Find(s => "stock." + s.Id == thematicItem); if (stock == null) return;
        ImGui.TextWrapped("A kiválasztott folyamat jelenlegi eredményét ezzel a készlettel szorzó képlet készül. A meglévő képletet helyettesíti. A készlet egységét és értéktartományát a kapcsolt folyamat szerint válaszd.");
        var hooks = WorldBehaviorPolicy.Hooks; newHook = Math.Clamp(newHook, 0, hooks.Length - 1); var hook = hooks[newHook];
        ImGui.SetNextItemWidth(420);
        if (ImGui.BeginCombo("Fogyasztó szabály", hook.Name + " · " + RuleWorkshopModel.KindName(hook.Kind))) {
            for (int i = 0; i < hooks.Length; i++) if (ImGui.Selectable(hooks[i].Name + " · " + RuleWorkshopModel.KindName(hooks[i].Kind) + "##stock-hook" + i)) newHook = i;
            ImGui.EndCombo();
        }
        if (ImGui.Button("Szorzóképlet létrehozása a típusszabályhoz")) {
            var graph = WorldBehaviorPolicy.DefaultGraph(hook); graph.Name = hook.Name + " · " + stock.Name;
            graph.Nodes.Add(new() { Id = "stock-input", Name = stock.Name, Operation = BehaviorOperation.NamedValue, Key = "stock." + stock.Id, X = 30, Y = 200 });
            graph.Nodes.Add(new() { Id = "stock-result", Name = "Készlettel szabályozott eredmény", Operation = BehaviorOperation.Multiply, A = graph.Output, B = "stock-input", X = 290, Y = 120 }); graph.Output = "stock-result";
            behaviors.UseTemplate(AuthoringOperations.Template(graph), null); var added = behaviors.Document.Graphs.Last(); SetTopic("graph." + added.Id, workshopTopic); OpenLibraryGraph(added);
        }
    }
    private void CreateAuthoringMenu(GameWorld world)
    {
        if (!ImGui.BeginPopup("workshop-create")) return;
        if (dismissAuthoringPopup) { ImGui.CloseCurrentPopup(); dismissAuthoringPopup = false; ImGui.EndPopup(); return; }
        EditorStyle.Section("Új elem ebben a témában");
        if (ImGui.Button("Készlet létrehozása")) { string id = dynamicsEditor.CreateStock(); SetTopic("stock." + id, workshopTopic); OpenDynamic(id, false); ImGui.CloseCurrentPopup(); }
        ImGui.BeginDisabled(dynamicsEditor.Draft.Stocks.Count == 0);
        if (ImGui.Button("Áramlás létrehozása")) { string id = dynamicsEditor.CreateFlow(); SetTopic("flow." + id, workshopTopic); OpenDynamic(id, true); ImGui.CloseCurrentPopup(); }
        ImGui.EndDisabled();
        ImGui.Separator(); ImGui.TextWrapped("Saját képlet a játék tényleges folyamatához. Az adott kötés jelenlegi képletét helyettesíti.");
        ImGui.SetNextItemWidth(420); ImGui.InputText("Név", ref newRuleName, 120);
        newHook = Math.Clamp(newHook, 0, WorldBehaviorPolicy.Hooks.Length - 1); var hook = WorldBehaviorPolicy.Hooks[newHook];
        ImGui.SetNextItemWidth(420);
        if (ImGui.BeginCombo("Játékfolyamat", hook.Name + " · " + RuleWorkshopModel.KindName(hook.Kind))) {
            for (int i = 0; i < WorldBehaviorPolicy.Hooks.Length; i++) if (ImGui.Selectable(WorldBehaviorPolicy.Hooks[i].Name + " · " + RuleWorkshopModel.KindName(WorldBehaviorPolicy.Hooks[i].Kind) + "##new" + i, newHook == i)) { newHook = i; templateSpecific = false; }
            ImGui.EndCombo();
        }
        ulong? target = TargetScope(hook.SupportsObjects);
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(newRuleName) || templateSpecific && target == null);
        if (ImGui.Button("Képlet létrehozása / kötés cseréje")) {
            var graph = WorldBehaviorPolicy.DefaultGraph(hook); graph.Name = newRuleName;
            behaviors.UseTemplate(AuthoringOperations.Template(graph), target); var added = behaviors.Document.Graphs.Last(); SetTopic("graph." + added.Id, workshopTopic); OpenLibraryGraph(added); ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled();
        ImGui.EndPopup();
    }
    private ulong? TargetScope(bool supportsObject)
    {
        if (!supportsObject) { templateSpecific = false; ImGui.TextWrapped("Hatókör: teljes világ / típusszabály."); return null; }
        ImGui.Checkbox("Egyedi objektumhoz kötöm", ref templateSpecific);
        if (!templateSpecific) { ImGui.TextWrapped("Hatókör: minden ilyen típusú objektum, az új objektumokra is."); return null; }
        ImGui.SetNextItemWidth(240); ImGui.InputText("Objektumazonosító", ref templateTarget, 24);
        if (ulong.TryParse(templateTarget, out ulong id)) return id;
        ImGui.TextWrapped("Válassz létező objektumot az objektumböngészőben, vagy írd be a saját világában érvényes ID-ját."); return null;
    }
    private void OpenLibraryGraph(BehaviorGraph graph)
    {
        behaviors.SelectGraph(graph.Id); ShowCommonRule("behavior:" + graph.Id);
    }
    private void DrawRuleLibrary(GameWorld world)
    {
        ImGui.TextWrapped("Aktív kötések és újrahasználható sablonok. A sablon másolásával nem keletkezik második, ütköző típusszabály.");
        ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##library-search", "Név, folyamat, típus vagy objektumazonosító", ref librarySearch, 160);
        bool Match(string value) => value.Contains(librarySearch, StringComparison.OrdinalIgnoreCase);
        if (ImGui.Button("Besorolás / sablon visszavonása")) AuthoringHistory(authoringUndo, authoringRedo);
        ImGui.SameLine(); if (ImGui.Button("Újra##library")) AuthoringHistory(authoringRedo, authoringUndo);
        if (ImGui.CollapsingHeader("Aktív képletek")) {
        foreach (var graph in behaviors.Document.Graphs.Where(g => Match(g.Name + " " + g.Hook + " " + g.Kind + " " + g.ObjectId)).ToArray()) {
            ImGui.PushID(graph.Id);
            if (ImGui.Selectable(graph.Name + " · " + RuleWorkshopModel.KindName(graph.Kind) + (graph.ObjectId.HasValue ? " #" + graph.ObjectId : " · típus"))) OpenLibraryGraph(graph);
            if (ImGui.SmallButton("Sablonként másolom")) { RememberAuthoring(); authoring.Templates.Add(AuthoringOperations.Template(graph)); }
            ImGui.SameLine(); if (ImGui.SmallButton(graph.ObjectId.HasValue ? "Egyedi felülírás eltávolítása" : "Beépített alap visszaállítása")) behaviors.RestoreDefault(graph.Hook, graph.Kind, graph.ObjectId);
            ImGui.PopID();
        }
        }
        if (ImGui.CollapsingHeader("Aktív munkaciklusok")) {
        foreach (var controller in behaviors.Document.Controllers.Where(c => Match(c.Name + " " + c.Kind + " " + c.ObjectId)).ToArray()) {
            ImGui.PushID(controller.Id);
            if (ImGui.Selectable(controller.Name + (controller.ObjectId.HasValue ? " #" + controller.ObjectId : " · típus"))) { behaviors.SelectControllerById(controller.Id); ShowCommonRule("controllerId:" + controller.Id); }
            if (ImGui.SmallButton("Sablonként másolom")) { RememberAuthoring(); authoring.Templates.Add(AuthoringOperations.Template(controller)); }
            ImGui.SameLine(); if (ImGui.SmallButton("Vissza az alapvezérléshez")) behaviors.RestoreController(controller.Kind, controller.ObjectId);
            ImGui.PopID();
        }
        }
        EditorStyle.Section("Újrahasználható sablonok");
        foreach (var template in authoring.Templates.Where(t => Match(t.Name)).ToArray()) {
            ImGui.PushID(template.Id);
            string name = template.Name; ImGui.SetNextItemWidth(260); if (ImGui.InputText("##template-name", ref name, 120)) { RememberAuthoring(); template.Name = name; }
            ImGui.SameLine(); if (ImGui.Button("Kötés létrehozása / cseréje")) { templateId = template.Id; templateSpecific = false; ImGui.OpenPopup("template-apply"); }
            ImGui.SameLine(); if (ImGui.Button("Sablon törlése")) { RememberAuthoring(); authoring.Templates.Remove(template); }
            if (ImGui.BeginPopup("template-apply")) {
                bool local = template.Controller != null || WorldBehaviorPolicy.Hooks.FirstOrDefault(h => h.Id == template.Graph?.Hook && h.Kind == template.Graph?.Kind)?.SupportsObjects == true;
                var target = TargetScope(local); ImGui.TextWrapped("A célkötés meglévő szabályát ezzel a független másolattal helyettesíted. A többi objektum egyedi kötése megmarad.");
                ImGui.BeginDisabled(templateSpecific && target == null);
                if (ImGui.Button("Másolat kötése")) { behaviors.UseTemplate(template, target); if (template.Graph != null) OpenLibraryGraph(behaviors.Document.Graphs.Last()); else ShowCommonRule("controllerId:" + behaviors.Document.Controllers.Last().Id); ImGui.CloseCurrentPopup(); }
                ImGui.EndDisabled(); ImGui.EndPopup();
            }
            ImGui.PopID();
        }
        EditorStyle.Section("Import / export a felhasználó és az AI számára");
        ImGui.SetNextItemWidth(-1); ImGui.InputText("##transfer-file", ref transferPath, 1024);
        if (ImGui.Button("Képletek és munkaciklusok importja sablonként")) Try(() => ImportRuleTemplates(transferPath));
        if (ImGui.Button("Sablonok exportja")) Try(() => { RuleProject.WriteAtomic(transferPath, JsonSerializer.Serialize(new { schema = "forest-rule-template-library", version = 1, templates = authoring.Templates }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })); message = "Sablonok exportálva: " + transferPath; });
    }
    private void ImportRuleTemplates(string file)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        if (!json.RootElement.TryGetProperty("schema", out var schema)) throw new InvalidDataException("Szabályprojekt vagy viselkedésdokumentum szükséges.");
        if (schema.GetString() == "forest-rule-template-library") {
            var templates = JsonSerializer.Deserialize<List<RuleTemplate>>(json.RootElement.GetProperty("templates").GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
            if (templates == null || templates.Count + authoring.Templates.Count > 1024 || templates.Any(t => t == null || t.Graph == null && t.Controller == null)) throw new InvalidDataException("Hibás vagy túl nagy sablontár.");
            RememberAuthoring(); authoring.Templates.AddRange(templates.Select(t => { var copy = t.Graph != null ? AuthoringOperations.Template(t.Graph) : AuthoringOperations.Template(t.Controller); copy.Name = t.Name; return copy; })); message = "Sablontár importálva; aktív kötés nem változott."; return;
        }
        BehaviorModel model = schema.GetString() switch { "forest-rule-project" => RuleProject.FromJson(json.RootElement.GetRawText()).Behaviors,
            "forest-behaviors" => BehaviorModel.FromJson(json.RootElement.GetRawText()), _ => throw new InvalidDataException("Nem támogatott sablonfájl.") };
        if (model.Graphs.Count + model.Controllers.Count + authoring.Templates.Count > 1024) throw new InvalidDataException("Túl sok sablon.");
        RememberAuthoring(); authoring.Templates.AddRange(model.Graphs.Select(AuthoringOperations.Template)); authoring.Templates.AddRange(model.Controllers.Select(AuthoringOperations.Template));
        message = "Sablonok beolvasva. Az aktív kötések és a játék még nem változtak.";
    }
    private void DrawGuidedStart()
    {
        ImGui.TextWrapped("Válassz célt. Ugyanabba a szerkeszthető munkatérbe lépsz; minden gráf és kapcsolat elérhető marad.");
        foreach (var goal in new[] { ("A fák növekedését módosítanám", "forest.growth"), ("A víz és az aszály hatását módosítanám", "water.stress"),
            ("Az utak kopását módosítanám", "traffic.wear"), ("Egy jármű munkaciklusát módosítanám", "machine.controller"), ("Az útvonalválasztást módosítanám", "route.find") })
            if (ImGui.Button(goal.Item1, new(-1, 0))) SelectWorkshopRule(goal.Item2);
        if (ImGui.Button("Egy konkrét játékobjektum szabályát módosítanám", new(-1, 0))) ShowObjects();
        if (ImGui.Button("Saját készletet és visszacsatolást építenék", new(-1, 0))) { workshopTopic = 5; ShowDynamics(); }
        ImGui.TextWrapped("A fő lépések: megérteni az összefüggést, szerkeszteni a gráfot, összehasonlító próbát futtatni, majd alkalmazni a játékban.");
    }
    internal void ShowRuleLibrary() => ShowCommonRule("library");
    internal void ShowGuidedStart() { workshopTool = WorkshopTool.Start; workshopPhase = WorkshopPhase.Understand; }
    internal void ShowCreationDialog() => requestCreatePopup = true;
    internal void DismissAuthoringPopups() => dismissAuthoringPopup = true;
    internal void ShowThematicStock(string id, int topic) { SetTopic("stock." + id, topic); ShowCommonRule("stock:" + id); }
}
