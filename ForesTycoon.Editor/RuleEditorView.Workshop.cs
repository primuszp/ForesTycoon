using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    private enum WorkshopTool { Rules, Objects, ObjectRule, Dynamics, Algorithms, Map, Library, Start, System }
    private sealed record RuleBinding(string Label, View Editor, BehaviorHook Hook = null, string Controller = null, RuleSource Source = null);
    // The workshop opens on the whole model; a rule, object or file argument switches to its own workspace.
    private WorkshopTool workshopTool = WorkshopTool.System;
    private WorkshopPhase workshopPhase, projectReturnPhase;
    private readonly WorkshopVersions workshopVersions = new();
    private readonly Stack<string> ruleBack = new();
    private string workshopRule = "forest.growth", workshopSearch = "", bindingSelectedFor, publishedProjectRevision;
    private string objectRuleTitle = "Objektumszabály", objectRuleScope = "";
    private string projectPathEdit;
    private int workshopTopic, bindingIndex;
    private bool workshopStarted, showAllMeasures, controllerNeedsCreation, hookNeedsCreation;
    private RuleBinding[] ruleBindings = Array.Empty<RuleBinding>();

    private string AuthoringRevision(GameWorld world)
    {
        var project = CollectProject(world); project.Algorithms = null;
        return project.ContentRevision() + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(authoring)))) + ":" + algorithms.ChangeVersion + ":" + algorithms.Workspace;
    }
    private void RefreshWorkshopRevision(GameWorld world)
    {
        workshopVersions.Observe(AuthoringRevision(world));
        bool fresh = liveObjects != null && DateTime.TryParse(liveObjects.PublishedUtc, out var date)
            && DateTime.UtcNow - date.ToUniversalTime() <= TimeSpan.FromSeconds(15);
        workshopVersions.Acknowledge(fresh ? liveObjects.ProjectRevision : null);
        if (workshopStarted) return;
        workshopStarted = true;
        if (view == View.Graph) { workshopTool = WorkshopTool.Rules; workshopRule = "road.trafficWear"; workshopTopic = 3; workshopPhase = WorkshopPhase.Edit; }
        else if (view == View.Behaviors && workshopTool == WorkshopTool.Rules) {
            workshopTool = WorkshopTool.ObjectRule; workshopPhase = WorkshopPhase.Edit; objectRuleTitle = "Importált viselkedésgráf";
        }
        if (currentRules.Index.Find(workshopRule) is GameRuleDefinition initialRule) workshopTopic = TopicOf(initialRule.Id, RuleWorkshopModel.Topic(initialRule));
    }
    private void WorkshopHeader(GameWorld world)
    {
        EditorStyle.Title("Szabályműhely", true);
        ImGui.SameLine(0, 22); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(projectName);
        ImGui.SameLine(0, 20); if (ImGui.Button("Projekt")) ImGui.OpenPopup("workshop-project");
        ImGui.SameLine(); if (ImGui.Button("Mentés · Ctrl+S")) Try(() => SaveProjectDraft(world));
        ImGui.SameLine(); if (ImGui.Button("Indulási segítség")) { workshopTool = WorkshopTool.Start; workshopPhase = WorkshopPhase.Understand; }
        if (ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S)) Try(() => SaveProjectDraft(world));
        WorkshopProjectMenu(world);
        // The project's way to the game: work copy -> comparison trial -> running in the game. Each step opens its page.
        bool trialFresh = workshopVersions.TestedCurrent, running = publishedProjectRevision != null && workshopVersions.Running == publishedProjectRevision && liveObjects?.Error == null;
        EditorStyle.Chip(workshopVersions.Unsaved ? "1 · Munkapéldány: mentetlen" : "1 · Munkapéldány: mentve", workshopVersions.Unsaved ? HudTheme.AmberAccent : HudTheme.Good, "pipe-save");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A Mentés (Ctrl+S) csak a külön munkapéldányt írja, a játékot nem változtatja.");
        ImGui.SameLine(0, 6);
        if (EditorStyle.Chip("2 · Próba: " + (trialFresh ? "aktuális változat" : trialResult != null ? "korábbi változaté" : "még nincs"),
            trialFresh ? HudTheme.Good : trialResult != null ? HudTheme.AmberAccent : HudTheme.Muted, "pipe-trial", true)) OpenProjectPhase(WorkshopPhase.Trial);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A teljes munkapéldány összehasonlító próbája az alapváltozattal, ugyanabból a világból.");
        ImGui.SameLine(0, 6);
        if (EditorStyle.Chip("3 · Játék: " + (publishedProjectRevision == null ? "nem alkalmaztál változást" : running ? "átvette" : "átvételre vár"),
            running ? HudTheme.Good : publishedProjectRevision != null ? HudTheme.AmberAccent : HudTheme.Muted, "pipe-apply", true)) OpenProjectPhase(WorkshopPhase.Apply);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A teljes projekt ellenőrzése, közzététele és a játék visszaigazolása.");
        ImGui.SameLine(0, 18); ImGui.AlignTextToFramePadding();
        EditorStyle.Text(HudTheme.Info, ObjectSourceLabel(world));
        ImGui.Separator();
    }
    private void WorkshopBody(GameWorld world, CompiledRoadRule compiled)
    {
        float sidebar = Math.Min(240, ImGui.GetContentRegionAvail().X * .27f);
        ImGui.BeginChild("workshop-navigation", new Vec2(sidebar, 0), ImGuiChildFlags.Borders);
        if (ImGui.Selectable("Rendszermodell", workshopTool == WorkshopTool.System)) ShowSystemModel();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Az egész játék készletei, áramlásai és visszacsatolási körei egy térképen.");
        EditorStyle.Section("Témák");
        for (int i = 0; i < RuleWorkshopModel.Topics.Length; i++)
            if (ImGui.Selectable(RuleWorkshopModel.Topics[i].Name, workshopTool == WorkshopTool.Rules && workshopTopic == i)) {
                workshopSearch = "";
                var first = currentRules.Index.Rules.FirstOrDefault(r => TopicOf(r.Id, RuleWorkshopModel.Topic(r)) == i);
                if (first != null) SelectWorkshopRule(first.Id);
            }
        ImGui.Separator(); ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##workshop-search", "Szabály vagy fogalom keresése…", ref workshopSearch, 160);
        EditorStyle.Section(workshopSearch.Length > 0 ? "Találatok minden témában" : "A téma szabályai");
        ImGui.BeginChild("workshop-rule-list", new Vec2(0, -128));
        DrawThematicItems(world);
        foreach (var rule in currentRules.Index.Search("", workshopSearch).Where(r => workshopSearch.Length > 0 || TopicOf(r.Id, RuleWorkshopModel.Topic(r)) == workshopTopic)) {
            if (ImGui.Selectable(rule.Name + "##" + rule.Id, workshopTool == WorkshopTool.Rules && workshopRule == rule.Id)) SelectWorkshopRule(rule.Id, clearSearch: false);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(rule.Name + "\n" + RuleWorkshopModel.Topics[RuleWorkshopModel.Topic(rule)].Name);
        }
        ImGui.EndChild();
        if (ImGui.Button("Új szabály / készlet / áramlás", new Vec2(-1, 0))) ImGui.OpenPopup("workshop-create");
        if (requestCreatePopup) { ImGui.OpenPopup("workshop-create"); requestCreatePopup = false; }
        CreateAuthoringMenu(world);
        if (ImGui.Button("Szabálytár és sablonok", new Vec2(-1, 0))) { workshopTool = WorkshopTool.Library; workshopPhase = WorkshopPhase.Edit; }
        if (ImGui.Button("Objektum kiválasztása", new Vec2(-1, 0))) ShowObjects();
        if (ImGui.Button("További eszközök", new Vec2(-1, 0))) ImGui.OpenPopup("workshop-tools");
        if (ImGui.BeginPopup("workshop-tools")) {
            if (ImGui.MenuItem("Saját készletek és áramlások")) ShowDynamics();
            if (ImGui.MenuItem("Szabálykapcsolatok térképe")) { workshopTool = WorkshopTool.Map; workshopPhase = WorkshopPhase.Understand; }
            if (ImGui.MenuItem("Összes belső algoritmusgráf")) ShowAlgorithms();
            ImGui.EndPopup();
        }
        ImGui.EndChild(); ImGui.SameLine();
        ImGui.BeginChild("workshop-workspace", new Vec2(0, 0));
        var selectedRule = currentRules.Index.Find(workshopRule);
        string title = workshopTool switch { WorkshopTool.Objects => "Játékobjektumok", WorkshopTool.ObjectRule => objectRuleTitle,
            WorkshopTool.Dynamics => thematicDynamics ? dynamicsEditor.Draft.Stocks.FirstOrDefault(s => "stock." + s.Id == thematicItem)?.Name
                ?? dynamicsEditor.Draft.Flows.FirstOrDefault(f => "flow." + f.Id == thematicItem)?.Name ?? "Saját készletek és áramlások" : "Saját készletek és áramlások", WorkshopTool.Algorithms => "Belső algoritmusgráfok",
            WorkshopTool.Map => "Szabálykapcsolatok térképe", WorkshopTool.System => "Rendszermodell", WorkshopTool.Library => "Szabálytár és sablonok", WorkshopTool.Start => "Mit szeretnél módosítani?", _ => selectedRule?.Name ?? "Válassz szabályt" };
        if (workshopTool == WorkshopTool.Rules && workshopPhase is WorkshopPhase.Understand or WorkshopPhase.Edit) {
            EditorStyle.Text(HudTheme.Muted, RuleWorkshopModel.Topics[workshopTopic].Name);
            if (ruleBack.Count > 0) { ImGui.SameLine(); if (ImGui.SmallButton("< Előző szabály")) SelectWorkshopRule(ruleBack.Pop(), remember: false); }
        }
        bool projectPage = workshopPhase is WorkshopPhase.Trial or WorkshopPhase.Apply;
        if (projectPage) {
            if (EditorStyle.QuietButton("‹ Vissza: " + title)) SetWorkshopPhase(projectReturnPhase, selectedRule);
            EditorStyle.Title(workshopPhase == WorkshopPhase.Trial ? "Próba · a teljes munkapéldány" : "Alkalmazás a játékban");
        } else {
            EditorStyle.Title(title);
            if (workshopTool == WorkshopTool.Rules) {
                if (ViewTab("Megértés", workshopPhase == WorkshopPhase.Understand, "Mit számol, miből, mit változtat és mely körökben vesz részt.")) SetWorkshopPhase(WorkshopPhase.Understand, selectedRule);
                ImGui.SameLine(0, 6);
                if (ViewTab("Szerkesztés", workshopPhase == WorkshopPhase.Edit, "A szabály képlete, munkaciklusa vagy belső algoritmusa.")) SetWorkshopPhase(WorkshopPhase.Edit, selectedRule);
            }
        }
        ImGui.Separator();
        ImGui.BeginChild("workshop-phase", new Vec2(0, -54), ImGuiChildFlags.None,
            workshopPhase == WorkshopPhase.Edit ? ImGuiWindowFlags.NoScrollbar : ImGuiWindowFlags.None);
        if (workshopPhase == WorkshopPhase.Trial) WorkshopTrial(world);
        else if (workshopPhase != WorkshopPhase.Apply && workshopTool == WorkshopTool.System) systemView.Draw(SystemModel(), SystemHost(world));
        else if (workshopPhase == WorkshopPhase.Apply) WorkshopApply(world);
        else if (workshopTool == WorkshopTool.Objects) ObjectsView(world);
        else if (workshopTool == WorkshopTool.Map) currentRules.Draw(world,
            model => { draft = model; SelectWorkshopRule("road.trafficWear"); SetWorkshopPhase(WorkshopPhase.Edit, currentRules.Index.Find(workshopRule)); },
            hook => { behaviors.SelectHook(hook); view = View.Behaviors; workshopTool = WorkshopTool.ObjectRule; workshopPhase = WorkshopPhase.Edit; objectRuleTitle = hook.Name; },
            kind => { behaviors.SelectController(kind); view = View.Behaviors; workshopTool = WorkshopTool.ObjectRule; workshopPhase = WorkshopPhase.Edit; objectRuleTitle = RuleWorkshopModel.KindName(kind) + " munkaciklusa"; });
        else if (workshopTool == WorkshopTool.Dynamics) DrawThematicDynamics(world);
        else if (workshopTool == WorkshopTool.Library) DrawRuleLibrary(world);
        else if (workshopTool == WorkshopTool.Start) DrawGuidedStart();
        else if (workshopTool == WorkshopTool.Algorithms) algorithms.Draw();
        else if (workshopTool == WorkshopTool.ObjectRule) {
            ImGui.TextWrapped("Hatókör: " + behaviors.ScopeSummary); behaviors.DrawEmbedded(world);
        } else if (selectedRule != null && workshopPhase == WorkshopPhase.Understand) WorkshopUnderstanding(selectedRule);
        else if (selectedRule != null) WorkshopEditing(world, selectedRule, compiled);
        ImGui.EndChild();
        WorkshopFooter(world, selectedRule);
        ImGui.EndChild();
    }
    private void SelectWorkshopRule(string id, bool remember = true, bool clearSearch = true)
    {
        var rule = currentRules.Index.Find(id); if (rule == null) return;
        if (remember && id != workshopRule) ruleBack.Push(workshopRule);
        workshopRule = id; workshopTopic = TopicOf(rule.Id, RuleWorkshopModel.Topic(rule)); thematicDynamics = false;
        workshopTool = WorkshopTool.Rules; workshopPhase = WorkshopPhase.Understand;
        if (clearSearch) workshopSearch = "";
        bindingSelectedFor = null;
    }
    private void SetWorkshopPhase(WorkshopPhase phase, GameRuleDefinition rule)
    {
        if (phase is WorkshopPhase.Trial or WorkshopPhase.Apply && workshopPhase is WorkshopPhase.Understand or WorkshopPhase.Edit) projectReturnPhase = workshopPhase;
        workshopPhase = phase;
        if (phase == WorkshopPhase.Edit && workshopTool == WorkshopTool.Rules && rule != null) EnsureRuleBinding(rule);
    }
    private void OpenProjectPhase(WorkshopPhase phase) => SetWorkshopPhase(phase, currentRules.Index.Find(workshopRule));
    private void WorkshopUnderstanding(GameRuleDefinition rule)
    {
        ImGui.TextWrapped(rule.Description); ImGui.Spacing();
        if (EditorStyle.BeginFacts("workshop-context")) {
            EditorStyle.Fact("Kikre vonatkozik?", rule.Scope);
            EditorStyle.Fact("Mikor fut?", rule.Schedule); ImGui.EndTable();
        }
        EditorStyle.Section("Helye a rendszermodellben");
        ImGui.TextWrapped("A kapcsolatok adatot adnak át; nem a végrehajtási sorrendet és nem a hatás előjelét mutatják.");
        RuleSystemContext(rule);
        if (ImGui.CollapsingHeader("Kapcsolódó szabályok mennyiségenként")) {
            if (ImGui.BeginTable("workshop-connections", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.PadOuterX)) {
                ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); DrawRuleConnections(rule, incoming: true);
                ImGui.TableSetColumnIndex(1); DrawRuleConnections(rule, incoming: false); ImGui.EndTable();
            }
        }
        if (rule.Formula.Length > 0) { EditorStyle.Section("A jelenlegi számítás"); ImGui.TextWrapped(rule.Formula); }
        if (rule.Parameters.Count > 0 && ImGui.CollapsingHeader("Kapcsolódó értékek és hangolható számok")) currentRules.DrawRuleParameters(rule);
        if (ImGui.CollapsingHeader("Forrás és technikai részletek")) {
            ImGui.TextUnformatted(rule.Id);
            foreach (var source in rule.Sources) ImGui.TextWrapped(source.File + " · " + source.Symbol);
            if (rule.Notes.Length > 0) ImGui.TextWrapped(rule.Notes);
        }
        DrawPresentation(rule.Id, RuleWorkshopModel.Topic(rule));
    }
    private void DrawRuleConnections(GameRuleDefinition rule, bool incoming)
    {
        EditorStyle.Section(incoming ? "Miből számol?" : "Mit változtat?");
        var fields = incoming ? rule.Reads : rule.Writes;
        if (fields.Length == 0) ImGui.TextWrapped(incoming ? "Nincs külső bemenete a katalógusban." : "Nincs állapotírása a katalógusban.");
        for (int i = 0; i < fields.Length; i++) {
            string field = fields[i];
            ImGui.PushID((incoming ? "in" : "out") + field);
            var neighbours = (incoming ? currentRules.Index.Writers(field) : currentRules.Index.Readers(field)).Where(r => r.Id != rule.Id).ToArray();
            if (ImGui.TreeNodeEx(RuleWorkshopModel.FieldName(field), ImGuiTreeNodeFlags.None)) {
                if (neighbours.Length == 0) ImGui.TextWrapped(incoming ? "Saját állapot vagy induló adat." : "Állapot / megjelenítés; nincs katalogizált fogyasztó.");
                foreach (var other in neighbours) {
                    if (ImGui.Selectable(other.Name + "##" + other.Id)) SelectWorkshopRule(other.Id);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(other.Name + "\n" + field);
                }
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
    }
    private string ObjectSourceLabel(GameWorld world)
    {
        if (!useLiveObjects) return $"Objektumforrás: editor tesztvilága · tick {world.SimulationTick}";
        if (liveObjects == null) return "Objektumforrás: nincs élő kapcsolat";
        bool fresh = DateTime.TryParse(liveObjects.PublishedUtc, out var date) && DateTime.UtcNow - date.ToUniversalTime() <= TimeSpan.FromSeconds(15);
        return fresh ? $"Objektumforrás: futó játék · tick {liveObjects.Tick}" + (liveObjects.Error != null ? " · kapcsolati hiba" : "")
            : "Objektumforrás: korábbi játékkatalógus (nem élő)";
    }
    private RuleBinding[] Bindings(GameRuleDefinition rule)
    {
        var result = new List<RuleBinding>();
        if (rule.Id == "road.trafficWear") result.Add(new("Áthaladási kopás képlete", View.Graph));
        if (rule.Id is "machine.controller" or "machine.forwarder" or "machine.processor" or "vehicle.transfer" or "fleet.orders")
            foreach (string kind in new[] { "truck", "forwarder", "processor" })
                if (rule.Id == "machine.controller" || rule.Id == "machine." + kind || kind == "truck" && rule.Id is "vehicle.transfer" or "fleet.orders")
                    result.Add(new(RuleWorkshopModel.KindName(kind) + " munkaciklusa", View.Behaviors, Controller: kind));
        foreach (var hook in WorldBehaviorPolicy.Hooks.Where(h => RuleWorkshopModel.HookBelongsTo(h, rule)))
            result.Add(new(hook.Name + " · " + RuleWorkshopModel.KindName(hook.Kind), View.Behaviors, Hook: hook));
        foreach (var source in rule.Sources) result.Add(new("Belső algoritmus · " + source.Symbol, View.Algorithms, Source: source));
        return result.ToArray();
    }
    private void EnsureRuleBinding(GameRuleDefinition rule)
    {
        if (bindingSelectedFor == rule.Id) return;
        ruleBindings = Bindings(rule); bindingIndex = 0; bindingSelectedFor = rule.Id;
        OpenRuleBinding();
    }
    private void OpenRuleBinding()
    {
        if (ruleBindings.Length == 0) return;
        var binding = ruleBindings[bindingIndex]; view = binding.Editor;
        hookNeedsCreation = false;
        if (binding.Hook != null) {
            hookNeedsCreation = !behaviors.Document.Graphs.Any(g => g.Hook == binding.Hook.Id && g.Kind == binding.Hook.Kind && g.ObjectId == null);
            if (!hookNeedsCreation) behaviors.SelectHook(binding.Hook);
        }
        else if (binding.Controller != null) {
            controllerNeedsCreation = !behaviors.Document.Controllers.Any(c => c.Kind == binding.Controller && c.ObjectId == null);
            if (!controllerNeedsCreation) behaviors.SelectController(binding.Controller);
        }
        else if (binding.Source != null) algorithms.SelectSource(binding.Source);
        else { selected = draft.Output; fitRequested = true; }
    }
    private void WorkshopEditing(GameWorld world, GameRuleDefinition rule, CompiledRoadRule compiled)
    {
        EnsureRuleBinding(rule);
        if (ruleBindings.Length == 0) { ImGui.TextWrapped("Ehhez a szabályhoz nincs szerkesztői kötés a katalógusban."); return; }
        if (ruleBindings.Length > 1) {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##workshop-binding", ruleBindings[bindingIndex].Label)) {
                for (int i = 0; i < ruleBindings.Length; i++) if (ImGui.Selectable(ruleBindings[i].Label, i == bindingIndex)) { bindingIndex = i; OpenRuleBinding(); }
                ImGui.EndCombo();
            }
        }
        ImGui.TextWrapped("Hatókör: " + rule.Scope + " · " + rule.Schedule);
        if (hookNeedsCreation) {
            ImGui.TextWrapped("Ehhez a folyamathoz még nincs típusszintű képlet a munkapéldányban. A játék ilyenkor a beépített eredményt használja.");
            if (ImGui.Button("Szerkeszthető képlet létrehozása")) {
                behaviors.SelectHook(ruleBindings[bindingIndex].Hook); hookNeedsCreation = false;
            }
            return;
        }
        if (ruleBindings[bindingIndex].Controller != null && controllerNeedsCreation) {
            ImGui.TextWrapped("Ez a típus jelenleg az automatikus alapvezérlést használja. Új munkaciklus létrehozásával külön szerkeszthető állapotgráfot készítesz; a szabály megnyitása önmagában nem hoz létre felülírást.");
            if (ImGui.Button("Szerkeszthető munkaciklus létrehozása")) {
                behaviors.SelectController(ruleBindings[bindingIndex].Controller, useTransportCycle: true); controllerNeedsCreation = false;
            }
            return;
        }
        if (view == View.Algorithms) {
            ImGui.TextWrapped("Részletes belső gráf: a kiválasztott műveletből indulsz. Alkalmazáskor fordítás és új futtató szükséges.");
            algorithms.Draw(embedded: true);
        } else if (view == View.Behaviors) behaviors.DrawEmbedded(world);
        else GraphView(world, compiled);
    }
    private void WorkshopTrial(GameWorld world)
    {
        ImGui.TextWrapped("Az alap- és a szerkesztett változat ugyanabból a mentésből indul. A próba a teljes projektet futtatja, a játékot nem módosítja.");
        if (ImGui.BeginCombo("Kiinduló világ", graphTestSavedWorld ? "Kiválasztott játékmentés" : "Editor tesztvilága")) {
            if (ImGui.Selectable("Editor tesztvilága", !graphTestSavedWorld)) graphTestSavedWorld = false;
            if (ImGui.Selectable("Kiválasztott játékmentés", graphTestSavedWorld)) graphTestSavedWorld = true;
            ImGui.EndCombo();
        }
        if (graphTestSavedWorld) { ImGui.SetNextItemWidth(-1); ImGui.InputText("##trial-world-file", ref worldPath, 1024); }
        ImGui.SetNextItemWidth(150); ImGui.InputInt("Időtáv (tick)", ref graphTestTicks); graphTestTicks = Math.Clamp(graphTestTicks, 0, 100000);
        ImGui.TextWrapped($"{graphTestTicks / 30d:G6} szimulációs másodperc. Az eredmény az eltelt erdőidőt is mutatja; havi folyamatokhoz elég hosszú próbát válassz.");
        if (graphSimulation != null) {
            EditorStyle.Text(HudTheme.Info, "Próba folyamatban · a pillanatfelvételt futtatja, közben szerkeszthetsz.");
            if (ImGui.Button("Próba megszakítása")) trialCancellation?.Cancel();
        }
        if (trialResult != null) {
            EditorStyle.Section(workshopVersions.TestedCurrent ? "Eredmény · az aktuális munkapéldány" : "Eredmény · korábbi munkapéldányhoz tartozik");
            ImGui.TextWrapped($"Forrás: {trialResult.Source}\nTick: {trialResult.StartTick} -> {trialResult.EndTick}\nErdőév · alap: {trialResult.StartYear:G7} -> {trialResult.BaselineEndYear:G7} · szerkesztett: {trialResult.EditedStartYear:G7} -> {trialResult.EndYear:G7}");
            ImGui.Checkbox("Minden elérhető mérés", ref showAllMeasures);
            var keys = workshopTopic switch {
                0 => new[] { "world.treeCount", "forest.totalVolume", "forest.meanHeight", "forest.meanDiameter", "forest.meanHealth", "world.water" },
                1 => new[] { "world.water", "world.rain", "world.temperature", "world.snow", "forest.meanHealth" },
                3 => new[] { "road.meanCondition", "world.roadCount", "world.balance", "world.runningCosts" },
                _ => new[] { "world.balance", "world.income", "world.expenses", "world.runningCosts", "forest.totalVolume", "world.treeCount" }
            };
            if (ImGui.BeginTable("trial-measures", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) {
                ImGui.TableSetupColumn("Mérés", ImGuiTableColumnFlags.WidthStretch, 2.5f);
                ImGui.TableSetupColumn("Alap", ImGuiTableColumnFlags.WidthStretch, 1);
                ImGui.TableSetupColumn("Szerkesztett", ImGuiTableColumnFlags.WidthStretch, 1);
                ImGui.TableSetupColumn("Eltérés", ImGuiTableColumnFlags.WidthStretch, 1); ImGui.TableHeadersRow();
                foreach (var measure in trialResult.Measures.Where(m => showAllMeasures || keys.Contains(m.Key))) {
                    ImGui.TableNextRow(); ImGui.TableSetColumnIndex(0); ImGui.TextWrapped(MeasureLabel(measure.Key));
                    ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(measure.Before.ToString("G7"));
                    ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(measure.After.ToString("G7"));
                    ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted((measure.After - measure.Before).ToString("+0.######;-0.######;0"));
                }
                ImGui.EndTable();
            }
            if (!trialResult.Measures.Any(m => keys.Contains(m.Key))) ImGui.TextWrapped("Ehhez a témához nincs mérési adat ebben a futtatóban.");
            if (ImGui.CollapsingHeader("Próba fájljai és verziója")) { ImGui.TextWrapped(trialResult.OutputPath); ImGui.TextWrapped(trialResult.DraftRevision); }
        }
        if (ImGui.CollapsingHeader("Helyi tesztvilág és gyors ellenőrzés")) {
            ImGui.TextWrapped("Ez az editor saját algoritmusverzióját futtatja; nem helyettesíti a teljes gráfprojekt próbáját.");
            if (ImGui.Button(behaviors.IsPaused ? "Tesztvilág indítása" : "Tesztvilág szünet")) behaviors.TogglePause();
            ImGui.SameLine(); if (ImGui.Button("Egy tick")) behaviors.Step(world);
            if (ImGui.Button("Projekt alkalmazása csak a tesztvilágban")) Try(() => ApplyProject(world, false));
            if (world.DynamicsError != null) ImGui.TextWrapped(world.DynamicsError);
        }
    }
    private static string MeasureLabel(string key) => key switch {
        "forest.totalVolume" => "Élő faállomány térfogata (m³)", "forest.meanHeight" => "Átlagmagasság (m)", "forest.meanDiameter" => "Átlagátmérő (m)",
        "forest.meanHealth" => "Átlagos faegészség (0–1)", "road.meanCondition" => "Átlagos útállapot (0–1)",
        "world.treeCount" => "Élő fák (db)", "world.balance" => "Egyenleg (eFt)", "world.income" => "Bevétel (eFt)", "world.expenses" => "Kiadások (eFt)",
        "world.runningCosts" => "Üzemeltetés (eFt)", "world.rain" => "Csapadékráta", "world.snow" => "Hókészlet (mm)", "world.time" => "Erdőév", _ => RuleWorkshopModel.FieldName(key)
    };
    private void WorkshopApply(GameWorld world)
    {
        ImGui.TextWrapped("A teljes munkapéldány kerül a játékhoz. A mentés önmagában nem alkalmazza a szabályokat.");
        EditorStyle.Section("Mi változott?");
        var mine = CollectProject(world); var baseline = world.RuleProjectDocument;
        bool any = false;
        void Change(string text) { ImGui.Bullet(); ImGui.SameLine(); ImGui.TextWrapped(text); any = true; }
        if (mine.Name != baseline.Name) Change("Projekt neve: " + mine.Name);
        if (mine.Road.ToJson() != baseline.Road.ToJson()) Change("Áthaladási útkopás gráfja");
        foreach (var spec in GameTuning.Specs)
            if ((mine.Tuning.TryGetValue(spec.Id, out double a) ? a : spec.Default) != (baseline.Tuning.TryGetValue(spec.Id, out double b) ? b : spec.Default)) Change("Hangolt érték: " + spec.Id);
        foreach (var graph in mine.Behaviors.Graphs)
            if (!baseline.Behaviors.Graphs.Any(g => g.Id == graph.Id && new BehaviorModel { Graphs = new() { g } }.ToJson() == new BehaviorModel { Graphs = new() { graph } }.ToJson()))
                Change(graph.Name + (graph.ObjectId.HasValue ? " · egyedi objektum #" + graph.ObjectId : " · " + RuleWorkshopModel.KindName(graph.Kind) + " típusszabály"));
        if (baseline.Behaviors.Graphs.Any(g => !mine.Behaviors.Graphs.Any(m => m.Id == g.Id))) Change("Eltávolított képletgráf / felülírás");
        if (System.Text.Json.JsonSerializer.Serialize(mine.Behaviors.Controllers) != System.Text.Json.JsonSerializer.Serialize(baseline.Behaviors.Controllers)) Change("Munkaciklusok és vezérlési kötések");
        if (new RuleProject { Dynamics = mine.Dynamics }.ToJson() != new RuleProject { Dynamics = baseline.Dynamics }.ToJson()) Change("Saját készletek és áramlások");
        if (algorithms.HasGraphs) Change("A teljes belső algoritmusgráf ellenőrzése és fordítása; szükség esetén új runtime");
        if (!any) ImGui.TextWrapped("Nincs eltérés az editor tesztvilágának projektjéhez képest.");
        EditorStyle.Section("Próba és aktiválás");
        ImGui.TextWrapped(workshopVersions.TestedCurrent ? "A jelenlegi munkapéldány összehasonlító próbája elkészült." : "Ehhez a munkapéldányhoz még nincs sikeres összehasonlító próba.");
        ImGui.TextWrapped("A szokásos szabálycsere a támogatott alkalmazási ponton történik. Más algoritmusverzióhoz fordítás és mentett állapotból új futtató szükséges. Újvilág-szabályok a meglévő világot nem generálják újra.");
        if (algorithmBuild != null) {
            EditorStyle.Text(HudTheme.Info, "Fordítás folyamatban · a közzététel a munkapéldány pillanatfelvételét használja.");
            if (ImGui.Button("Fordítás és közzététel megszakítása")) publishingCancellation?.Cancel();
        }
        if (publishedProjectRevision != null) {
            bool active = workshopVersions.Running == publishedProjectRevision && liveObjects?.Error == null;
            ImGui.TextWrapped(active ? "A játék visszaigazolta a közzétett projektverziót." : "Projekt közzétéve; a játék átvételi visszaigazolása még nem érkezett meg.");
            if (workshopVersions.Published != workshopVersions.Current) ImGui.TextWrapped("Azóta újabb módosítás van a munkapéldányban; a közzététel egy korábbi változaté.");
        }
    }
    private void WorkshopFooter(GameWorld world, GameRuleDefinition rule)
    {
        ImGui.Separator();
        bool busy = algorithms.IsLoading || algorithmBuild != null || graphSimulation != null;
        if (workshopPhase == WorkshopPhase.Understand && workshopTool == WorkshopTool.Rules) {
            if (EditorStyle.PrimaryButton("Gráf szerkesztése ->", rule != null)) SetWorkshopPhase(WorkshopPhase.Edit, rule);
        } else if (workshopPhase == WorkshopPhase.Edit) {
            if (EditorStyle.PrimaryButton("Ellenőrzés és próba ->")) OpenProjectPhase(WorkshopPhase.Trial);
        } else if (workshopPhase == WorkshopPhase.Trial) {
            if (EditorStyle.PrimaryButton(graphSimulation != null ? "Próba folyamatban…" : "Összehasonlító próba indítása", !busy)) Try(() => SimulateGraphs(world));
            ImGui.SameLine(); if (ImGui.Button("Alkalmazás áttekintése ->")) OpenProjectPhase(WorkshopPhase.Apply);
        } else if (workshopPhase == WorkshopPhase.Apply) {
            if (EditorStyle.PrimaryButton(algorithmBuild != null ? "Fordítás folyamatban…" : "Alkalmazás a játékban", !busy)) Try(() => ApplyProject(world, true));
            ImGui.SameLine(); if (ImGui.Button("Vissza a szerkesztéshez")) SetWorkshopPhase(WorkshopPhase.Edit, rule);
        }
    }
    private void SaveProjectDraft(GameWorld world)
    {
        if (algorithms.IsLoading) throw new InvalidOperationException("A gráfok beolvasása még tart. A befejezés után a teljes munkapéldány menthető.");
        draftGuard.RequireUnchanged(RuleWorkshopModel.DraftPath(projectPath));
        ProjectHistory.Archive(RuleWorkshopModel.DraftPath(projectPath));
        string destination = WorkshopDraftStore.Save(projectPath, CollectProject(world), algorithms.Document, algorithms.Workspace);
        draftGuard.Observe(destination);
        RefreshWorkshopRevision(world); workshopVersions.MarkSaved();
        message = "Munkapéldány mentve. A játékban futó szabályokat nem módosította. " + destination;
    }
    private void WorkshopProjectMenu(GameWorld world)
    {
        if (!ImGui.BeginPopup("workshop-project")) return;
        if (publishedGuard.Changed(projectPath) || draftGuard.Changed(RuleWorkshopModel.DraftPath(projectPath))) ImGui.TextWrapped("Külső módosítás érkezett. A mentés vagy alkalmazás nem írja felül: nyisd meg az új fájlt, vagy válassz másik útvonalat.");
        ImGui.SetNextItemWidth(460); ImGui.InputText("Projekt neve", ref projectName, 160);
        bool jobs = algorithmBuild != null || graphSimulation != null;
        ImGui.BeginDisabled(jobs);
        projectPathEdit ??= projectPath; ImGui.SetNextItemWidth(600);
        bool choosePath = ImGui.InputText("Aktív projekt útvonala", ref projectPathEdit, 1024, ImGuiInputTextFlags.EnterReturnsTrue);
        if (choosePath || ImGui.IsItemDeactivatedAfterEdit()) Try(() => projectPath = RuleWorkshopModel.PublishedPath(projectPathEdit));
        ImGui.TextWrapped("A munkapéldány külön fájl: " + RuleWorkshopModel.DraftPath(projectPath));
        if (ImGui.Button("Munkapéldány megnyitása")) Try(() => SwitchProject(world, RuleProject.FromJson(File.ReadAllText(RuleWorkshopModel.DraftPath(projectPath)))));
        ImGui.SameLine(); if (ImGui.Button("Közzétett projekt megnyitása")) Try(() => SwitchProject(world, RuleProject.FromJson(File.ReadAllText(projectPath))));
        if (ImGui.Button("Új, önálló projekt")) Try(() => {
            if (workshopVersions.Unsaved) SaveProjectDraft(world);
            projectPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath)), "projekt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + ".json");
            LoadProject(world, GameWorld.DefaultRuleProject()); workshopTool = WorkshopTool.Start; workshopPhase = WorkshopPhase.Understand;
        });
        if (ImGui.CollapsingHeader("Korábbi verzió megnyitása / helyreállítás")) {
            foreach (string folder in new[] { projectPath + ".history", RuleWorkshopModel.DraftPath(projectPath) + ".history" }.Where(Directory.Exists))
                foreach (string version in Directory.EnumerateFiles(folder, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(20))
                    if (ImGui.Selectable(File.GetLastWriteTime(version).ToString("yyyy-MM-dd HH:mm:ss") + " · " + Path.GetFileName(version)[..12] + "##" + version)) Try(() => SwitchProject(world, RuleProject.FromJson(File.ReadAllText(version))));
            string parent = Path.GetDirectoryName(Path.GetFullPath(projectPath));
            foreach (string recovery in (Directory.Exists(parent) ? Directory.EnumerateFiles(parent, Path.GetFileName(projectPath) + ".recovery-*.draft.json") : Array.Empty<string>()).OrderByDescending(File.GetLastWriteTimeUtc).Take(10))
                if (ImGui.Selectable("Helyreállítás: " + Path.GetFileName(recovery))) Try(() => SwitchProject(world, RuleProject.FromJson(File.ReadAllText(recovery))));
        }
        ImGui.EndDisabled();
        ImGui.Separator(); EditorStyle.Section("Világ és objektumforrás");
        if (liveStatus.Length > 0 && ImGui.CollapsingHeader("Kapcsolat részletei")) ImGui.TextWrapped(liveStatus);
        if (ImGui.RadioButton("Futó játék objektumai", useLiveObjects)) useLiveObjects = true;
        ImGui.SameLine(); if (ImGui.RadioButton("Editor tesztvilága", !useLiveObjects)) useLiveObjects = false;
        ImGui.SetNextItemWidth(600); ImGui.InputText("Játékmentés", ref worldPath, 1024);
        if (ImGui.Button("Világ beolvasása a tesztbe")) Try(() => { message = EditorWorldLoader.Load(world, worldPath); UseWorldFile(worldPath); });
        if (ImGui.Button("Tesztvilág szabályainak átvétele")) Try(() => SwitchProject(world, world.RuleProjectDocument));
        if (ImGui.Button("Tesztvilág mentése")) Try(() => { world.ExecutePendingCommands(); using var stream = new MemoryStream(); world.Save(stream); RuleProject.WriteAtomic(worldPath + ".editor.json", System.Text.Encoding.UTF8.GetString(stream.ToArray())); message = "Tesztvilág mentve: " + worldPath + ".editor.json"; });
        ImGui.EndPopup();
    }
    private void WorkshopStatus()
    {
        ImGui.Separator();
        string status = message.Split('\n')[0];
        EditorStyle.Text(HudTheme.Muted, status.Length > 115 ? status[..115] + "…" : status);
        if (message.Length > 0) { ImGui.SameLine(); if (ImGui.SmallButton("Részletek")) ImGui.OpenPopup("workshop-message"); }
        if (ImGui.BeginPopupModal("workshop-message", ImGuiWindowFlags.AlwaysAutoResize)) {
            ImGui.InputTextMultiline("##workshop-diagnostic", ref message, 30000, new Vec2(820, 420), ImGuiInputTextFlags.ReadOnly);
            if (ImGui.Button("Bezárás")) ImGui.CloseCurrentPopup(); ImGui.EndPopup();
        }
    }
    internal void ShowWorkshopRule(string id, WorkshopPhase phase = WorkshopPhase.Understand)
    {
        workshopRule = id; workshopPhase = phase; workshopTool = WorkshopTool.Rules; bindingSelectedFor = null;
        if (currentRules.Index.Find(id) is GameRuleDefinition rule) workshopTopic = TopicOf(rule.Id, RuleWorkshopModel.Topic(rule));
    }
    internal void ShowWorkshopComparison(RuleTrialResult result)
    {
        trialResult = result; workshopVersions.CompleteTrial(result.DraftRevision);
        workshopPhase = WorkshopPhase.Trial; showAllMeasures = true;
    }
}
