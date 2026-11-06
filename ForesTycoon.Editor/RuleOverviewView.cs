using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

/// <summary>
/// Which rules read and write each field of the object model, in run order. Two rules writing the same field are
/// flagged: the later one sees (or, when it sets the value, overwrites) the earlier one's result. Native causes come
/// from the [RuleCause] declarations next to the fields the game computes itself.
/// </summary>
internal sealed class RuleOverviewView(RuleBookSession book, RuleWorkspaceUiState state, Action<int> openRule, Action openOverview)
{
    private ref bool overviewOnlyShared => ref state.OverviewOnlyShared;


    /// <summary>Under the rule's header: one line when other rules write the same fields; the run order is in its tooltip.</summary>
    internal void DrawSharedWriters(RuleView rule)
    {
        int self = book.IndexOf(rule);
        var shared = book.FieldUses().Where(u => u.Writers.Any(w => w.Rule == self) && u.Writers.Any(w => w.Rule != self && w.Enabled)).ToArray();
        if (shared.Length == 0) return;
        var orders = shared.Select(use => $"{KeyDisplay(use.Key)}: " + string.Join("; ", use.Writers.Where(w => w.Enabled || w.Rule == self).OrderBy(w => w.Rule)
            .Select(w => $"{w.Rule + 1}. {(w.Rule == self ? "ez a szabály" : w.Name)} ({ScheduleLabel(w.Schedule)})")));
        EditorStyle.Text(HudTheme.AmberAccent, $"(!) {shared.Length} célmezőt más szabály is ír: {string.Join(", ", shared.Select(u => KeyDisplay(u.Key)))} · futási rend »");
        if (ImGui.IsItemClicked()) openOverview();
        if (ImGui.IsItemHovered()) {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(string.Join("\n", orders) + "\n\nAzonos indításnál listasorrendben futnak; külön eseményeknél a tényleges időrend dönt.\nKattintás: Mezőhasználat.");
        }
    }

    internal void Draw()
    {
        EditorStyle.BeginPanel("rule-overview", Vec2.Zero);
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, "Az azonos eseményre vagy időlépésre induló szabályok listasorrendben futnak. " +
            "Az áthaladási és időszakos szabályok a saját indításukkor futnak; a későbbi végrehajtás az addig kialakult állapotot látja.");
        var uses = book.FieldUses();
        int shared = uses.Count(u => u.Shared);
        if (shared > 0) EditorStyle.Text(HudTheme.AmberAccent, $"Figyelem: {shared} mezőt több bekapcsolt szabály is ír.");
        else EditorStyle.Text(HudTheme.Good, "Minden mezőt legfeljebb egy bekapcsolt szabály ír.");
        ImGui.Checkbox("Csak a több szabály által írt mezők", ref overviewOnlyShared);
        ImGui.PopTextWrapPos();
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (ImGui.BeginTable("field-uses", 4, flags)) {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Objektum · tulajdonság", ImGuiTableColumnFlags.WidthStretch, 1.1f);
            ImGui.TableSetupColumn("Írja (futási sorrendben)", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("Olvassa", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("A játék maga számolja", ImGuiTableColumnFlags.WidthStretch, 1.3f);
            ImGui.TableHeadersRow();
            foreach (var use in uses.Where(u => !overviewOnlyShared || u.Shared)) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.PushTextWrapPos(0);
                EditorStyle.Text(use.Shared ? HudTheme.AmberAccent : HudTheme.Parchment, (use.Shared ? "(!) " : "") + KeyDisplay(use.Key));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(FieldHelp(use.Entity, use.Field));
                ImGui.PopTextWrapPos();
                ImGui.TableNextColumn();
                foreach (var w in use.Writers.OrderBy(w => w.Rule)) RuleLink(w.Rule, $"{w.Rule + 1}. {w.Name} · {ScheduleLabel(w.Schedule)} · {WriteMode(w.Mode)}", w.Enabled);
                if (use.Writers.Count == 0) EditorStyle.Text(HudTheme.Muted, "—");
                ImGui.TableNextColumn();
                foreach (var r in use.Readers.OrderBy(r => r.Rule)) RuleLink(r.Rule, $"{r.Rule + 1}. {r.Name}", r.Enabled);
                if (use.Readers.Count == 0) EditorStyle.Text(HudTheme.Muted, "—");
                ImGui.TableNextColumn();
                ImGui.PushTextWrapPos(0);
                foreach (string n in use.Native) EditorStyle.Text(HudTheme.Muted, n);
                ImGui.PopTextWrapPos();
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();

        void RuleLink(int index, string text, bool enabled)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, enabled ? HudTheme.Parchment : HudTheme.Muted);
            if (ImGui.Selectable(text + (enabled ? "" : " (ki)") + "##link" + index + text)) openRule(index);
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Kattintás: a szabály megnyitása.");
        }
    }
}
