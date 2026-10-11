using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

/// <summary>
/// The whole game as one stock-and-flow model: quantities in subsystem columns ordered along the causal chain
/// (weather and water, forest, harvesting, roads, economy), their influences, coupled groups and feedback loops.
/// A quantity or loop selection explains itself in the inspector; its processes open the rule workshop.
/// </summary>
internal sealed class SystemModelView
{
    // Left to right roughly follows the material chain: water feeds trees, trees become timber, roads carry it, it is sold.
    private static readonly string[] ColumnOrder = { "environment", "forest", "logistics", "roads", "economy", "world", "visual" };
    private const float NodeWidth = 150, NodeHeight = 26, RowStep = 31, ColumnStep = 192, ColumnTop = 40;

    private SystemModelAnalysis laidOut;
    private bool laidOutAll, showAll, fitRequested = true;
    private readonly Dictionary<string, Vec2> positions = new(StringComparer.Ordinal);
    private readonly List<(string Subsystem, Vec2 At)> headings = new();
    private Vec2 pan = new(20, 20);
    private float zoom = 0.8f;
    private string search = "";

    internal string SelectedVariable { get; private set; }
    internal SystemLoop SelectedLoop { get; private set; }
    internal void Focus(string variable) { SelectedVariable = variable; SelectedLoop = null; fitRequested = true; }
    internal void FocusLoop(SystemLoop loop) { SelectedLoop = loop; SelectedVariable = null; }

    internal static Vec4 SubsystemColour(SystemModelAnalysis model, string subsystem) =>
        EditorStyle.TopicColour(Math.Max(0, model.Vocabulary.SubsystemIndex(subsystem)));

    /// <summary>A loop as one readable line, closed back to its first quantity.</summary>
    internal static string Chain(SystemModelAnalysis model, SystemLoop loop) =>
        string.Join(" › ", loop.Variables.Append(loop.Variables[0]).Select(model.Name));

    internal void Draw(SystemModelAnalysis model, Action<SystemProcess> openProcess, Action<SystemVariable> openVariable)
    {
        if (SelectedVariable != null && model.Variable(SelectedVariable) == null) SelectedVariable = null;
        if (laidOut != model || laidOutAll != showAll) Layout(model);
        float inspector = Math.Min(330, ImGui.GetContentRegionAvail().X * .32f);
        ImGui.BeginChild("system-canvas", new Vec2(-inspector - 8, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        Canvas(model);
        ImGui.EndChild(); ImGui.SameLine();
        ImGui.BeginChild("system-inspector", new Vec2(0, 0), ImGuiChildFlags.Borders);
        if (SelectedLoop != null) LoopDetails(model, SelectedLoop, openProcess);
        else if (model.Variable(SelectedVariable) is SystemVariable v) VariableDetails(model, v, openProcess, openVariable);
        else Overview(model);
        ImGui.EndChild();
    }

    private void Layout(SystemModelAnalysis model)
    {
        laidOut = model; laidOutAll = showAll; positions.Clear(); headings.Clear(); fitRequested = true;
        int column = 0;
        foreach (string subsystem in ColumnOrder.Concat(model.Vocabulary.Subsystems.Select(s => s.Id)).Distinct()) {
            var members = model.Variables.Where(v => v.Subsystem == subsystem && (showAll || SystemVocabulary.Endogenous(v.Kind)))
                .OrderBy(v => v.Kind).ThenBy(v => v.Name, StringComparer.CurrentCulture).ToArray();
            if (members.Length == 0) continue;
            var at = new Vec2(column++ * ColumnStep, 0);
            headings.Add((subsystem, at));
            for (int i = 0; i < members.Length; i++) positions[members[i].Id] = at + new Vec2(0, ColumnTop + i * RowStep);
        }
    }

    private void Canvas(SystemModelAnalysis model)
    {
        Vec2 origin = ImGui.GetCursorScreenPos(), size = ImGui.GetContentRegionAvail();
        size = new Vec2(Math.Max(1, size.X), Math.Max(1, size.Y));
        if (fitRequested && positions.Count > 0) Fit(size);
        ImGui.InvisibleButton("system-map", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        bool hover = ImGui.IsItemHovered(); var io = ImGui.GetIO();
        if (hover && ImGui.IsMouseDragging(ImGuiMouseButton.Right)) pan += io.MouseDelta;
        if (hover && io.MouseWheel != 0) {
            float next = Math.Clamp(zoom * (1 + io.MouseWheel * 0.1f), 0.3f, 1.8f);
            pan = io.MousePos - origin - (io.MousePos - origin - pan) * next / zoom; zoom = next;
        }
        var draw = ImGui.GetWindowDrawList(); draw.PushClipRect(origin, origin + size, true);
        EditorStyle.Grid(draw, origin, size, pan, zoom);
        Vec2 Screen(Vec2 p) => origin + pan + p * zoom;
        Vec2 box = new Vec2(NodeWidth, NodeHeight) * zoom;

        string over = null;
        foreach (var (id, p) in positions) if (hover && Inside(io.MousePos, Screen(p), box)) over = id;
        string query = search.Trim();
        bool Matches(string id) => query.Length > 0 && (model.Name(id) + " " + id).Contains(query, StringComparison.OrdinalIgnoreCase);

        // What is in focus: a loop, or a quantity with its direct neighbours, or (hovered) a quantity under the cursor.
        string focus = SelectedLoop == null ? SelectedVariable ?? over : null;
        var loopEdges = new HashSet<(string, string)>();
        if (SelectedLoop != null)
            for (int i = 0; i < SelectedLoop.Length; i++) loopEdges.Add((SelectedLoop.Variables[i], SelectedLoop.Variables[(i + 1) % SelectedLoop.Length]));
        var lit = new HashSet<string>(StringComparer.Ordinal);
        if (SelectedLoop != null) lit.UnionWith(SelectedLoop.Variables);
        else if (focus != null) {
            lit.Add(focus);
            foreach (var e in model.Influences) { if (e.From == focus) lit.Add(e.To); if (e.To == focus) lit.Add(e.From); }
        }
        bool dimming = lit.Count > 0 || query.Length > 0;

        foreach (var (subsystem, at) in headings) {
            Vec4 colour = SubsystemColour(model, subsystem); Vec2 h = Screen(at);
            draw.AddRectFilled(h, h + new Vec2(NodeWidth * zoom, 3 * zoom), EditorStyle.U(EditorStyle.Fade(colour, 0.85f)), 2);
            string name = model.Vocabulary.Subsystems.Find(s => s.Id == subsystem)?.Name.ToUpperInvariant() ?? subsystem;
            draw.AddText(ImGui.GetFont(), Math.Max(11, ImGui.GetFontSize() * zoom * .85f), h + new Vec2(0, 8 * zoom), EditorStyle.U(colour), name);
        }

        // Influences: faint context, coloured around the focus, a bold arrowed ring for the selected loop.
        foreach (var e in model.Influences) {
            if (!positions.TryGetValue(e.From, out var a) || !positions.TryGetValue(e.To, out var b)) continue;
            bool inLoop = loopEdges.Contains((e.From, e.To));
            bool incoming = focus != null && e.To == focus, outgoing = focus != null && e.From == focus;
            if (SelectedLoop != null && !inLoop) { if (dimming) continue; }
            uint colour = inLoop || outgoing ? EditorStyle.Selection : incoming ? EditorStyle.U(EditorStyle.OperationKind)
                : EditorStyle.U(1, 1, 1, dimming ? 0.025f : 0.06f);
            float width = inLoop ? 3f : incoming || outgoing ? 2f : 1f;
            Edge(draw, Screen(a), Screen(b), box, (ColumnStep - NodeWidth) * zoom, colour, width, inLoop || incoming || outgoing);
        }

        foreach (var (id, p) in positions) {
            var v = model.Variable(id); Vec2 s = Screen(p);
            Vec4 colour = SubsystemColour(model, v.Subsystem);
            bool selected = id == SelectedVariable || SelectedLoop?.Variables.Contains(id) == true;
            float alpha = !dimming || lit.Contains(id) || Matches(id) ? 1 : 0.28f;
            float radius = v.Kind switch { SystemVariableKind.Stock => 2, SystemVariableKind.Flow => NodeHeight / 2, SystemVariableKind.Auxiliary => 9, _ => 4 } * zoom;
            Vec4 fill = v.Kind switch {
                SystemVariableKind.Stock => EditorStyle.Dim(colour, 0.38f), SystemVariableKind.Flow => EditorStyle.Dim(colour, 0.24f),
                SystemVariableKind.Auxiliary => new Vec4(0.115f, 0.145f, 0.125f, 1), _ => new Vec4(0.09f, 0.11f, 0.10f, 1) };
            draw.AddRectFilled(s, s + box, EditorStyle.U(EditorStyle.Fade(fill, alpha * (id == over ? 1 : 0.95f))), radius);
            float border = selected ? 2.6f : v.Kind == SystemVariableKind.Stock ? 1.8f : 1f;
            uint borderColour = selected ? EditorStyle.Selection : Matches(id) ? EditorStyle.U(HudTheme.Info)
                : EditorStyle.U(EditorStyle.Fade(SystemVocabulary.Endogenous(v.Kind) ? colour : HudTheme.Muted, alpha * (v.Kind == SystemVariableKind.Stock ? 0.95f : 0.55f)));
            draw.AddRect(s, s + box, borderColour, radius, ImDrawFlags.None, border);
            draw.PushClipRect(s, s + box, true);
            float font = Math.Max(11, ImGui.GetFontSize() * zoom * .92f);
            draw.AddText(ImGui.GetFont(), font, s + new Vec2(8 * zoom, (box.Y - font) / 2),
                EditorStyle.U(EditorStyle.Fade(SystemVocabulary.Endogenous(v.Kind) ? HudTheme.Parchment : HudTheme.Muted, Math.Max(alpha, 0.35f))), v.Name);
            draw.PopClipRect();
        }
        draw.PopClipRect();

        if (hover && over != null) {
            var v = model.Variable(over);
            ImGui.SetTooltip(v.Name + "\n" + SystemVocabulary.KindName(v.Kind) + (v.Unit.Length > 0 ? " · " + v.Unit : "")
                + $"\nVáltoztatja: {model.Writers(over).Count} folyamat · olvassa: {model.Readers(over).Count}");
        }
        if (hover && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { SelectedVariable = over; SelectedLoop = null; }
        if (ImGui.IsWindowFocused() && !io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.F)) fitRequested = true;

        // Overlay: search, view controls and the shape legend.
        ImGui.SetCursorScreenPos(origin + new Vec2(10, 8));
        ImGui.SetNextItemWidth(200); ImGui.InputTextWithHint("##system-search", "Mennyiség keresése…", ref search, 80);
        ImGui.SameLine(); if (ImGui.Checkbox("Adatok és szerkezet is", ref showAll)) fitRequested = true;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Paraméterek, térkép, órák, parancsok és megjelenítés. Ezek nem vesznek részt visszacsatolásban.");
        ImGui.SameLine(); if (EditorStyle.QuietButton("Igazítás", "Minden elem a képbe (F)")) fitRequested = true;
        Legend(draw, origin + new Vec2(10, size.Y - 26));
    }

    private static void Legend(ImDrawListPtr draw, Vec2 at)
    {
        var colour = EditorStyle.U(HudTheme.Muted);
        void Item(string label, float radius, float border)
        {
            draw.AddRect(at, at + new Vec2(26, 14), colour, radius, ImDrawFlags.None, border);
            draw.AddText(at + new Vec2(32, -1), colour, label);
            at.X += 40 + ImGui.CalcTextSize(label).X + 12;
        }
        Item("Készlet", 1, 1.8f); Item("Áramlás", 7, 1); Item("Segédváltozó", 4, 1);
        draw.AddLine(at + new Vec2(0, 7), at + new Vec2(26, 7), EditorStyle.Selection, 2);
        draw.AddText(at + new Vec2(32, -1), colour, "hatás a kijelölésből");
        at.X += 40 + ImGui.CalcTextSize("hatás a kijelölésből").X + 12;
        draw.AddLine(at + new Vec2(0, 7), at + new Vec2(26, 7), EditorStyle.U(EditorStyle.OperationKind), 2);
        draw.AddText(at + new Vec2(32, -1), colour, "hatás a kijelölésre");
    }

    private static void Edge(ImDrawListPtr draw, Vec2 a, Vec2 b, Vec2 box, float gap, uint colour, float width, bool arrow)
    {
        Vec2 start, end, c1, c2; float pull;
        if (Math.Abs(a.X - b.X) < 1) {
            // Same column: bow into the gap on the right, without crossing into the next column.
            start = a + new Vec2(box.X, box.Y / 2); end = b + new Vec2(box.X, box.Y / 2);
            pull = Math.Min(10 + Math.Abs(b.Y - a.Y) * 0.2f, gap * 1.1f);
            c1 = start + new Vec2(pull, 0); c2 = end + new Vec2(pull, 0);
        } else {
            bool forward = b.X > a.X;
            start = a + new Vec2(forward ? box.X : 0, box.Y / 2); end = b + new Vec2(forward ? 0 : box.X, box.Y / 2);
            pull = Math.Max(30, Math.Abs(end.X - start.X) * 0.45f) * (forward ? 1 : -1);
            c1 = start + new Vec2(pull, 0); c2 = end - new Vec2(pull, 0);
        }
        if (arrow) EditorStyle.BezierArrow(draw, start, c1, c2, end, 8, colour, width);
        else draw.AddBezierCubic(start, c1, c2, end, colour, width);
    }

    private void Fit(Vec2 size)
    {
        Vec2 min = new(positions.Values.Min(p => p.X), 0), max = new(positions.Values.Max(p => p.X) + NodeWidth, positions.Values.Max(p => p.Y) + NodeHeight);
        Vec2 content = max - min + new Vec2(30, 100);
        zoom = Math.Clamp(Math.Min(size.X / content.X, size.Y / content.Y), 0.3f, 1.1f);
        pan = new Vec2((size.X - (max.X - min.X) * zoom) / 2, 46) - min * zoom;
        fitRequested = false;
    }

    private static bool Inside(Vec2 p, Vec2 min, Vec2 size) => p.X >= min.X && p.Y >= min.Y && p.X <= min.X + size.X && p.Y <= min.Y + size.Y;

    // ── Inspector ────────────────────────────────────────────────────────
    private void Overview(SystemModelAnalysis model)
    {
        EditorStyle.Title("A játék rendszermodellje");
        ImGui.TextWrapped("Minden szabály mennyiségeket olvas és ír. A készletek felhalmozódnak, az áramlások töltik vagy apasztják őket, a segédváltozók köztes számítások. Kattints egy mennyiségre vagy egy körre.");
        var counts = model.Variables.GroupBy(v => v.Kind).ToDictionary(g => g.Key, g => g.Count());
        if (EditorStyle.BeginFacts("system-counts")) {
            foreach (var kind in new[] { SystemVariableKind.Stock, SystemVariableKind.Flow, SystemVariableKind.Auxiliary })
                EditorStyle.Fact(SystemVocabulary.KindName(kind), counts.GetValueOrDefault(kind).ToString());
            EditorStyle.Fact("Folyamat", model.Processes.Count.ToString());
            ImGui.EndTable();
        }
        EditorStyle.Section("Összecsatolt csoportok");
        ImGui.TextWrapped("Egy csoporton belül minden mennyiség — közvetve — visszahat a többire, ezért ott egy módosítás hatása körbeér.");
        foreach (var group in model.CoupledGroups) {
            var subsystems = group.Select(id => model.Variable(id).Subsystem).Distinct().ToArray();
            ImGui.Bullet(); ImGui.SameLine();
            ImGui.TextWrapped($"{group.Count} mennyiség · " + string.Join(", ", subsystems.Select(s => model.Vocabulary.Subsystems.Find(x => x.Id == s)?.Name ?? s)));
        }
        EditorStyle.Section("Fő visszacsatolási körök");
        ImGui.TextWrapped("Minden készlet legrövidebb köre; elöl a több alrendszert összekötők.");
        foreach (var loop in model.KeyLoops)
            if (ImGui.Selectable(Chain(model, loop) + "##key" + loop.Key)) FocusLoop(loop);
        var open = model.Variables.Where(v => v.Kind == SystemVariableKind.Stock && model.CoupledGroupOf(v.Id) == null).ToArray();
        if (open.Length > 0) {
            EditorStyle.Section("Nyitott készletek");
            ImGui.TextWrapped("Ezek változnak, de értékük nem hat vissza a saját alakulásukra. Visszacsatolást saját képlettel adhatsz hozzájuk.");
            foreach (var v in open) if (ImGui.Selectable(v.Name + "##open" + v.Id)) Focus(v.Id);
        }
        if (model.Unclassified.Count > 0) {
            EditorStyle.Section("Besorolatlan mezők");
            ImGui.TextWrapped(string.Join(", ", model.Unclassified));
        }
    }

    private void VariableDetails(SystemModelAnalysis model, SystemVariable v, Action<SystemProcess> openProcess, Action<SystemVariable> openVariable)
    {
        if (EditorStyle.QuietButton("‹ Áttekintés")) { SelectedVariable = null; return; }
        EditorStyle.Title(v.Name);
        EditorStyle.Chip(SystemVocabulary.KindName(v.Kind), SubsystemColour(model, v.Subsystem), "kind" + v.Id);
        ImGui.TextWrapped(v.Description.Length > 0 ? v.Description : SystemVocabulary.KindHelp(v.Kind));
        if (EditorStyle.BeginFacts("variable-facts")) {
            EditorStyle.Fact("Alrendszer", model.Vocabulary.Subsystems.Find(s => s.Id == v.Subsystem)?.Name ?? v.Subsystem);
            EditorStyle.Fact("Egység", v.Unit.Length > 0 ? v.Unit : "nincs rögzítve");
            EditorStyle.Fact("Képletben", v.Observe.Length > 0 ? v.Observe : "nem olvasható közvetlenül");
            EditorStyle.Fact("Azonosító", v.Id);
            ImGui.EndTable();
        }
        if (v.Id.StartsWith("stock.") && ImGui.Button("Készlet szerkesztése")) openVariable(v);
        ProcessList(model, v.Kind == SystemVariableKind.Stock ? "Be- és kiáramlások (ezek változtatják)" : "Ezek számolják", model.Writers(v.Id), openProcess, "w");
        ProcessList(model, "Ezek használják", model.Readers(v.Id), openProcess, "r");
        EditorStyle.Section("Visszacsatolási körök");
        var loops = model.LoopsThroughVariable(v.Id);
        if (!SystemVocabulary.Endogenous(v.Kind)) ImGui.TextWrapped("Külső vagy rögzített érték: nem része visszacsatolásnak.");
        else if (loops.Count == 0) ImGui.TextWrapped("Nincs kör: a hatása nem tér vissza ehhez a mennyiséghez.");
        foreach (var loop in loops.Take(8)) if (ImGui.Selectable(Chain(model, loop) + "##vl" + loop.Key)) FocusLoop(loop);
        if (loops.Count > 8) EditorStyle.Text(HudTheme.Muted, $"+ {loops.Count - 8} további kör");
    }

    private static void ProcessList(SystemModelAnalysis model, string title, IReadOnlyList<SystemProcess> processes, Action<SystemProcess> open, string prefix)
    {
        EditorStyle.Section(title);
        if (processes.Count == 0) { EditorStyle.Text(HudTheme.Muted, "Nincs katalogizált folyamat."); return; }
        foreach (var p in processes) {
            if (ImGui.Selectable(p.Name + "##" + prefix + p.Id)) open(p);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Megnyitás a szabályműhelyben\n" + p.Id);
        }
    }

    private void LoopDetails(SystemModelAnalysis model, SystemLoop loop, Action<SystemProcess> openProcess)
    {
        if (EditorStyle.QuietButton("‹ Áttekintés")) { SelectedLoop = null; return; }
        EditorStyle.Title($"Visszacsatolási kör · {loop.Length} lépés");
        ImGui.TextWrapped("A kör minden lépése egy folyamat, amely az egyik mennyiséget olvassa és a következőt írja. A katalógus az adatkapcsolatot rögzíti, a hatás előjelét nem: hogy a kör erősít vagy kiegyenlít, azt az összehasonlító próba mutatja meg.");
        for (int i = 0; i < loop.Length; i++) {
            ImGui.PushID(i);
            if (ImGui.Selectable(model.Name(loop.Variables[i]) + "##lv")) Focus(loop.Variables[i]);
            ImGui.Indent(18);
            foreach (string p in loop.Steps[i]) {
                var process = model.Process(p);
                if (EditorStyle.Chip(process.Name, SubsystemColour(model, process.Subsystem), "step" + p, true)) openProcess(process);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Szabály megnyitása");
            }
            ImGui.Unindent(18);
            ImGui.PopID();
        }
        EditorStyle.Text(HudTheme.Muted, "› vissza: " + model.Name(loop.Variables[0]));
        var spans = loop.Variables.Select(v => model.Variable(v).Subsystem).Distinct().Select(s => model.Vocabulary.Subsystems.Find(x => x.Id == s)?.Name ?? s);
        ImGui.Spacing(); ImGui.TextWrapped("Érintett alrendszerek: " + string.Join(", ", spans));
    }
}
