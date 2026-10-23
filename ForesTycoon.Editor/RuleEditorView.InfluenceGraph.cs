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
    private string influenceHover, influenceHoverRule; // the box under the mouse last frame: its wires are drawn lit
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
        if (effect.Mode == "flow") return direction > 0 ? "+ Gyorsítja" : "- Lassítja";
        // The field is the pace of a process (decay, ruts smoothing out): a source speeds it up or slows it down.
        int pace = scope != "passage" && effect.Mode == "add" ? SchemaField(scope, effect.Target)?.Pace ?? 0 : 0;
        if (pace != 0) return direction == pace ? "+ Gyorsítja" : "- Lassítja";
        return direction > 0 ? "+ Növeli" : "- Csökkenti";
    }

    /// <summary>Every source a connection reads. Gates (conditions, steps) switch the connection on or off; the others scale it.</summary>
    private static IEnumerable<(string Source, int Direction, string Label, bool Gate)> GraphInputs(string scope, RuleEffect effect, InfluenceTerm term)
    {
        if (term.AmountField.Length > 0) yield return (term.AmountField, term.Decrease ? -1 : 1, "Erősség forrása", false);
        foreach (var factor in term.Factors) yield return (factor.Source, ConnectionDirection(term, factor), ConnectionLabel(scope, effect, term, factor), factor.Shape == "step");
        foreach (var condition in term.Conditions) {
            int direction = condition.Comparison is "less" or "atMost" or "notEqual" ? -1 : 1;
            yield return (condition.Field, direction, direction > 0 ? "Kiváltja" : "Gátolja", true);
            if (condition.ValueCurve is { } curve) yield return (curve.Source, -direction * curve.ToCurve().Sign, "Küszöb görbéje", true);
            if (condition.ValueField.Length > 0) yield return (condition.ValueField, -direction, direction > 0 ? "Gátolja" : "Kiváltja", true);
        }
    }

    /// <summary>“– 0,05 / év”: the strength of a connection as shown on its box.</summary>
    private static string StrengthLabel(GameWorld world, string scope, RuleEffect effect, InfluenceTerm t)
    {
        if (!t.Enabled) return "Kikapcsolva";
        string amount = t.AmountField.Length > 0
            ? (t.AmountField.StartsWith("tuning.", StringComparison.Ordinal) && GameTuning.Spec(t.AmountField[7..]) is { } spec
                ? RuleSentence.Number(world.Tuning[spec.Key]) + " (param.)" : FieldDisplay(scope, t.AmountField))
            : RuleSentence.Number(t.Amount);
        if (effect.Mode == "action") return "Kiváltja";
        if (effect.Mode == "set") return "= " + amount + (t.AmountScale != 1 ? " × " + RuleSentence.Number(t.AmountScale) : "");
        if (effect.Mode == "approach") return "Cél: " + (t.Decrease ? "− " : "+ ") + amount;
        string unit = FieldUnit(scope, effect.Target);
        return (t.Decrease ? "– " : "+ ") + amount + (unit.Length == 0 || unit == "0–1" ? "" : " " + unit) + (scope == "passage" ? " / áthaladás" : effect.Influence.PerYear ? " / év" : " / lépés");
    }

    /// <summary>
    /// A flow's own settings: which stock it drains, which it fills (or the outside, or a collection such as the neighbours,
    /// shared evenly or downhill) and its name in the flow ledger.
    /// </summary>
    private void FlowSettings(GameWorld world, WorldRule rule, RuleScope scope, RuleEffect effect, string[] writable, ref bool changed)
    {
        ImGui.TextWrapped("Az objektum készletéből a szabályok sorrendjében ad át. Mindig a még rendelkezésre álló mennyiséget használja; az előbb futó átadás elsőbbséget kap.");
        const string outside = "";
        var into = writable.Concat(scope.Collections.SelectMany(c => RuleEngine.Scope(c.Target).Fields.Where(f => f.Write != null && !f.Name.Contains('.'))
            .Select(f => c.Name + "." + f.Name))).ToArray();
        string Label(string field) => field.Length == 0 ? "Kívülről / a rendszeren kívülre" : FieldTitle(rule.Scope, field);
        bool Choose(string label, string current, string[] choices, Action<string> set)
        {
            bool picked = false;
            ImGui.TextUnformatted(label); ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##" + label, Label(current), ImGuiComboFlags.HeightLarge)) {
                foreach (string f in choices.Prepend(outside))
                    if (ImGui.Selectable(Label(f) + "##" + f, f == current)) { set(f); picked = true; }
                ImGui.EndCombo();
            }
            return picked;
        }
        changed |= Choose("Honnan?", effect.Target, writable, v => effect.Target = v);
        changed |= Choose("Hová?", effect.To, into, v => { effect.To = v; if (!v.Contains('.') || writable.Contains(v)) effect.Spread = "even"; });
        if (effect.Target == effect.To) EditorStyle.Text(HudTheme.Bad, "A forrás és a cél nem lehet ugyanaz.");
        int dot = effect.To.IndexOf('.');
        if (dot > 0 && scope.Collections.FirstOrDefault(c => c.Name == effect.To[..dot]) is { } collection) {
            int spread = effect.Spread == "downhill" ? 1 : 0;
            ImGui.RadioButton("Egyenlően", ref spread, 0); ImGui.SameLine(); ImGui.RadioButton("Lejtés szerint", ref spread, 1);
            if (spread != (effect.Spread == "downhill" ? 1 : 0)) {
                effect.Spread = spread == 1 ? "downhill" : "even";
                if (spread == 1 && effect.Level.Length == 0) effect.Level = RuleEngine.Scope(collection.Target).Fields.FirstOrDefault(f => f.Name == "waterLevel")?.Name ?? "";
                changed = true;
            }
            if (effect.Spread == "downhill") {
                var levels = RuleEngine.Scope(collection.Target).Fields.Where(f => !f.Name.Contains('.') && scope.Fields.Any(o => o.Name == f.Name)).Select(f => f.Name).ToArray();
                changed |= ObjectFieldPicker("Mi szerint lejt?", rule.Scope, effect.Level, levels, v => effect.Level = v);
                EditorStyle.Text(HudTheme.Muted, "Minden tag annyival több részt kap, amennyivel alacsonyabban van; a magasabban lévők semmit.");
            }
        }
        string name = effect.FlowName;
        ImGui.TextUnformatted("Az áramlás neve"); ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##flow-name", "például: párolgás", ref name, 60)) effect.FlowName = name;
        changed |= ImGui.IsItemDeactivatedAfterEdit();
        if (world.RuleBook.FlowTotals.TryGetValue(effect.FlowName.Length > 0 ? effect.FlowName : "áramlás", out double moved))
            EditorStyle.Text(HudTheme.Info, $"A tesztvilágban eddig áramlott: {FieldValue(rule.Scope, effect.Target.Length > 0 ? effect.Target : effect.To, moved)}");
        EditorStyle.Text(HudTheme.Muted, "A forrásból legfeljebb annyi fogy, amennyi van, és a célba legfeljebb annyi kerül, amennyi elfér. "
            + "Amennyi az egyikből elfogy, annyi a másikba kerül.");
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
            influence.Terms.Add(new() { Name = FieldDisplay(rule.Scope, source) + " hatása", Decrease = rule.Effects[effect].Mode is not ("action" or "approach"), Factors = new() { new() { Source = source } } });
            term = influence.Terms.Count - 1;
        }
        selectedEffect = effect; selectedTermIndex = term;
        return true;
    }

    // The canvas mutates the same Influence data as the inspector, never a second graph representation.
    private void DrawInfluenceGraph(GameWorld world, WorldRule rule, RuleScope scope, ref bool changed)
    {
        var readable = ReadableFields(world, scope);
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
            changed |= ObjectFieldPicker("Cél vagy művelet", rule.Scope, "", WritableFields(world, scope)
                .Concat(scope.Actions.Select(a => a.Name)).ToArray(), field => {
                int index = rule.Effects.FindIndex(e => e.Target == field);
                if (index < 0) {
                    bool action = scope.Actions.Any(a => a.Name == field);
                    rule.Effects.Add(new() { Target = field, Mode = action ? "action" : "add", Influence = new() { PerYear = !action && rule.Schedule != "passage" } });
                    index = rule.Effects.Count - 1;
                }
                selectedInfluence = index; selectedTerm = 0;
            });
        }
        bool pending = pendingInfluenceRule == rule.Id && pendingInfluenceSource != null;
        EditorStyle.Text(pending ? HudTheme.AmberAccent : HudTheme.Muted, pending
            ? $"„{FieldDisplay(rule.Scope, pendingInfluenceSource)}” kiválasztva: kattints egy célra (új kapcsolat) vagy egy hatásra (szorzó). Esc: mégse."
            : "Forrást húzz vagy kattints, majd kattints a célra (új kapcsolat) vagy egy hatásra (szorzó). Kék szaggatott vonal: feltétel.");

        var terms = rule.Effects.SelectMany((e, ei) => e.Influence.Terms.Select((t, ti) => (Effect: ei, Term: ti, Value: t))).ToArray();
        var inputs = terms.Select(t => GraphInputs(rule.Scope, rule.Effects[t.Effect], t.Value).ToArray()).ToArray();
        // Sources in the order of the first connection they feed: lines run mostly level and cross less.
        var sources = inputs.SelectMany((list, i) => list.Select(input => (input.Source, Row: i)))
            .GroupBy(s => s.Source).OrderBy(g => g.Key.StartsWith("tuning.", StringComparison.Ordinal) ? 1 : 0).ThenBy(g => g.Average(s => s.Row)).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).ToArray();
        // Every wire has a port of its own: a connection gets one input port per source, a target one per connection.
        const float top = 28, gap = 14, portStep = 17, boxMin = 62, lane = 54;
        static float Tall(int ports) => Math.Max(boxMin, ports * portStep + 18);
        var sourceSize = new Vec2(142, boxMin);
        const float termWidth = 156, targetWidth = 124;
        var termY = new float[terms.Length]; var termH = new float[terms.Length];
        float nextTerm = top;
        for (int i = 0; i < terms.Length; i++) { termY[i] = nextTerm; termH[i] = Tall(inputs[i].Length); nextTerm += termH[i] + gap; }
        var feeding = Enumerable.Range(0, rule.Effects.Count).Select(e => Enumerable.Range(0, terms.Length).Where(i => terms[i].Effect == e).ToArray()).ToArray();
        // Each target sits level with the middle of its connections, never overlapping the previous one.
        var targetY = new float[rule.Effects.Count]; var targetH = new float[rule.Effects.Count]; float nextFree = top;
        for (int e = 0; e < rule.Effects.Count; e++) {
            targetH[e] = Tall(feeding[e].Length);
            float middle = feeding[e].Length > 0 ? feeding[e].Average(i => termY[i] + termH[i] / 2) - targetH[e] / 2 : nextFree;
            targetY[e] = Math.Max(nextFree, middle); nextFree = targetY[e] + targetH[e] + gap;
        }
        float height = Math.Max(240, Math.Max(Math.Max(nextFree, nextTerm), top + sources.Length * (sourceSize.Y + gap)) + 8);
        // Columns keep lanes for the wires between them; a narrow panel scrolls sideways instead of squeezing them.
        float needed = 10 + sourceSize.X + lane + termWidth + lane + targetWidth + 10;
        ImGui.BeginChild("influence-canvas", new Vec2(0, Math.Min(Math.Max(380, ImGui.GetContentRegionAvail().Y - 4), height + 18)), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar);
        var draw = ImGui.GetWindowDrawList(); var origin = ImGui.GetCursorScreenPos();
        float width = Math.Max(needed, ImGui.GetContentRegionAvail().X - 1);
        ImGui.InvisibleButton("canvas", new Vec2(width, height), ImGuiButtonFlags.MouseButtonLeft);
        bool hovered = ImGui.IsItemHovered(); var mouse = ImGui.GetIO().MousePos;
        draw.AddRectFilled(origin, origin + new Vec2(width, height), EditorStyle.Canvas, 5);
        float termX = 10 + sourceSize.X + lane + (width - needed) * .5f, targetX = width - targetWidth - 10;
        Vec2 Source(int i) => origin + new Vec2(10, top + i * (sourceSize.Y + gap));
        Vec2 Term(int i) => origin + new Vec2(termX, termY[i]);
        Vec2 Target(int i) => origin + new Vec2(targetX, targetY[i]);
        Vec2 TermSize(int i) => new(termWidth, termH[i]);
        Vec2 TargetSize(int i) => new(targetWidth, targetH[i]);
        Vec2 SourcePort(int i) => Source(i) + new Vec2(sourceSize.X, sourceSize.Y / 2);
        Vec2 TermIn(int i, int j) => Term(i) + new Vec2(0, termH[i] * (j + 1) / (inputs[i].Length + 1));
        Vec2 TermOut(int i) => Term(i) + new Vec2(termWidth, termH[i] / 2);
        Vec2 TargetIn(int e, int j) => Target(e) + new Vec2(0, targetH[e] * (j + 1) / (feeding[e].Length + 1));
        InfluenceGraphAnchors.Clear();
        for (int i = 0; i < sources.Length; i++) InfluenceGraphAnchors["source:" + sources[i]] = Source(i) + sourceSize / 2;
        for (int i = 0; i < rule.Effects.Count; i++) InfluenceGraphAnchors["target:" + rule.Effects[i].Target] = Target(i) + TargetSize(i) / 2;
        for (int i = 0; i < terms.Length; i++) InfluenceGraphAnchors[$"effect:{terms[i].Effect}:{terms[i].Term}"] = Term(i) + TermSize(i) / 2;
        bool Hit(Vec2 p, Vec2 size) => hovered && mouse.X >= p.X && mouse.Y >= p.Y && mouse.X <= p.X + size.X && mouse.Y <= p.Y + size.Y;
        void Box(Vec2 p, Vec2 size, uint border, string head, uint headColour, string middle, uint middleColour, string bottom = "", uint bottomColour = 0, bool lit = false)
        {
            EditorStyle.GraphBox(draw, p, size, border, head, headColour, middle, middleColour, bottom, bottomColour, lit);
        }

        draw.AddText(origin + new Vec2(10, 7), EditorStyle.TextMuted, "FORRÁS");
        draw.AddText(origin + new Vec2(termX, 7), EditorStyle.TextMuted, "KAPCSOLAT · ERŐSSÉG");
        draw.AddText(origin + new Vec2(targetX, 7), EditorStyle.TextMuted, "MIT VÁLTOZTAT");

        // Hovering a box (last frame's hit test) brings its wires forward and dims the others.
        string focus = influenceHoverRule == rule.Id ? influenceHover : null;
        bool Lit(string source, int term, int effect) => focus == null || focus == "source:" + source || focus == "term:" + term || focus == "target:" + effect;
        uint Tone(uint colour, bool lit) => lit ? colour : EditorStyle.Fade(colour, .25f);
        var plugged = new HashSet<string>();
        for (int i = 0; i < terms.Length; i++) {
            var entry = terms[i]; var t = entry.Value;
            bool chosen = selectedInfluence == entry.Effect && selectedTerm == entry.Term;
            for (int j = 0; j < inputs[i].Length; j++) {
                var input = inputs[i][j];
                int source = Array.IndexOf(sources, input.Source);
                if (source < 0) continue;
                bool lit = Lit(input.Source, i, entry.Effect);
                uint colour = EditorStyle.U(!t.Enabled ? HudTheme.Muted : input.Gate ? HudTheme.Info
                    : input.Direction < 0 ? HudTheme.AmberAccent : input.Direction > 0 ? HudTheme.Good : HudTheme.Info);
                var to = TermIn(i, j);
                EditorStyle.Wire(draw, SourcePort(source), to - new Vec2(6, 0), Tone(colour, lit), chosen || focus != null && lit ? 2.6f : 1.8f, input.Gate, EditorStyle.Canvas);
                plugged.Add("source:" + source);
            }
            uint outColour = EditorStyle.U(!t.Enabled ? HudTheme.Muted : rule.Effects[entry.Effect].Mode == "action" ? HudTheme.Info : t.Decrease ? HudTheme.AmberAccent : HudTheme.Good);
            bool outLit = focus == null || focus == "term:" + i || focus == "target:" + entry.Effect;
            EditorStyle.Wire(draw, TermOut(i), TargetIn(entry.Effect, Array.IndexOf(feeding[entry.Effect], i)) - new Vec2(6, 0),
                Tone(outColour, outLit), chosen || focus != null && outLit ? 3f : 2f, false, EditorStyle.Canvas);
        }
        string hoverNow = null;
        bool dragging = draggingInfluenceSource != null && draggingInfluenceRule == rule.Id && ImGui.IsMouseDown(ImGuiMouseButton.Left)
            && Vec2.Distance(mouse, influenceDragStart) >= 5;
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
            bool over = Hit(p, sourceSize);
            Box(p, sourceSize, chosen ? EditorStyle.Selection : EditorStyle.U(EditorStyle.InputKind),
                ObjectDisplay(rule.Scope, FieldObject(rule.Scope, source)) + (range.Length > 0 ? " · " + range : ""), EditorStyle.TextMuted,
                FieldDisplay(rule.Scope, source), EditorStyle.TextBright, label, EditorStyle.U(labelColour), over || chosen);
            EditorStyle.Port(draw, SourcePort(i), EditorStyle.U(EditorStyle.InputKind), plugged.Contains("source:" + i));
            if (over) {
                hoverNow = "source:" + source;
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
            bool over = Hit(p, TermSize(i)), dropping = over && (dragging || pending);
            Box(p, TermSize(i), selected || dropping ? EditorStyle.Selection : EditorStyle.GridMinor, t.Name, EditorStyle.TextBright,
                StrengthLabel(world, rule.Scope, effect, t), EditorStyle.U(!t.Enabled ? HudTheme.Muted : effect.Mode == "action" ? HudTheme.Info : t.Decrease ? HudTheme.AmberAccent : HudTheme.Good),
                detail, EditorStyle.TextMuted, over);
            uint termPort = !t.Enabled ? EditorStyle.U(HudTheme.Muted) : EditorStyle.TextBright;
            for (int j = 0; j < inputs[i].Length; j++) EditorStyle.Port(draw, TermIn(i, j), inputs[i][j].Gate ? EditorStyle.U(HudTheme.Info) : termPort, true);
            EditorStyle.Port(draw, TermOut(i), termPort, true);
            if (over) {
                hoverNow = "term:" + i;
                ImGui.SetTooltip(Sentences(world, rule.Scope).Term(effect, t) + "\nKattintás: beállítások. Forrás ráhúzása: új szorzó.");
                if (Clicked()) { changed |= ConnectSource(rule, pendingInfluenceSource, entry.Effect, entry.Term, ref selectedInfluence, ref selectedTerm); consumed = true; }
                else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { selectedInfluence = entry.Effect; selectedTerm = entry.Term; }
                if (Dropped()) changed |= ConnectSource(rule, draggingInfluenceSource, entry.Effect, entry.Term, ref selectedInfluence, ref selectedTerm);
            }
        }
        for (int i = 0; i < rule.Effects.Count; i++) {
            var p = Target(i); var effect = rule.Effects[i];
            string mode = effect.Mode == "action" ? "művelet" : effect.Mode == "approach" ? $"közelít · {RuleSentence.Number(effect.ApproachPerYear)} / év" : effect.Mode == "set" ? "beállítja"
                : effect.Mode == "flow" ? "áramlás › " + (effect.To.Length == 0 ? "ki a rendszerből" : FieldTitle(rule.Scope, effect.To))
                : rule.Schedule == "passage" ? "változtatja / áthaladás" : effect.Influence.PerYear ? "változtatja / év" : "változtatja / lépés";
            string range = effect.Mode is "action" or "flow" ? "" : FieldRange(rule.Scope, effect.Target);
            bool over = Hit(p, TargetSize(i)), dropping = over && (dragging || pending);
            Box(p, TargetSize(i), dropping ? EditorStyle.Selection : EditorStyle.U(EditorStyle.OutputKind), ObjectDisplay(rule.Scope, FieldObject(rule.Scope, effect.Target)), EditorStyle.TextMuted,
                effect.Target.Length == 0 ? "Kívülről" : FieldDisplay(rule.Scope, effect.Target), EditorStyle.TextBright, mode + (range.Length > 0 ? " · " + range : ""), 0, over);
            for (int j = 0; j < feeding[i].Length; j++) EditorStyle.Port(draw, TargetIn(i, j), EditorStyle.U(EditorStyle.OutputKind), true);
            if (feeding[i].Length == 0) EditorStyle.Port(draw, Target(i) + new Vec2(0, targetH[i] / 2), EditorStyle.U(EditorStyle.OutputKind), false);
            if (over) {
                hoverNow = "target:" + i;
                ImGui.SetTooltip(FieldHelp(rule.Scope, effect.Target) + "\nKattintás: kijelölés. Forrás ráhúzása: új kapcsolat.");
                if (Clicked()) { changed |= ConnectSource(rule, pendingInfluenceSource, i, -1, ref selectedInfluence, ref selectedTerm); consumed = true; }
                else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) selectedInfluence = i;
                if (Dropped()) changed |= ConnectSource(rule, draggingInfluenceSource, i, -1, ref selectedInfluence, ref selectedTerm);
            }
        }
        // The wire being made follows the mouse from the source's port.
        if (dragging) {
            int source = Array.IndexOf(sources, draggingInfluenceSource);
            if (source >= 0) EditorStyle.Wire(draw, SourcePort(source), mouse, EditorStyle.Selection, 2.4f, false, EditorStyle.Canvas);
        }
        if (pending && hovered) {
            int source = Array.IndexOf(sources, pendingInfluenceSource);
            if (source >= 0) EditorStyle.Wire(draw, SourcePort(source), mouse, EditorStyle.Selection, 2f, true, EditorStyle.Canvas);
        }
        influenceHover = hoverNow; influenceHoverRule = hoverNow == null ? null : rule.Id;
        // A connection made, Esc, or a click on empty canvas ends click-to-connect.
        if (consumed || pending && ImGui.IsKeyPressed(ImGuiKey.Escape)
            || pending && hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !sources.Select((s, i) => Hit(Source(i), sourceSize)).Any(h => h)) {
            pendingInfluenceSource = null; pendingInfluenceRule = null;
        }
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) || draggingInfluenceRule != rule.Id) { draggingInfluenceSource = null; draggingInfluenceRule = null; }
        ImGui.EndChild();
    }
}
