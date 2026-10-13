using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

/// <summary>
/// The causal map: what influences what. Nodes are fields of the object model, grouped by object kind; arrows are rules,
/// blue + when the two move together, red − when opposite, grey ± when it depends. Native links (dashed) come from the
/// [RuleCause] attributes in the game code, rule-book links from the rules' formulas. Feedback loops are listed and lit.
/// Clicking a rule-book arrow edits its curve right here.
/// </summary>
internal sealed partial class RuleEditorView
{
    private readonly Dictionary<string, Vec2> mapPositions = new(StringComparer.Ordinal);
    private Vec2 mapPan = new(20, 20);
    private int mapEdge = -1, mapLoop = -1;
    private string mapDragNode;
    private bool mapEditableOnly;

    private static readonly Vec2 NodeSize = new(180, 42);
    private static readonly Vec4[] GroupTints = {
        new(0.36f, 0.55f, 0.70f, 0.13f), new(0.40f, 0.62f, 0.32f, 0.14f), new(0.70f, 0.56f, 0.30f, 0.13f), new(0.60f, 0.45f, 0.65f, 0.13f) };

    private string pendingEdgeRule;
    /// <summary>Opens the map with the first link of a rule selected.</summary>
    internal void ShowCausalEdge(string ruleId) { ShowRules(RulesTopic.Map); pendingEdgeRule = ruleId; }

    private void CausalMapArea(GameWorld world)
    {
        var rules = RuleBookRules(world);
        var map = RuleCausalMap.Build(Objects, rules);
        if (pendingEdgeRule != null) { mapEdge = map.Edges.ToList().FindIndex(e => e.Rule == pendingEdgeRule); pendingEdgeRule = null; }
        mapEdge = Math.Min(mapEdge, map.Edges.Count - 1); mapLoop = Math.Min(mapLoop, map.Loops.Count - 1);
        Layout(map);

        // ── Left: legend, filter, loops ────────────────────────────────
        ImGui.BeginChild("map-side", new Vec2(260, 0), ImGuiChildFlags.Borders);
        EditorStyle.Section("Jelmagyarázat");
        Legend(HudTheme.Info, "+  együtt nőnek", false); Legend(HudTheme.Bad, "-  ellentétesen", false);
        Legend(HudTheme.Muted, "±  a képlettől függ", false); Legend(HudTheme.Parchment, "natív C# (szaggatott)", true);
        EditorStyle.Text(HudTheme.AmberAccent, "Sárga keret: írható mező");
        ImGui.Checkbox("Csak a szabálykönyv szabályai", ref mapEditableOnly);
        EditorStyle.Section($"Visszacsatolási hurkok · {map.Loops.Count}");
        if (map.Loops.Count == 0) EditorStyle.Text(HudTheme.Muted, "Nincs hurok.");
        int b = 0, r = 0, u = 0;
        for (int i = 0; i < map.Loops.Count; i++) {
            var loop = map.Loops[i];
            string tag = loop.Polarity < 0 ? "B" + ++b : loop.Polarity > 0 ? "R" + ++r : "?" + ++u;
            string kind = loop.Polarity < 0 ? "kiegyenlítő" : loop.Polarity > 0 ? "erősítő" : "előjele bizonytalan";
            ImGui.PushStyleColor(ImGuiCol.Text, loop.Polarity < 0 ? HudTheme.Info : loop.Polarity > 0 ? HudTheme.AmberAccent : HudTheme.Muted);
            if (ImGui.Selectable($"{tag} · {kind}##loop{i}", mapLoop == i)) mapLoop = mapLoop == i ? -1 : i;
            ImGui.PopStyleColor();
            ImGui.Indent(12); ImGui.PushTextWrapPos(0);
            EditorStyle.Text(HudTheme.Muted, string.Join(" › ", loop.Edges.Select(e => NodeLabel(map, e.From)).Append(NodeLabel(map, loop.Edges[0].From))));
            ImGui.PopTextWrapPos(); ImGui.Unindent(12);
        }
        ImGui.Spacing();
        ImGui.PushTextWrapPos(0);
        EditorStyle.Text(HudTheme.Muted, "Kiegyenlítő hurok: a változás önmagát fékezi. Erősítő: önmagát gyorsítja. " +
            "Húzd a mezőket a rendezéshez, a jobb egérgombbal a térképet.");
        ImGui.PopTextWrapPos();
        if (ImGui.Button("Elrendezés alaphelyzetbe")) { mapPositions.Clear(); mapPan = new(20, 20); }
        ImGui.EndChild(); ImGui.SameLine();

        // ── Middle: the map ────────────────────────────────────────────
        bool detail = mapEdge >= 0;
        ImGui.BeginChild("map-canvas", new Vec2(detail ? -380 : 0, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        DrawMap(map);
        ImGui.EndChild();

        // ── Right: the selected rule, with its curve ─────────────────────
        if (detail) { ImGui.SameLine(); ImGui.BeginChild("map-rule", Vec2.Zero, ImGuiChildFlags.Borders); EdgeDetail(world, map, map.Edges[mapEdge], rules); ImGui.EndChild(); }
    }

    private static void Legend(Vec4 colour, string text, bool dashed)
    {
        var p = ImGui.GetCursorScreenPos(); var dl = ImGui.GetWindowDrawList(); uint c = EditorStyle.U(colour);
        float y = p.Y + ImGui.GetTextLineHeight() / 2;
        if (dashed) for (float x = 0; x < 26; x += 7) dl.AddLine(new(p.X + x, y), new(p.X + x + 4, y), c, 2);
        else dl.AddLine(new(p.X, y), new(p.X + 26, y), c, 2.2f);
        ImGui.Dummy(new Vec2(32, ImGui.GetTextLineHeight())); ImGui.SameLine(); EditorStyle.Text(HudTheme.Parchment, text);
    }

    private static string NodeLabel(RuleCausalMap map, string key) => map.Nodes.FirstOrDefault(n => n.Key == key)?.Field.Display ?? key;

    /// <summary>Default places: one column per object kind, in the order the links run; dragged nodes keep their place.</summary>
    private void Layout(RuleCausalMap map)
    {
        // Within a kind, causes above their effects: order by how many links lead into a field.
        var entities = map.Nodes.Select(n => n.Entity).Distinct().ToList();
        float x = 40;
        foreach (string entity in entities) {
            var column = map.Nodes.Where(n => n.Entity == entity).OrderBy(n => map.Edges.Count(e => e.To == n.Key && e.From != n.Key)).ToList();
            int cols = column.Count > 4 ? 2 : 1;
            for (int i = 0; i < column.Count; i++)
                if (!mapPositions.ContainsKey(column[i].Key))
                    mapPositions[column[i].Key] = new(x + i % cols * (NodeSize.X + 50), 70 + i / cols * 100 + i % cols * 50);
            x += cols * (NodeSize.X + 50) + 70;
        }
    }

    private void DrawMap(RuleCausalMap map)
    {
        var origin = ImGui.GetCursorScreenPos(); var size = ImGui.GetContentRegionAvail();
        ImGui.InvisibleButton("map-surface", Vec2.Max(size, new Vec2(50, 50)), ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        bool hovered = ImGui.IsItemHovered(); var io = ImGui.GetIO(); var mouse = io.MousePos;
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Right)) mapPan += io.MouseDelta;
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(origin, origin + size, true);
        Vec2 At(string key) => origin + mapPan + mapPositions[key];

        // Object kinds as tinted areas behind their fields, named with their lineage.
        var entities = map.Nodes.Select(n => n.Entity).Distinct().ToList();
        for (int i = 0; i < entities.Count; i++) {
            var keys = map.Nodes.Where(n => n.Entity == entities[i]).Select(n => At(n.Key)).ToList();
            var lo = keys.Aggregate(Vec2.Min) - new Vec2(22, 46); var hi = keys.Aggregate(Vec2.Max) + NodeSize + new Vec2(22, 18);
            dl.AddRectFilled(lo, hi, EditorStyle.U(GroupTints[i % GroupTints.Length]), 10);
            dl.AddText(lo + new Vec2(12, 10), EditorStyle.U(HudTheme.Muted), string.Join(" › ", Objects.Lineage(entities[i]).Select(e => e.Name)).ToUpperInvariant());
        }

        var loopEdges = mapLoop >= 0 ? map.Loops[mapLoop].Edges.ToHashSet() : null;
        int hoverEdge = -1; float best = 8;
        for (int i = 0; i < map.Edges.Count; i++) {
            var e = map.Edges[i];
            // Parallel links between the same fields fan out.
            int twins = map.Edges.Take(i).Count(x => x.From == e.From && x.To == e.To || x.From == e.To && x.To == e.From);
            var a = At(e.From) + NodeSize / 2; var bpt = At(e.To) + NodeSize / 2;
            var dir = Vec2.Normalize(bpt - a + new Vec2(1e-3f, 0)); var normal = new Vec2(-dir.Y, dir.X);
            var start = a + Exit(dir); var end = bpt - Exit(dir) - dir * 4;
            var control = (start + end) / 2 + normal * (28 + 26 * twins);
            bool dim = mapEditableOnly && e.Native || loopEdges != null && !loopEdges.Contains(e);
            bool lit = loopEdges != null && loopEdges.Contains(e) || mapEdge == i;
            var colour = lit && loopEdges != null ? HudTheme.AmberAccent : e.Sign > 0 ? HudTheme.Info : e.Sign < 0 ? HudTheme.Bad : HudTheme.Muted;
            if (dim) colour = colour with { W = 0.15f };
            float thick = mapEdge == i ? 4 : lit ? 3.2f : 2;
            uint c = EditorStyle.U(colour);
            Vec2 Q(float t) => (1 - t) * (1 - t) * start + 2 * (1 - t) * t * control + t * t * end;
            for (int s = 0; s < 24; s++) {
                if (e.Native && s % 2 == 1) continue;
                dl.AddLine(Q(s / 24f), Q((s + 1) / 24f), c, thick);
            }
            for (int s = 0; s <= 24 && hovered && !dim; s++) { float d = Vec2.Distance(Q(s / 24f), mouse); if (d < best) { best = d; hoverEdge = i; } }
            var tip = Q(1); var back = Vec2.Normalize(tip - Q(0.92f)); var side = new Vec2(-back.Y, back.X);
            dl.AddTriangleFilled(tip + back * 4, tip - back * 9 + side * 5.5f, tip - back * 9 - side * 5.5f, c);
            var mid = Q(0.5f) + normal * 10; string sign = e.Sign > 0 ? "+" : e.Sign < 0 ? "-" : "±";
            dl.AddText(mid - ImGui.CalcTextSize(sign) / 2, c, sign);
        }

        // Fields: drag to arrange; hover for help.
        string hoverNode = null;
        foreach (var n in map.Nodes) {
            var p = At(n.Key); bool over = hovered && mouse.X >= p.X && mouse.Y >= p.Y && mouse.X <= p.X + NodeSize.X && mouse.Y <= p.Y + NodeSize.Y;
            if (over) hoverNode = n.Key;
            bool touched = mapEdge >= 0 && (map.Edges[mapEdge].From == n.Key || map.Edges[mapEdge].To == n.Key);
            dl.AddRectFilled(p, p + NodeSize, EditorStyle.U(over ? HudTheme.PanelLight : HudTheme.Panel), 6);
            dl.AddRect(p, p + NodeSize, EditorStyle.U(n.Field.Writable ? HudTheme.AmberAccent : touched ? HudTheme.Parchment : HudTheme.Muted with { W = 0.6f }), 6, ImDrawFlags.None, n.Field.Writable || touched ? 2.2f : 1.2f);
            var title = n.Field.Display; var code = n.Entity + "." + n.Field.Name;
            dl.AddText(p + new Vec2((NodeSize.X - ImGui.CalcTextSize(title).X) / 2, 5), EditorStyle.U(HudTheme.Parchment), title);
            dl.AddText(p + new Vec2((NodeSize.X - ImGui.CalcTextSize(code).X) / 2, 22), EditorStyle.U(HudTheme.Muted), code);
        }

        if (ImGui.IsItemActivated() && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
            mapDragNode = hoverNode;
            if (hoverNode == null) { mapEdge = hoverEdge; if (hoverEdge >= 0) draggedPoint = -1; }
        }
        if (ImGui.IsItemActive() && mapDragNode != null && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) mapPositions[mapDragNode] += io.MouseDelta;
        if (ImGui.IsItemDeactivated()) mapDragNode = null;

        if (hoverNode != null) {
            var n = map.Nodes.First(x => x.Key == hoverNode);
            ImGui.SetTooltip($"{n.Field.Display} ({n.Field.Unit})\n{n.Field.Help}\nKód: {n.Field.Property.DeclaringType.Name}.{n.Field.Property.Name}" +
                (n.Field.Writable ? "\nÍrható: szabály célja lehet." : ""));
        } else if (hoverEdge >= 0) {
            var e = map.Edges[hoverEdge];
            ImGui.SetTooltip($"{e.RuleName}{(e.Native ? "  · natív C#" : "  · szabálykönyv")}\n{NodeLabel(map, e.From)} › {NodeLabel(map, e.To)}" +
                (e.Help.Length > 0 ? "\n" + e.Help : "") + "\nKattintás: megnyitás");
        }
        if (map.Nodes.Count == 0) dl.AddText(origin + new Vec2(20, 20), EditorStyle.U(HudTheme.Muted), "A térképen még nincs objektummező.");
        dl.PopClipRect();
    }

    private static Vec2 Exit(Vec2 dir)
    {
        // Where a line from the node centre leaves its rectangle.
        float sx = Math.Abs(dir.X) < 1e-4f ? float.MaxValue : NodeSize.X / 2 / Math.Abs(dir.X);
        float sy = Math.Abs(dir.Y) < 1e-4f ? float.MaxValue : NodeSize.Y / 2 / Math.Abs(dir.Y);
        return dir * Math.Min(sx, sy);
    }

    private void EdgeDetail(GameWorld world, RuleCausalMap map, CausalEdge edge, List<WorldRule> rules)
    {
        EditorStyle.Text(HudTheme.Parchment, edge.RuleName);
        EditorStyle.Text(edge.Native ? HudTheme.Muted : HudTheme.Good, edge.Native ? "Natív C# szabály" : "Szabálykönyv · adatként fut");
        ImGui.SameLine(); if (EditorStyle.QuietButton("bezár##edge", "A részletek bezárása.")) { mapEdge = -1; return; }
        EditorStyle.Text(HudTheme.Muted, $"{NodeLabel(map, edge.From)}  ›  {NodeLabel(map, edge.To)}");
        ImGui.PushTextWrapPos(0);
        if (edge.Help.Length > 0) EditorStyle.Text(HudTheme.Muted, edge.Help);
        if (edge.Native) {
            EditorStyle.Text(HudTheme.Parchment, $"A kapcsolat a játék kódjában van ([RuleCause] a {edge.To} mezőn, szabály: {edge.Rule}).");
            EditorStyle.Text(HudTheme.Muted, "Átköltöztetés után itt görbével szerkeszthető, mint a szabálykönyv szabályai.");
            ImGui.PopTextWrapPos();
            return;
        }
        ImGui.PopTextWrapPos();
        var rule = rules[edge.RuleIndex];
        if (!rule.Enabled) {
            EditorStyle.Text(HudTheme.AmberAccent, "Kikapcsolva: nem fut a játékban.");
            ImGui.SameLine(); if (ImGui.SmallButton("Bekapcsolás")) { rule.Enabled = true; ApplyRuleBook(world, rule.Name + ": bekapcsolva."); }
        }
        if (ImGui.Button("Megnyitás a szabálykönyvben")) ShowRuleBook(edge.RuleIndex);
        EditorStyle.Section("Hatás");
        string to = edge.To[(edge.To.IndexOf('.') + 1)..];
        bool finished = false, any = false;
        var scope = RuleEngine.Scope(rule.Scope);
        var inputs = RuleEngine.Names(rule.Scope, RuleBookFields(world), dynamicsEditor.Draft.Stocks.Select(s => s.Id)).Where(n => n.Operation == BehaviorOperation.Input).Select(n => n.Name).ToArray();
        for (int i = 0; i < rule.Effects.Count; i++) {
            var e = rule.Effects[i];
            if (e.Target != to || e.Mode == "action") continue;
            any = true;
            EditorStyle.Text(HudTheme.Muted, e.Mode == "add" ? $"{e.Target} változása lépésenként:" : $"{e.Target} értéke:");
            if (RuleCurve.TryParse(e.Formula, out var curve)) {
                if (CurveEditor(rule.Id + "#" + i, curve, scope?.Entity ?? rule.Scope, inputs, ref finished)) e.Formula = curve.ToFormula();
            } else {
                ImGui.PushTextWrapPos(0); ImGui.TextUnformatted(e.Formula); ImGui.PopTextWrapPos();
                EditorStyle.Text(HudTheme.Muted, "Nem görbe: a szabálykönyvben görbévé alakítható.");
            }
        }
        if (!any) EditorStyle.Text(HudTheme.Muted, "A hatás a feltételen keresztül.");
        if (finished) ApplyRuleBook(world, rule.Name + " módosítva.");
    }
}
