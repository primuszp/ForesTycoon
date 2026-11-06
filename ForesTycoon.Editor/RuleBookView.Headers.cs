using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
namespace ForesTycoon.Editor;
internal sealed partial class RuleBookView
{
    /// <summary>A state rule's header: title and chips on the left, the rule's own commands on the right.</summary>
    private void RuleHeader(GameWorld world, RuleView rule, ref bool changed, ref bool delete)
    {
        var facts = WorldRuleFacts(world, rule);
        string reset = "Visszaállítás a mintára", copy = "Másolat", remove = "Törlés";
        var style = ImGui.GetStyle();
        float actions = EditorStyle.ButtonWidth(copy) + EditorStyle.ButtonWidth(remove) + style.ItemSpacing.X
            + (rule.Template.Length > 0 ? EditorStyle.ButtonWidth(reset) + style.ItemSpacing.X : 0);
        float top = ImGui.GetCursorPosY(), edge = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(200, ImGui.GetContentRegionAvail().X - actions - 16));
        CardHeader(rule.Name, "Állapotot változtat", rule.Enabled,
            (ScheduleChip(rule.Schedule), EditorStyle.OperationKind, facts.When),
            (ObjectDisplay(rule.Scope, rule.Scope), EditorStyle.InputKind, "Ezen az objektumon fut: " + (Objects.Find(rule.Scope)?.Help ?? rule.Scope)),
            (string.IsNullOrWhiteSpace(rule.Group) ? null : rule.Group, HudTheme.AmberAccent, "Saját csoport a szabálylistában."));
        ImGui.PopTextWrapPos();
        float bottom = ImGui.GetCursorPosY();
        ImGui.SetCursorPos(new Vec2(edge - actions, top));
        if (rule.Template.Length > 0) {
            if (EditorStyle.QuietButton(reset, "A szabály hatásai a minta szerinti változatra állnak vissza. Visszavonható.")) ResetVisualRule(world, rule.Id);
            ImGui.SameLine();
        }
        if (ImGui.Button(copy)) {
            var clone = ruleBook.Copy(rule);
            selectedBookRule = ruleBook.IndexOf(clone); changed = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Kikapcsolt másolat a lista végén.");
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, HudTheme.Bad);
        if (ImGui.Button(remove)) delete = true;
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A szabály törlése. Ctrl+Z visszahozza.");
        ImGui.SetCursorPosY(Math.Max(bottom, ImGui.GetCursorPosY()));
        if (!rule.Enabled) EditorStyle.Text(HudTheme.AmberAccent, facts.Off);
    }

    private CardFacts WorldRuleFacts(GameWorld world, RuleView rule)
    {
        var all = RuleBookRules(world); int self = ruleBook.IndexOf(rule);
        var uses = FieldUses(all);
        // Include directly authored sources too, including custom fields absent from the built-in object schema.
        var authored = rule.Effects.Where(e => e.Influence != null).SelectMany(e => e.Influence.Terms
            .SelectMany(t => GraphInputs(rule.Scope, e, t).Select(input => input.Source).Append(t.AmountField)))
            .Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => FieldTitle(rule.Scope, f));
        var condition = rule.ConditionGraph;
        var equations = rule.Effects.Select(effect => effect.Computation).Where(graph => graph != null)
            .Concat(condition == null ? Array.Empty<BehaviorGraph>() : new[] { condition });
        var equationReads = equations.SelectMany(ResponseRuleDescription.ConnectedNodes)
            .Where(n => n.Operation is BehaviorOperation.Input or BehaviorOperation.NamedValue).Select(n => FieldTitle(rule.Scope, n.Key));
        var reads = authored.Concat(equationReads).Concat(uses.Where(u => u.Readers.Any(r => r.Rule == self)).Select(u => KeyDisplay(u.Key))).Distinct().ToArray();
        var targets = rule.Effects.SelectMany(e => e.Mode == "flow" ? new[] { e.Target, e.To } : new[] { e.Target }).Where(f => f.Length > 0).Select(f => FieldTitle(rule.Scope, f)).Distinct();
        var consumers = uses.Where(u => u.Writers.Any(w => w.Rule == self)).SelectMany(u => u.Readers.Where(r => r.Rule != self && r.Enabled).Select(r => r.Name)).Distinct().ToArray();
        string when = rule.Schedule switch { "passage" => "Jármű áthaladásakor", "month" => "Minden erdőhónap végén", "growth" => "A fa növekedési rátájának frissítésekor", "wildlife" => "Minden állati lépésben; a lépés időegysége másodperc", "population" => "Állománykezeléskor, élőhelyfelmérés után; másodpercben", "birth" => "Létrehozáskor, csak az új egyedeken; egyszeri kezdőállapot", _ => "0,5 játékmásodpercenként, szeletelt feldolgozással" };
        return new("Állapotot változtat", when, reads, string.Join("; ", targets),
            consumers.Length > 0 ? string.Join("; ", consumers) : "Az érintett objektum állapotát használó folyamatok, a megjelenítés és a mentés.", rule.Enabled,
            "Kikapcsolva nem hajtja végre a hatásait. A korábbi változtatásokat nem vonja vissza; más szabályok tovább hathatnak az objektumra.");
    }
}
