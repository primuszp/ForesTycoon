using System;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

/// <summary>
/// The object library: the kinds of world objects the game exposes to rules, discovered from its code (see
/// ForesTycoon/World/RuleEntities.cs). A kind inherits its parent's fields; the tree shows where each field comes from,
/// what is attached to the object, what a rule may do to it, and which rules already run on it.
/// </summary>
internal sealed partial class RuleEditorView
{
    private static RuleSchema Objects => GameWorld.RuleObjects;

    /// <summary>One node of the hierarchy; concrete kinds are selectable, abstract ones only when <paramref name="abstractSelectable"/>.</summary>
    private static void ObjectNode(RuleEntitySchema e, string selected, Action<string> select, bool abstractSelectable)
    {
        var children = Objects.Children(e.Id).ToArray();
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth
            | (children.Length == 0 ? ImGuiTreeNodeFlags.Leaf : 0) | (selected == e.Id ? ImGuiTreeNodeFlags.Selected : 0);
        ImGui.PushStyleColor(ImGuiCol.Text, e.Abstract ? HudTheme.Muted : HudTheme.Parchment);
        bool open = ImGui.TreeNodeEx(e.Name + "##obj" + e.Id, flags);
        ImGui.PopStyleColor();
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen() && (abstractSelectable || !e.Abstract || RuleEngine.Scope(e.Id) != null)) select(e.Id);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{e.Help}\n{e.Fields.Length} mező · {e.Relations.Length} kapcsolat · {e.Actions.Length} művelet");
        if (!open) return;
        foreach (var c in children) ObjectNode(c, selected, select, abstractSelectable);
        ImGui.TreePop();
    }
}
