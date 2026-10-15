using System;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    private readonly System.Collections.Generic.Dictionary<string, string> fieldPickerObjects = new();
    private static string Words(string text) => System.Text.RegularExpressions.Regex.Replace(text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..], "([a-z])([A-Z])", "$1 $2");
    private static string FieldObject(string scope, string field) => field.Contains('.') ? field[..field.IndexOf('.')] : scope;
    private static string FieldDisplay(string scope, string field) => (field.StartsWith("tuning.", StringComparison.Ordinal) ? GameTuning.Spec(field[7..])?.Name : null) ?? SchemaField(scope, field)?.Display
        ?? Objects.Find(scope)?.Actions.FirstOrDefault(a => a.Name == field)?.Display
        ?? Words(field[(field.LastIndexOf('.') + 1)..]);
    private static string FieldTitle(string scope, string field) => Words(FieldObject(scope, field)) + " · " + FieldDisplay(scope, field);

    private bool ObjectFieldPicker(string label, string scope, string current, string[] choices, Action<string> set, bool empty = false)
    {
        bool changed = false;
        ImGui.PushID(label);
        ImGui.TextUnformatted(label);
        string key = scope + ":" + ImGui.GetID("##object");
        var groups = choices.Select(f => FieldObject(scope, f)).Distinct().ToArray();
        if (!fieldPickerObjects.TryGetValue(key, out string group) || !groups.Contains(group))
            group = choices.Contains(current) ? FieldObject(scope, current) : groups.FirstOrDefault() ?? scope;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##object", Words(group))) {
            foreach (string g in groups) if (ImGui.Selectable(Words(g), g == group)) group = g;
            ImGui.EndCombo();
        }
        fieldPickerObjects[key] = group;
        ImGui.SetNextItemWidth(-1);
        string preview = current.Length == 0 || FieldObject(scope, current) != group ? "Válassz tulajdonságot…" : FieldTitle(scope, current);
        if (ImGui.BeginCombo("##field", preview)) {
            if (empty && ImGui.Selectable("Nincs / állandó", current.Length == 0)) { set(""); changed = true; }
            foreach (string f in choices.Where(f => FieldObject(scope, f) == group)) {
                if (ImGui.Selectable(FieldDisplay(scope, f) + "##" + f, f == current)) { set(f); changed = true; }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(f + "\n" + (SchemaField(scope, f)?.Help ?? ""));
            }
            ImGui.EndCombo();
        }
        ImGui.PopID(); return changed;
    }
}
