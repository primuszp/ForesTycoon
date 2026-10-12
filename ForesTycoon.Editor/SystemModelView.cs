using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor;

/// <summary>What the system map needs from its host: live values, one tile's section and the workshop's editing panels.</summary>
internal sealed class SystemModelHost
{
    internal Func<string, double> Value = _ => double.NaN;
    internal Func<int, IReadOnlyDictionary<string, string>> Tile = _ => new Dictionary<string, string>();
    internal int TileCount;
    internal Action<SystemProcess> OpenProcess = _ => { };
    internal Action<SystemProcess> DrawProcess = _ => { };
    internal Action<SystemVariable> DrawQuantity = _ => { };
    internal Func<SystemProcess, string> Editable = _ => "";
}

/// <summary>
/// The game model as a stack of tile layers — atmosphere, vegetation, surface, terrain, soil, groundwater — beside the
/// non-spatial management. Every rule is a link between quantities, often across layers (soil and groundwater decide
/// how passable a dirt track is). Three levels drawn like the formula graphs: the layer stack, one layer with its
/// neighbours, one feedback loop. The inspector edits the selection: wording, influences, parameters and formulas.
/// </summary>
internal sealed class SystemModelView
{
    private const float BandLabel = 176, ChipWidth = 200, ChipHeight = 30, ChipStepX = 208, ChipStepY = 38, BandGap = 16, ChipColumns = 3;
    private const float BandWidth = BandLabel + ChipColumns * ChipStepX + 6;
    private const float NodeWidth = 196, NodeHeader = 24, NodeHeight = 58, GhostHeight = 40, LayerStep = 260, RowStep = 74;

    private enum Level { Stack, Layer, Loop }
    private Level level = Level.Stack, beforeLoop = Level.Stack;
    private string layer;
    private SystemModelAnalysis laidOut; private string laidOutFor, laidOutSelection; private Level laidOutLevel; private SystemLoop laidOutLoop;
    private readonly Dictionary<string, (Vec2 At, Vec2 Size, bool Ghost)> boxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Vec2 At, Vec2 Size)> bands = new(StringComparer.Ordinal);
    private readonly List<(Vec2 Min, Vec2 Max, string Process)> labels = new();
    private Vec2 pan = new(20, 20);
    private float zoom = 0.8f;
    private bool fitRequested = true;
    private string search = "";
    private int tile = -1;
    private IReadOnlyDictionary<string, string> section = new Dictionary<string, string>();

    internal string SelectedVariable { get; private set; }
    internal SystemLoop SelectedLoop { get; private set; }
    internal int SelectedTile { get => tile; set => tile = value; }

    internal void ShowStack() { level = Level.Stack; SelectedVariable = null; SelectedLoop = null; fitRequested = true; }
    internal void ShowLayer(string id) { level = Level.Layer; layer = id; SelectedLoop = null; fitRequested = true; }
    internal void Focus(SystemModelAnalysis model, string variable)
    {
        SelectedLoop = null; SelectedVariable = variable;
        if (model?.Variable(variable) is SystemVariable v && v.Layer.Length > 0) ShowLayer(v.Layer);
    }
    internal void FocusLoop(SystemLoop loop)
    {
        if (level != Level.Loop) beforeLoop = level;
        SelectedLoop = loop; SelectedVariable = null; level = Level.Loop; fitRequested = true;
    }

    // Colours follow what the layer is: air, leaves, track, rock, earth, water; management in gold.
    internal static Vec4 LayerColour(string id) => id switch {
        "atmosphere" => new(0.62f, 0.78f, 0.90f, 1), "vegetation" => new(0.47f, 0.72f, 0.38f, 1), "surface" => new(0.88f, 0.58f, 0.32f, 1),
        "terrain" => new(0.70f, 0.66f, 0.56f, 1), "soil" => new(0.66f, 0.48f, 0.32f, 1), "groundwater" => new(0.36f, 0.58f, 0.88f, 1),
        "management" => new(0.90f, 0.78f, 0.40f, 1), _ => new(0.6f, 0.6f, 0.6f, 1) };
    private static Vec4 Colour(SystemVariable v) => LayerColour(v.Layer);
    private static string LayerName(SystemModelAnalysis model, string id) => model.Vocabulary.Layers.Find(l => l.Id == id)?.Name ?? id;
    internal static string Chain(SystemModelAnalysis model, SystemLoop loop) => string.Join(" › ", loop.Variables.Append(loop.Variables[0]).Select(model.Name));
    private static bool Shown(SystemVariable v) => SystemModelAnalysis.OnLayer(v);
    private static string KindTag(SystemVariableKind kind) => kind switch {
        SystemVariableKind.Stock => "KÉSZLET", SystemVariableKind.Flow => "ÁRAMLÁS", SystemVariableKind.Auxiliary => "SEGÉD",
        SystemVariableKind.Parameter => "ADAT", _ => "TÉRKÉP" };

    internal static IEnumerable<(string From, string To, SystemProcess[] Processes)> CrossLayerRules(SystemModelAnalysis model) => model.CrossLayerRules();

    internal void Draw(SystemModelAnalysis model, SystemModelHost host)
    {
        if (SelectedVariable != null && model.Variable(SelectedVariable) == null) SelectedVariable = null;
        if (level == Level.Layer && model.Vocabulary.LayerIndex(layer) < 0) ShowStack();
        if (level == Level.Loop && SelectedLoop == null) level = beforeLoop;
        section = tile >= 0 ? host.Tile(tile) : new Dictionary<string, string>();
        Breadcrumb(model, host);
        float inspector = Math.Min(360, ImGui.GetContentRegionAvail().X * .33f);
        ImGui.BeginChild("system-canvas", new Vec2(-inspector - 8, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        Canvas(model, host);
        ImGui.EndChild(); ImGui.SameLine();
        ImGui.BeginChild("system-inspector", new Vec2(0, 0), ImGuiChildFlags.Borders);
        if (SelectedLoop != null && level == Level.Loop) LoopDetails(model, SelectedLoop, host);
        else if (model.Variable(SelectedVariable) is SystemVariable v) VariableDetails(model, v, host);
        else if (level == Level.Layer) LayerDetails(model, host);
        else StackDetails(model, host);
        ImGui.EndChild();
    }

    private void Breadcrumb(SystemModelAnalysis model, SystemModelHost host)
    {
        if (level == Level.Stack) { ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Parchment, "Rétegek"); }
        else if (EditorStyle.QuietButton("‹ Rétegek")) ShowStack();
        if (level == Level.Layer) {
            ImGui.SameLine(); ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, "›"); ImGui.SameLine();
            ImGui.AlignTextToFramePadding(); EditorStyle.Text(LayerColour(layer), LayerName(model, layer));
        } else if (level == Level.Loop) {
            ImGui.SameLine(); ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, "›"); ImGui.SameLine();
            ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.AmberAccent, $"Visszacsatolási kör · {SelectedLoop?.Length} lépés");
        }
        ImGui.SameLine(0, 20);
        foreach (var l in model.Vocabulary.Layers) {
            ImGui.SameLine(0, 4);
            if (EditorStyle.Chip(l.Name, LayerColour(l.Id), "crumb" + l.Id, true)) { SelectedVariable = null; ShowLayer(l.Id); }
        }
        // The tile whose section the map shows.
        ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, "Csempe:"); ImGui.SameLine();
        ImGui.SetNextItemWidth(130);
        int value = tile;
        if (ImGui.InputInt("##section-tile", ref value)) tile = Math.Clamp(value, -1, Math.Max(-1, host.TileCount - 1));
        ImGui.SameLine();
        if (tile < 0) EditorStyle.Text(HudTheme.Muted, "nincs kiválasztva: a dobozok a világ összesített értékeit mutatják, ahol van ilyen");
        else EditorStyle.Text(HudTheme.Info, section.TryGetValue("soil.profile", out var soil) ? $"#{tile} metszete · talaj: {soil}" : $"#{tile} metszete");
    }

    // ── Layout ───────────────────────────────────────────────────────────
    private void Layout(SystemModelAnalysis model)
    {
        laidOut = model; laidOutFor = layer; laidOutLevel = level; laidOutLoop = SelectedLoop; laidOutSelection = SelectedVariable;
        boxes.Clear(); bands.Clear(); fitRequested = true;
        if (level == Level.Stack) StackLayout(model);
        else if (level == Level.Loop) {
            int n = SelectedLoop.Length; float radius = Math.Max(200, n * 78);
            for (int i = 0; i < n; i++) {
                double angle = -Math.PI / 2 + 2 * Math.PI * i / n;
                var centre = new Vec2((float)Math.Cos(angle) * radius * 1.35f, (float)Math.Sin(angle) * radius);
                boxes[SelectedLoop.Variables[i]] = (centre - new Vec2(NodeWidth, NodeHeight) / 2, new Vec2(NodeWidth, NodeHeight), false);
            }
        } else LayerLayout(model);
    }

    private void StackLayout(SystemModelAnalysis model)
    {
        float y = 0;
        foreach (var l in model.Vocabulary.Layers) {
            // Management is not per tile: only its stocks and flows, below the tile's layers.
            var members = Members(model, l.Id).Where(v => l.PerTile || v.Kind is SystemVariableKind.Stock or SystemVariableKind.Flow).ToArray();
            if (!l.PerTile) y += BandGap * 2;
            int rows = Math.Max(1, (members.Length + (int)ChipColumns - 1) / (int)ChipColumns);
            float height = Math.Max(78, 16 + rows * ChipStepY);
            bands[l.Id] = (new Vec2(0, y), new Vec2(BandWidth, height));
            for (int i = 0; i < members.Length; i++)
                boxes[members[i].Id] = (new Vec2(BandLabel + i % (int)ChipColumns * ChipStepX, y + 12 + i / (int)ChipColumns * ChipStepY), new Vec2(ChipWidth, ChipHeight), false);
            y += height + BandGap;
        }
    }

    private static SystemVariable[] Members(SystemModelAnalysis model, string id) => model.Variables.Where(v => v.Layer == id && Shown(v))
        .OrderBy(v => v.Kind).ThenBy(v => v.Name, StringComparer.CurrentCulture).ToArray();

    /// <summary>The layer's quantities in a compact grid; other layers' sources on the left, consumers on the right.</summary>
    private void LayerLayout(SystemModelAnalysis model)
    {
        var ordered = Members(model, layer);
        var members = ordered.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        int columns = ordered.Length > 12 ? 3 : 2, rows = Math.Max(1, (ordered.Length + columns - 1) / columns);
        for (int i = 0; i < ordered.Length; i++)
            boxes[ordered[i].Id] = (new Vec2(i % columns * (NodeWidth + 44), (i / columns - (rows - 1) / 2f) * RowStep), new Vec2(NodeWidth, NodeHeight), false);
        float right = columns * (NodeWidth + 44) + 60;
        // Other layers appear only around the selected quantity; the layer itself stays readable.
        bool Outside(string id) => !members.Contains(id) && model.Variable(id) is SystemVariable v && Shown(v);
        bool Around(SystemInfluence e) => SelectedVariable != null && (e.From == SelectedVariable || e.To == SelectedVariable);
        var sources = model.Influences.Where(e => members.Contains(e.To) && Outside(e.From) && Around(e)).Select(e => e.From).Distinct()
            .OrderBy(id => model.Vocabulary.LayerIndex(model.Variable(id).Layer)).ThenBy(model.Name).ToArray();
        var consumers = model.Influences.Where(e => members.Contains(e.From) && Outside(e.To) && Around(e)).Select(e => e.To).Where(id => !sources.Contains(id)).Distinct()
            .OrderBy(id => model.Vocabulary.LayerIndex(model.Variable(id).Layer)).ThenBy(model.Name).ToArray();
        Place(sources, -NodeWidth - 110, GhostHeight, true);
        Place(consumers, right, GhostHeight, true);

        void Place(IEnumerable<string> ids, float x, float height, bool ghost)
        {
            var list = ids.ToArray();
            float step = ghost ? GhostHeight + 10 : RowStep, top = -(list.Length - 1) * step / 2;
            for (int i = 0; i < list.Length; i++) boxes[list[i]] = (new Vec2(x, top + i * step), new Vec2(NodeWidth, height), ghost);
        }
    }

    // ── Canvas ───────────────────────────────────────────────────────────
    private void Canvas(SystemModelAnalysis model, SystemModelHost host)
    {
        if (laidOut != model || laidOutFor != layer || laidOutLevel != level || laidOutLoop != SelectedLoop
            || level == Level.Layer && laidOutSelection != SelectedVariable) Layout(model);
        Vec2 origin = ImGui.GetCursorScreenPos(), size = ImGui.GetContentRegionAvail();
        size = new Vec2(Math.Max(1, size.X), Math.Max(1, size.Y));
        if (fitRequested && boxes.Count + bands.Count > 0) Fit(size);
        ImGui.InvisibleButton("system-map", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        bool hover = ImGui.IsItemHovered(); var io = ImGui.GetIO();
        if (hover && ImGui.IsMouseDragging(ImGuiMouseButton.Right)) pan += io.MouseDelta;
        if (hover && io.MouseWheel != 0 && level == Level.Stack && !io.KeyCtrl) pan.Y += io.MouseWheel * 60;
        else if (hover && io.MouseWheel != 0) {
            float next = Math.Clamp(zoom * (1 + io.MouseWheel * 0.1f), 0.35f, 1.6f);
            pan = io.MousePos - origin - (io.MousePos - origin - pan) * next / zoom; zoom = next;
        }
        var draw = ImGui.GetWindowDrawList(); draw.PushClipRect(origin, origin + size, true);
        EditorStyle.Grid(draw, origin, size, pan, zoom);
        Vec2 Screen(Vec2 p) => origin + pan + p * zoom;
        string over = null, overBand = null;
        foreach (var (id, box) in boxes) if (hover && Inside(io.MousePos, Screen(box.At), box.Size * zoom)) over = id;
        if (over == null) foreach (var (id, band) in bands) if (hover && Inside(io.MousePos, Screen(band.At), band.Size * zoom)) overBand = id;
        labels.Clear();

        if (level == Level.Stack) StackCanvas(model, host, draw, Screen, over, overBand);
        else GraphCanvas(model, host, draw, Screen, over);
        draw.PopClipRect();

        string label = labels.LastOrDefault(l => Inside(io.MousePos, l.Min, l.Max - l.Min)).Process;
        if (hover && label != null) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); ImGui.SetTooltip(model.Name(label) + "\nKattintás: a szabály megnyitása"); }
        else if (hover && over != null) Tooltip(model, host, over);
        else if (hover && overBand != null) ImGui.SetTooltip(LayerName(model, overBand) + "\nKattintás: a réteg szabályai és gráfja");
        if (hover && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
            bool twice = ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
            if (label != null) host.OpenProcess(model.Process(label));
            else if (overBand != null) { SelectedVariable = null; ShowLayer(overBand); }
            else if (over != null && (twice && level != Level.Layer || level == Level.Layer && boxes[over].Ghost && twice)) Focus(model, over);
            else if (over != null && level != Level.Loop) SelectedVariable = over;
            else if (over == null && level != Level.Loop) SelectedVariable = null;
        }
        if (ImGui.IsWindowFocused() && !io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.F)) fitRequested = true;

        ImGui.SetCursorScreenPos(origin + new Vec2(10, 8));
        ImGui.SetNextItemWidth(240);
        if (ImGui.InputTextWithHint("##system-search", "Mennyiség keresése, Enter: ugrás", ref search, 80, ImGuiInputTextFlags.EnterReturnsTrue)) {
            var found = model.Variables.FirstOrDefault(v => Shown(v) && (v.Name + " " + v.Id).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
            if (found != null) Focus(model, found.Id);
        }
        ImGui.SameLine(); if (EditorStyle.QuietButton("Igazítás", "Minden elem a képbe (F)")) fitRequested = true;
        ImGui.SetCursorScreenPos(origin + new Vec2(10, size.Y - ImGui.GetTextLineHeight() - 8));
        EditorStyle.Text(EditorStyle.Fade(HudTheme.Muted, 0.85f), level switch {
            Level.Stack => "Egy csempe rétegei felülről lefelé · mennyiség: kapcsolatai · réteg neve: a réteg gráfja · görgő: lapozás, Ctrl+görgő: nagyítás",
            Level.Layer => "Kattintás egy mennyiségre: a más rétegekkel való kapcsolatai is megjelennek · halvány doboz: más réteg (dupla kattintás: oda lép)",
            _ => "Címke: a lépés szabálya · dupla kattintás egy mennyiségen: a rétege"
        });
    }

    private string ValueText(SystemModelHost host, SystemVariable v)
    {
        if (section.TryGetValue(v.Id, out var text)) return text;
        if (tile >= 0 || v.Observe.Length == 0) return null;
        double value = host.Value(v.Observe);
        return double.IsFinite(value) ? Format(value) + (v.Unit.Length > 0 ? " " + v.Unit : "") : null;
    }

    private void Tooltip(SystemModelAnalysis model, SystemModelHost host, string id)
    {
        var v = model.Variable(id); string value = ValueText(host, v);
        ImGui.SetTooltip(v.Name + "\n" + SystemVocabulary.KindName(v.Kind) + " · " + LayerName(model, v.Layer)
            + (value != null ? "\nÉrték: " + value : "")
            + $"\nVáltoztatja {model.Writers(id).Count} szabály, használja {model.Readers(id).Count}");
    }

    private void StackCanvas(SystemModelAnalysis model, SystemModelHost host, ImDrawListPtr draw, Func<Vec2, Vec2> screen, string over, string overBand)
    {
        float font = ImGui.GetFontSize() * zoom;
        var cross = CrossLayerRules(model).ToArray();
        foreach (var (id, band) in bands) {
            Vec2 p = screen(band.At), s = band.Size * zoom; Vec4 colour = LayerColour(id);
            bool perTile = model.Vocabulary.Layers.Find(l => l.Id == id)?.PerTile != false;
            float labelWidth = (BandLabel - 22) * zoom, small = Math.Max(11, font * .78f);
            draw.AddRectFilled(p, p + s, EditorStyle.U(EditorStyle.Fade(EditorStyle.Dim(colour, .32f), overBand == id ? .75f : .5f)), 6 * zoom);
            draw.AddRectFilled(p, p + new Vec2(6 * zoom, s.Y), EditorStyle.U(colour), 6 * zoom, ImDrawFlags.RoundCornersLeft);
            draw.AddRect(p, p + s, EditorStyle.U(EditorStyle.Fade(colour, overBand == id ? .9f : .35f)), 6 * zoom, ImDrawFlags.None, overBand == id ? 2 : 1);
            Text(draw, p + new Vec2(16, 8) * zoom, Math.Max(13, font * 1.0f), EditorStyle.TextBright, LayerName(model, id), labelWidth);
            int links = cross.Where(c => c.From == id || c.To == id).Sum(c => c.Processes.Length);
            Text(draw, p + new Vec2(16, 8) * zoom + new Vec2(0, Math.Max(16, font * 1.25f)), small, EditorStyle.TextMuted, perTile ? $"{links} rétegközi szabály" : "nem csempéhez kötött", labelWidth);
            Text(draw, p + new Vec2(16, 8) * zoom + new Vec2(0, Math.Max(16, font * 1.25f) + small + 4), small, EditorStyle.U(EditorStyle.Fade(colour, .9f)), "megnyitás ›", labelWidth);
        }
        string focus = SelectedVariable ?? over;
        var pills = new List<(Vec2 At, string Text, Vec4 Colour, string Process)>();
        if (focus != null && boxes.ContainsKey(focus)) {
            // The selected quantity's influences, chip to chip, with the rule on each link.
            foreach (var e in model.Influences.Where(e => (e.From == focus || e.To == focus) && boxes.ContainsKey(e.From) && boxes.ContainsKey(e.To))) {
                var a = boxes[e.From]; var b = boxes[e.To];
                Vec2 ca = screen(a.At + a.Size / 2), cb = screen(b.At + b.Size / 2);
                Vec2 start = Exit(ca, cb, screen(a.At), a.Size * zoom), end = Exit(cb, ca, screen(b.At), b.Size * zoom);
                Vec2 bend = new Vec2(-(cb - ca).Y, (cb - ca).X) * .12f, mid = (start + end) / 2 + bend;
                bool outgoing = e.From == focus;
                uint colour = outgoing ? EditorStyle.Selection : EditorStyle.U(EditorStyle.OperationKind);
                EditorStyle.BezierArrow(draw, start, Vec2.Lerp(start, mid, .7f), Vec2.Lerp(end, mid, .7f), end, 8 * zoom + 3, colour, 2.2f);
                pills.Add((mid, e.Processes.Count == 1 ? model.Name(e.Processes[0]) : model.Name(e.Processes[0]) + $" +{e.Processes.Count - 1}",
                    outgoing ? HudTheme.AmberAccent : EditorStyle.OperationKind, e.Processes[0]));
            }
        }
        var lit = new HashSet<string>(StringComparer.Ordinal);
        if (focus != null) { lit.Add(focus); foreach (var e in model.Influences) { if (e.From == focus) lit.Add(e.To); if (e.To == focus) lit.Add(e.From); } }
        foreach (var (id, box) in boxes) {
            var v = model.Variable(id); Vec2 p = screen(box.At), s = box.Size * zoom; Vec4 colour = Colour(v);
            float alpha = focus == null || lit.Contains(id) ? 1 : .4f;
            draw.AddRectFilled(p, p + s, EditorStyle.U(id == over ? .15f : .10f, id == over ? .19f : .13f, id == over ? .16f : .11f, .96f * Math.Max(alpha, .7f)), 5 * zoom);
            draw.AddRectFilled(p, p + new Vec2(4 * zoom, s.Y), EditorStyle.U(EditorStyle.Fade(colour, alpha)), 5 * zoom, ImDrawFlags.RoundCornersLeft);
            draw.AddRect(p, p + s, id == SelectedVariable ? EditorStyle.Selection : EditorStyle.U(EditorStyle.Fade(colour, (v.Kind == SystemVariableKind.Stock ? .9f : .35f) * alpha)),
                5 * zoom, ImDrawFlags.None, id == SelectedVariable ? 2.4f : v.Kind == SystemVariableKind.Stock ? 1.8f : 1);
            string value = ValueText(host, v);
            float chipFont = Math.Max(12, font * .88f), valueFont = Math.Max(11, font * .82f);
            float valueWidth = value == null ? 0 : Math.Min(s.X * .45f, ImGui.CalcTextSize(value).X * valueFont / ImGui.GetFontSize() + 6);
            Text(draw, p + new Vec2(10 * zoom, (s.Y - chipFont) / 2), chipFont, EditorStyle.U(EditorStyle.Fade(HudTheme.Parchment, Math.Max(alpha, .5f))), v.Name, s.X - valueWidth - 14 * zoom);
            if (value != null) Text(draw, p + new Vec2(s.X - valueWidth - 4 * zoom, (s.Y - valueFont) / 2), valueFont, EditorStyle.U(EditorStyle.Fade(colour, alpha)), value, valueWidth);
        }
        foreach (var (at, text, colour, process) in pills) { var rect = Pill(draw, at, text, colour); labels.Add((rect.Min, rect.Max, process)); }
    }

    private void GraphCanvas(SystemModelAnalysis model, SystemModelHost host, ImDrawListPtr draw, Func<Vec2, Vec2> screen, string over)
    {
        string focus = SelectedVariable ?? (over != null && !boxes[over].Ghost ? over : null);
        var edges = new List<(string From, string To, IReadOnlyList<string> Via)>();
        if (level == Level.Loop)
            for (int i = 0; i < SelectedLoop.Length; i++) edges.Add((SelectedLoop.Variables[i], SelectedLoop.Variables[(i + 1) % SelectedLoop.Length], SelectedLoop.Steps[i]));
        else edges.AddRange(model.Influences.Where(e => boxes.ContainsKey(e.From) && boxes.ContainsKey(e.To) && !(boxes[e.From].Ghost && boxes[e.To].Ghost))
            .Select(e => (e.From, e.To, e.Processes)));
        var lit = new HashSet<string>(StringComparer.Ordinal);
        if (focus != null) { lit.Add(focus); foreach (var e in edges) { if (e.From == focus) lit.Add(e.To); if (e.To == focus) lit.Add(e.From); } }

        float PortY(string id) => boxes[id].Ghost ? boxes[id].Size.Y / 2 : NodeHeader + (NodeHeight - NodeHeader) / 2;
        Vec2 Out(string id) => screen(boxes[id].At + new Vec2(NodeWidth, PortY(id)));
        Vec2 In(string id) => screen(boxes[id].At + new Vec2(0, PortY(id)));
        var pills = new List<(Vec2 At, string Text, Vec4 Colour, string Process)>();
        foreach (var (from, to, via) in edges) {
            bool ring = level == Level.Loop, outgoing = focus == from, incoming = focus == to;
            uint colour = ring || outgoing ? EditorStyle.Selection : incoming ? EditorStyle.U(EditorStyle.OperationKind)
                : EditorStyle.U(EditorStyle.Fade(HudTheme.Parchment, focus == null ? 0.2f : 0.05f));
            Vec2 a = Out(from), b = In(to);
            float pull = Math.Max(60 * zoom, Math.Abs(b.X - a.X) * 0.45f);
            Vec2 c1 = a + new Vec2(pull, 0), c2 = b - new Vec2(pull, 0);
            EditorStyle.BezierArrow(draw, a, c1, c2, b, 8 * zoom + 3, colour, ring ? 3 : outgoing || incoming ? 2.2f : 1.3f);
            // One label per rule: several inputs of the same rule share it.
            if (ring || (outgoing || incoming) && !pills.Any(p => p.Process == via[0] && !ring))
                pills.Add((Bezier(a, c1, c2, b, .5f), via.Count == 1 ? model.Name(via[0]) : model.Name(via[0]) + $" +{via.Count - 1}",
                    ring || outgoing ? HudTheme.AmberAccent : EditorStyle.OperationKind, via[0]));
        }
        foreach (var (id, box) in boxes) {
            var v = model.Variable(id); Vec2 p = screen(box.At), s = box.Size * zoom;
            Vec4 colour = Colour(v);
            float alpha = focus == null || lit.Contains(id) ? 1 : 0.35f;
            if (box.Ghost) alpha *= 0.75f;
            NodeFrame(draw, p, s, colour, id == over, id == SelectedVariable, alpha, box.Ghost ? 0 : NodeHeader, v.Kind == SystemVariableKind.Stock);
            float font = Math.Max(13, ImGui.GetFontSize() * zoom);
            uint text = EditorStyle.U(EditorStyle.Fade(HudTheme.Parchment, Math.Max(alpha, .45f)));
            if (box.Ghost) {
                draw.AddRectFilled(p, p + new Vec2(5 * zoom, s.Y), EditorStyle.U(EditorStyle.Fade(colour, alpha)), 8 * zoom, ImDrawFlags.RoundCornersLeft);
                Text(draw, p + new Vec2(12, 4) * zoom, font * .9f, text, v.Name, s.X - 18 * zoom);
                Text(draw, p + new Vec2(12, 21) * zoom, font * .75f, EditorStyle.TextMuted, LayerName(model, v.Layer), s.X - 18 * zoom);
                continue;
            }
            string tag = KindTag(v.Kind); float tagWidth = ImGui.CalcTextSize(tag).X * zoom * .72f;
            Text(draw, p + new Vec2(10, 4) * zoom, font * .92f, text, v.Name, s.X - tagWidth - 22 * zoom);
            draw.AddText(ImGui.GetFont(), font * .72f, p + new Vec2(s.X - tagWidth - 8 * zoom, 7 * zoom), EditorStyle.U(EditorStyle.Fade(HudTheme.Parchment, .6f * alpha)), tag);
            string body = ValueText(host, v) ?? (v.Unit.Length > 0 ? v.Unit : "—");
            Text(draw, p + new Vec2(12, NodeHeader + 9) * zoom, font * 1.0f, EditorStyle.U(EditorStyle.Fade(colour, alpha)), body, s.X * .66f);
            int loops = model.LoopsThroughVariable(id).Count;
            if (loops > 0) {
                string count = $"{loops} kör"; float w = ImGui.CalcTextSize(count).X * zoom * .78f;
                draw.AddText(ImGui.GetFont(), font * .78f, p + new Vec2(s.X - w - 10 * zoom, (NodeHeader + 13) * zoom), EditorStyle.TextMuted, count);
            }
            float portY = (NodeHeader + (NodeHeight - NodeHeader) / 2) * zoom;
            draw.AddCircleFilled(p + new Vec2(0, portY), 4.5f * zoom, EditorStyle.U(EditorStyle.Fade(colour, alpha)));
            draw.AddCircleFilled(p + new Vec2(s.X, portY), 4.5f * zoom, EditorStyle.U(EditorStyle.Fade(colour, alpha)));
        }
        foreach (var (at, name, colour, process) in pills) { var rect = Pill(draw, at, name, colour); labels.Add((rect.Min, rect.Max, process)); }
    }

    /// <summary>The node look of the formula editor: shadow, body, coloured header with an accent line, border.</summary>
    private void NodeFrame(ImDrawListPtr draw, Vec2 p, Vec2 s, Vec4 colour, bool hovered, bool selected, float alpha, float header, bool stock = false)
    {
        float r = 8 * zoom;
        draw.AddRectFilled(p + new Vec2(3, 4), p + s + new Vec2(3, 4), EditorStyle.U(0, 0, 0, .35f * alpha), r);
        draw.AddRectFilled(p, p + s, EditorStyle.U(hovered ? .14f : .115f, hovered ? .18f : .145f, hovered ? .15f : .125f, .97f * Math.Max(alpha, .6f)), r);
        if (header > 0) {
            draw.AddRectFilled(p, p + new Vec2(s.X, header * zoom), EditorStyle.U(EditorStyle.Fade(EditorStyle.Dim(colour, 0.55f), alpha)), r, ImDrawFlags.RoundCornersTop);
            draw.AddRectFilled(p + new Vec2(0, header * zoom - 2), p + new Vec2(s.X, header * zoom), EditorStyle.U(EditorStyle.Fade(colour, alpha)));
        }
        uint border = selected ? EditorStyle.Selection : EditorStyle.U(EditorStyle.Fade(colour, (stock ? .9f : .5f) * alpha));
        draw.AddRect(p, p + s, border, r, ImDrawFlags.None, selected ? 2.6f : stock ? 2f : 1.2f);
    }

    private static void Text(ImDrawListPtr draw, Vec2 at, float size, uint colour, string text, float width)
    {
        draw.PushClipRect(at, at + new Vec2(Math.Max(1, width), size + 4), true);
        draw.AddText(ImGui.GetFont(), size, at, colour, text);
        draw.PopClipRect();
    }

    private (Vec2 Min, Vec2 Max) Pill(ImDrawListPtr draw, Vec2 centre, string text, Vec4 colour)
    {
        float size = Math.Max(11, ImGui.GetFontSize() * zoom * .82f);
        Vec2 t = ImGui.CalcTextSize(text) * size / ImGui.GetFontSize();
        Vec2 min = centre - t / 2 - new Vec2(7, 3), max = centre + t / 2 + new Vec2(7, 3);
        draw.AddRectFilled(min, max, EditorStyle.U(0.06f, 0.08f, 0.07f, 0.94f), (max.Y - min.Y) / 2);
        draw.AddRect(min, max, EditorStyle.U(EditorStyle.Fade(colour, .7f)), (max.Y - min.Y) / 2);
        draw.AddText(ImGui.GetFont(), size, centre - t / 2, EditorStyle.U(colour), text);
        return (min, max);
    }

    /// <summary>Where the segment from a point inside a box towards another point leaves the box.</summary>
    private static Vec2 Exit(Vec2 from, Vec2 towards, Vec2 min, Vec2 size)
    {
        Vec2 d = towards - from, max = min + size; float t = 1;
        if (d.X > 1e-4f) t = Math.Min(t, (max.X - from.X) / d.X); else if (d.X < -1e-4f) t = Math.Min(t, (min.X - from.X) / d.X);
        if (d.Y > 1e-4f) t = Math.Min(t, (max.Y - from.Y) / d.Y); else if (d.Y < -1e-4f) t = Math.Min(t, (min.Y - from.Y) / d.Y);
        return from + d * Math.Max(0, t);
    }
    private static Vec2 Bezier(Vec2 a, Vec2 b, Vec2 c, Vec2 d, float t)
    {
        float s = 1 - t; return s * s * s * a + 3 * s * s * t * b + 3 * s * t * t * c + t * t * t * d;
    }
    internal static string Format(double v) => Math.Abs(v) >= 10000 ? v.ToString("N0") : Math.Abs(v) >= 100 ? v.ToString("0.#") : v.ToString("0.###");

    private void Fit(Vec2 size)
    {
        var rects = boxes.Values.Select(b => (b.At, b.Size)).Concat(bands.Values.Select(b => (b.At, b.Size))).ToArray();
        Vec2 min = new(rects.Min(b => b.At.X), rects.Min(b => b.At.Y));
        Vec2 max = new(rects.Max(b => b.At.X + b.Size.X), rects.Max(b => b.At.Y + b.Size.Y));
        Vec2 content = max - min + new Vec2(40, 110);
        zoom = level == Level.Stack ? Math.Clamp(size.X / content.X, 0.5f, 1.1f) : Math.Clamp(Math.Min(size.X / content.X, size.Y / content.Y), 0.35f, 1.15f);
        if (level == Level.Stack) { pan = new Vec2((size.X - (max.X - min.X) * zoom) / 2, 50) - min * zoom; fitRequested = false; return; }
        pan = new Vec2((size.X - (max.X - min.X) * zoom) / 2, Math.Max(46, (size.Y - (max.Y - min.Y) * zoom) / 2)) - min * zoom;
        fitRequested = false;
    }

    private static bool Inside(Vec2 p, Vec2 min, Vec2 size) => p.X >= min.X && p.Y >= min.Y && p.X <= min.X + size.X && p.Y <= min.Y + size.Y;

    // ── Inspector ────────────────────────────────────────────────────────
    private void StackDetails(SystemModelAnalysis model, SystemModelHost host)
    {
        EditorStyle.Title("Egy csempe rétegei");
        ImGui.TextWrapped("A terep csempékből áll; minden csempének van domborzata, talaja, talajvize, felszíne és növényzete. A szabályok ezeket kötik össze, gyakran rétegeken át: a talaj és a talajvíz dönti el, hogyan nő a fa, és mennyire járható esőben a földút. Válassz csempét fent, és a dobozok az ő értékeit mutatják.");
        if (tile >= 0 && section.Count > 0) {
            EditorStyle.Section($"A #{tile} csempe metszete");
            if (EditorStyle.BeginFacts("tile-section")) {
                foreach (var l in model.Vocabulary.Layers.Where(l => l.PerTile))
                    foreach (var v in Members(model, l.Id).Where(v => section.ContainsKey(v.Id)))
                        EditorStyle.Fact(v.Name, section[v.Id], LayerColour(l.Id));
                ImGui.EndTable();
            }
        }
        EditorStyle.Section("Szabályok a rétegek között");
        ImGui.TextWrapped("Melyik réteg mire hat. Nyisd ki: a szabály paraméterei itt állíthatók, a képlete egy kattintással nyílik.");
        foreach (var (from, to, processes) in CrossLayerRules(model).Where(c => model.Vocabulary.Layers.Find(l => l.Id == c.From)?.PerTile == true)) {
            ImGui.PushID(from + to);
            EditorStyle.Text(LayerColour(from), LayerName(model, from)); ImGui.SameLine(); EditorStyle.Text(HudTheme.Muted, "›"); ImGui.SameLine();
            EditorStyle.Text(LayerColour(to), LayerName(model, to));
            ImGui.Indent(10); foreach (var p in processes) ProcessHeader(model, host, p, "x"); ImGui.Unindent(10);
            ImGui.PopID();
        }
        EditorStyle.Section("Fő visszacsatolási körök");
        foreach (var loop in model.KeyLoops.Take(10))
            if (ImGui.Selectable(Chain(model, loop) + "##key" + loop.Key)) FocusLoop(loop);
    }

    private void LayerDetails(SystemModelAnalysis model, SystemModelHost host)
    {
        var info = model.Vocabulary.Layers.Find(l => l.Id == layer);
        EditorStyle.Title(info?.Name ?? layer);
        ImGui.TextWrapped(info?.Description ?? "");
        foreach (var (from, to, processes) in CrossLayerRules(model).Where(c => c.From == layer || c.To == layer)) {
            ImGui.PushID(from + to);
            EditorStyle.Section((from == layer ? "Hat erre: " + LayerName(model, to) : "Hat rá: " + LayerName(model, from)));
            foreach (var p in processes) ProcessHeader(model, host, p, "c");
            ImGui.PopID();
        }
        EditorStyle.Section("A réteg saját szabályai");
        var members = Members(model, layer).Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var p in model.Processes.Where(p => p.Writes.Any(members.Contains) && p.Reads.Where(r => model.Variable(r) is SystemVariable v && Shown(v)).All(members.Contains)))
            ProcessHeader(model, host, p, "own");
    }

    private static void ProcessHeader(SystemModelAnalysis model, SystemModelHost host, SystemProcess p, string prefix)
    {
        if (p == null) return;
        string editable = host.Editable(p);
        if (!ImGui.CollapsingHeader(p.Name + (editable.Length > 0 ? "  · " + editable : "") + "##" + prefix + p.Id)) return;
        ImGui.Indent(8);
        var reads = p.Reads.Select(model.Variable).Where(v => v != null && Shown(v)).ToArray();
        var writes = p.Writes.Select(model.Variable).Where(v => v != null && Shown(v)).ToArray();
        ImGui.PushTextWrapPos();
        EditorStyle.Text(HudTheme.Muted, "Olvassa: " + string.Join(", ", reads.Select(v => v.Name)));
        EditorStyle.Text(HudTheme.Muted, "Változtatja: " + string.Join(", ", writes.Select(v => v.Name)));
        ImGui.PopTextWrapPos();
        ImGui.PushID(prefix + p.Id); host.DrawProcess(p); ImGui.PopID();
        ImGui.Unindent(8); ImGui.Spacing();
    }

    private void VariableDetails(SystemModelAnalysis model, SystemVariable v, SystemModelHost host)
    {
        if (EditorStyle.QuietButton("‹ " + (level == Level.Layer ? LayerName(model, layer) : "Rétegek"))) { SelectedVariable = null; return; }
        EditorStyle.Title(v.Name);
        EditorStyle.Chip(SystemVocabulary.KindName(v.Kind) + " · " + LayerName(model, v.Layer), Colour(v), "kind" + v.Id);
        string value = ValueText(host, v);
        if (value != null) { ImGui.SameLine(); ImGui.AlignTextToFramePadding(); EditorStyle.Text(Colour(v), value); }
        ImGui.TextWrapped(v.Description.Length > 0 ? v.Description : SystemVocabulary.KindHelp(v.Kind));
        ImGui.PushID("quantity" + v.Id); host.DrawQuantity(v); ImGui.PopID();
        EditorStyle.Section(v.Kind == SystemVariableKind.Stock ? "Be- és kiáramlások" : "Ezek számolják");
        foreach (var p in model.Writers(v.Id)) ProcessHeader(model, host, p, "w");
        if (model.Writers(v.Id).Count == 0) EditorStyle.Text(HudTheme.Muted, "Nincs katalogizált szabály.");
        EditorStyle.Section("Ezek használják");
        foreach (var p in model.Readers(v.Id)) ProcessHeader(model, host, p, "r");
        if (model.Readers(v.Id).Count == 0) EditorStyle.Text(HudTheme.Muted, "Nincs katalogizált szabály.");
        EditorStyle.Section("Visszacsatolási körök");
        var loops = model.LoopsThroughVariable(v.Id);
        if (!SystemVocabulary.Endogenous(v.Kind)) ImGui.TextWrapped("Rögzített adat vagy térkép: hat a többire, de visszacsatolás nem változtatja.");
        else if (loops.Count == 0) ImGui.TextWrapped("Nincs kör: a hatása nem tér vissza ehhez a mennyiséghez.");
        foreach (var loop in loops.Take(8)) if (ImGui.Selectable(Chain(model, loop) + "##vl" + loop.Key)) FocusLoop(loop);
    }

    private void LoopDetails(SystemModelAnalysis model, SystemLoop loop, SystemModelHost host)
    {
        if (EditorStyle.QuietButton("‹ Vissza")) { SelectedLoop = null; level = beforeLoop; fitRequested = true; return; }
        EditorStyle.Title($"Visszacsatolási kör · {loop.Length} lépés");
        ImGui.TextWrapped("Minden lépés egy szabály, amely az egyik mennyiséget olvassa és a következőt írja. A kör erősségét ezek a szabályok adják: nyisd ki és állítsd őket. Hogy a kör erősít vagy kiegyenlít, az összehasonlító próba mutatja meg.");
        for (int i = 0; i < loop.Length; i++) {
            ImGui.PushID(i);
            EditorStyle.Text(Colour(model.Variable(loop.Variables[i])), model.Name(loop.Variables[i]) + "  ›  " + model.Name(loop.Variables[(i + 1) % loop.Length]));
            foreach (string p in loop.Steps[i]) ProcessHeader(model, host, model.Process(p), "l" + i);
            ImGui.PopID();
        }
    }
}
