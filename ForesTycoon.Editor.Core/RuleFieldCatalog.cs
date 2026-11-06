#nullable enable
using System;
using System.Linq;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor.Core;
/// <summary>Schema-derived field labels, units, ranges and sentences shared by rule pages.</summary>
internal static class RuleFieldCatalog
{
    internal static RuleSchema Objects => SimulationWorld.RuleObjects;
    internal static bool Contains(string? text, string part) => text?.IndexOf(part, StringComparison.CurrentCultureIgnoreCase) >= 0;
    internal static string Words(string text) => System.Text.RegularExpressions.Regex.Replace(text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..], "([a-z])([A-Z])", "$1 $2");
    /// <summary>The object a field belongs to: the scope, an attached object (“tile”), or a collection (“trees”, “tile.trees”) for a summary.</summary>
    internal static string FieldObject(string scope, string field)
    {
        if (scope != "world" && field.StartsWith("world.", StringComparison.Ordinal)) return "world";
        if (SchemaField(scope, field)?.Collection is { } c) {
            int at = field.LastIndexOf(c.Name + ".", StringComparison.Ordinal);
            if (at >= 0) return field[..(at + c.Name.Length)];
        }
        return field.Contains('.') ? field[..field.IndexOf('.')] : scope;
    }
    internal static string FieldDisplay(string scope, string field) => (field.StartsWith("tuning.", StringComparison.Ordinal) ? GameTuning.Spec(field[7..])?.Name : null) ?? SchemaField(scope, field)?.Display
        ?? Objects.Find(scope)?.Actions.FirstOrDefault(a => a.Name == field)?.Display
        ?? Words(field[(field.LastIndexOf('.') + 1)..]);
    internal static string FieldTitle(string scope, string field)
    {
        string obj = ObjectDisplay(scope, FieldObject(scope, field)), name = FieldDisplay(scope, field);
        // A collection member's name already says its collection (“Szomszéd csempék › Felszíni víz”).
        return name.StartsWith(obj, StringComparison.Ordinal) ? name : obj + " · " + name;
    }

    /// <summary>The designer's name of the object a field belongs to: the scope itself, an attached object (“Csempe”) or the project parameters.</summary>
    internal static string ObjectDisplay(string scope, string obj)
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
    internal static string ScopeElement(string scope) => Objects.Find(scope)?.Element is { Length: > 0 } e ? e : RuleEngine.Scope(scope)?.Element ?? "elem";

    internal static string FieldUnit(string scope, string field) =>
        field.StartsWith("tuning.", StringComparison.Ordinal) ? GameTuning.Spec(field[7..])?.Unit ?? "" : SchemaField(scope, field)?.Unit ?? "";

    /// <summary>“0–1”, “min. 0 m”: the range a source moves in, for its box on the canvas.</summary>
    internal static string FieldRange(string scope, string field)
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
    internal static string FieldValue(string scope, string field, double value)
    {
        if (SchemaField(scope, field)?.Options.FirstOrDefault(o => o.Value == value) is { } option) return option.Display;
        string unit = FieldUnit(scope, field);
        return RuleSentence.Number(value) + (unit.Length == 0 || unit == "0–1" ? "" : " " + unit);
    }

    /// <summary>Help of a field in the designer's words, with the code name as a secondary hint.</summary>
    internal static string FieldHelp(string scope, string field)
    {
        string help = field.StartsWith("tuning.", StringComparison.Ordinal) ? "Projektparaméter: a Típusok › Játékparaméterek lapon hangolható, több szabály is használhatja."
            : SchemaField(scope, field)?.Help ?? Objects.Find(scope)?.Actions.FirstOrDefault(a => a.Name == field)?.Help ?? "";
        string range = FieldRange(scope, field);
        return FieldTitle(scope, field) + (range.Length > 0 ? $" ({range})" : "") + "\n" + help + "\nKód: " + field;
    }

    /// <summary>Reads rules of <paramref name="scope"/> as Hungarian sentences; parameters show their current value in <paramref name="world"/>.</summary>
    internal static RuleSentence Sentences(SimulationWorld? world, string scope) => new(ScopeElement(scope), field => {
        if (field.StartsWith("tuning.", StringComparison.Ordinal) && GameTuning.Spec(field[7..]) is { } spec)
            return new(spec.Name, spec.Unit, Current: world?.Tuning[spec.Key]);
        var f = SchemaField(scope, field);
        return new(FieldDisplay(scope, field), f?.Unit ?? "", f?.True, f?.False) {
            Options = f?.Options.Select(o => (o.Value, o.Display)).ToArray() ?? Array.Empty<(double, string)>() };
    }, scope == "passage" ? "áthaladásonként" : "lépésenként");

    /// <summary>A scope field or relation field (“tile.waterSupply”) of an object kind, from the object model.</summary>
    internal static RuleFieldSchema? SchemaField(string scope, string name)
    {
        var e = Objects.Find(scope);
        if (e == null || name == null) return null;
        var own = e.Fields.FirstOrDefault(f => f.Name == name) ?? e.Summaries.FirstOrDefault(f => f.Name == name);
        if (own != null) return own;
        // Every rule reads the world's fields as world.<field>.
        if (name.StartsWith("world.", StringComparison.Ordinal) && scope != "world") return SchemaField("world", name[6..]);
        foreach (var r in e.Relations)
            if (name.StartsWith(r.Name + ".", StringComparison.Ordinal))
            {
                var target = Objects.Find(r.Target); string rest = name[(r.Name.Length + 1)..];
                return target?.Fields.FirstOrDefault(f => f.Name == rest) ?? target?.Summaries.FirstOrDefault(f => f.Name == rest);
            }
        // A field of a collection's members (“neighbours.surfaceWater”): where a flow can be shared out.
        foreach (var c in e.Collections)
            if (name.StartsWith(c.Name + ".", StringComparison.Ordinal) && Objects.Find(c.Target)?.Fields.FirstOrDefault(f => f.Name == name[(c.Name.Length + 1)..]) is { } member)
                return member with { Display = c.Display + " › " + member.Display };
        return null;
    }
    internal static string KeyDisplay(string key)
    {
        int dot = key.IndexOf('.');
        if (dot < 0) return key;
        string entity = key[..dot], field = key[(dot + 1)..];
        return (Objects.Find(entity)?.Name ?? entity) + " · " + FieldDisplay(entity, field);
    }

    internal static string WriteMode(string mode) => mode switch { "set" => "felülírja", "approach" => "közelíti", "action" => "végrehajtja", _ => "változtatja" };
    internal static string ScheduleLabel(string schedule) => ScheduleChip(schedule);
    internal static string ScheduleChip(string schedule) => schedule switch {
        "passage" => "áthaladáskor", "month" => "erdőhónaponként", "growth" => "növekedéskor", "wildlife" => "állati lépésenként",
        "population" => "állománykezeléskor", "birth" => "létrehozáskor", _ => "időszakonként"
    };

}
