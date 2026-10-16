using System;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    private readonly System.Collections.Generic.Dictionary<string, string> fieldPickerObjects = new();
    private static string Words(string text) => System.Text.RegularExpressions.Regex.Replace(text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..], "([a-z])([A-Z])", "$1 $2");
    /// <summary>The object a field belongs to: the scope, an attached object (“tile”), or a collection (“trees”, “tile.trees”) for a summary.</summary>
    private static string FieldObject(string scope, string field)
    {
        if (SchemaField(scope, field)?.Collection is { } c) {
            int at = field.LastIndexOf(c.Name + ".", StringComparison.Ordinal);
            if (at >= 0) return field[..(at + c.Name.Length)];
        }
        return field.Contains('.') ? field[..field.IndexOf('.')] : scope;
    }
    private static string FieldDisplay(string scope, string field) => (field.StartsWith("tuning.", StringComparison.Ordinal) ? GameTuning.Spec(field[7..])?.Name : null) ?? SchemaField(scope, field)?.Display
        ?? Objects.Find(scope)?.Actions.FirstOrDefault(a => a.Name == field)?.Display
        ?? Words(field[(field.LastIndexOf('.') + 1)..]);
    private static string FieldTitle(string scope, string field)
    {
        string obj = ObjectDisplay(scope, FieldObject(scope, field)), name = FieldDisplay(scope, field);
        // A collection member's name already says its collection (“Szomszéd csempék › Felszíni víz”).
        return name.StartsWith(obj, StringComparison.Ordinal) ? name : obj + " · " + name;
    }

    /// <summary>
    /// Everything a rule on <paramref name="scope"/> can read, as one system: its own and attached objects' fields, its custom
    /// fields, the world, the project parameters and the stocks of the system model. Nothing here is listed by hand.
    /// </summary>
    private string[] ReadableFields(GameWorld world, RuleScope scope) => scope.Fields.Select(f => f.Name)
        .Concat(RuleBookFields(world).Where(f => f.Scope == scope.Id).Select(f => f.Name))
        .Concat(scope.Id == "world" ? Array.Empty<string>() : GameWorld.RuleWorldFields)
        .Concat(dynamicsEditor.Draft.Stocks.Select(s => "stock." + s.Id))
        .Concat(GameTuning.Specs.Select(s => "tuning." + s.Id)).Distinct().ToArray();

    /// <summary>What a rule on <paramref name="scope"/> can change: every writable field of the object and its custom fields.</summary>
    private string[] WritableFields(GameWorld world, RuleScope scope) => scope.Fields.Where(f => f.Write != null).Select(f => f.Name)
        .Concat(RuleBookFields(world).Where(f => f.Scope == scope.Id).Select(f => f.Name)).Distinct().ToArray();

    /// <summary>The designer's name of the object a field belongs to: the scope itself, an attached object (“Csempe”) or the project parameters.</summary>
    private static string ObjectDisplay(string scope, string obj)
    {
        if (obj == "tuning") return "Játékparaméter";
        if (obj == "saját") return "Saját mező";
        if (obj == "stock") return "Készlet";
        if (obj == "world" && scope != "world") return Objects.Find("world")?.Name ?? "Világ";
        var entity = Objects.Find(scope);
        if (obj == scope) return entity?.Name ?? RuleEngine.Scope(scope)?.Name ?? Words(scope);
        // A collection of an attached object: “Csempe › Fák a csempén”.
        if (obj.Contains('.') && entity?.Relations.FirstOrDefault(r => r.Name == obj[..obj.IndexOf('.')]) is { } relation)
            return relation.Display + " › " + (Objects.Find(relation.Target)?.Collections.FirstOrDefault(c => c.Name == obj[(obj.IndexOf('.') + 1)..])?.Display ?? Words(obj));
        return entity?.Relations.FirstOrDefault(r => r.Name == obj)?.Display ?? entity?.Collections.FirstOrDefault(c => c.Name == obj)?.Display ?? Words(obj);
    }
    private static string ScopeElement(string scope) => Objects.Find(scope)?.Element is { Length: > 0 } e ? e : RuleEngine.Scope(scope)?.Element ?? "elem";

    private static string FieldUnit(string scope, string field) =>
        field.StartsWith("tuning.", StringComparison.Ordinal) ? GameTuning.Spec(field[7..])?.Unit ?? "" : SchemaField(scope, field)?.Unit ?? "";

    /// <summary>“0–1”, “min. 0 m”: the range a source moves in, for its box on the canvas.</summary>
    private static string FieldRange(string scope, string field)
    {
        string unit = FieldUnit(scope, field);
        if (field.StartsWith("tuning.", StringComparison.Ordinal)) return unit;
        var f = SchemaField(scope, field);
        if (f == null) return unit;
        if (f.Options.Length > 0) return string.Join(" / ", f.Options.Select(o => o.Display));
        string N(double v) => RuleSentence.Number(v);
        string range = double.IsFinite(f.Min) && double.IsFinite(f.Max) ? $"{N(f.Min)}–{N(f.Max)}" : double.IsFinite(f.Min) ? $"min. {N(f.Min)}" : "";
        if (unit == "0–1") return range.Length > 0 ? range : unit;
        return (range + " " + unit).Trim();
    }

    /// <summary>A value with its unit, rounded for reading: “0,78”, “12,5 erdőév”.</summary>
    private static string FieldValue(string scope, string field, double value)
    {
        if (SchemaField(scope, field)?.Options.FirstOrDefault(o => o.Value == value) is { } option) return option.Display;
        string unit = FieldUnit(scope, field);
        return RuleSentence.Number(value) + (unit.Length == 0 || unit == "0–1" ? "" : " " + unit);
    }

    /// <summary>Help of a field in the designer's words, with the code name as a secondary hint.</summary>
    private static string FieldHelp(string scope, string field)
    {
        string help = field.StartsWith("tuning.", StringComparison.Ordinal) ? "Projektparaméter: a Típusok › Játékparaméterek lapon hangolható, több szabály is használhatja."
            : SchemaField(scope, field)?.Help ?? Objects.Find(scope)?.Actions.FirstOrDefault(a => a.Name == field)?.Help ?? "";
        string range = FieldRange(scope, field);
        return FieldTitle(scope, field) + (range.Length > 0 ? $" ({range})" : "") + "\n" + help + "\nKód: " + field;
    }

    /// <summary>Reads rules of <paramref name="scope"/> as Hungarian sentences; parameters show their current value in <paramref name="world"/>.</summary>
    private static RuleSentence Sentences(GameWorld world, string scope) => new(ScopeElement(scope), field => {
        if (field.StartsWith("tuning.", StringComparison.Ordinal) && GameTuning.Spec(field[7..]) is { } spec)
            return new(spec.Name, spec.Unit, Current: world?.Tuning[spec.Key]);
        var f = SchemaField(scope, field);
        return new(FieldDisplay(scope, field), f?.Unit ?? "", f?.True, f?.False) {
            Options = f?.Options.Select(o => (o.Value, o.Display)).ToArray() ?? Array.Empty<(double, string)>() };
    });

    private bool ObjectFieldPicker(string label, string scope, string current, string[] choices, Action<string> set, bool empty = false)
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
