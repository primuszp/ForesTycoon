using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

/// <summary>
/// The system model inside the workshop: the catalogued game processes plus this project's own stocks, flows and the
/// formulas that read model quantities, so a feedback loop authored in the editor shows up next to the native ones.
/// </summary>
internal sealed partial class RuleEditorView
{
    private readonly SystemModelView systemView = new();
    private SystemModelAnalysis systemModel;
    private string systemModelKey;

    private int influenceTarget;
    private double influenceStrength = 0.5;

    private SystemModelAnalysis SystemModel()
    {
        var dynamics = dynamicsEditor.Draft;
        var formulas = behaviors.Document.Graphs.Where(g => g.Nodes.Any(n => n.Operation == BehaviorOperation.NamedValue)).ToArray();
        string key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(currentRules.Catalog) + "|"
            + new RuleProject { Dynamics = dynamics }.ToJson().GetHashCode() + "|"
            + string.Join(";", formulas.Select(g => g.Id + ":" + g.Name + ":" + g.Hook + ":" + g.Kind + ":" + string.Join(",", g.Nodes.Where(n => n.Operation == BehaviorOperation.NamedValue).Select(n => n.Key))))
            + "|" + string.Join(",", dynamics.Stocks.Select(s => TopicOf("stock." + s.Id, 5)))
            + "|" + System.Text.Json.JsonSerializer.Serialize(authoring.Quantities);
        if (systemModel != null && key == systemModelKey) return systemModel;
        systemModelKey = key;
        return systemModel = RuleWorkshopModel.SystemModel(currentRules.Catalog, dynamics, formulas,
            id => id.StartsWith("flow.") ? FlowTopic(dynamics.Flows.Single(f => "flow." + f.Id == id)) : TopicOf(id, 5), authoring.Quantities);
    }

    private SystemModelHost SystemHost(GameWorld world) => new() {
        Value = key => { try { return PreviewValue(world, key); } catch (ArithmeticException) { return double.NaN; } },
        Tile = id => world.DescribeTile(id), TileCount = world.Map.Tiles.Count,
        OpenProcess = OpenSystemProcess, Editable = EditableSummary,
        DrawProcess = DrawProcessEditing, DrawQuantity = v => DrawQuantityEditing(world, v)
    };

    internal void ShowSystemModel(string variable = null)
    {
        workshopTool = WorkshopTool.System; workshopPhase = WorkshopPhase.Understand;
        if (variable != null) systemView.Focus(systemModel, variable);
    }
    /// <summary>Opens the system map on one tile's vertical section.</summary>
    internal void ShowSystemTile(int tile, string layer = null)
    {
        ShowSystemModel(); systemView.SelectedTile = tile;
        if (layer != null) systemView.ShowLayer(layer); else systemView.ShowStack();
    }

    private void ShowSystemLoop(SystemLoop loop) { ShowSystemModel(); systemView.FocusLoop(loop); }
    /// <summary>Opens the longest of the shortest loops through a quantity: the one that crosses the most of the model.</summary>
    internal void ShowSystemLoopThrough(string variable)
    {
        var loops = systemModel?.LoopsThroughVariable(variable) ?? Array.Empty<SystemLoop>();
        if (loops.Count > 0) ShowSystemLoop(loops.OrderByDescending(systemModel.Span).ThenByDescending(l => l.Length).First());
        else ShowSystemModel(variable);
    }

    private void OpenSystemProcess(SystemProcess process)
    {
        if (process.Id.StartsWith("flow.", StringComparison.Ordinal)) { OpenDynamic(process.Id[5..], true); return; }
        if (process.RuleId == null) return;
        SelectWorkshopRule(process.RuleId);
        if (process.Id.StartsWith("graph.", StringComparison.Ordinal)) SetWorkshopPhase(WorkshopPhase.Edit, currentRules.Index.Find(process.RuleId));
    }

    /// <summary>What can be changed in a rule without touching its internal algorithm.</summary>
    private string EditableSummary(SystemProcess process)
    {
        if (process.Id.StartsWith("flow.", StringComparison.Ordinal)) return "saját áramlás";
        if (currentRules.Index.Find(process.RuleId) is not GameRuleDefinition rule) return "";
        var parts = new List<string>();
        var bindings = Bindings(rule);
        if (bindings.Any(b => b.Hook != null && !b.Hook.Id.StartsWith("tuning."))) parts.Add("képlet");
        if (rule.Parameters.Any(p => p.Tunable)) parts.Add("paraméter");
        if (bindings.Any(b => b.Controller != null)) parts.Add("munkaciklus");
        return parts.Count > 0 ? string.Join(", ", parts) : "belső algoritmus";
    }

    /// <summary>A rule's editing points inside the system map: parameters in place, formula and cycle one click away.</summary>
    private void DrawProcessEditing(SystemProcess process)
    {
        if (process.Id.StartsWith("flow.", StringComparison.Ordinal)) {
            if (ImGui.SmallButton("Áramlás szerkesztése")) OpenDynamic(process.Id[5..], true);
            return;
        }
        if (currentRules.Index.Find(process.RuleId) is not GameRuleDefinition rule) return;
        ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, rule.Description); ImGui.PopTextWrapPos();
        if (rule.Formula.Length > 0) { ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Info, rule.Formula); ImGui.PopTextWrapPos(); }
        var bindings = Bindings(rule);
        bool algorithmShown = false;
        for (int i = 0; i < bindings.Length; i++) {
            var binding = bindings[i];
            if (binding.Hook?.Id.StartsWith("tuning.") == true) continue;
            if (binding.Source != null) { if (algorithmShown) continue; algorithmShown = true; }
            string state = "";
            if (binding.Hook != null) {
                var graph = behaviors.Document.Graphs.Find(g => g.Hook == binding.Hook.Id && g.Kind == binding.Hook.Kind && g.ObjectId == null);
                state = graph != null && graph.Nodes.Find(n => n.Id == graph.Output)?.Operation != BehaviorOperation.Native ? " · saját képlet" : " · beépített";
            }
            string label = binding.Hook != null ? "Képlet: " + binding.Hook.Name + state : binding.Controller != null ? binding.Label : "Belső algoritmus";
            if (ImGui.SmallButton(label + "##bind" + i)) OpenRuleBinding(rule, i);
        }
        if (rule.Parameters.Any(p => p.Tunable)) currentRules.DrawRuleParameters(rule);
        if (ImGui.SmallButton("Megnyitás a műhelyben")) SelectWorkshopRule(rule.Id);
    }

    private void OpenRuleBinding(GameRuleDefinition rule, int index)
    {
        SelectWorkshopRule(rule.Id);
        SetWorkshopPhase(WorkshopPhase.Edit, rule);
        if (index < ruleBindings.Length && index != bindingIndex) { bindingIndex = index; OpenRuleBinding(); }
    }

    /// <summary>Wording of a quantity (stored in the project) and new influences from it, as formulas.</summary>
    private void DrawQuantityEditing(GameWorld world, SystemVariable v)
    {
        if (ImGui.CollapsingHeader("Megnevezés és réteg")) {
            authoring.Quantities.TryGetValue(v.Id, out var saved);
            string name = v.Name, unit = v.Unit, description = v.Description;
            var layers = SystemVocabulary.Default.Layers;
            int layer = Math.Max(0, layers.FindIndex(l => l.Id == v.Layer));
            QuantityPresentation Edit() { RememberAuthoring(); if (!authoring.Quantities.TryGetValue(v.Id, out var q)) authoring.Quantities[v.Id] = q = new(); return q; }
            ImGui.SetNextItemWidth(-90); if (ImGui.InputText("Név", ref name, 120, ImGuiInputTextFlags.EnterReturnsTrue) && name.Trim().Length > 0) Edit().Name = name.Trim();
            ImGui.SetNextItemWidth(-90); if (ImGui.InputText("Egység", ref unit, 40, ImGuiInputTextFlags.EnterReturnsTrue)) Edit().Unit = unit.Trim();
            ImGui.SetNextItemWidth(-90); if (ImGui.Combo("Réteg", ref layer, layers.Select(l => l.Name).ToArray(), layers.Count)) Edit().Layer = layers[layer].Id;
            if (ImGui.InputTextMultiline("Leírás", ref description, 1000, new Vec2(-90, 60), ImGuiInputTextFlags.EnterReturnsTrue)) Edit().Description = description.Trim();
            ImGui.TextWrapped("Enter rögzíti. A megnevezés a közös projektbe kerül; a játék számítását nem változtatja.");
            if (saved != null && ImGui.SmallButton("Eredeti megnevezés")) { RememberAuthoring(); authoring.Quantities.Remove(v.Id); }
        }
        if (!ImGui.CollapsingHeader("Hatás hozzáadása ebből a mennyiségből")) return;
        if (v.Observe.Length == 0) {
            ImGui.TextWrapped("Ez a mennyiség még nem olvasható képletből. Ehhez a játéknak mérhető mezőként kell közzétennie (a szótár observe kulcsa).");
            return;
        }
        var targets = WorldBehaviorPolicy.Hooks.Where(h => h.Kind != "dynamics")
            .Select(h => (Hook: h, Rule: currentRules.Index.Rules.FirstOrDefault(r => RuleWorkshopModel.HookBelongsTo(h, r))))
            .Where(t => t.Rule != null).OrderBy(t => RuleWorkshopModel.Topic(t.Rule)).ThenBy(t => t.Rule.Name).ToArray();
        influenceTarget = Math.Clamp(influenceTarget, 0, targets.Length - 1);
        string Label((BehaviorHook Hook, GameRuleDefinition Rule) t) => t.Rule.Name + " · " + t.Hook.Name;
        ImGui.TextWrapped($"A választott szabály eredményét szorozza: × (1 + erősség × ({v.Name} − jelenlegi érték)). Pozitív erősség: nagyobb {v.Name}, nagyobb eredmény.");
        ImGui.SetNextItemWidth(-90);
        if (ImGui.BeginCombo("Szabály", Label(targets[influenceTarget]))) {
            for (int i = 0; i < targets.Length; i++) if (ImGui.Selectable(Label(targets[i]) + "##target" + i, i == influenceTarget)) influenceTarget = i;
            ImGui.EndCombo();
        }
        ImGui.SetNextItemWidth(-90); ImGui.InputDouble("Erősség", ref influenceStrength, 0.05, 0.5, "%.3f");
        double reference = PreviewValue(world, v.Observe);
        if (!double.IsFinite(reference)) reference = 0;
        if (EditorStyle.PrimaryButton("Hatás létrehozása")) {
            var target = targets[influenceTarget];
            behaviors.AddInfluence(target.Hook, v.Observe, v.Name, reference, influenceStrength);
            message = $"Új hatás: {v.Name} → {target.Rule.Name}. A képlet a Szerkesztés lépésben finomítható; mentés után próbáld ki.";
        }
    }

    /// <summary>The rule's place in the model: what it reads, what it changes, and the loops it closes.</summary>
    private void RuleSystemContext(GameRuleDefinition rule)
    {
        var model = SystemModel();
        var process = model.Process(rule.Id);
        if (process == null) return;
        if (!ImGui.BeginTable("rule-system-context", 3, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.PadOuterX)) return;
        ImGui.TableNextRow();
        void Quantities(int column, string label, IEnumerable<string> fields)
        {
            ImGui.TableSetColumnIndex(column);
            ImGui.BeginChild("context-" + column, new Vec2(0, 150), ImGuiChildFlags.Borders);
            EditorStyle.Text(HudTheme.Muted, label);
            var all = fields.Select(model.Variable).Where(v => v != null).ToArray();
            foreach (var v in all.Where(v => SystemVocabulary.Endogenous(v.Kind))) {
                if (ImGui.Selectable(v.Name + "##ctx" + column + v.Id)) ShowSystemModel(v.Id);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(SystemVocabulary.KindName(v.Kind) + (v.Unit.Length > 0 ? " · " + v.Unit : "")
                    + "\nTermelik: " + string.Join(", ", model.Writers(v.Id).Where(p => p.Id != rule.Id).Select(p => p.Name).DefaultIfEmpty("—"))
                    + "\nHasználják: " + string.Join(", ", model.Readers(v.Id).Where(p => p.Id != rule.Id).Select(p => p.Name).DefaultIfEmpty("—"))
                    + "\nKattintás: megnyitás a rendszertérképen");
                ImGui.SameLine(); EditorStyle.Text(HudTheme.Muted, SystemVocabulary.KindName(v.Kind).ToLowerInvariant());
            }
            var fixedData = all.Where(v => !SystemVocabulary.Endogenous(v.Kind) && v.Kind != SystemVariableKind.Presentation && v.Id != "rules.behaviors").ToArray();
            if (fixedData.Length > 0) { ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, "Adat és szerkezet: " + string.Join(", ", fixedData.Select(v => v.Name))); ImGui.PopTextWrapPos(); }
            if (all.Length == 0) EditorStyle.Text(HudTheme.Muted, "Nincs katalogizált mennyiség.");
            ImGui.EndChild();
        }
        Quantities(0, "MIBŐL SZÁMOL?", rule.Reads);
        ImGui.TableSetColumnIndex(1);
        ImGui.BeginChild("context-rule", new Vec2(0, 150), ImGuiChildFlags.Borders);
        EditorStyle.Text(HudTheme.AmberAccent, "EZT SZERKESZTED"); EditorStyle.Title(rule.Name);
        var stocks = model.StocksChangedBy(rule.Id);
        ImGui.PushTextWrapPos();
        ImGui.TextUnformatted(stocks.Count > 0 ? "Áramlás: " + string.Join(", ", stocks.Select(s => s.Name)) + " készletét változtatja."
            : rule.Execution == GameRuleExecution.Presentation ? "Megjelenítés: nem hat vissza a modellre." : "Segédszámítás vagy döntés: készletet közvetlenül nem változtat.");
        if (model.ReadsOwnStock(rule.Id)) EditorStyle.Text(HudTheme.Muted, "A változás a készlet pillanatnyi értékétől is függ.");
        ImGui.PopTextWrapPos();
        if (ImGui.Button("Gráf megnyitása")) SetWorkshopPhase(WorkshopPhase.Edit, rule);
        ImGui.EndChild();
        Quantities(2, "MIT VÁLTOZTAT?", rule.Writes);
        ImGui.EndTable();

        EditorStyle.Section("Visszacsatolási körök");
        var loops = model.LoopsThroughProcess(rule.Id);
        if (loops.Count == 0) ImGui.TextWrapped("Ez a folyamat nem zár kört: a hatása nem tér vissza a bemeneteihez.");
        else ImGui.TextWrapped($"{loops.Count} kör halad át ezen a szabályon; a módosítás hatása ezeken keresztül visszaér. A legrövidebbek:");
        foreach (var loop in loops.Take(4)) {
            if (ImGui.Selectable(SystemModelView.Chain(model, loop) + "##rl" + loop.Key)) ShowSystemLoop(loop);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Megnyitás a rendszertérképen");
        }
    }
}
