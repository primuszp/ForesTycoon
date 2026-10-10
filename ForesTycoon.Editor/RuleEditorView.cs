using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ImGuiNET;
using ForesTycoon.Rules;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace ForesTycoon.Editor
{
    /// <summary>
    /// The rule system editor: an overview of the game's rules and a node editor for the executable road-wear graph.
    /// One header (title, view tabs, file menu, primary action), one canvas with an inspector, one status bar.
    /// </summary>
    internal sealed class RuleEditorView
    {
        private enum View { Overview, Tuning, Graph }

        private RuleModel draft;
        private string selected = "wear", message = "";
        private string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForesTycoon", "rules", "road-wear.json");
        private Vec2 pan = new(40, 40);
        private float zoom = 1, sampleWear = 0.0015f, sampleSurface = 1, sampleDamage = 0;
        private string dragging, linkingFrom;
        private bool panned, fitRequested = true;
        private Vec2 contextPosition;
        private readonly CurrentRulesView currentRules = new();
        private readonly RoadRuleDraftCompiler compiler = new();
        private View view = View.Overview;
        private readonly Dictionary<string, double> values = new(StringComparer.Ordinal);

        // Node geometry in canvas units.
        private const float NodeWidth = 210, Header = 28, PortRow = 26, ValueRow = 34;

        private static readonly (RuleOperation Op, string Name, string Symbol, string Help)[] Kinds =
        {
            (RuleOperation.TrafficWear, "Áthaladási terhelés", "világ » terhelés", "A jármű áthaladásának kopása: 0,0015 × össztömeg / 36 t. Mértékegysége állapotveszteség."),
            (RuleOperation.SurfaceFactor, "Burkolati szorzó", "világ » burkolat", "Aszfalt: 0,2; makadám: 1. Dimenzió nélküli."),
            (RuleOperation.RoadDamage, "Út sérültsége", "világ » sérültség", "1 – útállapot az áthaladás előtt. Dimenzió nélküli."),
            (RuleOperation.Constant, "Állandó", "érték", "Rögzített szám, például egy szorzó."),
            (RuleOperation.Add, "Összeadás", "A + B", "Azonos mértékegységű bemenetek összege."),
            (RuleOperation.Multiply, "Szorzás", "A × B", "Szorzat; egy állapotveszteség szorozható dimenzió nélküli számmal."),
            (RuleOperation.Minimum, "Minimum", "min(A, B)", "A kisebbik bemenet; felső korlátként használható."),
            (RuleOperation.Maximum, "Maximum", "max(A, B)", "A nagyobbik bemenet; alsó korlátként használható."),
        };
        private static (RuleOperation Op, string Name, string Symbol, string Help) Kind(RuleOperation op) => Kinds[(int)op];

        internal RuleEditorView(string modelPath = null)
        {
            if (modelPath == null) return;
            path = Path.GetFullPath(modelPath);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("schema", out var schema) && schema.GetString() == "forest-current-rules")
            { currentRules.Load(GameRuleCatalog.FromJson(document.RootElement.GetRawText()), path); return; }
            draft = RuleModel.FromJson(document.RootElement.GetRawText());
            // Startup files must be valid; incomplete drafts can be opened via the UI for repair.
            var checkedModel = new CompiledRoadRule(draft); checkedModel.ValidateRange();
            selected = draft.Output;
            view = View.Graph;
        }

        internal void Draw(GameWorld world, ref bool open, bool standalone = false)
        {
            if (!open) return;
            draft ??= world.RuleDocument;
            var flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
            if (standalone) flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoTitleBar;
            if (!standalone) ImGui.SetNextWindowSize(new Vec2(1280, 820), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Szabályrendszer editor", ref open, flags)) { ImGui.End(); return; }
            CompiledRoadRule compiled = compiler.Compile(draft);
            HeaderBar(world, compiled);
            float statusHeight = ImGui.GetFrameHeightWithSpacing() + 6;
            ImGui.BeginChild("editor-body", new Vec2(0, -statusHeight), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
            if (view == View.Overview)
                currentRules.Draw(world, model => { draft = model; selected = draft.Output; view = View.Graph; fitRequested = true; });
            else if (view == View.Tuning)
            {
                currentRules.DrawTuning(world);
                if (currentRules.OverviewRequested) { currentRules.OverviewRequested = false; view = View.Overview; }
            }
            else GraphView(world, compiled);
            ImGui.EndChild();
            StatusBar(world, compiled);
            ImGui.End();
        }

        // ── Header: title, view switch, file menu, primary action ─────────────
        private void HeaderBar(GameWorld world, CompiledRoadRule compiled)
        {
            float headerRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
            EditorStyle.Title("Szabályrendszer", true);
            ImGui.SameLine(0, 18);
            ImGui.AlignTextToFramePadding();
            if (ViewTab("Áttekintés", view == View.Overview, "A játék összes szabálya, rendszerenként, a köztük futó adatkapcsolatokkal."))
            { if (view == View.Graph) currentRules.SetRoadTrafficModel(world, draft); view = View.Overview; }
            ImGui.SameLine(0, 4);
            int changedTuning = currentRules.TuningOverrides(world).Count;
            if (ViewTab(changedTuning > 0 ? $"Hangolás · {changedTuning}" : "Hangolás", view == View.Tuning, "Minden hangolható szám egy listában: kapacitások, ráták, árak, kopás, járműfizika."))
            { if (view == View.Graph) currentRules.SetRoadTrafficModel(world, draft); view = View.Tuning; }
            ImGui.SameLine(0, 4);
            if (ViewTab("Útkopás · gráf", view == View.Graph, "Az útkopás futtatható egyenlete: ezt a játék a jármű minden áthaladásakor számolja."))
            {
                if (view != View.Graph) { draft = currentRules.RoadTrafficModel ?? draft; selected = draft.Output; fitRequested = true; }
                view = View.Graph;
            }
            float right = headerRight;
            float fileWidth = ImGui.CalcTextSize("Fájl").X + 20;
            float applyWidth = ImGui.CalcTextSize("Alkalmazás a tesztvilágban").X + 24;
            ImGui.SameLine(right - fileWidth - applyWidth - 8);
            if (ImGui.Button("Fájl")) ImGui.OpenPopup("file-menu");
            FileMenu(world);
            ImGui.SameLine();
            // One action applies the whole document: the road-wear graph and every tuned number.
            var model = view == View.Graph ? draft : currentRules.RoadTrafficModel ?? draft;
            bool valid = view == View.Graph ? compiled != null : compiler.Compile(model) != null;
            if (EditorStyle.PrimaryButton("Alkalmazás a tesztvilágban", valid,
                    !valid ? "Az útkopási gráf hibás: javítsd a gráf nézetben." : "Az útkopási gráf és minden hangolt szám érvénybe lép a tesztvilágban (naplózott parancsok)."))
                Try(() =>
                {
                    var overrides = currentRules.TuningOverrides(world);
                    world.QueueRuleModel(model); world.QueueTuning(overrides);
                    message = $"Alkalmazva: útkopási gráf és {overrides.Count} hangolt szám. A játékba: Fájl » Mentés, majd F12 » Szabálymodell.";
                });
            ImGui.Separator();
        }

        private static bool ViewTab(string label, bool active, string tooltip)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, active ? HudTheme.Moss : new Vec4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.Text, active ? HudTheme.Parchment : HudTheme.Muted);
            bool clicked = ImGui.Button(label);
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
            return clicked;
        }

        private void FileMenu(GameWorld world)
        {
            if (!ImGui.BeginPopup("file-menu")) return;
            if (view != View.Graph)
            {
                currentRules.FileMenu(world);
                if (currentRules.Message.Length > 0) { message = currentRules.Message; currentRules.Message = ""; }
                ImGui.EndPopup(); return;
            }
            EditorStyle.Section("Útkopási modell");
            ImGui.SetNextItemWidth(420); ImGui.InputText("##model-path", ref path, 1024);
            if (ImGui.MenuItem("Mentés", "Ctrl+S")) Save();
            if (ImGui.MenuItem("Megnyitás")) Try(() =>
            {
                var loaded = RuleModel.FromJson(File.ReadAllText(path));
                // Incomplete drafts can be saved and reopened; only validated models can be applied.
                if (loaded.Version != 1 || loaded.Binding != "road.trafficWear" || loaded.Nodes == null || loaded.Nodes.Count > 128 ||
                    string.IsNullOrWhiteSpace(loaded.Name) ||
                    loaded.Nodes.Any(n => n == null || string.IsNullOrWhiteSpace(n.Id) || n.Name == null || !Enum.IsDefined(n.Operation) || !float.IsFinite(n.X) || !float.IsFinite(n.Y)))
                    throw new InvalidDataException("Érvénytelen szerkesztési dokumentum.");
                draft = loaded; selected = draft.Output; fitRequested = true; message = "Modell megnyitva. Az Alkalmazás gombbal kerül a tesztvilágba.";
            });
            ImGui.Separator();
            if (ImGui.MenuItem("Aktív modell átvétele a tesztvilágból")) { draft = world.RuleDocument; selected = draft.Output; fitRequested = true; message = "Az aktív modell a szerkesztőbe került."; }
            if (ImGui.MenuItem("Alapmodell visszaállítása")) { draft = RuleModel.Default(); selected = draft.Output; fitRequested = true; message = "Alapmodell betöltve (a tesztvilágba külön kell alkalmazni)."; }
            ImGui.EndPopup();
        }

        private void Save() => Try(() =>
        {
            string file = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temporary = file + ".tmp";
            File.WriteAllText(temporary, draft.ToJson()); File.Move(temporary, file, true);
            message = "Mentve: " + file;
        });

        // ── Status bar ───────────────────────────────────────────────────────
        private void StatusBar(GameWorld world, CompiledRoadRule compiled)
        {
            ImGui.Separator();
            if (view == View.Graph)
            {
                EditorStyle.Pill(compiled != null ? "Érvényes modell" : "Hiba: " + compiler.ValidationMessage, compiled != null);
                ImGui.SameLine(0, 14);
            }
            int unapplied = currentRules.UnappliedTuning(world);
            if (unapplied > 0) { EditorStyle.Chip($"{unapplied} hangolt szám még nincs alkalmazva", HudTheme.AmberAccent, "unapplied"); ImGui.SameLine(0, 14); }
            ImGui.AlignTextToFramePadding();
            EditorStyle.Text(HudTheme.Muted, "Tesztvilág modellje:"); ImGui.SameLine(); ImGui.TextUnformatted(world.ActiveRuleName);
            if (message.Length > 0) { ImGui.SameLine(0, 18); EditorStyle.Text(HudTheme.Info, message); }
        }

        // ── Graph view: canvas + inspector ───────────────────────────────────
        private void GraphView(GameWorld world, CompiledRoadRule compiled)
        {
            Evaluate();
            float inspector = 330;
            ImGui.BeginChild("graph-canvas", new Vec2(-inspector - 8, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            Canvas(compiled);
            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.BeginChild("graph-inspector", new Vec2(0, 0), ImGuiChildFlags.Borders);
            Inspector(world, compiled);
            ImGui.EndChild();
        }

        // Value of every node for the sample inputs (null where it cannot be computed yet).
        private void Evaluate()
        {
            values.Clear();
            var nodes = draft.Nodes.Where(n => n != null && !string.IsNullOrEmpty(n.Id)).GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
            var visiting = new HashSet<string>();
            double? Value(string id)
            {
                if (id == null || !nodes.TryGetValue(id, out var n)) return null;
                if (values.TryGetValue(id, out double known)) return known;
                if (!visiting.Add(id)) return null;
                double? v = n.Operation switch
                {
                    RuleOperation.TrafficWear => sampleWear,
                    RuleOperation.SurfaceFactor => sampleSurface,
                    RuleOperation.RoadDamage => sampleDamage,
                    RuleOperation.Constant => n.Value,
                    _ => Value(n.A) is double a && Value(n.B) is double b ? n.Operation switch
                    {
                        RuleOperation.Add => a + b, RuleOperation.Multiply => a * b,
                        RuleOperation.Minimum => Math.Min(a, b), _ => Math.Max(a, b)
                    } : null
                };
                visiting.Remove(id);
                if (v is double value && double.IsFinite(value)) values[id] = value;
                return v;
            }
            foreach (var id in nodes.Keys) Value(id);
        }

        private static bool IsLoss(RuleModel model, string id, int depth = 0)
        {
            var n = model.Nodes.Find(x => x?.Id == id);
            if (n == null || depth > 64) return false;
            if (n.Operation == RuleOperation.TrafficWear) return true;
            if (!RuleModel.Binary(n.Operation)) return false;
            return n.Operation == RuleOperation.Multiply ? IsLoss(model, n.A, depth + 1) || IsLoss(model, n.B, depth + 1) : IsLoss(model, n.A, depth + 1);
        }

        private static Vec4 KindColour(RuleModel model, RuleNode n) =>
            model.Output == n.Id ? EditorStyle.OutputKind :
            n.Operation <= RuleOperation.RoadDamage ? EditorStyle.InputKind :
            n.Operation == RuleOperation.Constant ? EditorStyle.ConstantKind : EditorStyle.OperationKind;

        private static float NodeHeight(RuleNode n) => Header + (RuleModel.Binary(n.Operation) ? PortRow * 2 : 0) + ValueRow;

        private void Canvas(CompiledRoadRule compiled)
        {
            Vec2 origin = ImGui.GetCursorScreenPos(), size = ImGui.GetContentRegionAvail();
            size = new Vec2(Math.Max(1, size.X), Math.Max(1, size.Y));
            if (fitRequested) { Fit(size); fitRequested = false; }
            ImGui.InvisibleButton("canvas", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
            bool hovered = ImGui.IsItemHovered(), focused = ImGui.IsWindowFocused();
            var io = ImGui.GetIO();
            var draw = ImGui.GetWindowDrawList();
            draw.PushClipRect(origin, origin + size, true);
            EditorStyle.Grid(draw, origin, size, pan, zoom);

            Vec2 Screen(Vec2 canvas) => origin + pan + canvas * zoom;
            Vec2 NodePos(RuleNode n) => Screen(new Vec2(n.X, n.Y));
            Vec2 OutPort(RuleNode n) => NodePos(n) + new Vec2(NodeWidth, Header + (NodeHeight(n) - Header) / 2) * zoom;
            Vec2 InPort(RuleNode n, bool a) => NodePos(n) + new Vec2(0, Header + PortRow * (a ? 0.5f : 1.5f)) * zoom;

            // Pan (right drag) and zoom (wheel, about the cursor).
            if (hovered && ImGui.IsMouseDragging(ImGuiMouseButton.Right)) { pan += io.MouseDelta; panned = true; }
            if (hovered && io.MouseWheel != 0)
            {
                float next = Math.Clamp(zoom * (1 + io.MouseWheel * 0.12f), 0.4f, 1.8f);
                pan = io.MousePos - origin - (io.MousePos - origin - pan) * (next / zoom); zoom = next;
            }

            string invalidName = compiled == null ? compiler.ValidationMessage : null;
            var nodes = draft.Nodes.Where(n => n != null).ToList();
            string hoverNode = null, hoverPort = null; bool hoverPortA = false; bool hoverOutput = false;
            foreach (var n in nodes)
            {
                Vec2 p = NodePos(n), s = new Vec2(NodeWidth, NodeHeight(n)) * zoom;
                if (Near(io.MousePos, OutPort(n), 9 * zoom)) { hoverPort = n.Id; hoverOutput = true; }
                if (RuleModel.Binary(n.Operation))
                {
                    if (Near(io.MousePos, InPort(n, true), 9 * zoom)) { hoverPort = n.Id; hoverPortA = true; }
                    else if (Near(io.MousePos, InPort(n, false), 9 * zoom)) { hoverPort = n.Id; hoverPortA = false; }
                }
                if (io.MousePos.X >= p.X && io.MousePos.Y >= p.Y && io.MousePos.X <= p.X + s.X && io.MousePos.Y <= p.Y + s.Y) hoverNode = n.Id;
            }
            if (!hovered) { hoverNode = null; hoverPort = null; }

            // Wires: from the producer's output to the consumer's input; colour by unit, value at the midpoint.
            foreach (var n in nodes.Where(n => RuleModel.Binary(n.Operation)))
                for (int port = 0; port < 2; port++)
                {
                    string source = port == 0 ? n.A : n.B;
                    var from = nodes.Find(v => v.Id == source); if (from == null) continue;
                    Vec2 a = OutPort(from), b = InPort(n, port == 0);
                    bool loss = IsLoss(draft, from.Id);
                    uint colour = EditorStyle.U(loss ? new Vec4(0.93f, 0.68f, 0.32f, 0.95f) : new Vec4(0.48f, 0.72f, 0.80f, 0.95f));
                    bool focus = selected == n.Id || selected == from.Id;
                    Wire(draw, a, b, colour, focus ? 3.2f : 2.2f);
                    if (zoom > 0.6f && Vec2.Distance(a, b) > 110 && values.TryGetValue(from.Id, out double v))
                    {
                        Vec2 mid = (a + b) / 2; string label = Format(v);
                        Vec2 ts = ImGui.CalcTextSize(label);
                        draw.AddRectFilled(mid - ts / 2 - new Vec2(5, 2), mid + ts / 2 + new Vec2(5, 2), EditorStyle.U(0.06f, 0.08f, 0.07f, 0.9f), 4);
                        draw.AddText(mid - ts / 2, EditorStyle.TextMuted, label);
                    }
                }

            // A wire being dragged from an output port.
            if (linkingFrom != null)
            {
                var from = nodes.Find(v => v.Id == linkingFrom);
                if (from != null) Wire(draw, OutPort(from), io.MousePos, EditorStyle.Selection, 2.5f);
                if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    if (hoverPort != null && !hoverOutput && hoverPort != linkingFrom)
                    {
                        var target = nodes.Find(v => v.Id == hoverPort);
                        if (hoverPortA) target.A = linkingFrom; else target.B = linkingFrom;
                        selected = target.Id; message = $"Kapcsolva: {from?.Name} » {target.Name} ({(hoverPortA ? "A" : "B")})";
                    }
                    linkingFrom = null;
                }
            }

            // Nodes.
            foreach (var n in nodes)
            {
                Vec2 p = NodePos(n), s = new Vec2(NodeWidth, NodeHeight(n)) * zoom;
                Vec4 kind = KindColour(draft, n);
                bool isSelected = selected == n.Id, isHover = hoverNode == n.Id;
                bool error = invalidName != null && n.Name.Length > 0 && invalidName.Contains(n.Name, StringComparison.Ordinal);
                draw.AddRectFilled(p + new Vec2(3, 4), p + s + new Vec2(3, 4), EditorStyle.NodeShadow, 8 * zoom);
                draw.AddRectFilled(p, p + s, isHover ? EditorStyle.NodeBodyHover : EditorStyle.NodeBody, 8 * zoom);
                draw.AddRectFilled(p, p + new Vec2(s.X, Header * zoom), EditorStyle.U(EditorStyle.Dim(kind, 0.55f)), 8 * zoom, ImDrawFlags.RoundCornersTop);
                draw.AddRectFilled(p + new Vec2(0, Header * zoom - 2), p + new Vec2(s.X, Header * zoom), EditorStyle.U(kind));
                uint border = error ? EditorStyle.ErrorColour : isSelected ? EditorStyle.Selection : EditorStyle.U(EditorStyle.Fade(kind, 0.55f));
                draw.AddRect(p, p + s, border, 8 * zoom, ImDrawFlags.None, isSelected || error ? 2.5f : 1.2f);
                float font = ImGui.GetFontSize() * zoom;
                draw.PushClipRect(p, p + s, true);
                draw.AddText(ImGui.GetFont(), font, p + new Vec2(10, 6) * zoom, EditorStyle.TextBright, n.Name);
                var info = Kind(n.Operation);
                string tag = draft.Output == n.Id ? "KIMENET" : info.Symbol;
                Vec2 tagSize = ImGui.CalcTextSize(tag) * zoom;
                draw.AddText(ImGui.GetFont(), font * 0.9f, p + new Vec2(s.X - tagSize.X - 10 * zoom, 7 * zoom), EditorStyle.U(EditorStyle.Fade(HudTheme.Parchment, 0.65f)), tag);
                if (RuleModel.Binary(n.Operation))
                {
                    for (int port = 0; port < 2; port++)
                    {
                        string id = port == 0 ? n.A : n.B;
                        string label = (port == 0 ? "A  " : "B  ") + (nodes.Find(v => v.Id == id)?.Name ?? "— nincs bekötve —");
                        draw.AddText(ImGui.GetFont(), font * 0.9f, p + new Vec2(16, Header + PortRow * port + 6) * zoom,
                            string.IsNullOrEmpty(id) ? EditorStyle.ErrorColour : EditorStyle.TextMuted, label);
                    }
                }
                // The value for the sample inputs, large.
                string valueText = values.TryGetValue(n.Id, out double value) ? Format(value) : "—";
                if (n.Operation == RuleOperation.Constant) valueText = Format(n.Value);
                float valueTop = NodeHeight(n) - ValueRow + 6;
                draw.AddText(ImGui.GetFont(), font * 1.15f, p + new Vec2(12, valueTop) * zoom, EditorStyle.U(kind), valueText);
                string unit = IsLoss(draft, n.Id) ? "állapotveszteség" : "szorzó";
                Vec2 unitSize = ImGui.CalcTextSize(unit) * zoom * 0.85f;
                draw.AddText(ImGui.GetFont(), font * 0.85f, p + new Vec2(s.X / zoom - 10 - unitSize.X / zoom, valueTop + 3) * zoom, EditorStyle.TextMuted, unit);
                draw.PopClipRect();
                // Ports.
                bool outHover = hoverPort == n.Id && hoverOutput;
                draw.AddCircleFilled(OutPort(n), (outHover ? 7 : 5.5f) * zoom, EditorStyle.U(kind));
                draw.AddCircle(OutPort(n), (outHover ? 7 : 5.5f) * zoom, EditorStyle.Canvas, 0, 2);
                if (RuleModel.Binary(n.Operation))
                    for (int port = 0; port < 2; port++)
                    {
                        bool a = port == 0, connected = !string.IsNullOrEmpty(a ? n.A : n.B);
                        bool ph = hoverPort == n.Id && !hoverOutput && hoverPortA == a;
                        uint pc = ph && linkingFrom != null ? EditorStyle.Selection : connected ? EditorStyle.TextMuted : EditorStyle.ErrorColour;
                        draw.AddCircleFilled(InPort(n, a), (ph ? 7 : 5) * zoom, pc);
                        draw.AddCircle(InPort(n, a), (ph ? 7 : 5) * zoom, EditorStyle.Canvas, 0, 2);
                    }
            }

            // Mouse: start a link from an output port, unplug an input (drag it elsewhere), select and drag nodes.
            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                if (hoverPort != null && hoverOutput) linkingFrom = hoverPort;
                else if (hoverPort != null)
                {
                    var target = nodes.Find(v => v.Id == hoverPort);
                    string source = hoverPortA ? target.A : target.B;
                    if (!string.IsNullOrEmpty(source)) { if (hoverPortA) target.A = ""; else target.B = ""; linkingFrom = source; }
                }
                else if (hoverNode != null) { selected = hoverNode; dragging = hoverNode; }
                else selected = "";
            }
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) dragging = null;
            if (dragging != null && linkingFrom == null && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 1))
            {
                var n = nodes.Find(v => v.Id == dragging);
                if (n != null) { n.X += io.MouseDelta.X / zoom; n.Y += io.MouseDelta.Y / zoom; }
            }
            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) panned = false;
            if (hovered && ImGui.IsMouseReleased(ImGuiMouseButton.Right) && !panned)
            {
                contextPosition = (io.MousePos - origin - pan) / zoom;
                if (hoverNode != null) selected = hoverNode;
                ImGui.OpenPopup(hoverNode != null ? "node-menu" : "canvas-menu");
            }
            draw.PopClipRect();

            // Keyboard.
            if (focused && !io.WantTextInput)
            {
                if ((ImGui.IsKeyPressed(ImGuiKey.Delete) || ImGui.IsKeyPressed(ImGuiKey.Backspace)) && selected.Length > 0) DeleteSelected();
                if (ImGui.IsKeyPressed(ImGuiKey.F)) Fit(size);
            }
            if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S)) Save();

            if (ImGui.BeginPopup("canvas-menu"))
            {
                EditorStyle.Section("Új csomópont");
                foreach (var kind in Kinds)
                    if (ImGui.MenuItem(kind.Name, kind.Symbol)) AddNode(kind.Op, contextPosition);
                ImGui.EndPopup();
            }
            if (ImGui.BeginPopup("node-menu"))
            {
                var n = nodes.Find(v => v.Id == selected);
                if (n != null)
                {
                    if (ImGui.MenuItem("Legyen ez a kimenet", "", draft.Output == n.Id)) draft.Output = n.Id;
                    if (ImGui.MenuItem("Kettőzés")) { var copy = AddNode(n.Operation, new Vec2(n.X + 30, n.Y + 30)); copy.Value = n.Value; copy.Name = n.Name + " (másolat)"; copy.A = n.A; copy.B = n.B; }
                    if (ImGui.MenuItem("Törlés", "Del")) DeleteSelected();
                }
                ImGui.EndPopup();
            }

            // Overlay: hints and view controls.
            ImGui.SetCursorScreenPos(origin + new Vec2(10, 10));
            if (EditorStyle.QuietButton("+ Csomópont")) { contextPosition = (size / 2 - pan) / zoom; ImGui.OpenPopup("canvas-menu"); }
            ImGui.SameLine();
            if (EditorStyle.QuietButton("Nézet igazítása", "Minden csomópont a képbe (F)")) Fit(size);
            ImGui.SameLine();
            EditorStyle.Text(HudTheme.Muted, $"{zoom * 100:0}%");
            ImGui.SetCursorScreenPos(origin + new Vec2(10, size.Y - ImGui.GetTextLineHeight() - 8));
            EditorStyle.Text(EditorStyle.Fade(HudTheme.Muted, 0.8f),
                "Húzás kimenettől bemenetig: kapcsolás · bemenet elhúzása: leválasztás · jobb gomb: menü / mozgatás · görgő: nagyítás · Del: törlés");
        }

        private static void Wire(ImDrawListPtr draw, Vec2 a, Vec2 b, uint colour, float width)
        {
            float pull = Math.Max(40, Math.Abs(b.X - a.X) * 0.5f);
            draw.AddBezierCubic(a, a + new Vec2(pull, 0), b - new Vec2(pull, 0), b, colour, width);
        }

        private static bool Near(Vec2 a, Vec2 b, float r) => (a - b).LengthSquared() <= r * r;

        private static string Format(double v) => Math.Abs(v) >= 1000 || (Math.Abs(v) < 0.001 && v != 0) ? v.ToString("0.###e0") : v.ToString("0.#####");

        private RuleNode AddNode(RuleOperation op, Vec2 at)
        {
            if (draft.Nodes.Count >= 128) { message = "Legfeljebb 128 csomópont lehet."; return null; }
            var node = new RuleNode { Id = Guid.NewGuid().ToString("N"), Name = Kind(op).Name, Operation = op, X = at.X, Y = at.Y };
            draft.Nodes.Add(node); selected = node.Id;
            return node;
        }

        private void DeleteSelected()
        {
            var n = draft.Nodes.Find(v => v?.Id == selected);
            if (n == null) return;
            draft.Nodes.Remove(n);
            foreach (var other in draft.Nodes.Where(v => v != null)) { if (other.A == n.Id) other.A = ""; if (other.B == n.Id) other.B = ""; }
            message = $"Törölve: {n.Name}"; selected = "";
        }

        private void Fit(Vec2 size)
        {
            var nodes = draft.Nodes.Where(n => n != null).ToList();
            if (nodes.Count == 0) { pan = new(40, 40); zoom = 1; return; }
            float minX = nodes.Min(n => n.X), minY = nodes.Min(n => n.Y);
            float maxX = nodes.Max(n => n.X + NodeWidth), maxY = nodes.Max(n => n.Y + NodeHeight(n));
            Vec2 content = new(maxX - minX + 120, maxY - minY + 140);
            zoom = Math.Clamp(Math.Min(size.X / content.X, size.Y / content.Y), 0.4f, 1.3f);
            pan = (size - new Vec2(maxX - minX, maxY - minY) * zoom) / 2 - new Vec2(minX, minY) * zoom;
        }

        // ── Inspector ────────────────────────────────────────────────────────
        private void Inspector(GameWorld world, CompiledRoadRule compiled)
        {
            var n = draft.Nodes.Find(v => v?.Id == selected);
            if (n != null)
            {
                var info = Kind(n.Operation);
                EditorStyle.Chip(draft.Output == n.Id ? "Kimenet" : n.Operation <= RuleOperation.RoadDamage ? "Világ bemenete"
                    : n.Operation == RuleOperation.Constant ? "Állandó" : "Művelet", KindColour(draft, n), "kind");
                ImGui.SameLine(); EditorStyle.Text(HudTheme.Muted, info.Symbol);
                string name = n.Name; ImGui.SetNextItemWidth(-1);
                if (ImGui.InputText("##node-name", ref name, 120)) n.Name = name;
                ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, info.Help); ImGui.PopTextWrapPos();
                if (n.Operation == RuleOperation.Constant)
                {
                    EditorStyle.Section("Érték");
                    double value = n.Value; ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputDouble("##value", ref value, 0.001, 0.1, "%.5f")) n.Value = Math.Max(0, value);
                }
                if (RuleModel.Binary(n.Operation))
                {
                    EditorStyle.Section("Bemenetek");
                    Link("A", n, true); Link("B", n, false);
                }
                EditorStyle.Section("Szerep");
                bool isOutput = draft.Output == n.Id;
                if (ImGui.Checkbox("Ez a modell kimenete", ref isOutput) && isOutput) draft.Output = n.Id;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("A kimenet értéke csökkenti az út állapotát áthaladáskor (0–1 közé korlátozva).");
                ImGui.Spacing();
                if (ImGui.Button("Csomópont törlése")) DeleteSelected();
            }
            else
            {
                EditorStyle.Title(draft.Name);
                string name = draft.Name; ImGui.SetNextItemWidth(-1);
                if (ImGui.InputText("##model-name", ref name, 120)) draft.Name = name;
                ImGui.PushTextWrapPos();
                EditorStyle.Text(HudTheme.Muted, "Világkötés: minden közúti csempe, jármű-áthaladáskor. A kimenet az út állapotát csökkenti; a rosszabb út lassítja a járműveket és növeli a fogyasztást, ami visszahat a következő áthaladásra.");
                ImGui.PopTextWrapPos();
                EditorStyle.Section("Jelmagyarázat");
                EditorStyle.Chip("Világ bemenete", EditorStyle.InputKind, "l1"); ImGui.SameLine(); EditorStyle.Chip("Állandó", EditorStyle.ConstantKind, "l2");
                EditorStyle.Chip("Művelet", EditorStyle.OperationKind, "l3"); ImGui.SameLine(); EditorStyle.Chip("Kimenet", EditorStyle.OutputKind, "l4");
                EditorStyle.Text(HudTheme.Muted, "Narancs vezeték: állapotveszteség · kék: szorzó");
            }

            // Test bench: the sample inputs every node's value is computed for.
            EditorStyle.Section("Próbapad");
            ImGui.SetNextItemWidth(-90); ImGui.SliderFloat("Terhelés", ref sampleWear, 0, 0.01f, "%.5f");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Áthaladási terhelés: 0,0015 × össztömeg / 36 t (egy rakott teherautó kb. 0,0015).");
            ImGui.SetNextItemWidth(-90); ImGui.SliderFloat("Burkolat", ref sampleSurface, 0, 1, "%.2f");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Aszfalt 0,2 · makadám 1");
            ImGui.SetNextItemWidth(-90); ImGui.SliderFloat("Sérültség", ref sampleDamage, 0, 1, "%.2f");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("1 – útállapot az áthaladás előtt");
            if (compiled != null)
            {
                try
                {
                    float result = compiled.Evaluate(new(sampleWear, sampleSurface, sampleDamage));
                    ImGui.Spacing();
                    EditorStyle.Text(HudTheme.Muted, "Állapotcsökkenés áthaladásonként");
                    EditorStyle.Title(result.ToString("0.000000"), true, HudTheme.AmberAccent);
                    EditorStyle.Text(HudTheme.Muted, result > 0 ? $"kb. {1 / result:N0} áthaladás nullázza az utat" : "Nem kopik");
                }
                catch (Exception e) when (Expected(e)) { EditorStyle.Text(HudTheme.Bad, e.Message); }
            }
            else
            {
                ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Bad, compiler.ValidationMessage); ImGui.PopTextWrapPos();
            }
            if (!string.IsNullOrEmpty(world.LastRuleSample))
            {
                EditorStyle.Section("Utolsó áthaladás a tesztvilágban");
                ImGui.PushTextWrapPos(); EditorStyle.Text(HudTheme.Muted, world.LastRuleSample); ImGui.PopTextWrapPos();
            }
        }

        private void Link(string label, RuleNode node, bool first)
        {
            string id = first ? node.A : node.B;
            ImGui.AlignTextToFramePadding(); EditorStyle.Text(HudTheme.Muted, label); ImGui.SameLine(28);
            ImGui.SetNextItemWidth(-1);
            if (!ImGui.BeginCombo("##" + label, draft.Nodes.Find(n => n?.Id == id)?.Name ?? "— nincs bekötve —")) return;
            if (ImGui.Selectable("— nincs bekötve —", string.IsNullOrEmpty(id))) { if (first) node.A = ""; else node.B = ""; }
            foreach (var candidate in draft.Nodes.Where(n => n != null && n.Id != node.Id))
                if (ImGui.Selectable(candidate.Name + "##" + candidate.Id, candidate.Id == id))
                { if (first) node.A = candidate.Id; else node.B = candidate.Id; }
            ImGui.EndCombo();
        }

        private void Try(Action action) { try { action(); } catch (Exception e) when (Expected(e)) { message = e.Message; } }
        private static bool Expected(Exception e) => e is IOException or JsonException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException;
    }
}
