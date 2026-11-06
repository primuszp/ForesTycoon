using System;
using System.Linq;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;
namespace ForesTycoon.Editor;
internal static class RuleCardPresentation
{
    /// <summary>What a card tells about its rule: when it runs, what it reads and changes, who uses the result.</summary>
    internal sealed record CardFacts(string Kind, string When, string[] Reads, string Targets, string Consumers, bool Enabled, string Off);

    /// <summary>The card's title and a row of chips: the kind of rule, whether it runs, and any further facts.</summary>
    internal static void CardHeader(string name, string kind, bool enabled, params (string Text, Vec4 Colour, string Tip)[] chips)
    {
        EditorStyle.Title(name, true);
        EditorStyle.Chip(kind, EditorStyle.PresentationKind, "card-kind");
        ImGui.SameLine(0, 6);
        EditorStyle.Chip(enabled ? "aktív" : "kikapcsolva", enabled ? HudTheme.Good : HudTheme.Muted, "card-state");
        for (int i = 0; i < chips.Length; i++) {
            if (string.IsNullOrEmpty(chips[i].Text)) continue;
            ImGui.SameLine(0, 6);
            EditorStyle.Chip(chips[i].Text, chips[i].Colour, "card-chip" + i);
            if (chips[i].Tip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(chips[i].Tip);
        }
    }

    internal static void FactsTable(CardFacts f)
    {
        if (!EditorStyle.BeginFacts("rule-card-facts")) return;
        EditorStyle.Fact("Mikor fut?", f.When);
        string inputs = f.Kind == "Értéket számol" && !f.Enabled ? "A megadott fix értékből; a gráf forrásait most nem olvassa."
            : f.Reads.Length == 0 ? "Állandókból; nincs bekötött forrás."
            : string.Join("; ", f.Reads.Take(6)) + (f.Reads.Length > 6 ? $"; +{f.Reads.Length - 6} további forrás a gráfon" : "");
        EditorStyle.Fact("Miből számol?", inputs);
        if (ImGui.IsItemHovered() && f.Reads.Length > 0) ImGui.SetTooltip(string.Join("\n", f.Reads));
        EditorStyle.Fact("Mire hat?", f.Targets);
        EditorStyle.Fact("Ki használja?", f.Consumers);
        EditorStyle.Fact("Kikapcsolva", f.Off, f.Enabled ? null : HudTheme.AmberAccent);
        ImGui.EndTable();
    }

    /// <summary>Header and folded facts of a response or process card.</summary>
    internal static void Facts(string name, string kind, string when, string[] reads, string targets, string consumers, bool enabled, string off)
    {
        var facts = new CardFacts(kind, when, reads, targets, consumers, enabled, off);
        CardHeader(name, kind, enabled);
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Mikor fut, mire hat, ki használja?##rule-details")) FactsTable(facts);
        else if (!enabled) EditorStyle.Text(HudTheme.AmberAccent, off);
        ImGui.Separator();
    }

}
