using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    private readonly Dictionary<string, HashSet<string>> influencePalette = new();
    private string draggingInfluenceSource, draggingInfluenceRule;
    // Screen anchors also let the native smoke test exercise real mouse gestures, without duplicating layout math.
    internal Dictionary<string, Vec2> InfluenceGraphAnchors { get; } = new();
    internal Dictionary<string, string> InfluenceGraphSourceLabels { get; } = new();

    // Derived from the current curves; reversing a curve also reverses its visible relationship.
    private static int ConnectionDirection(InfluenceTerm term, InfluenceFactor factor)
    {
        if (!term.Enabled || term.AmountField.Length == 0 && term.Amount == 0) return 0;
        // Signed multipliers can reverse the relation over their range: do not claim a single direction then.
        if (term.Factors.Any(f => f.ToCurve().Points.Any(p => p.Y < 0) || f.Shape == "step" && f.OutputHigh < 0)) return 0;
        return factor.ToCurve().Sign * (term.Decrease ? -1 : 1);
    }

    private static string ConnectionLabel(RuleEffect effect, InfluenceTerm term, InfluenceFactor factor)
    {
        if (!term.Enabled) return "Kikapcsolva";
        int direction = ConnectionDirection(term, factor);
        if (direction == 0) return "Tartományfüggő";
        if (effect.Mode == "action") return direction > 0 ? "Kiváltja" : "Gátolja";
        if (effect.Target == "wear" && effect.Mode == "add") return direction < 0 ? "+ Gyorsítja" : "- Lassítja";
        if (effect.Target == "decayProgress" && effect.Mode == "add") return direction > 0 ? "+ Gyorsítja" : "- Lassítja";
        return direction > 0 ? "+ Növeli" : "- Csökkenti";
    }

    private static IEnumerable<(string Source, int Direction, string Label)> GraphInputs(RuleEffect effect, InfluenceTerm term)
    {
        foreach (var factor in term.Factors) yield return (factor.Source, ConnectionDirection(term, factor), ConnectionLabel(effect, term, factor));
        foreach (var condition in term.Conditions) {
            int direction = condition.Comparison is "less" or "atMost" ? -1 : 1;
            yield return (condition.Field, direction, direction > 0 ? "Kiváltja" : "Gátolja");
            if (condition.ValueField.Length > 0) yield return (condition.ValueField, -direction, direction > 0 ? "Gátolja" : "Kiváltja");
        }
    }

    // The canvas mutates the same Influence data as the inspector, never a second graph representation.
    private void DrawInfluenceGraph(WorldRule rule, RuleScope scope, ref bool changed)
    {
        if (!influencePalette.TryGetValue(rule.Id + rule.Scope, out var extra)) influencePalette[rule.Id + rule.Scope] = extra = new();
        if (ImGui.CollapsingHeader("+ Forrás vagy cél hozzáadása")) {
            ObjectFieldPicker("Forrás", rule.Scope, "", scope.Fields.Select(f => f.Name).ToArray(), f => extra.Add(f));
            changed |= ObjectFieldPicker("Cél vagy művelet", rule.Scope, "", scope.Fields.Where(f => f.Write != null).Select(f => f.Name)
                .Concat(scope.Actions.Select(a => a.Name)).ToArray(), field => {
                int index = rule.Effects.FindIndex(e => e.Target == field);
                if (index < 0) {
                    bool action = scope.Actions.Any(a => a.Name == field);
                    rule.Effects.Add(new() { Target = field, Mode = action ? "action" : "add", Influence = new() { PerYear = !action } });
                    index = rule.Effects.Count - 1; rule.UseLegacyRoadOverride = false;
                }
                selectedInfluence = index; selectedTerm = 0;
            });
        }
        EditorStyle.Text(HudTheme.Muted, "Forrásból célra húzás: új kapcsolat. Hatásra húzás: szorzó.");
        var sources = rule.Effects.SelectMany(e => e.Influence.Terms.SelectMany(t => GraphInputs(e, t).Select(i => i.Source)))
            .Concat(extra.OrderBy(s => s)).Distinct().ToArray();
        var terms = rule.Effects.SelectMany((e, ei) => e.Influence.Terms.Select((t, ti) => (Effect: ei, Term: ti, Value: t))).ToArray();
        ImGui.BeginChild("influence-canvas", new Vec2(0, rule.Scope == "stump" ? 275 : 350), ImGuiChildFlags.Borders);
        var draw = ImGui.GetWindowDrawList(); var origin = ImGui.GetCursorScreenPos();
        float width = Math.Max(460, ImGui.GetContentRegionAvail().X);
        float height = Math.Max(rule.Scope == "stump" ? 240 : 320, Math.Max(sources.Length * 74, terms.Length * 78) + 50);
        ImGui.InvisibleButton("canvas", new Vec2(width, height), ImGuiButtonFlags.MouseButtonLeft);
        bool hovered = ImGui.IsItemHovered(); var mouse = ImGui.GetIO().MousePos;
        draw.AddRectFilled(origin, origin + new Vec2(width, height), EditorStyle.Canvas, 5);
        var sourceSize = new Vec2(140, 61); var termSize = new Vec2(150, 61); var targetSize = new Vec2(100, 48);
        Vec2 Source(int i) => origin + new Vec2(8, 30 + i * 74);
        Vec2 Term(int i) => origin + new Vec2(width * .43f - 20, 30 + i * 78);
        Vec2 Target(int i) => origin + new Vec2(width - 108, 58 + i * 85);
        InfluenceGraphAnchors.Clear();
        for (int i = 0; i < sources.Length; i++) InfluenceGraphAnchors["source:" + sources[i]] = Source(i) + sourceSize / 2;
        for (int i = 0; i < rule.Effects.Count; i++) InfluenceGraphAnchors["target:" + rule.Effects[i].Target] = Target(i) + targetSize / 2;
        for (int i = 0; i < terms.Length; i++) InfluenceGraphAnchors[$"effect:{terms[i].Effect}:{terms[i].Term}"] = Term(i) + termSize / 2;
        bool Hit(Vec2 p, Vec2 size) => hovered && mouse.X >= p.X && mouse.Y >= p.Y && mouse.X <= p.X + size.X && mouse.Y <= p.Y + size.Y;
        void Link(Vec2 a, Vec2 b, uint colour, float thickness = 2)
        {
            float offset = Math.Max(18, Math.Abs(b.X - a.X) * .5f);
            EditorStyle.BezierArrow(draw, a, a + new Vec2(offset, 0), b - new Vec2(offset, 0), b, 9, colour, thickness, Vec2.UnitX);
        }
        void Box(Vec2 p, Vec2 size, string title, uint border, string detail = "")
        {
            draw.AddRectFilled(p, p + size, EditorStyle.NodeBody, 5);
            draw.AddRect(p, p + size, border, 5, ImDrawFlags.None, 1.5f);
            draw.PushClipRect(p + new Vec2(4, 0), p + size - new Vec2(4, 0), true);
            draw.AddText(p + new Vec2(6, 7), EditorStyle.TextBright, title);
            if (detail.Length > 0) draw.AddText(p + new Vec2(6, 27), EditorStyle.TextMuted, detail);
            draw.PopClipRect();
        }
        draw.AddText(origin + new Vec2(8, 5), EditorStyle.TextMuted, "FORRÁS");
        draw.AddText(origin + new Vec2(width * .43f - 20, 5), EditorStyle.TextMuted, "HATÁS");
        draw.AddText(origin + new Vec2(width - 108, 5), EditorStyle.TextMuted, "CÉL");
        for (int i = 0; i < terms.Length; i++) {
            var entry = terms[i]; var t = entry.Value;
            uint colour = EditorStyle.U(!t.Enabled ? HudTheme.Muted : t.Decrease ? HudTheme.AmberAccent : HudTheme.Good);
            foreach (var input in GraphInputs(rule.Effects[entry.Effect], t)) {
                int source = Array.IndexOf(sources, input.Source);
                int direction = input.Direction;
                uint inputColour = EditorStyle.U(!t.Enabled ? HudTheme.Muted : direction < 0 ? HudTheme.AmberAccent : direction > 0 ? HudTheme.Good : HudTheme.Info);
                Link(Source(source) + new Vec2(sourceSize.X, sourceSize.Y / 2), Term(i) + new Vec2(-2, termSize.Y / 2), inputColour);
            }
            Link(Term(i) + new Vec2(termSize.X, termSize.Y / 2), Target(entry.Effect) + new Vec2(-6, targetSize.Y / 2), colour,
                selectedInfluence == entry.Effect && selectedTerm == entry.Term ? 3 : 1.5f);
        }
        InfluenceGraphSourceLabels.Clear();
        for (int i = 0; i < sources.Length; i++) {
            var p = Source(i);
            Box(p, sourceSize, FieldDisplay(rule.Scope, sources[i]), EditorStyle.U(EditorStyle.InputKind), Words(FieldObject(rule.Scope, sources[i])));
            var connections = terms.SelectMany(t => GraphInputs(rule.Effects[t.Effect], t.Value).Where(input => input.Source == sources[i])).ToArray();
            string[] labels = connections.Select(c => c.Label).Distinct().ToArray();
            string label = labels.Length == 1 ? labels[0] : labels.Length > 1 ? "Többféle hatás" : "Nincs kapcsolat";
            InfluenceGraphSourceLabels[sources[i]] = label;
            var labelColour = connections.Length > 0 && connections.All(c => c.Direction < 0) ? HudTheme.AmberAccent
                : connections.Length > 0 && connections.All(c => c.Direction > 0) ? HudTheme.Good : HudTheme.Muted;
            draw.AddText(p + new Vec2(6, 44), EditorStyle.U(labelColour), label);
            draw.AddCircleFilled(p + new Vec2(sourceSize.X, sourceSize.Y / 2), 5, EditorStyle.U(EditorStyle.InputKind));
            if (Hit(p, sourceSize)) {
                ImGui.SetTooltip(FieldTitle(rule.Scope, sources[i]) + "\n" + sources[i] + "\n" + scope.Fields.FirstOrDefault(f => f.Name == sources[i])?.Help + "\nHúzd egy célmezőre vagy hatásra.");
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { draggingInfluenceSource = sources[i]; draggingInfluenceRule = rule.Id; }
            }
        }
        for (int i = 0; i < terms.Length; i++) {
            var entry = terms[i]; var t = entry.Value; var p = Term(i);
            Box(p, termSize, t.Name, selectedInfluence == entry.Effect && selectedTerm == entry.Term ? EditorStyle.Selection : EditorStyle.GridMinor,
                (t.Enabled ? t.Conditions.Count > 0 ? "Mind teljesüljön" : t.Decrease ? "Csökkenti" : "Növeli" : "Kikapcsolva") + (t.Conditions.Count > 0 ? $" · {t.Conditions.Count}" : ""));
            if (Hit(p, termSize)) {
                ImGui.SetTooltip(t.Name + "\nKattintás: beállítások. Forrás ráhúzása: új szorzó.");
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { selectedInfluence = entry.Effect; selectedTerm = entry.Term; }
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && draggingInfluenceRule == rule.Id && draggingInfluenceSource != null && t.Factors.Count < 8) {
                    t.Factors.Add(new() { Source = draggingInfluenceSource }); selectedInfluence = entry.Effect; selectedTerm = entry.Term; changed = true;
                }
            }
        }
        for (int i = 0; i < rule.Effects.Count; i++) {
            var p = Target(i); var effect = rule.Effects[i];
            Box(p, targetSize, FieldDisplay(rule.Scope, effect.Target), EditorStyle.U(EditorStyle.OutputKind), effect.Mode == "action" ? "művelet" : effect.Mode == "set" ? "érték" : "változás");
            draw.AddCircleFilled(p + new Vec2(0, targetSize.Y / 2), 5, EditorStyle.U(EditorStyle.OutputKind));
            if (Hit(p, targetSize)) {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) selectedInfluence = i;
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && draggingInfluenceRule == rule.Id && draggingInfluenceSource != null && effect.Influence.Terms.Count < 16) {
                    effect.Influence.Terms.Add(new() { Name = FieldTitle(rule.Scope, draggingInfluenceSource) + " hatása", Decrease = effect.Mode != "action", Factors = new() { new() { Source = draggingInfluenceSource } } });
                    selectedInfluence = i; selectedTerm = effect.Influence.Terms.Count - 1; changed = true;
                }
            }
        }
        if (draggingInfluenceSource != null && draggingInfluenceRule == rule.Id) {
            int source = Array.IndexOf(sources, draggingInfluenceSource);
            if (source >= 0) Link(Source(source) + new Vec2(sourceSize.X, sourceSize.Y / 2), mouse, EditorStyle.Selection);
        }
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) || draggingInfluenceRule != rule.Id) { draggingInfluenceSource = null; draggingInfluenceRule = null; }
        ImGui.EndChild();
    }
}
