using System;
using System.Linq;
using ImGuiNET;
namespace ForesTycoon.Editor;
internal sealed class RuleFieldPickerView(RuleWorkspaceUiState state)
{
    private System.Collections.Generic.Dictionary<string, string> fieldPickerObjects => state.FieldPickerObjects;
    internal bool Draw(string label, string scope, string current, string[] choices, Action<string> set, bool empty = false)
    {
        bool changed = false;
        ImGui.PushID(label);
        ImGui.TextUnformatted(label);
        string key = scope + ":" + ImGui.GetID("##object");
        var groups = choices.Select(f => FieldObject(scope, f)).Distinct().ToArray();
        if (!fieldPickerObjects.TryGetValue(key, out string group) || !groups.Contains(group))
            group = choices.Contains(current) ? FieldObject(scope, current) : groups.FirstOrDefault() ?? scope;
        // With a single object the choice is obvious; only the property needs picking.
        if (groups.Length > 1) {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##object", ObjectDisplay(scope, group))) {
                foreach (string g in groups) if (ImGui.Selectable(ObjectDisplay(scope, g) + "##" + g, g == group)) group = g;
                ImGui.EndCombo();
            }
        }
        fieldPickerObjects[key] = group;
        ImGui.SetNextItemWidth(-1);
        string preview = current.Length == 0 || FieldObject(scope, current) != group ? "Válassz tulajdonságot…" : groups.Length > 1 ? FieldTitle(scope, current) : FieldDisplay(scope, current);
        if (ImGui.BeginCombo("##field", preview)) {
            if (empty && ImGui.Selectable("Nincs / állandó", current.Length == 0)) { set(""); changed = true; }
            foreach (string f in choices.Where(f => FieldObject(scope, f) == group)) {
                string range = FieldRange(scope, f);
                if (ImGui.Selectable(FieldDisplay(scope, f) + (range.Length > 0 ? "   (" + range + ")" : "") + "##" + f, f == current)) { set(f); changed = true; }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(FieldHelp(scope, f));
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered() && current.Length > 0) ImGui.SetTooltip(FieldHelp(scope, current));
        ImGui.PopID(); return changed;
    }
}
