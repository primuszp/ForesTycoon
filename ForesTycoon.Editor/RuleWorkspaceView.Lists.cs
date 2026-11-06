using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
namespace ForesTycoon.Editor;
internal sealed partial class RuleWorkspaceView
{
    /// <summary>The page that lists a common rule key ("behavior:…", "controller:…", "stock:…", "flow:…", "library").</summary>
    private EditorPage CommonPage(string key)
    {
        if (key == "library") return EditorPage.Library;
        if (key.StartsWith("controller", StringComparison.Ordinal)) return EditorPage.Controllers;
        if (key.StartsWith("stock:", StringComparison.Ordinal) || key.StartsWith("flow:", StringComparison.Ordinal)) return EditorPage.Stocks;
        var graph = behaviors.Document.Graphs.FirstOrDefault(g => "behavior:" + g.Id == key);
        return graph != null && !EcologyResponseProgram.IsPureKind(graph.Kind) ? EditorPage.Processes : EditorPage.Responses;
    }

    /// <summary>Opens the graph bound to a hook, creating the type-level graph when the project has none yet.</summary>
    internal void OpenHook(BehaviorHook hook)
    {
        var graph = behaviors.Document.Graphs.FirstOrDefault(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null);
        if (graph == null) { behaviors.SelectHook(hook); graph = behaviors.Document.Graphs.Last(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null); }
        ShowCommonRule("behavior:" + graph.Id);
    }

    internal void OpenController(string kind)
    {
        var controller = behaviors.Document.Controllers.FirstOrDefault(c => c.Kind == kind && c.ObjectId == null);
        ShowCommonRule(controller != null ? "controllerId:" + controller.Id : "controller:" + kind);
    }

    // ── Pages listing the common rules: a list on the left, the selected item's workspace on the right ──
    private const float ListWidth = RuleCardWidgets.ListWidth;
    private ref string commonFilter => ref ui.Rules.CommonFilter;

    private void CommonListPage(GameWorld world, Func<GameWorld, string, string> list, string empty)
    {
        EditorStyle.BeginPanel("common-list", new Vec2(ListWidth, 0));
        string filter = EditorStyle.Search("##common-filter", "Keresés…", ref commonFilter);
        ImGui.Spacing();
        // The list returns its first item, opened when nothing of this page is selected yet.
        string first = list(world, filter);
        EditorStyle.EndPanel();
        if ((commonSelection == null || CommonPage(commonSelection) != page) && first != null) ShowCommonRule(first);
        ImGui.SameLine();
        if (commonSelection != null && CommonPage(commonSelection) == page) { DrawCommonSelection(world); return; }
        EditorStyle.BeginPanel("common-empty", Vec2.Zero);
        EditorStyle.Text(HudTheme.Muted, empty);
        EditorStyle.EndPanel();
    }

    private static bool Matches(string filter, params string[] texts) => filter.Length == 0 || texts.Any(t => Contains(t, filter));

    internal static string ResponseFamily(string kind) => kind switch {
        "site" => "Termőhely és víz",
        "competition" or "competitionPair" or "resource" => "Verseny, fény és erőforrás",
        "regeneration" => "Felújulás",
        "initialTree" => "Kezdő állomány",
        "environment" => "Környezet és időjárás",
        "vehicleResponse" => "Járművek",
        "gameplay" => "Játékmenet és gazdaság",
        _ when LogisticsRules.IsKind(kind) => "Logisztika · " + RuleWorkshopModel.KindName(kind),
        _ => kind
    };

    private string ResponseList(GameWorld world, string filter)
    {
        string first = null;
        var groups = behaviors.Document.Graphs.Where(g => EcologyResponseProgram.IsPureKind(g.Kind) && Matches(filter, g.Name, g.Hook))
            .GroupBy(g => ResponseFamily(g.Kind)).OrderBy(g => g.Key, StringComparer.CurrentCulture);
        foreach (var group in groups) {
            var members = group.ToList();
            if (filter.Length > 0 || members.Any(g => commonSelection == "behavior:" + g.Id)) ImGui.SetNextItemOpen(true, filter.Length > 0 ? ImGuiCond.Always : ImGuiCond.Once);
            bool open = ImGui.TreeNodeEx($"{group.Key}  ({members.Count(g => !g.Disabled)}/{members.Count})##family-{group.Key}",
                ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.SpanAvailWidth);
            first ??= "behavior:" + members[0].Id;
            if (!open) continue;
            foreach (var g in members)
                RuleCardEntry("behavior:" + g.Id, g.Name + (g.ObjectId.HasValue ? " · #" + g.ObjectId : ""), "Értéket számol", commonSelection == "behavior:" + g.Id, !g.Disabled,
                    () => ShowCommonRule("behavior:" + g.Id), enabled => RequestResponseAvailability(g, enabled),
                    g.Disabled ? "Fix eredményt ad. Kattintás: az összefüggés visszakapcsolása." : "Kikapcsoláskor te választod ki a helyettesítő eredményt.");
            ImGui.TreePop();
        }
        if (first == null) EditorStyle.Text(HudTheme.Muted, "Nincs találat.");
        return first;
    }

    private IEnumerable<BehaviorGraph> ProcessGraphs() => behaviors.Document.Graphs.Where(g => !EcologyResponseProgram.IsPureKind(g.Kind)
        && (!g.Hook.StartsWith("tuning.", StringComparison.Ordinal) || g.Nodes.Any(n => n.Operation != BehaviorOperation.Native)));

    private string ProcessList(GameWorld world, string filter)
    {
        if (ImGui.Button("+ Új folyamatkapcsolat", new Vec2(-1, 0))) ImGui.OpenPopup("new-process");
        if (ImGui.BeginPopup("new-process")) {
            EditorStyle.Text(HudTheme.Muted, "Melyik folyamatba kössön?");
            foreach (var hook in WorldBehaviorPolicy.Hooks.Where(h => !h.Id.StartsWith("tuning.", StringComparison.Ordinal)))
                if (ImGui.Selectable(hook.Name + " · " + hook.Kind)) {
                    behaviors.SelectHook(hook);
                    ShowCommonRule("behavior:" + behaviors.Document.Graphs.Last(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null).Id);
                }
            ImGui.EndPopup();
        }
        ImGui.Spacing();
        string first = null;
        foreach (var g in ProcessGraphs().Where(g => Matches(filter, g.Name, g.Hook))) {
            string key = "behavior:" + g.Id; first ??= key;
            if (ImGui.Selectable(g.Name + "##common-" + key, commonSelection == key)) ShowCommonRule(key);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(g.Hook + " · " + g.Kind);
        }
        if (first == null) EditorStyle.Text(HudTheme.Muted, filter.Length > 0 ? "Nincs találat." : "Még nincs folyamatkapcsolat.");
        return first;
    }

    private string ControllerList(GameWorld world, string filter)
    {
        string first = null;
        var model = behaviors.Document;
        foreach (var c in model.Controllers.Where(c => Matches(filter, c.Name, c.Kind))) {
            string key = "controllerId:" + c.Id; first ??= key;
            if (ImGui.Selectable(c.Name + (c.ObjectId.HasValue ? " · #" + c.ObjectId : "") + "##common-" + key, commonSelection == key)) ShowCommonRule(key);
        }
        var missing = new[] { "forwarder", "processor", "truck" }.Where(kind => !model.Controllers.Any(c => c.Kind == kind && c.ObjectId == null)).ToArray();
        if (missing.Length > 0) {
            ImGui.Spacing(); EditorStyle.Section("Új munkaciklus");
            foreach (string kind in missing) {
                string key = "controller:" + kind;
                if (ImGui.Selectable("+ " + RuleWorkshopModel.KindName(kind) + " munkaciklusa##common-" + key, commonSelection == key)) ShowCommonRule(key);
            }
        }
        return first;
    }

    private string StockList(GameWorld world, string filter)
    {
        float half = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2;
        if (ImGui.Button("+ Készlet", new Vec2(half, 0))) ShowCommonRule("stock:" + dynamicsEditor.CreateStock());
        ImGui.SameLine();
        ImGui.BeginDisabled(dynamicsEditor.Stocks.Count == 0);
        if (ImGui.Button("+ Áramlás", new Vec2(half, 0))) ShowCommonRule("flow:" + dynamicsEditor.CreateFlow());
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Áramlás két készlet között; előbb készlet kell.");
        string first = null;
        void Item(string key, string label) {
            first ??= key;
            if (ImGui.Selectable(label + "##common-" + key, commonSelection == key)) ShowCommonRule(key);
        }
        var stocks = dynamicsEditor.Stocks.Where(s => Matches(filter, s.Name, s.Id)).ToList();
        var flows = dynamicsEditor.Flows.Where(f => Matches(filter, f.Name, f.Id)).ToList();
        if (stocks.Count > 0) { EditorStyle.Section("Készletek"); foreach (var s in stocks) Item("stock:" + s.Id, s.Name); }
        if (flows.Count > 0) { EditorStyle.Section("Áramlások"); foreach (var f in flows) Item("flow:" + f.Id, f.Name); }
        return first;
    }
}
