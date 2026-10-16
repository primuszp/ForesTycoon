using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

/// <summary>
/// The curve editor: an effect written as <c>curve.family(input; points…) * modifier</c> is edited by its shape. Pick a
/// family, drag the points, hover the plot to read a value. The formula text is the stored form, so the game runs exactly
/// what is drawn here.
/// </summary>
internal sealed partial class RuleEditorView
{
    private int draggedPoint = -1;
    private (double X0, double X1, double Y0, double Y1)? draggedBounds;
    private string draggedCurve;
    private readonly HashSet<string> formulaView = new(StringComparer.Ordinal);

    /// <summary>Draws the editor; returns true while it changed the curve, and sets <paramref name="finished"/> when an edit is complete.</summary>
    private bool CurveEditor(string id, RuleCurve curve, string scope, IReadOnlyList<string> inputs, ref bool finished, bool bindings = true)
    {
        bool changed = false;
        ImGui.PushID(id);

        // Wrap by panel width, including the narrow graph inspector.
        int columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / 78), 1, RuleCurve.Families.Length);
        if (ImGui.BeginTable("curve-families", columns, ImGuiTableFlags.SizingFixedFit)) {
            foreach (var family in RuleCurve.Families) {
                ImGui.TableNextColumn();
                if (FamilyButton(family, curve.Family == family.Id)) { curve.SetFamily(family.Id); changed = finished = true; }
            }
            ImGui.EndTable();
        }

        if (bindings) {
        // Input: any readable field of the scope, its relations included.
        ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, "bemenet"); ImGui.SameLine();
        ImGui.SetNextItemWidth(220);
        if (ImGui.BeginCombo("##curve-input", curve.Input)) {
            foreach (string name in inputs)
                if (ImGui.Selectable(name, name == curve.Input)) { curve.Input = name; changed = finished = true; }
            ImGui.EndCombo();
        }
        }
        var field = SchemaField(scope, curve.Input);
        if (field != null) { if (bindings) ImGui.SameLine(); EditorStyle.Text(HudTheme.Muted, $"{field.Display} · {field.Unit}"); }

        try { curve.Validate(); changed |= CurvePlot(id, curve, field, editable: true, ref finished); }
        catch (System.IO.InvalidDataException e) { EditorStyle.Text(HudTheme.Bad, e.Message); }
        var familyHelp = RuleCurve.Find(curve.Family);
        if (familyHelp != null) ImGui.TextWrapped(familyHelp.Help);

        var (xmin, xmax) = InputRange(curve, field);
        double ymin = Math.Min(0, curve.Points.Min(p => p.Y)), ymax = Math.Max(1, curve.Points.Max(p => p.Y));
        for (int i = 0; i < curve.Points.Count; i++) {
            var (x, y) = curve.Points[i];
            bool edit = CurveNumber($"{(curve.Family == "step" ? "Küszöb" : $"{i + 1}. pont · bemenet")}##px{i}", ref x, xmin, xmax, ref finished);
            edit |= CurveNumber($"{(curve.Family == "step" ? "Küszöb alatt" : "Kimenet")}##py{i}", ref y, ymin, ymax, ref finished);
            if (edit) { curve.Points[i] = (x, y); changed = true; }
        }
        if (curve.Family == "step") {
            double above = curve.Above;
            if (CurveNumber("Küszöbtől##above", ref above, Math.Min(ymin, above), Math.Max(ymax, above), ref finished)) { curve.Above = above; changed = true; }
        }
        if (curve.Family == "points") {
            ImGui.BeginDisabled(curve.Points.Count >= 16);
            if (ImGui.SmallButton("+ pont")) { var (a, b) = (curve.Points[^2], curve.Points[^1]); curve.Points.Insert(curve.Points.Count - 1, ((a.X + b.X) / 2, (a.Y + b.Y) / 2)); changed = finished = true; }
            ImGui.EndDisabled();
            ImGui.SameLine(); ImGui.BeginDisabled(curve.Points.Count <= 2);
            if (ImGui.SmallButton("- pont")) { curve.Points.RemoveAt(curve.Points.Count - 2); changed = finished = true; }
            ImGui.EndDisabled();
        }
        if (bindings) {
        ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, "szorzó"); ImGui.SameLine();
        string tail = curve.Tail ?? ""; ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##tail", "például: * lépés", ref tail, 400)) { curve.Tail = tail; changed = true; }
        finished |= ImGui.IsItemDeactivatedAfterEdit();
        }
        if (curve.Family == "points") curve.Points.Sort((a, b) => a.X.CompareTo(b.X));
        ImGui.PopID();
        return changed;
    }

    private static readonly Dictionary<uint, (double Min, double Max)> sliderRanges = new();
    private static bool CurveNumber(string label, ref double value, double min, double max, ref bool finished)
    {
        ImGui.PushID(label);
        ImGui.TextUnformatted(label.Split("##")[0]);
        ImGui.SetNextItemWidth(Math.Max(80, ImGui.GetContentRegionAvail().X * .35f));
        double typed = value;
        bool changed = ImGui.InputDouble("##number", ref typed, 0, 0, "%.17g") && double.IsFinite(typed) && Math.Abs(typed) <= 1e9;
        if (changed) value = typed;
        finished |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.SameLine(); ImGui.SetNextItemWidth(-1);
        uint sliderId = ImGui.GetID("##slider");
        if (sliderRanges.TryGetValue(sliderId, out var held)) { min = held.Min; max = held.Max; }
        float sliding = (float)value;
        if (ImGui.SliderFloat("##slider", ref sliding, (float)min, (float)Math.Max(min + 1e-6, max), "%.4g")) { value = sliding; changed = true; }
        finished |= ImGui.IsItemDeactivatedAfterEdit();
        if (ImGui.IsItemActivated()) sliderRanges[sliderId] = (min, max);
        if (ImGui.IsItemDeactivated()) sliderRanges.Remove(sliderId);
        ImGui.PopID(); return changed;
    }

    private static bool FamilyButton(RuleCurveFamily family, bool on)
    {
        var size = new Vec2(70, 44);
        bool clicked = ImGui.InvisibleButton("fam-" + family.Id, size);
        bool hovered = ImGui.IsItemHovered();
        if (hovered) ImGui.SetTooltip($"{family.Name}\n{family.Help}");
        var dl = ImGui.GetWindowDrawList(); var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        dl.AddRectFilled(min, max, EditorStyle.U(on ? HudTheme.Moss with { W = 0.35f } : HudTheme.PanelLight), 4);
        dl.AddRect(min, max, EditorStyle.U(on ? HudTheme.MossBright : hovered ? HudTheme.Muted : HudTheme.Muted with { W = 0.3f }), 4);
        var demo = new RuleCurve { Family = family.Id, Points = family.Id switch {
            "decay" => new() { (0.1, 0.9), (0.9, 0.1) }, "bell" => new() { (0.5, 0.9), (0.85, 0.1) }, "step" => new() { (0.5, 0.15) },
            "points" => new() { (0.1, 0.1), (0.4, 0.8), (0.7, 0.4), (0.9, 0.9) }, _ => new() { (0.1, 0.1), (0.9, 0.9) } }, Above = 0.85 };
        var colour = EditorStyle.U(on ? HudTheme.Good : HudTheme.Parchment);
        Vec2 P(double x) => new(min.X + 6 + (float)x * (size.X - 12), min.Y + 4 + (1 - (float)Math.Clamp(demo.Evaluate(x), 0, 1)) * 22);
        for (int i = 0; i < 30; i++) dl.AddLine(P(i / 30.0), P((i + 1) / 30.0), colour, 1.6f);
        var label = family.Name; var ts = ImGui.CalcTextSize(label);
        dl.AddText(new Vec2(min.X + (size.X - ts.X) / 2, max.Y - ts.Y - 1), EditorStyle.U(on ? HudTheme.Parchment : HudTheme.Muted), label);
        return clicked;
    }

    /// <summary>The plot: grid, curve, draggable points, and the value under the mouse.</summary>
    private bool CurvePlot(string key, RuleCurve curve, RuleFieldSchema field, bool editable, ref bool finished, float height = 190)
    {
        var (x0, x1) = InputRange(curve, field);
        var ys = Enumerable.Range(0, 41).Select(i => curve.Evaluate(x0 + (x1 - x0) * i / 40)).Concat(curve.Points.Select(p => p.Y)).Append(curve.Above).Append(0).ToArray();
        double y0 = ys.Min(), y1 = ys.Max(); double pad = Math.Max(1e-3, (y1 - y0) * 0.12); y0 -= pad; y1 += pad;

        if (draggedCurve == key && draggedPoint >= 0 && draggedBounds is { } bounds) {
            x0 = bounds.X0; x1 = bounds.X1; y0 = bounds.Y0; y1 = bounds.Y1;
        }
        float width = Math.Max(160, ImGui.GetContentRegionAvail().X);
        ImGui.InvisibleButton("plot", new Vec2(width, height));
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        const float left = 46, bottom = 20, top = 8, right = 10;
        float X(double x) => min.X + left + (float)((x - x0) / (x1 - x0)) * (width - left - right);
        float Y(double y) => min.Y + top + (1 - (float)((y - y0) / (y1 - y0))) * (height - top - bottom);
        double Xi(float px) => x0 + (px - min.X - left) / (width - left - right) * (x1 - x0);
        double Yi(float py) => y0 + (1 - (py - min.Y - top) / (height - top - bottom)) * (y1 - y0);

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(min, max, EditorStyle.U(HudTheme.Panel), 4);
        uint grid = EditorStyle.U(HudTheme.Muted with { W = 0.18f }), text = EditorStyle.U(HudTheme.Muted);
        for (int i = 0; i <= 4; i++) {
            double gx = x0 + (x1 - x0) * i / 4, gy = y0 + (y1 - y0) * i / 4;
            dl.AddLine(new(X(gx), min.Y + top), new(X(gx), max.Y - bottom), grid);
            dl.AddLine(new(min.X + left, Y(gy)), new(max.X - right, Y(gy)), grid);
            string lx = gx.ToString("G3"), ly = gy.ToString("G3");
            dl.AddText(new(X(gx) - ImGui.CalcTextSize(lx).X / 2, max.Y - bottom + 3), text, lx);
            dl.AddText(new(min.X + left - 6 - ImGui.CalcTextSize(ly).X, Y(gy) - 7), text, ly);
        }
        if (y0 < 0 && y1 > 0) dl.AddLine(new(min.X + left, Y(0)), new(max.X - right, Y(0)), EditorStyle.U(HudTheme.Muted with { W = 0.6f }));

        uint line = EditorStyle.U(HudTheme.Good);
        Vec2 prev = default;
        for (int i = 0; i <= 120; i++) {
            double x = x0 + (x1 - x0) * i / 120; var p = new Vec2(X(x), Y(curve.Evaluate(x)));
            if (i > 0) dl.AddLine(prev, p, line, 2.4f);
            prev = p;
        }

        // Handles: the curve's points (and a step's level above).
        var handles = curve.Points.Select((p, i) => (i, new Vec2(X(p.X), Y(p.Y)))).ToList();
        if (curve.Family == "step") handles.Add((curve.Points.Count, new Vec2(X(curve.Points[0].X) + 18, Y(curve.Above))));
        var mouse = ImGui.GetIO().MousePos; bool changed = false;
        if (editable) {
            if (ImGui.IsItemActivated()) {
                var near = handles.OrderBy(h => Vec2.Distance(h.Item2, mouse)).FirstOrDefault();
                draggedPoint = handles.Count > 0 && Vec2.Distance(near.Item2, mouse) < 12 ? near.i : -1; draggedCurve = key; draggedBounds = (x0, x1, y0, y1);
            }
            if (ImGui.IsItemActive() && draggedPoint >= 0 && draggedCurve == key) {
                double nx = Math.Clamp(Xi(mouse.X), x0, x1), ny = Yi(mouse.Y);
                if (draggedPoint < curve.Points.Count) {
                    double eps = Math.Max(1e-8, (x1 - x0) * .0001);
                    nx = Snap(nx, x1 - x0);
                    if (draggedPoint > 0) nx = Math.Max(nx, curve.Points[draggedPoint - 1].X + eps);
                    if (draggedPoint + 1 < curve.Points.Count) nx = Math.Min(nx, curve.Points[draggedPoint + 1].X - eps);
                    curve.Points[draggedPoint] = (nx, Snap(ny, y1 - y0));
                }
                else curve.Above = Snap(ny, y1 - y0);
                changed = true;
            }
            if (ImGui.IsItemDeactivated() && draggedCurve == key) { if (draggedPoint >= 0) finished = true; draggedPoint = -1; }
        }
        foreach (var (i, h) in handles) {
            bool hot = editable && (draggedPoint == i && draggedCurve == key || Vec2.Distance(h, mouse) < 12 && ImGui.IsItemHovered());
            dl.AddCircleFilled(h, hot ? 7 : 5.5f, EditorStyle.U(HudTheme.Panel));
            dl.AddCircle(h, hot ? 7 : 5.5f, EditorStyle.U(editable ? HudTheme.AmberAccent : HudTheme.Muted), 0, 2.2f);
        }
        // Probe: the value of the curve under the mouse.
        if (ImGui.IsItemHovered() && mouse.X > min.X + left && mouse.X < max.X - right) {
            double x = Xi(mouse.X), y = curve.Evaluate(x);
            dl.AddLine(new(mouse.X, min.Y + top), new(mouse.X, max.Y - bottom), EditorStyle.U(HudTheme.AmberAccent with { W = 0.6f }));
            dl.AddCircleFilled(new(mouse.X, Y(y)), 4, EditorStyle.U(HudTheme.AmberAccent));
            string probe = $"{curve.Input} = {x:G3}  ›  {y:G3}{curve.Tail}";
            dl.AddText(new(min.X + left + 6, min.Y + top + 2), EditorStyle.U(HudTheme.AmberAccent), probe);
        }
        return changed;
    }

    private static double Snap(double v, double span) { double step = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(span, 1e-9))) - 2); return Math.Round(v / step) * step; }

    /// <summary>The plot's input range: the field's declared range when finite, otherwise around the curve's points.</summary>
    private static (double, double) InputRange(RuleCurve curve, RuleFieldSchema field)
    {
        double lo = curve.Points.Min(p => p.X), hi = curve.Points.Max(p => p.X);
        if (field != null && double.IsFinite(field.Min) && double.IsFinite(field.Max)) return (Math.Min(field.Min, lo), Math.Max(field.Max, hi));
        double from = field != null && double.IsFinite(field.Min) ? Math.Min(field.Min, lo) : Math.Min(0, lo);
        double span = Math.Max(hi - from, Math.Abs(hi) + 1e-3);
        return (from, hi + span * 0.5);
    }

    /// <summary>A scope field or relation field (“tile.waterSupply”) of an object kind, from the object model.</summary>
    private static RuleFieldSchema SchemaField(string scope, string name)
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
}
