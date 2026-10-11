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

    private SystemModelAnalysis SystemModel()
    {
        var dynamics = dynamicsEditor.Draft;
        var formulas = behaviors.Document.Graphs.Where(g => g.Nodes.Any(n => n.Operation == BehaviorOperation.NamedValue)).ToArray();
        string key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(currentRules.Catalog) + "|"
            + new RuleProject { Dynamics = dynamics }.ToJson().GetHashCode() + "|"
            + string.Join(";", formulas.Select(g => g.Id + ":" + g.Name + ":" + g.Hook + ":" + g.Kind + ":" + string.Join(",", g.Nodes.Where(n => n.Operation == BehaviorOperation.NamedValue).Select(n => n.Key))))
            + "|" + string.Join(",", dynamics.Stocks.Select(s => TopicOf("stock." + s.Id, 5)));
        if (systemModel != null && key == systemModelKey) return systemModel;
        systemModelKey = key;
        return systemModel = RuleWorkshopModel.SystemModel(currentRules.Catalog, dynamics, formulas,
            id => id.StartsWith("flow.") ? FlowTopic(dynamics.Flows.Single(f => "flow." + f.Id == id)) : TopicOf(id, 5));
    }

    internal void ShowSystemModel(string variable = null)
    {
        workshopTool = WorkshopTool.System; workshopPhase = WorkshopPhase.Understand;
        if (variable != null) systemView.Focus(variable);
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

    private void OpenSystemVariable(SystemVariable variable)
    {
        if (variable.Id.StartsWith("stock.", StringComparison.Ordinal)) OpenDynamic(variable.Id[6..], false);
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
