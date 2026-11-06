using System;
using System.Collections.Generic;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
namespace ForesTycoon.Editor;
internal sealed class RuleCardWidgets(RuleWorkspaceUiState state)
{
    internal const float ListWidth = 300;
    internal Dictionary<string, Vec2> Anchors { get; } = new();
    private ref bool scrollRuleCard => ref state.ScrollRuleCard;
    internal bool Entry(string id, string name, string kind, bool selected, bool enabled, Action select, Action<bool> enable, string tooltip)
    {
        ImGui.PushID(id);
        bool on = enabled;
        if (EditorStyle.Toggle("##enabled", ref on)) enable(on);
        Anchors["enable:" + id] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        ImGui.SameLine(0, 8);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (ImGui.GetFrameHeight() - ImGui.GetFrameHeight() * 0.62f) / 2);
        ImGui.PushStyleColor(ImGuiCol.Text, enabled ? HudTheme.Parchment : HudTheme.Muted);
        bool picked = ImGui.Selectable(name, selected, ImGuiSelectableFlags.None, new Vec2(0, ImGui.GetFrameHeight() - 4));
        ImGui.PopStyleColor();
        if (picked) select();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        ImGui.PopID();
        if (selected && scrollRuleCard) { ImGui.SetScrollHereY(.4f); scrollRuleCard = false; }
        return on != enabled;
    }
}
