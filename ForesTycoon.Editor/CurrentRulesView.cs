using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor
{
    /// <summary>
    /// Overview of the game's rules: a sidebar of systems grouped by domain, a map (all systems, or one system's rules
    /// with their outside producers and consumers) and an inspector for the selected rule.
    /// </summary>
    internal sealed class CurrentRulesView
    {
        private sealed record Domain(string Name, Vec4 Colour, string[] Modules);

        // Systems grouped by what they model; a module not listed here falls into "Egyéb".
        private static readonly Domain[] Domains =
        {
            new("Alapok", new Vec4(0.62f, 0.64f, 0.58f, 1), new[] { "Idő és vezérlés" }),
            new("Környezet", new Vec4(0.45f, 0.68f, 0.82f, 1), new[] { "Terep", "Talaj és klíma", "Vízháztartás" }),
            new("Erdő", new Vec4(0.47f, 0.70f, 0.38f, 1), new[] { "Erdő", "Erdészeti beavatkozás", "Vadak" }),
            new("Infrastruktúra", new Vec4(0.88f, 0.63f, 0.34f, 1), new[] { "Utak és nyomok", "Járművek", "Fenntartás" }),
            new("Gazdaság", new Vec4(0.90f, 0.78f, 0.40f, 1), new[] { "Faanyag és logisztika", "Gazdaság" }),
            new("Megjelenítés", new Vec4(0.62f, 0.60f, 0.85f, 1), new[] { "Megjelenítés" }),
        };
        private static readonly Domain Other = new("Egyéb", new Vec4(0.6f, 0.6f, 0.6f, 1), Array.Empty<string>());

        private GameRuleCatalog catalog;
        private GameRuleIndex index;
        private GameRuleDefinition[] visibleRules;
        private string visibleModule, visibleSearch;
        private List<(Domain Domain, string[] Modules)> groups = new();
        private readonly Dictionary<string, Vec2> moduleLayout = new(StringComparer.Ordinal);
        private readonly List<(Domain Domain, Vec2 Origin)> domainLayout = new();
        private Dictionary<(string, string), int> moduleLinks = new();
        private readonly Dictionary<string, Vec2> positions = new(StringComparer.Ordinal);
        private string observedRule;
        private string[] observations = Array.Empty<string>();
        private long observedAt;
        private string module = "", selected = "", search = "", dragging, hovered;
        private string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForesTycoon", "rules", "current-game-rules.json");
        private Vec2 pan = new(10, 10);
        private float zoom = 0.85f;
        private bool fitRequested = true;

        private const float ModuleWidth = 210, ModuleHeight = 78, ModuleColumn = 260, ModuleRow = 98;
        private const int DomainsPerRow = 3;
        private const float RuleWidth = 220, RuleHeight = 92, GhostWidth = 180, GhostHeight = 54, GhostGap = 80;

        /// <summary>The last file operation's result, shown by the editor's status bar.</summary>
        internal string Message { get; set; } = "";
        internal RuleModel RoadTrafficModel => catalog?.RoadTrafficModel;

        internal void SetRoadTrafficModel(GameWorld world, RuleModel draft)
        {
            if (catalog == null) Load(world.DescribeCurrentRules());
            // Both tabs edit one authoring document; an invalid draft is retained and cannot be exported as a valid catalog.
            catalog.RoadTrafficModel = draft;
        }

        internal void Load(GameRuleCatalog loaded, string file = null, GameRuleCatalog current = null)
        {
            loaded = CurrentGameRules.Refresh(loaded, current ?? (file != null ? CurrentGameRules.Build() : loaded));
            var nextIndex = new GameRuleIndex(loaded);
            catalog = loaded; index = nextIndex;
            groups = Domains.Select(d => (d, d.Modules.Where(m => index.Modules.Contains(m)).ToArray()))
                .Append((Other, index.Modules.Where(m => !Domains.Any(d => d.Modules.Contains(m))).ToArray()))
                .Where(g => g.Item2.Length > 0).ToList();
            // Domains in a grid of three columns, each a heading above its systems.
            moduleLayout.Clear(); domainLayout.Clear();
            float top = 0;
            for (int first = 0; first < groups.Count; first += DomainsPerRow)
            {
                int tallest = 0;
                for (int column = first; column < Math.Min(groups.Count, first + DomainsPerRow); column++)
                {
                    var origin = new Vec2((column - first) * ModuleColumn, top);
                    domainLayout.Add((groups[column].Domain, origin));
                    for (int row = 0; row < groups[column].Modules.Length; row++)
                        moduleLayout[groups[column].Modules[row]] = origin + new Vec2(0, 34 + row * ModuleRow);
                    tallest = Math.Max(tallest, groups[column].Modules.Length);
                }
                top += 34 + tallest * ModuleRow + 40;
            }
            moduleLinks = index.Connections.Select(c => (index.Find(c.From).Module, index.Find(c.To).Module))
                .Where(p => p.Item1 != p.Item2).GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());
            // The code-generated catalog has a plain grid; lay such systems out by their data flow instead.
            foreach (string m in index.Modules)
            {
                var rules = index.InModule(m);
                if (rules.Select((r, i) => r.X == 25 + i % 3 * 245 && r.Y == 30 + i / 3 * 130).All(x => x)) AutoLayout(m);
            }
            visibleRules = null; observedRule = null; dragging = null;
            module = selected = ""; fitRequested = true; if (file != null) path = file;
        }

        /// <summary>Columns by data flow inside the system (producers left, at most two columns), stacked and centred.</summary>
        private void AutoLayout(string moduleName)
        {
            var rules = index.InModule(moduleName);
            var ids = new HashSet<string>(rules.Select(r => r.Id));
            var inner = index.Connections.Where(c => c.From != c.To && ids.Contains(c.From) && ids.Contains(c.To)).ToArray();
            var depth = rules.ToDictionary(r => r.Id, _ => 0);
            for (int pass = 0; pass < rules.Count; pass++)
                foreach (var c in inner) depth[c.To] = Math.Min(rules.Count, Math.Max(depth[c.To], depth[c.From] + 1));
            // A rule read only by later ones goes left; cycles (feedback) collapse into two columns.
            var columns = rules.GroupBy(r => Math.Min(1, depth[r.Id])).OrderBy(g => g.Key).Select(g => g.ToArray()).ToArray();
            int tallest = columns.Max(col => col.Length);
            for (int x = 0; x < columns.Length; x++)
                for (int y = 0; y < columns[x].Length; y++)
                {
                    columns[x][y].X = x * (RuleWidth + 70);
                    columns[x][y].Y = (tallest - columns[x].Length) * (RuleHeight + 18) / 2f + y * (RuleHeight + 18);
                }
        }

        internal void SelectModule(string value)
        { module = value; selected = index.InModule(value).FirstOrDefault()?.Id ?? ""; fitRequested = true; }

        private Domain DomainOf(string moduleName) => groups.FirstOrDefault(g => g.Modules.Contains(moduleName)).Domain ?? Other;

        private static Vec4 ExecutionColour(GameRuleExecution e) => e switch
        {
            GameRuleExecution.EditableGraph or GameRuleExecution.EditableController or GameRuleExecution.EditableExpressions => EditorStyle.GraphKind,
            GameRuleExecution.Presentation => EditorStyle.PresentationKind,
            _ => EditorStyle.NativeKind
        };

        private static string Status(GameRuleExecution execution) => execution switch
        {
            GameRuleExecution.EditableGraph => "Szerkeszthető gráf",
            GameRuleExecution.EditableController => "Szerkeszthető vezérlés",
            GameRuleExecution.EditableExpressions => "Viselkedésképletek",
            GameRuleExecution.Presentation => "Megjelenítés",
            _ => "Natív C#"
        };

        private static string StatusHelp(GameRuleExecution execution) => execution switch
        {
            GameRuleExecution.EditableGraph => "Az editor egyenlete fut a játékban; a gráf nézetben módosítható.",
            GameRuleExecution.EditableController => "Az editor állapotgráfja vezérli a gépet; a Viselkedések fülön módosítható.",
            GameRuleExecution.EditableExpressions => "A folyamat döntéseit és mennyiségeit a Viselkedések fül képletgráfjai adják.",
            GameRuleExecution.Presentation => "Csak megjelenítés: a szimuláció állapotát olvassa, nem változtatja.",
            _ => "A meglévő C# folyamat futtatja; a képlet és a paraméterek az implementációból feltérképezett adatok."
        };

        // ── File menu (hosted by the editor's header) ────────────────────────
        internal void FileMenu(GameWorld world)
        {
            if (catalog == null) Load(world.DescribeCurrentRules());
            EditorStyle.Section("Szabálykatalógus");
            ImGui.SetNextItemWidth(420); ImGui.InputText("##catalog-path", ref path, 1024);
            if (ImGui.MenuItem("Mentés")) Try(() =>
            {
                string file = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file + ".tmp", catalog.ToJson()); File.Move(file + ".tmp", file, true);
                Message = "Katalógus, elrendezés és tervezői megjegyzések mentve.";
            });
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("A katalógus az elrendezést, a megjegyzéseket és az útkopási gráfot is tartalmazza.");
            if (ImGui.MenuItem("Megnyitás")) Try(() => { Load(GameRuleCatalog.FromJson(File.ReadAllText(path)), current: world.DescribeCurrentRules()); Message = "Katalógus betöltve, a folyamatleírások a jelenlegi kódhoz frissítve."; });
            ImGui.Separator();
            if (ImGui.MenuItem("Frissítés a játék kódjából")) Try(() => { Load(catalog, current: world.DescribeCurrentRules()); Message = "Folyamatok frissítve; a hangolások, gráf, megjegyzések és elrendezés megmaradtak."; });
        }

        // ── Layout ───────────────────────────────────────────────────────────
        internal void Draw(GameWorld world, Action<RuleModel> editGraph, Action<BehaviorHook> editBehavior = null, Action<string> editController = null)
        {
            if (catalog == null) Load(world.DescribeCurrentRules());
            float height = Math.Max(260, ImGui.GetContentRegionAvail().Y);
            ImGui.BeginChild("rules-sidebar", new Vec2(250, height), ImGuiChildFlags.Borders);
            Sidebar();
            ImGui.EndChild(); ImGui.SameLine();
            float canvasWidth = Math.Max(200, ImGui.GetContentRegionAvail().X - 370);
            ImGui.BeginChild("rules-map", new Vec2(canvasWidth, height), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (module == "gaps") Gaps(); else Canvas();
            ImGui.EndChild(); ImGui.SameLine();
            ImGui.BeginChild("rules-inspector", new Vec2(0, height), ImGuiChildFlags.Borders);
            Inspector(world, editGraph, editBehavior, editController);
            ImGui.EndChild();
        }

        private GameRuleDefinition[] VisibleRules()
        {
            if (visibleRules == null || visibleModule != module || visibleSearch != search)
            { visibleRules = index.Search(module, search); visibleModule = module; visibleSearch = search; }
            return visibleRules;
        }

        // ── Sidebar ──────────────────────────────────────────────────────────
        private void Sidebar()
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##search", "Keresés: szabály, mező…", ref search, 120);
            ImGui.Spacing();
            if (SidebarRow("all", "Összes rendszer", EditorStyle.Fade(HudTheme.Parchment, 0.8f), catalog.Rules.Count, module.Length == 0))
            { module = ""; selected = ""; fitRequested = true; }
            foreach (var (domain, modules) in groups)
            {
                ImGui.Spacing();
                EditorStyle.Text(EditorStyle.Fade(domain.Colour, 0.9f), domain.Name.ToUpperInvariant());
                foreach (string m in modules)
                    if (SidebarRow(m, m, domain.Colour, index.InModule(m).Count, module == m)) SelectModule(m);
            }
            ImGui.Spacing();
            if (SidebarRow("gaps", "Hiányzó folyamatok", HudTheme.Bad, catalog.Gaps.Count, module == "gaps")) { module = "gaps"; selected = ""; }

            if ((module.Length > 0 || search.Length > 0) && module != "gaps")
            {
                EditorStyle.Section(search.Length > 0 ? "Találatok" : "Szabályok");
                var rules = VisibleRules();
                if (rules.Length == 0) EditorStyle.Text(HudTheme.Muted, "Nincs találat.");
                foreach (var rule in rules)
                    if (SidebarRow("r" + rule.Id, rule.Name, ExecutionColour(rule.Execution), -1, selected == rule.Id))
                    { if (module.Length > 0 && rule.Module != module) SelectModule(rule.Module); selected = rule.Id; }
            }
        }

        private static bool SidebarRow(string id, string label, Vec4 colour, int count, bool active)
        {
            float rowHeight = ImGui.GetTextLineHeight() + 8;
            bool clicked = ImGui.Selectable("##" + id, active, ImGuiSelectableFlags.None, new Vec2(0, rowHeight));
            Vec2 min = ImGui.GetItemRectMin(), max = ImGui.GetItemRectMax();
            var draw = ImGui.GetWindowDrawList();
            if (active) draw.AddRectFilled(min, new Vec2(min.X + 3, max.Y), EditorStyle.U(HudTheme.AmberAccent), 1);
            draw.AddCircleFilled(new Vec2(min.X + 13, (min.Y + max.Y) / 2), 3.5f, EditorStyle.U(colour));
            draw.PushClipRect(min, max - new Vec2(count >= 0 ? 34 : 4, 0), true);
            draw.AddText(new Vec2(min.X + 24, min.Y + 4), active ? EditorStyle.TextBright : EditorStyle.U(EditorStyle.Fade(HudTheme.Parchment, 0.86f)), label);
            draw.PopClipRect();
            if (count >= 0)
            {
                string text = count.ToString();
                Vec2 ts = ImGui.CalcTextSize(text);
                Vec2 badge = new(max.X - ts.X - 14, min.Y + 3);
                draw.AddRectFilled(badge, badge + ts + new Vec2(10, 2), EditorStyle.U(1, 1, 1, 0.07f), (ts.Y + 2) / 2);
                draw.AddText(badge + new Vec2(5, 1), EditorStyle.TextMuted, text);
            }
            if (ImGui.IsItemHovered() && ImGui.CalcTextSize(label).X > max.X - min.X - 60) ImGui.SetTooltip(label);
            return clicked;
        }

        // ── Map ──────────────────────────────────────────────────────────────
        private void Canvas()
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

        private void Navigate(string ruleId)
        {
            var rule = index.Find(ruleId); if (rule == null) return;
            search = ""; SelectModule(rule.Module); selected = rule.Id;
        }

        private static bool Inside(Vec2 m, Vec2 p, Vec2 s) => m.X >= p.X && m.Y >= p.Y && m.X <= p.X + s.X && m.Y <= p.Y + s.Y;

        private static Vec2 Bezier(Vec2 a, Vec2 b, Vec2 c, Vec2 d, float t)
        {
            float u = 1 - t;
            return a * (u * u * u) + b * (3 * u * u * t) + c * (3 * u * t * t) + d * (t * t * t);
        }

        private void Gaps()
        {
            ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vec2(8, 6));
            EditorStyle.Title("Hiányzó játékfolyamatok és korlátok");
            EditorStyle.Text(HudTheme.Muted, "Amit a jelenlegi szabályrendszer még nem modellez. Tervezési teendőlista.");
            ImGui.Spacing();
            int i = 1;
            foreach (string gap in catalog.Gaps)
            {
                var draw = ImGui.GetWindowDrawList();
                Vec2 p = ImGui.GetCursorScreenPos();
                float width = ImGui.GetContentRegionAvail().X - 8;
                ImGui.SetCursorScreenPos(p + new Vec2(40, 8));
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - 56);
                EditorStyle.Text(HudTheme.Parchment, gap);
                ImGui.PopTextWrapPos();
                float height = ImGui.GetItemRectMax().Y - p.Y + 8;
                draw.AddRect(p, p + new Vec2(width, height), EditorStyle.U(EditorStyle.Fade(HudTheme.Bad, 0.3f)), 6);
                draw.AddText(p + new Vec2(12, 8), EditorStyle.ErrorColour, $"{i++:00}");
                ImGui.SetCursorScreenPos(p + new Vec2(0, height + 6));
                ImGui.Dummy(new Vec2(1, 1));
            }
        }

        // ── Inspector ────────────────────────────────────────────────────────
        private void Inspector(GameWorld world, Action<RuleModel> editGraph, Action<BehaviorHook> editBehavior, Action<string> editController)
        {
            var rule = index.Find(selected);
            if (rule != null) { RuleDetails(world, rule, editGraph, editBehavior, editController); return; }
            if (module.Length > 0 && module != "gaps") { ModuleSummary(); return; }

            EditorStyle.Title(catalog.Name);
            ImGui.PushTextWrapPos();
            EditorStyle.Text(HudTheme.Muted, "A játék összes szabálya rendszerenként. Egy szabály mezőket olvas és ír; ahol egy szabály kimenete egy másiknak bemenete, ott kapcsolat fut közöttük.");
            ImGui.PopTextWrapPos();
            if (EditorStyle.BeginFacts("overview-facts"))
            {
                EditorStyle.Fact("Szabályok", catalog.Rules.Count.ToString());
                EditorStyle.Fact("Rendszerek", index.Modules.Count.ToString());
                EditorStyle.Fact("Kapcsolatok", $"{index.Connections.Count} mező szerint");
                EditorStyle.Fact("Hiányok", catalog.Gaps.Count.ToString(), HudTheme.Bad);
                ImGui.EndTable();
            }
            EditorStyle.Section("Végrehajtás");
            foreach (GameRuleExecution e in Enum.GetValues<GameRuleExecution>())
            {
                int count = catalog.Rules.Count(r => r.Execution == e);
                EditorStyle.Chip($"{Status(e)} · {count}", ExecutionColour(e), "legend" + e);
                ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, StatusHelp(e)); ImGui.PopTextWrapPos();
            }
            EditorStyle.Section("Tartományok");
            foreach (var (domain, modules) in groups)
            {
                EditorStyle.Chip(domain.Name, domain.Colour, "domain" + domain.Name); ImGui.SameLine();
                ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, $"{modules.Length} rendszer, {modules.Sum(m => index.InModule(m).Count)} szabály");
            }
        }

        private void ModuleSummary()
        {
            var domain = DomainOf(module);
            EditorStyle.Chip(domain.Name, domain.Colour, "module-domain");
            EditorStyle.Title(module);
            var rules = index.InModule(module);
            EditorStyle.Text(HudTheme.Muted, $"{rules.Count} szabály. Válassz egyet a térképen vagy az oldalsávban.");
            EditorStyle.Section("Ebből a rendszerből táplálkozik");
            ModuleLinks(moduleLinks.Where(k => k.Key.Item2 == module).Select(k => (k.Key.Item1, k.Value)));
            EditorStyle.Section("Ezt a rendszert táplálja");
            ModuleLinks(moduleLinks.Where(k => k.Key.Item1 == module).Select(k => (k.Key.Item2, k.Value)));
        }

        private void ModuleLinks(IEnumerable<(string Module, int Count)> links)
        {
            var list = links.OrderByDescending(l => l.Count).ToList();
            if (list.Count == 0) { EditorStyle.Text(HudTheme.Muted, "—"); return; }
            foreach (var (m, count) in list)
            {
                if (EditorStyle.Chip(m, DomainOf(m).Colour, "ml" + m, true)) SelectModule(m);
                ImGui.SameLine(); ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, $"{count} mező");
            }
        }

        private void RuleDetails(GameWorld world, GameRuleDefinition rule, Action<RuleModel> editGraph, Action<BehaviorHook> editBehavior, Action<string> editController)
        {
            var domain = DomainOf(rule.Module);
            if (EditorStyle.Chip(rule.Module, domain.Colour, "rule-module", true)) { search = ""; string id = rule.Id; SelectModule(rule.Module); selected = id; }
            ImGui.SameLine();
            EditorStyle.Chip(Status(rule.Execution), ExecutionColour(rule.Execution), "rule-exec");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(StatusHelp(rule.Execution));
            EditorStyle.Title(rule.Name);
            if (editController != null && rule.Id is "machine.controller" or "machine.forwarder" or "machine.processor" or "vehicle.transfer" or "fleet.orders")
                foreach (string kind in new[] { "forwarder", "processor", "truck" })
                    if ((rule.Id == "machine.controller" || rule.Id == "machine." + kind || kind == "truck" && rule.Id is "vehicle.transfer" or "fleet.orders")
                        && ImGui.Button("Állapotgráf: " + kind)) editController(kind);
            if (editBehavior != null)
                foreach (var hook in WorldBehaviorPolicy.Hooks.Where(h => h.Id == rule.Id
                    || h.Id.StartsWith("tuning.") && GameTuning.Spec(h.Id[7..])?.Rule == rule.Id
                    || h.Id.StartsWith("route.") && rule.Id is "route.find" or "route.policy"
                    || (h.Id.StartsWith("loading.") || h.Id == "unloading.amount") && (rule.Id == "loading.policy" || rule.Id == "machine.logTransfer" && h.Kind == "forwarder" || rule.Id == "vehicle.transfer" && h.Kind == "truck")
                    || h.Id == "machine.pace" && rule.Id == (h.Kind == "forwarder" ? "machine.forwarder" : "machine.processor")))
                    if (ImGui.Button("Viselkedésgráf: " + hook.Name + "##" + hook.Id + hook.Kind)) editBehavior(hook);
            ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Parchment, rule.Description); ImGui.PopTextWrapPos();
            if (rule.Execution == GameRuleExecution.EditableGraph)
            {
                ImGui.Spacing();
                if (EditorStyle.PrimaryButton("Gráf megnyitása szerkesztésre")) editGraph(catalog.RoadTrafficModel);
            }
            if (EditorStyle.BeginFacts("rule-facts"))
            {
                EditorStyle.Fact("Hatókör", rule.Scope);
                EditorStyle.Fact("Ütem", rule.Schedule);
                EditorStyle.Fact("Azonosító", rule.Id, HudTheme.Muted);
                ImGui.EndTable();
            }
            if (rule.Formula.Length > 0)
            {
                EditorStyle.Section("Egyenlet / művelet");
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vec4(0.05f, 0.065f, 0.055f, 1));
                ImGui.BeginChild("formula", new Vec2(-1, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);
                ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.AmberAccent, rule.Formula); ImGui.PopTextWrapPos();
                ImGui.EndChild();
                ImGui.PopStyleColor();
            }

            EditorStyle.Section($"Bemenetek · {rule.Reads.Length}");
            foreach (string field in rule.Reads)
                FieldRow(field, index.Writers(field).Where(r => r.Id != rule.Id).ToArray(), "‹", "saját állapot vagy induló adat");
            EditorStyle.Section($"Kimenetek · {rule.Writes.Length}");
            foreach (string field in rule.Writes)
                FieldRow(field, index.Readers(field).Where(r => r.Id != rule.Id).ToArray(), "›", "nincs olvasója");

            if (rule.Parameters.Count > 0)
            {
                int tunable = rule.Parameters.Count(p => p.Tunable);
                EditorStyle.Section(tunable > 0 ? $"Játékparaméterek · {tunable} hangolható" : "Játékparaméterek");
                if (BeginParameterTable("parameters", false))
                {
                    for (int i = 0; i < rule.Parameters.Count; i++) ParameterRow(rule, i, false);
                    ImGui.EndTable();
                }
                if (rule.Parameters.Any(p => !p.Tunable))
                    EditorStyle.Text(EditorStyle.Fade(HudTheme.Muted, 0.75f), "Halvány érték: tájékoztató adat, a világ létrehozásakor rögzül.");
            }
            long now = Stopwatch.GetTimestamp();
            if (observedRule != rule.Id || Stopwatch.GetElapsedTime(observedAt, now).TotalSeconds >= .25)
            {
                observations = rule.Reads.Concat(rule.Writes).Distinct().Select(world.ObserveRuleField).Where(value => value != null).ToArray();
                observedRule = rule.Id; observedAt = now;
            }
            if (observations.Length > 0)
            {
                EditorStyle.Section("Élő adatok a tesztvilágból");
                foreach (string value in observations) { ImGui.Bullet(); ImGui.SameLine(); ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Info, value); ImGui.PopTextWrapPos(); }
            }
            EditorStyle.Section("Forrás a projektben");
            foreach (var source in rule.Sources)
            {
                EditorStyle.Text(HudTheme.Parchment, source.Symbol);
                EditorStyle.Text(HudTheme.Muted, source.File);
            }
            EditorStyle.Section("Tervezői megjegyzés");
            string notes = rule.Notes;
            if (ImGui.InputTextMultiline("##notes", ref notes, 4096, new Vec2(-1, 96))) rule.Notes = notes;
            if (notes.Length == 0 && !ImGui.IsItemActive())
            {
                Vec2 min = ImGui.GetItemRectMin();
                ImGui.GetWindowDrawList().AddText(min + new Vec2(8, 6), EditorStyle.U(EditorStyle.Fade(HudTheme.Muted, 0.6f)), "Ötlet, kérdés, hangolási jegyzet… (a katalógussal mentődik)");
            }
        }

        // ── Tuning ───────────────────────────────────────────────────────────
        private string tuningSearch = "";
        private bool changedOnly;

        /// <summary>Set when the tuning list asks to show a rule in the overview.</summary>
        internal bool OverviewRequested { get; set; }

        /// <summary>The edited tunable numbers that differ from the game's defaults.</summary>
        internal Dictionary<string, double> TuningOverrides(GameWorld world)
        {
            if (catalog == null) Load(world.DescribeCurrentRules());
            return catalog.TuningOverrides();
        }

        /// <summary>How many tunables differ between this document and the test world.</summary>
        internal int UnappliedTuning(GameWorld world)
        {
            var mine = TuningOverrides(world); var live = world.Tuning.ToOverrides();
            return GameTuning.Specs.Count(s => (mine.TryGetValue(s.Id, out double a) ? a : s.Default) != (live.TryGetValue(s.Id, out double b) ? b : s.Default));
        }

        private static bool BeginParameterTable(string id, bool withDefault)
        {
            if (!ImGui.BeginTable(id, withDefault ? 5 : 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX)) return false;
            ImGui.TableSetupColumn("Paraméter", ImGuiTableColumnFlags.WidthStretch, 1.5f);
            ImGui.TableSetupColumn("Érték", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            if (withDefault) ImGui.TableSetupColumn("Alap", ImGuiTableColumnFlags.WidthStretch, 0.55f);
            ImGui.TableSetupColumn("Egység", ImGuiTableColumnFlags.WidthStretch, 0.8f);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 44);
            if (withDefault) ImGui.TableHeadersRow();
            return true;
        }

        private static string Format(RuleParameter p) => "%.6g";

        /// <summary>One parameter: a drag field when tunable (Ctrl+click to type), its unit and a reset to the default.</summary>
        private void ParameterRow(GameRuleDefinition rule, int index, bool withDefault)
        {
            var p = rule.Parameters[index];
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            if (p.Changed)
            {
                Vec2 at = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddCircleFilled(at + new Vec2(-6, ImGui.GetTextLineHeight() / 2 + 3), 3, EditorStyle.Selection);
            }
            ImGui.AlignTextToFramePadding();
            ImGui.PushTextWrapPos(); EditorStyle.Text(p.Tunable ? HudTheme.Parchment : HudTheme.Muted, p.Name); ImGui.PopTextWrapPos();
            if (ImGui.IsItemHovered() && p.Tunable)
                ImGui.SetTooltip($"{p.Name}\nKulcs: {p.Key}\nAlapérték: {p.Default:G6} {p.Unit}\nTartomány: {p.Min:G6} – {p.Max:G6}\nHúzás: hangolás · Ctrl+kattintás: beírás");
            ImGui.TableNextColumn();
            if (p.Tunable)
            {
                float value = (float)p.Value;
                float speed = (float)Math.Max(Math.Abs(p.Value) * 0.005, (p.Max - p.Min) * 0.0005);
                ImGui.SetNextItemWidth(-1);
                if (p.Changed) ImGui.PushStyleColor(ImGuiCol.Text, HudTheme.AmberAccent);
                if (ImGui.DragFloat("##" + rule.Id + p.Key, ref value, speed, (float)p.Min, (float)p.Max, Format(p), ImGuiSliderFlags.AlwaysClamp))
                    rule.Parameters[index] = p with { Value = value == (float)p.Default ? p.Default : Math.Clamp(value, p.Min, p.Max) };
                if (p.Changed) ImGui.PopStyleColor();
            }
            else { ImGui.AlignTextToFramePadding(); EditorStyle.Text(EditorStyle.Fade(HudTheme.Muted, 0.85f), p.Value.ToString("G6")); }
            if (withDefault)
            {
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding();
                EditorStyle.Text(HudTheme.Muted, p.Tunable ? p.Default.ToString("G6") : "");
            }
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding();
            ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, p.Unit); ImGui.PopTextWrapPos();
            ImGui.TableNextColumn();
            if (p.Changed)
            {
                if (EditorStyle.QuietButton("alap##" + rule.Id + p.Key, $"Vissza az alapértékre: {p.Default:G6}")) rule.Parameters[index] = p with { Value = p.Default };
            }
        }

        /// <summary>Every tunable number of the game in one list, by system and rule.</summary>
        internal void DrawTuning(GameWorld world)
        {
            if (catalog == null) Load(world.DescribeCurrentRules());
            var all = catalog.Rules.SelectMany(r => r.Parameters).Where(p => p.Tunable).ToList();
            int changed = all.Count(p => p.Changed), unapplied = UnappliedTuning(world);
            ImGui.SetNextItemWidth(320);
            ImGui.InputTextWithHint("##tuning-search", "Keresés: paraméter, szabály, rendszer…", ref tuningSearch, 120);
            ImGui.SameLine(); ImGui.Checkbox("Csak a módosítottak", ref changedOnly);
            ImGui.SameLine(0, 18); ImGui.AlignTextToFramePadding();
            EditorStyle.Text(HudTheme.Muted, $"{all.Count} hangolható szám · ");
            ImGui.SameLine(0, 0); EditorStyle.Text(changed > 0 ? HudTheme.AmberAccent : HudTheme.Muted, $"{changed} eltér az alapértéktől");
            if (unapplied > 0) { ImGui.SameLine(0, 0); EditorStyle.Text(HudTheme.Info, $" · {unapplied} még nincs alkalmazva"); }
            if (changed > 0)
            {
                ImGui.SameLine(0, 18);
                if (EditorStyle.QuietButton("Mind alapra", "Minden hangolt értéket visszaállít az alapértékre (alkalmazni külön kell)."))
                    foreach (var rule in catalog.Rules)
                        for (int i = 0; i < rule.Parameters.Count; i++)
                            if (rule.Parameters[i].Changed) rule.Parameters[i] = rule.Parameters[i] with { Value = rule.Parameters[i].Default };
            }
            ImGui.Spacing();
            ImGui.BeginChild("tuning-list", new Vec2(0, 0), ImGuiChildFlags.Borders);
            string query = tuningSearch.Trim();
            bool Match(GameRuleDefinition r, RuleParameter p) =>
                (!changedOnly || p.Changed) && (query.Length == 0 || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Key.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    r.Module.Contains(query, StringComparison.OrdinalIgnoreCase));
            bool any = false;
            foreach (var (domain, modules) in groups)
                foreach (string m in modules)
                {
                    var rules = index.InModule(m).Where(r => r.Parameters.Any(p => p.Tunable && Match(r, p))).ToList();
                    if (rules.Count == 0) continue;
                    any = true;
                    ImGui.Spacing();
                    EditorStyle.Chip(m, domain.Colour, "tm" + m);
                    foreach (var rule in rules)
                    {
                        ImGui.Indent(10);
                        ImGui.Spacing();
                        ImGui.PushStyleColor(ImGuiCol.Text, HudTheme.Parchment);
                        if (EditorStyle.QuietButton(rule.Name + "##tr" + rule.Id, "Megnyitás az áttekintésben"))
                        { search = ""; SelectModule(rule.Module); selected = rule.Id; OverviewRequested = true; }
                        ImGui.PopStyleColor();
                        ImGui.SameLine(); ImGui.AlignTextToFramePadding(); EditorStyle.Text(EditorStyle.Fade(HudTheme.Muted, 0.8f), rule.Scope);
                        if (BeginParameterTable("tt" + rule.Id, true))
                        {
                            for (int i = 0; i < rule.Parameters.Count; i++)
                                if (rule.Parameters[i].Tunable && Match(rule, rule.Parameters[i])) ParameterRow(rule, i, true);
                            ImGui.EndTable();
                        }
                        ImGui.Unindent(10);
                    }
                }
            if (!any) EditorStyle.Text(HudTheme.Muted, changedOnly ? "Nincs az alapértéktől eltérő szám." : "Nincs találat.");
            ImGui.EndChild();
        }

        private void FieldRow(string field, GameRuleDefinition[] others, string arrow, string empty)
        {
            EditorStyle.Text(HudTheme.Parchment, field);
            ImGui.Indent(14);
            if (others.Length == 0) EditorStyle.Text(EditorStyle.Fade(HudTheme.Muted, 0.7f), $"{arrow} {empty}");
            float right = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX();
            for (int i = 0; i < others.Length; i++)
            {
                var other = others[i];
                if (i > 0)
                {
                    float next = ImGui.CalcTextSize(other.Name).X + 30;
                    ImGui.SameLine();
                    if (ImGui.GetCursorPosX() + next > right) ImGui.NewLine();
                }
                if (EditorStyle.Chip(other.Name, DomainOf(other.Module).Colour, field + arrow + other.Id, true)) Navigate(other.Id);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{other.Module} › {other.Name}\nKattintás: ugrás a szabályhoz");
            }
            ImGui.Unindent(14);
            ImGui.Spacing();
        }

        private void Try(Action action)
        { try { action(); } catch (Exception e) when (e is IOException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException) { Message = e.Message; } }
    }
}
