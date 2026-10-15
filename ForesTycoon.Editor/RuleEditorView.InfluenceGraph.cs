using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    private string draggingInfluenceSource, draggingInfluenceRule;
    // Click-to-connect: a clicked source waits here until a target or an effect is clicked.
    private string pendingInfluenceSource, pendingInfluenceRule;
    private Vec2 influenceDragStart;
    private int addSourceAs; // 0: new connection to the selected target, 1: multiplier of the selected connection
    // Screen anchors also let the native smoke test exercise real mouse gestures, without duplicating layout math.
    internal Dictionary<string, Vec2> InfluenceGraphAnchors { get; } = new();
    internal Dictionary<string, string> InfluenceGraphSourceLabels { get; } = new();
    internal IReadOnlyList<string> InfluenceSentences { get; private set; } = Array.Empty<string>();

    // Derived from the current curves; reversing a curve also reverses its visible relationship.
    private static int ConnectionDirection(InfluenceTerm term, InfluenceFactor factor)
    {
        if (!term.Enabled || term.AmountField.Length == 0 && term.Amount == 0) return 0;
        // Signed multipliers can reverse the relation over their range: do not claim a single direction then.
        if (term.Factors.Any(f => f.ToCurve().Points.Any(p => p.Y < 0) || f.Shape == "step" && f.OutputHigh < 0)) return 0;
        return factor.ToCurve().Sign * (term.Decrease ? -1 : 1);
    }

    private static string ConnectionLabel(string scope, RuleEffect effect, InfluenceTerm term, InfluenceFactor factor)
    {
        if (!term.Enabled) return "Kikapcsolva";
        int direction = ConnectionDirection(term, factor);
        if (direction == 0) return "Tartományfüggő";
        if (effect.Mode == "action") return direction > 0 ? "Kiváltja" : "Gátolja";
        // The field is the pace of a process (decay, ruts smoothing out): a source speeds it up or slows it down.
        int pace = effect.Mode == "add" ? SchemaField(scope, effect.Target)?.Pace ?? 0 : 0;
        if (pace != 0) return direction == pace ? "+ Gyorsítja" : "- Lassítja";
        return direction > 0 ? "+ Növeli" : "- Csökkenti";
    }

    /// <summary>Every source a connection reads. Gates (conditions, steps) switch the connection on or off; the others scale it.</summary>
    private static IEnumerable<(string Source, int Direction, string Label, bool Gate)> GraphInputs(string scope, RuleEffect effect, InfluenceTerm term)
    {
        foreach (var factor in term.Factors) yield return (factor.Source, ConnectionDirection(term, factor), ConnectionLabel(scope, effect, term, factor), factor.Shape == "step");
        foreach (var condition in term.Conditions) {
            int direction = condition.Comparison is "less" or "atMost" or "notEqual" ? -1 : 1;
            yield return (condition.Field, direction, direction > 0 ? "Kiváltja" : "Gátolja", true);
            if (condition.ValueField.Length > 0) yield return (condition.ValueField, -direction, direction > 0 ? "Gátolja" : "Kiváltja", true);
        }
    }

    /// <summary>“– 0,05 / év”: the strength of a connection as shown on its box.</summary>
    private static string StrengthLabel(GameWorld world, string scope, RuleEffect effect, InfluenceTerm t)
    {
        if (!t.Enabled) return "Kikapcsolva";
        string amount = t.AmountField.Length > 0
            ? (t.AmountField.StartsWith("tuning.", StringComparison.Ordinal) && GameTuning.Spec(t.AmountField[7..]) is { } spec
                ? RuleSentence.Number(world.Tuning[spec.Key]) + " (param.)" : FieldDisplay("", t.AmountField))
            : RuleSentence.Number(t.Amount);
        if (effect.Mode == "action") return "Kiváltja";
        if (effect.Mode == "set") return "= " + amount;
        string unit = FieldUnit(scope, effect.Target);
        return (t.Decrease ? "– " : "+ ") + amount + (unit.Length == 0 || unit == "0–1" ? "" : " " + unit) + (effect.Influence.PerYear ? " / év" : " / lépés");
    }

    /// <summary>Connects a source: to a target it adds a new connection, to an existing connection a multiplier.</summary>
    private static bool ConnectSource(WorldRule rule, string source, int effect, int term, ref int selectedEffect, ref int selectedTermIndex)
    {
        if (effect < 0 || effect >= rule.Effects.Count) return false;
        var influence = rule.Effects[effect].Influence;
        if (term >= 0) {
            if (term >= influence.Terms.Count || influence.Terms[term].Factors.Count >= 8) return false;
            influence.Terms[term].Factors.Add(new() { Source = source });
        } else {
            if (influence.Terms.Count >= 16) return false;
            influence.Terms.Add(new() { Name = FieldDisplay(rule.Scope, source) + " hatása", Decrease = rule.Effects[effect].Mode != "action", Factors = new() { new() { Source = source } } });
            term = influence.Terms.Count - 1;
        }
        selectedEffect = effect; selectedTermIndex = term; rule.UseLegacyRoadOverride = false;
        return true;
    }

    private static void DashedBezier(ImDrawListPtr draw, Vec2 a, Vec2 b, uint colour, float thickness)
    {
        float offset = Math.Max(18, Math.Abs(b.X - a.X) * .5f);
        Vec2 c1 = a + new Vec2(offset, 0), c2 = b - new Vec2(offset, 0);
        Vec2 At(float t) { float u = 1 - t; return u * u * u * a + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * b; }
        const int steps = 28;
        for (int i = 0; i < steps; i += 2) draw.AddLine(At(i / (float)steps), At((i + 1) / (float)steps), colour, thickness);
        EditorStyle.Arrow(draw, b, Vec2.Normalize(b - At(.95f)), 8, colour);
    }

    // The canvas mutates the same Influence data as the inspector, never a second graph representation.
    private void DrawInfluenceGraph(GameWorld world, WorldRule rule, RuleScope scope, ref bool changed)
    {
        var readable = scope.Fields.Select(f => f.Name).ToArray();
        if (ImGui.CollapsingHeader("+ Forrás, cél vagy művelet hozzáadása")) {
            if (rule.Effects.Count > 0) {
                int effectIndex = Math.Clamp(selectedInfluence, 0, rule.Effects.Count - 1);
                var selected = rule.Effects[effectIndex];
                bool hasTerm = selected.Influence.Terms.Count > 0;
                ImGui.RadioButton($"új kapcsolatként: {FieldDisplay(rule.Scope, selected.Target)}", ref addSourceAs, 0);
                if (hasTerm) {
                    ImGui.SameLine();
                    ImGui.RadioButton($"szorzóként: {selected.Influence.Terms[Math.Clamp(selectedTerm, 0, selected.Influence.Terms.Count - 1)].Name}", ref addSourceAs, 1);
                } else addSourceAs = 0;
                int termIndex = addSourceAs == 1 ? Math.Clamp(selectedTerm, 0, selected.Influence.Terms.Count - 1) : -1;
                changed |= ObjectFieldPicker("Forrás (azonnal bekötve)", rule.Scope, "", readable,
                    f => ConnectSource(rule, f, effectIndex, termIndex, ref selectedInfluence, ref selectedTerm));
            } else EditorStyle.Text(HudTheme.Muted, "Előbb adj meg egy célt; a forrás arra kötődik be.");
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
        bool pending = pendingInfluenceRule == rule.Id && pendingInfluenceSource != null;
        EditorStyle.Text(pending ? HudTheme.AmberAccent : HudTheme.Muted, pending
            ? $"„{FieldDisplay(rule.Scope, pendingInfluenceSource)}” kiválasztva: kattints egy célra (új kapcsolat) vagy egy hatásra (szorzó). Esc: mégse."
            : "Forrást húzz vagy kattints, majd kattints a célra (új kapcsolat) vagy egy hatásra (szorzó). Szaggatott „ha” vonal: feltétel.");

        var terms = rule.Effects.SelectMany((e, ei) => e.Influence.Terms.Select((t, ti) => (Effect: ei, Term: ti, Value: t))).ToArray();
        // Sources in the order of the first connection they feed: lines run mostly level and cross less.
        var sources = terms.SelectMany((t, i) => GraphInputs(rule.Scope, rule.Effects[t.Effect], t.Value).Select(input => (input.Source, Row: i)))
            .GroupBy(s => s.Source).OrderBy(g => g.Average(s => s.Row)).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).ToArray();
        float rowSource = 74, rowTerm = 80;
        var sourceSize = new Vec2(150, 62); var termSize = new Vec2(165, 62); var targetSize = new Vec2(130, 62);
        // Each target sits level with the middle of its connections, never overlapping the previous one.
        var targetY = new float[rule.Effects.Count]; float nextFree = 26;
        for (int e = 0; e < rule.Effects.Count; e++) {
            var rows = terms.Select((t, i) => (t, i)).Where(x => x.t.Effect == e).Select(x => 26 + x.i * rowTerm).ToArray();
            targetY[e] = Math.Max(nextFree, rows.Length > 0 ? rows.Average() : nextFree); nextFree = targetY[e] + targetSize.Y + 12;
        }
        // As tall as the graph needs, within limits; a taller graph scrolls inside the canvas.
        float height = Math.Max(Math.Max(240, nextFree), Math.Max(sources.Length * rowSource, terms.Length * rowTerm) + 40);
        ImGui.BeginChild("influence-canvas", new Vec2(0, Math.Min(340, height + 4)), ImGuiChildFlags.Borders);
        var draw = ImGui.GetWindowDrawList(); var origin = ImGui.GetCursorScreenPos();
        float width = Math.Max(500, ImGui.GetContentRegionAvail().X);
        ImGui.InvisibleButton("canvas", new Vec2(width, height), ImGuiButtonFlags.MouseButtonLeft);
        bool hovered = ImGui.IsItemHovered(); var mouse = ImGui.GetIO().MousePos;
        draw.AddRectFilled(origin, origin + new Vec2(width, height), EditorStyle.Canvas, 5);
        float termX = Math.Max(sourceSize.X + 60, width * .42f - 30);
        Vec2 Source(int i) => origin + new Vec2(8, 26 + i * rowSource);
        Vec2 Term(int i) => origin + new Vec2(termX, 26 + i * rowTerm);
        Vec2 Target(int i) => origin + new Vec2(width - targetSize.X - 8, targetY[i]);
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
        void Box(Vec2 p, Vec2 size, uint border, string top, uint topColour, string middle, uint middleColour, string bottom = "", uint bottomColour = 0)
        {
            draw.AddRectFilled(p, p + size, EditorStyle.NodeBody, 5);
            draw.AddRect(p, p + size, border, 5, ImDrawFlags.None, 1.5f);
            draw.PushClipRect(p + new Vec2(4, 0), p + size - new Vec2(4, 0), true);
            draw.AddText(p + new Vec2(6, 5), topColour, top);
            draw.AddText(p + new Vec2(6, 24), middleColour, middle);
            if (bottom.Length > 0) draw.AddText(p + new Vec2(6, 43), bottomColour == 0 ? EditorStyle.TextMuted : bottomColour, bottom);
            draw.PopClipRect();
        }
        draw.AddText(origin + new Vec2(8, 5), EditorStyle.TextMuted, "FORRÁS");
        draw.AddText(origin + new Vec2(termX, 5), EditorStyle.TextMuted, "KAPCSOLAT · ERŐSSÉG");
        draw.AddText(origin + new Vec2(width - targetSize.X - 8, 5), EditorStyle.TextMuted, "MIT VÁLTOZTAT");
        for (int i = 0; i < terms.Length; i++) {
            var entry = terms[i]; var t = entry.Value;
            uint colour = EditorStyle.U(!t.Enabled ? HudTheme.Muted : t.Decrease ? HudTheme.AmberAccent : HudTheme.Good);
            foreach (var input in GraphInputs(rule.Scope, rule.Effects[entry.Effect], t)) {
                int source = Array.IndexOf(sources, input.Source);
                if (source < 0) continue;
                var from = Source(source) + new Vec2(sourceSize.X, sourceSize.Y / 2); var to = Term(i) + new Vec2(-2, termSize.Y / 2);
                if (input.Gate) {
                    uint gate = EditorStyle.U(!t.Enabled ? HudTheme.Muted : HudTheme.Info);
                    DashedBezier(draw, from, to, gate, 1.6f);
                    var label = to - new Vec2(26, 9);
                    draw.AddRectFilled(label - new Vec2(2, 1), label + new Vec2(16, 15), EditorStyle.Canvas, 3);
                    draw.AddText(label, gate, "ha");
                } else {
                    int direction = input.Direction;
                    Link(from, to, EditorStyle.U(!t.Enabled ? HudTheme.Muted : direction < 0 ? HudTheme.AmberAccent : direction > 0 ? HudTheme.Good : HudTheme.Info));
                }
            }
            Link(Term(i) + new Vec2(termSize.X, termSize.Y / 2), Target(entry.Effect) + new Vec2(-6, targetSize.Y / 2), colour,
                selectedInfluence == entry.Effect && selectedTerm == entry.Term ? 3 : 1.5f);
        }
        InfluenceGraphSourceLabels.Clear();
        for (int i = 0; i < sources.Length; i++) {
            var p = Source(i); string source = sources[i];
            var connections = terms.SelectMany(t => GraphInputs(rule.Scope, rule.Effects[t.Effect], t.Value).Where(input => input.Source == source)).ToArray();
            string[] labels = connections.Select(c => c.Gate ? "Feltétel" : c.Label).Distinct().ToArray();
            string label = labels.Length == 1 ? labels[0] : labels.Length > 1 ? "Többféle hatás" : "Nincs kapcsolat";
            InfluenceGraphSourceLabels[source] = label;
            var labelColour = labels.Length == 1 && labels[0] == "Feltétel" ? HudTheme.Info
                : connections.Length > 0 && connections.All(c => c.Direction < 0) ? HudTheme.AmberAccent
                : connections.Length > 0 && connections.All(c => c.Direction > 0) ? HudTheme.Good : HudTheme.Muted;
            bool chosen = pendingInfluenceRule == rule.Id && pendingInfluenceSource == source;
            string range = FieldRange(rule.Scope, source);
            Box(p, sourceSize, chosen ? EditorStyle.Selection : EditorStyle.U(EditorStyle.InputKind),
                ObjectDisplay(rule.Scope, FieldObject(rule.Scope, source)) + (range.Length > 0 ? " · " + range : ""), EditorStyle.TextMuted,
                FieldDisplay(rule.Scope, source), EditorStyle.TextBright, label, EditorStyle.U(labelColour));
            draw.AddCircleFilled(p + new Vec2(sourceSize.X, sourceSize.Y / 2), 5, EditorStyle.U(EditorStyle.InputKind));
            if (Hit(p, sourceSize)) {
                ImGui.SetTooltip(FieldHelp(rule.Scope, source) + "\nHúzd vagy kattints rá, majd kattints egy célra vagy hatásra.");
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { draggingInfluenceSource = source; draggingInfluenceRule = rule.Id; influenceDragStart = mouse; }
                // A click without movement selects the source for click-to-connect.
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && draggingInfluenceSource == source && Vec2.Distance(mouse, influenceDragStart) < 5) {
                    bool same = pendingInfluenceSource == source && pendingInfluenceRule == rule.Id;
                    pendingInfluenceSource = same ? null : source; pendingInfluenceRule = same ? null : rule.Id;
                }
            }
        }
        bool consumed = false;
        bool Dropped() => ImGui.IsMouseReleased(ImGuiMouseButton.Left) && draggingInfluenceRule == rule.Id && draggingInfluenceSource != null
            && Vec2.Distance(mouse, influenceDragStart) >= 5;
        bool Clicked() => ImGui.IsMouseClicked(ImGuiMouseButton.Left) && pendingInfluenceRule == rule.Id && pendingInfluenceSource != null;
        for (int i = 0; i < terms.Length; i++) {
            var entry = terms[i]; var t = entry.Value; var p = Term(i);
            var effect = rule.Effects[entry.Effect];
            int gates = t.Conditions.Count + t.Factors.Count(f => f.Shape == "step"), multipliers = t.Factors.Count(f => f.Shape != "step");
            string detail = (gates > 0 ? $"ha: {gates} feltétel" : "mindig") + (multipliers > 0 ? $" · ×{multipliers} szorzó" : "");
            bool selected = selectedInfluence == entry.Effect && selectedTerm == entry.Term;
            Box(p, termSize, selected ? EditorStyle.Selection : EditorStyle.GridMinor, t.Name, EditorStyle.TextBright,
                StrengthLabel(world, rule.Scope, effect, t), EditorStyle.U(!t.Enabled ? HudTheme.Muted : effect.Mode == "action" ? HudTheme.Info : t.Decrease ? HudTheme.AmberAccent : HudTheme.Good),
                detail, EditorStyle.TextMuted);
            if (Hit(p, termSize)) {
                ImGui.SetTooltip(Sentences(world, rule.Scope).Term(effect, t) + "\nKattintás: beállítások. Forrás ráhúzása: új szorzó.");
                if (Clicked()) { changed |= ConnectSource(rule, pendingInfluenceSource, entry.Effect, entry.Term, ref selectedInfluence, ref selectedTerm); consumed = true; }
                else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { selectedInfluence = entry.Effect; selectedTerm = entry.Term; }
                if (Dropped()) changed |= ConnectSource(rule, draggingInfluenceSource, entry.Effect, entry.Term, ref selectedInfluence, ref selectedTerm);
            }
        }
        for (int i = 0; i < rule.Effects.Count; i++) {
            var p = Target(i); var effect = rule.Effects[i];
            string mode = effect.Mode == "action" ? "művelet" : effect.Mode == "set" ? "beállítja" : effect.Influence.PerYear ? "változtatja / év" : "változtatja / lépés";
            string range = effect.Mode == "action" ? "" : FieldRange(rule.Scope, effect.Target);
            Box(p, targetSize, EditorStyle.U(EditorStyle.OutputKind), ObjectDisplay(rule.Scope, rule.Scope), EditorStyle.TextMuted,
                FieldDisplay(rule.Scope, effect.Target), EditorStyle.TextBright, mode + (range.Length > 0 ? " · " + range : ""));
            draw.AddCircleFilled(p + new Vec2(0, targetSize.Y / 2), 5, EditorStyle.U(EditorStyle.OutputKind));
            if (Hit(p, targetSize)) {
                ImGui.SetTooltip(FieldHelp(rule.Scope, effect.Target) + "\nKattintás: kijelölés. Forrás ráhúzása: új kapcsolat.");
                if (Clicked()) { changed |= ConnectSource(rule, pendingInfluenceSource, i, -1, ref selectedInfluence, ref selectedTerm); consumed = true; }
                else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) selectedInfluence = i;
                if (Dropped()) changed |= ConnectSource(rule, draggingInfluenceSource, i, -1, ref selectedInfluence, ref selectedTerm);
            }
        }
        if (draggingInfluenceSource != null && draggingInfluenceRule == rule.Id && ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
            int source = Array.IndexOf(sources, draggingInfluenceSource);
            if (source >= 0 && Vec2.Distance(mouse, influenceDragStart) >= 5) Link(Source(source) + new Vec2(sourceSize.X, sourceSize.Y / 2), mouse, EditorStyle.Selection);
        }
        if (pending && hovered) {
            int source = Array.IndexOf(sources, pendingInfluenceSource);
            if (source >= 0) DashedBezier(draw, Source(source) + new Vec2(sourceSize.X, sourceSize.Y / 2), mouse, EditorStyle.Selection, 1.5f);
        }
        // A connection made, Esc, or a click on empty canvas ends click-to-connect.
        if (consumed || pending && ImGui.IsKeyPressed(ImGuiKey.Escape)
            || pending && hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !sources.Select((s, i) => Hit(Source(i), sourceSize)).Any(h => h)) {
            pendingInfluenceSource = null; pendingInfluenceRule = null;
        }
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) || draggingInfluenceRule != rule.Id) { draggingInfluenceSource = null; draggingInfluenceRule = null; }
        ImGui.EndChild();
    }
}
