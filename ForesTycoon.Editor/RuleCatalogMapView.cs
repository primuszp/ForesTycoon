using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;
using static ForesTycoon.Editor.RuleCatalogPresentation;
namespace ForesTycoon.Editor;

using static ForesTycoon.Editor.RuleCatalogLayout;
internal sealed class RuleCatalogMapView(RuleCatalogNavigation navigation)
{
    private GameRuleIndex index => navigation.Session.Index;
    private Dictionary<(string, string), int> moduleLinks => navigation.Layout.Links;
    private ref string module => ref navigation.State.Module;
    private ref string selected => ref navigation.State.Selected;
    private ref string search => ref navigation.State.Search;
    private ref bool fitRequested => ref navigation.State.FitRequested;
    private RuleCatalogLayout.Domain DomainOf(string name) => navigation.Layout.DomainOf(name);
    private void SelectModule(string name) => navigation.SelectModule(name);
    private GameRuleDefinition[] VisibleRules() => navigation.VisibleRules();
    private Dictionary<string, Vec2> moduleLayout => navigation.Layout.Modules;
    private List<(RuleCatalogLayout.Domain Domain, Vec2 Origin)> domainLayout => navigation.Layout.DomainsAt;
    private readonly Dictionary<string, Vec2> positions = new(StringComparer.Ordinal);
    private ref string dragging => ref navigation.State.Dragging;
    private ref string hovered => ref navigation.State.Hovered;
    private ref Vec2 pan => ref navigation.State.Pan;
    private ref float zoom => ref navigation.State.Zoom;
    private void AutoLayout(string module) => navigation.Layout.AutoLayout(module);
    internal void Draw()
    {
        Vec2 origin = ImGui.GetCursorScreenPos(), size = ImGui.GetContentRegionAvail();
        size = new(Math.Max(1, size.X), Math.Max(1, size.Y));
        ImGui.InvisibleButton("network", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        bool hover = ImGui.IsItemHovered(); var io = ImGui.GetIO();
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) dragging = null;
        if (hover && ImGui.IsMouseDragging(ImGuiMouseButton.Right)) pan += io.MouseDelta;
        if (hover && io.MouseWheel != 0)
        {
            float next = Math.Clamp(zoom * (1 + io.MouseWheel * 0.1f), 0.35f, 1.6f);
            pan = io.MousePos - origin - (io.MousePos - origin - pan) * next / zoom; zoom = next;
        }
        var draw = ImGui.GetWindowDrawList(); draw.PushClipRect(origin, origin + size, true);
        EditorStyle.Grid(draw, origin, size, pan, zoom);
        bool globalSearch = module.Length == 0 && search.Length > 0;
        if (module.Length == 0 && !globalSearch) SystemMap(draw, origin, size, hover, io);
        else RuleMap(draw, origin, size, hover, io, globalSearch);
        draw.PopClipRect();

        // Overlay: breadcrumb and view controls.
        ImGui.SetCursorScreenPos(origin + new Vec2(10, 8));
        if (module.Length > 0)
        {
            if (EditorStyle.QuietButton("‹ Összes rendszer")) { module = ""; selected = ""; fitRequested = true; }
            ImGui.SameLine(); ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, "›"); ImGui.SameLine();
            EditorStyle.Text(DomainOf(module).Colour, module);
        }
        else { ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, globalSearch ? $"Keresés: „{search}”" : "Rendszertérkép"); }
        ImGui.SameLine(); if (EditorStyle.QuietButton("Igazítás", "Minden elem a képbe")) fitRequested = true;
        if (module.Length > 0 && search.Length == 0)
        {
            ImGui.SameLine();
            if (EditorStyle.QuietButton("Automatikus elrendezés", "A szabályokat az adatfolyam szerint rendezi (a kézi elrendezés felülíródik)."))
            { AutoLayout(module); fitRequested = true; }
        }
        ImGui.SetCursorScreenPos(origin + new Vec2(10, size.Y - ImGui.GetTextLineHeight() - 8));
        EditorStyle.Text(EditorStyle.Fade(HudTheme.Muted, 0.8f), module.Length == 0 && !globalSearch
            ? "Rámutatás: kapcsolatok · kattintás: a rendszer szabályai · jobb húzás: mozgatás · görgő: nagyítás"
            : "Kattintás: kijelölés · húzás: elrendezés · halvány kártya: másik rendszer · jobb húzás: mozgatás");
    }

    private void Fit(Vec2 size, Vec2 min, Vec2 max)
    {
        Vec2 content = max - min + new Vec2(80, 120);
        zoom = Math.Clamp(Math.Min(size.X / content.X, size.Y / content.Y), 0.35f, 1.15f);
        pan = (size - (max - min) * zoom) / 2 - min * zoom + new Vec2(0, 10);
        fitRequested = false;
    }

    private void SystemMap(ImDrawListPtr draw, Vec2 origin, Vec2 size, bool hover, ImGuiIOPtr io)
    {
        if (fitRequested && moduleLayout.Count > 0)
            Fit(size, new Vec2(0, 0), new Vec2(moduleLayout.Values.Max(p => p.X) + ModuleWidth, moduleLayout.Values.Max(p => p.Y) + ModuleHeight));
        Vec2 Screen(Vec2 p) => origin + pan + p * zoom;
        Vec2 box = new Vec2(ModuleWidth, ModuleHeight) * zoom;

        string over = null;
        foreach (var (name, p) in moduleLayout)
        {
            Vec2 s = Screen(p);
            if (hover && Inside(io.MousePos, s, box)) over = name;
        }
        hovered = over;
        var linked = over == null ? null : new HashSet<string>(moduleLinks.Keys.Where(k => k.Item1 == over || k.Item2 == over).SelectMany(k => new[] { k.Item1, k.Item2 }));

        // Domain column headings.
        foreach (var (domain, at) in domainLayout)
        {
            Vec2 h = Screen(at);
            draw.AddRectFilled(h + new Vec2(0, 6 * zoom), h + new Vec2(ModuleWidth * zoom, 9 * zoom), EditorStyle.U(EditorStyle.Fade(domain.Colour, 0.8f)), 2);
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * zoom, h + new Vec2(0, 14 * zoom), EditorStyle.U(domain.Colour), domain.Name.ToUpperInvariant());
        }

        // Edges: faint when nothing is hovered; the hovered system's inputs (blue) and outputs (amber) otherwise.
        foreach (var ((from, to), count) in moduleLinks)
        {
            if (!moduleLayout.TryGetValue(from, out var pf) || !moduleLayout.TryGetValue(to, out var pt)) continue;
            bool outgoing = over == from, incoming = over == to;
            if (over != null && !outgoing && !incoming) continue;
            Vec4 colour = outgoing ? HudTheme.AmberAccent : incoming ? HudTheme.Info : EditorStyle.Fade(HudTheme.Muted, 0.13f);
            float width = (over == null ? 1f : 1.4f + Math.Min(count, 8) * 0.35f) * Math.Max(zoom, 0.6f);
            bool sameColumn = Math.Abs(pf.X - pt.X) < 1;
            bool backwards = pt.X < pf.X;
            Vec2 a = Screen(pf + new Vec2(backwards ? 0 : ModuleWidth, ModuleHeight / 2));
            Vec2 b = Screen(pt + new Vec2(sameColumn || backwards ? ModuleWidth : 0, ModuleHeight / 2));
            if (sameColumn) a = Screen(pf + new Vec2(ModuleWidth, ModuleHeight / 2));
            float pull = (sameColumn ? 60 : Math.Max(50, Math.Abs(b.X - a.X) / zoom * 0.45f)) * zoom;
            Vec2 c1 = a + new Vec2(backwards ? -pull : pull, 0), c2 = b + new Vec2(sameColumn || backwards ? pull : -pull, 0);
            uint u = EditorStyle.U(colour);
            if (over != null) EditorStyle.BezierArrow(draw, a, c1, c2, b, 8 * zoom + width, u, width);
            else draw.AddBezierCubic(a, c1, c2, b, u, width);
        }

        string clicked = null;
        foreach (var (name, p) in moduleLayout)
        {
            Vec2 s = Screen(p);
            var domain = DomainOf(name);
            bool isOver = over == name;
            float alpha = linked == null || linked.Contains(name) ? 1 : 0.35f;
            var rules = index.InModule(name);
            draw.AddRectFilled(s + new Vec2(2, 3), s + box + new Vec2(2, 3), EditorStyle.U(0, 0, 0, 0.3f * alpha), 7 * zoom);
            draw.AddRectFilled(s, s + box, isOver ? EditorStyle.NodeBodyHover : EditorStyle.U(EditorStyle.Fade(new Vec4(0.115f, 0.145f, 0.125f, 1), 0.97f * alpha)), 7 * zoom);
            draw.AddRectFilled(s, s + new Vec2(4 * zoom, box.Y), EditorStyle.U(EditorStyle.Fade(domain.Colour, alpha)), 7 * zoom, ImDrawFlags.RoundCornersLeft);
            draw.AddRect(s, s + box, EditorStyle.U(EditorStyle.Fade(isOver ? domain.Colour : HudTheme.Muted, (isOver ? 0.9f : 0.22f) * alpha)), 7 * zoom, ImDrawFlags.None, isOver ? 2 : 1);
            float font = ImGui.GetFontSize() * zoom;
            draw.PushClipRect(s, s + box, true);
            draw.AddText(ImGui.GetFont(), font, s + new Vec2(14, 10) * zoom, EditorStyle.U(EditorStyle.Fade(HudTheme.Parchment, alpha)), name);
            int ins = moduleLinks.Where(k => k.Key.Item2 == name).Sum(k => k.Value), outs = moduleLinks.Where(k => k.Key.Item1 == name).Sum(k => k.Value);
            draw.AddText(ImGui.GetFont(), font * 0.9f, s + new Vec2(14, 32) * zoom, EditorStyle.U(EditorStyle.Fade(HudTheme.Muted, alpha)),
                $"{rules.Count} szabály · {ins} be · {outs} ki");
            // Execution mix: native / graph / presentation share of the system's rules.
            float x = 14, barWidth = ModuleWidth - 28;
            foreach (var group in rules.GroupBy(r => r.Execution).OrderBy(g => g.Key))
            {
                float w = barWidth * group.Count() / Math.Max(1, rules.Count);
                draw.AddRectFilled(s + new Vec2(x, 58) * zoom, s + new Vec2(x + w - 1, 63) * zoom, EditorStyle.U(EditorStyle.Fade(ExecutionColour(group.Key), 0.85f * alpha)), 2);
                x += w;
            }
            draw.PopClipRect();
            if (isOver && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) clicked = name;
        }
        if (over != null) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (clicked != null) SelectModule(clicked);
    }

    private void RuleMap(ImDrawListPtr draw, Vec2 origin, Vec2 size, bool hover, ImGuiIOPtr io, bool globalSearch)
    {
        var nodes = VisibleRules();
        var inView = new HashSet<string>(nodes.Select(n => n.Id));
        var local = new Dictionary<string, Vec2>(StringComparer.Ordinal);
        for (int i = 0; i < nodes.Length; i++)
            local[nodes[i].Id] = globalSearch ? new Vec2(i % 3 * 250, 40 + i / 3 * 120) : new Vec2(nodes[i].X, nodes[i].Y);

        // Other systems appear as one card each ("@" + name): those feeding this one on the left, its consumers on the right.
        string End(string id) => inView.Contains(id) ? id : "@" + index.Find(id).Module;
        var edges = globalSearch
            ? index.Connections.Where(c => inView.Contains(c.From) && inView.Contains(c.To)).GroupBy(c => (c.From, c.To))
            : index.Connections.Where(c => inView.Contains(c.From) || inView.Contains(c.To)).GroupBy(c => (End(c.From), End(c.To)));
        var edgeList = edges.Select(g => (From: g.Key.Item1, To: g.Key.Item2, Fields: g.Select(c => c.Field).Distinct().ToArray())).ToList();
        var producers = edgeList.Where(e => e.From.StartsWith('@')).Select(e => e.From).Distinct().ToList();
        var consumers = edgeList.Where(e => e.To.StartsWith('@')).Select(e => e.To).Distinct().Where(m => !producers.Contains(m)).ToList();
        if (local.Count > 0)
        {
            float minX = local.Values.Min(p => p.X), maxX = local.Values.Max(p => p.X) + RuleWidth;
            float midY = (local.Values.Min(p => p.Y) + local.Values.Max(p => p.Y) + RuleHeight) / 2;
            for (int i = 0; i < producers.Count; i++) local[producers[i]] = new Vec2(minX - GhostWidth - GhostGap, midY + (i - (producers.Count - 1) / 2f) * (GhostHeight + 12) - GhostHeight / 2);
            for (int i = 0; i < consumers.Count; i++) local[consumers[i]] = new Vec2(maxX + GhostGap, midY + (i - (consumers.Count - 1) / 2f) * (GhostHeight + 12) - GhostHeight / 2);
        }
        if (fitRequested && local.Count > 0)
        {
            Vec2 min = new(local.Values.Min(p => p.X), local.Values.Min(p => p.Y));
            Vec2 max = new(local.Max(p => p.Value.X + (inView.Contains(p.Key) ? RuleWidth : GhostWidth)), local.Max(p => p.Value.Y + (inView.Contains(p.Key) ? RuleHeight : GhostHeight)));
            Fit(size, min, max);
        }

        positions.Clear();
        foreach (var (id, p) in local) positions[id] = origin + pan + p * zoom;
        Vec2 Box(string id) => (inView.Contains(id) ? new Vec2(RuleWidth, RuleHeight) : new Vec2(GhostWidth, GhostHeight)) * zoom;

        string over = null;
        foreach (var (id, p) in positions) if (hover && Inside(io.MousePos, p, Box(id))) over = id;
        hovered = over;
        string focus = over ?? (inView.Contains(selected) ? selected : null);
        var neighbours = focus == null ? null : new HashSet<string>(edgeList.Where(e => e.From == focus || e.To == focus).SelectMany(e => new[] { e.From, e.To }));

        // Edges; the focused card's edges are coloured (amber: its outputs, blue: its inputs) and labelled with their fields.
        foreach (var (from, to, fields) in edgeList)
        {
            if (!positions.TryGetValue(from, out var pf) || !positions.TryGetValue(to, out var pt)) continue;
            bool focused = focus != null && (from == focus || to == focus);
            Vec2 bf = Box(from), bt = Box(to);
            Vec2 a = pf + new Vec2(bf.X, bf.Y / 2), b = pt + new Vec2(0, bt.Y / 2), c1, c2;
            if (pt.X < pf.X + bf.X)
            {
                // Same column or backwards (feedback): leave and enter on the right, bulging outwards.
                b = pt + new Vec2(bt.X, bt.Y / 2);
                float outside = Math.Max(a.X, b.X) + 30 * zoom + Math.Abs(b.Y - a.Y) * 0.3f;
                c1 = new Vec2(outside, a.Y); c2 = new Vec2(outside, b.Y);
            }
            else
            {
                float pull = Math.Max(40 * zoom, Math.Abs(b.X - a.X) * 0.45f);
                c1 = a + new Vec2(pull, 0); c2 = b - new Vec2(pull, 0);
            }
            Vec4 colour = focused ? (from == focus ? HudTheme.AmberAccent : HudTheme.Info) : EditorStyle.Fade(HudTheme.Muted, focus == null ? 0.3f : 0.08f);
            uint u = EditorStyle.U(colour);
            EditorStyle.BezierArrow(draw, a, c1, c2, b, (focused ? 9 : 6) * zoom, u, focused ? 2.2f : 1.2f);
            if (focused && zoom > 0.45f)
            {
                string label = fields.Length <= 2 ? string.Join(", ", fields) : $"{fields[0]} +{fields.Length - 1}";
                float labelScale = Math.Min(1, zoom * 1.1f);
                Vec2 mid = Bezier(a, c1, c2, b, 0.5f), ts = ImGui.CalcTextSize(label) * labelScale;
                draw.AddRectFilled(mid - ts / 2 - new Vec2(5, 2), mid + ts / 2 + new Vec2(5, 2), EditorStyle.U(0.06f, 0.08f, 0.07f, 0.92f), 4);
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * labelScale, mid - ts / 2, u, label);
            }
        }

        string clicked = null;
        foreach (var (id, p) in positions)
        {
            Vec2 box = Box(id);
            bool isOver = over == id;
            float alpha = neighbours == null || neighbours.Contains(id) || id == focus ? 1 : 0.35f;
            float font = ImGui.GetFontSize() * zoom;
            if (id.StartsWith('@'))
            {
                string other = id[1..];
                var domain = DomainOf(other);
                int fields = edgeList.Where(e => e.From == id || e.To == id).Sum(e => e.Fields.Length);
                draw.AddRectFilled(p, p + box, EditorStyle.U(EditorStyle.Fade(new Vec4(0.09f, 0.11f, 0.10f, 1), (isOver ? 0.95f : 0.7f) * alpha)), 6 * zoom);
                draw.AddRect(p, p + box, EditorStyle.U(EditorStyle.Fade(domain.Colour, (isOver ? 0.85f : 0.35f) * alpha)), 6 * zoom, ImDrawFlags.None, isOver ? 2 : 1);
                draw.PushClipRect(p, p + box, true);
                draw.AddCircleFilled(p + new Vec2(13, 17) * zoom, 3.5f * zoom, EditorStyle.U(EditorStyle.Fade(domain.Colour, alpha)));
                draw.AddText(ImGui.GetFont(), font * 0.95f, p + new Vec2(22, 9) * zoom, EditorStyle.U(EditorStyle.Fade(HudTheme.Parchment, 0.85f * alpha)), other);
                draw.AddText(ImGui.GetFont(), font * 0.85f, p + new Vec2(22, 30) * zoom, EditorStyle.U(EditorStyle.Fade(HudTheme.Muted, alpha)),
                    $"{(producers.Contains(id) ? "forrás" : "fogyasztó")} · {fields} mező");
                draw.PopClipRect();
                if (isOver)
                {
                    ImGui.SetTooltip(other + "\nKattintás: ugrás ehhez a rendszerhez");
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) clicked = other;
                }
                continue;
            }
            var rule = index.Find(id); if (rule == null) continue;
            bool isSelected = selected == id;
            Vec4 exec = ExecutionColour(rule.Execution);
            if (!globalSearch && dragging == id && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            { rule.X += io.MouseDelta.X / zoom; rule.Y += io.MouseDelta.Y / zoom; }
            draw.AddRectFilled(p + new Vec2(2, 3), p + box + new Vec2(2, 3), EditorStyle.U(0, 0, 0, 0.3f * alpha), 7 * zoom);
            draw.AddRectFilled(p, p + box, isOver ? EditorStyle.NodeBodyHover : EditorStyle.U(EditorStyle.Fade(new Vec4(0.115f, 0.145f, 0.125f, 1), 0.97f * alpha)), 7 * zoom);
            draw.AddRectFilled(p, p + new Vec2(box.X, 4 * zoom), EditorStyle.U(EditorStyle.Fade(exec, alpha)), 7 * zoom, ImDrawFlags.RoundCornersTop);
            draw.AddRect(p, p + box, isSelected ? EditorStyle.Selection : EditorStyle.U(EditorStyle.Fade(HudTheme.Muted, (isOver ? 0.6f : 0.22f) * alpha)), 7 * zoom, ImDrawFlags.None, isSelected ? 2.5f : 1);
            draw.PushClipRect(p + new Vec2(4, 2), p + box - new Vec2(4, 2), true);
            uint text = EditorStyle.U(EditorStyle.Fade(HudTheme.Parchment, alpha)), muted = EditorStyle.U(EditorStyle.Fade(HudTheme.Muted, alpha));
            draw.AddText(ImGui.GetFont(), font, p + new Vec2(10, 12) * zoom, text, rule.Name);
            draw.AddText(ImGui.GetFont(), font * 0.88f, p + new Vec2(10, 34) * zoom, muted, rule.Scope);
            draw.AddText(ImGui.GetFont(), font * 0.88f, p + new Vec2(10, 54) * zoom, muted, rule.Schedule);
            draw.AddCircleFilled(p + new Vec2(14, 79) * zoom, 3.5f * zoom, EditorStyle.U(EditorStyle.Fade(exec, alpha)));
            draw.AddText(ImGui.GetFont(), font * 0.85f, p + new Vec2(22, 71) * zoom, EditorStyle.U(EditorStyle.Fade(exec, alpha)), Status(rule.Execution));
            string counts = $"{rule.Reads.Length} be · {rule.Writes.Length} ki";
            float w = ImGui.CalcTextSize(counts).X * 0.85f;
            draw.AddText(ImGui.GetFont(), font * 0.85f, p + new Vec2(RuleWidth - 10 - w, 71) * zoom, muted, counts);
            if (rule.Parameters.Any(x => x.Changed))
                draw.AddCircleFilled(p + new Vec2(RuleWidth - 14, 16) * zoom, 4 * zoom, EditorStyle.Selection);
            draw.PopClipRect();
            if (isOver && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { selected = id; dragging = id; }
        }
        if (hover && over == null && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) selected = "";
        if (over != null && dragging == null) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (clicked != null) { search = ""; SelectModule(clicked); }
    }

    private static bool Inside(Vec2 m, Vec2 p, Vec2 s) => m.X >= p.X && m.Y >= p.Y && m.X <= p.X + s.X && m.Y <= p.Y + s.Y;

    private static Vec2 Bezier(Vec2 a, Vec2 b, Vec2 c, Vec2 d, float t)
    {
        float u = 1 - t;
        return a * (u * u * u) + b * (3 * u * u * t) + c * (3 * u * t * t) + d * (t * t * t);
    }

}
