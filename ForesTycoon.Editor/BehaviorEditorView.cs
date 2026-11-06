using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor
{
    internal sealed partial class BehaviorEditorView
    {
        private readonly BehaviorEditorState editorState;
        private BehaviorModel model => editorState.Draft;
        private BehaviorHook[] availableHooks => editorState.Hooks;
        private HashSet<string> namedKeys => editorState.NamedKeys;
        private BehaviorGraphEditor graphs => editorState.Graphs;
        private BehaviorControllerEditor controllers => editorState.Controllers;
        internal BehaviorEditorView(BehaviorEditorState state = null, BehaviorViewUiState ui = null)
        {
            editorState = state ?? new(); viewState = ui ?? new();
            editorState.Replaced += ReconcileSelection;
        }
        internal Func<string, System.Collections.Generic.IEnumerable<(ulong Id, string Label)>> ObjectSource { get; set; }
        internal Action ApplyProject { get; set; }
        internal Func<string, string, string[], Action<string>, bool> PickField { get; set; }
        internal Func<string, string> SourceDisplay { get; set; }
        internal Action<BehaviorGraph> StoreGraphTemplate { get; set; }
        internal Func<string, RuleCurve, bool> EditCurve { get; set; }
        internal Action<string, double, Action<double>> EditNumber { get; set; }
        internal Action<BehaviorController> StoreControllerTemplate { get; set; }
        internal void SelectGraph(string id) { if (selectedGraph != id || selectedController != null) CommitEdit(); selectedController = null; selectedGraph = id; selectedNode = model.Graphs.Find(g => g.Id == id)?.Output; }
        internal void RestoreDefault(string hook, string kind, ulong? id)
        {
            var graph = graphs.RestoreDefault(hook, kind, id);
            selectedGraph = graph?.Id;
            if (EcologyResponseProgram.IsPureKind(kind) && !id.HasValue) selectedNode = graph?.Output;
        }
        internal void UseTemplate(RuleTemplate template, ulong? objectId)
        {
            var instance = editorState.Instantiate(template, objectId);
            if (instance.Graph != null) SelectGraph(instance.Graph.Id); else SelectControllerById(instance.Controller.Id);
        }
        internal void Configure(BehaviorHook[] hooks, HashSet<string> keys, Func<string, double> read)
            => editorState.Configure(hooks, keys, read);
        internal void DrawEquation(GameWorld world)
        {
            Validate();
            if (ImGui.Button("Vissza##equation")) Undo();
            ImGui.SameLine(); if (ImGui.Button("Újra##equation")) Redo();
            ImGui.TextWrapped(error ?? "Válassz csomópontot az értéke és kapcsolatainak szerkesztéséhez.");
            var graph = model.Graphs.FirstOrDefault();
            if (graph == null) return;
            float inspectorWidth = Math.Min(300, ImGui.GetContentRegionAvail().X * .43f);
            ImGui.BeginChild("flow-canvas", new Vec2(Math.Max(100, ImGui.GetContentRegionAvail().X - inspectorWidth - 8), 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            Canvas(graph); ImGui.EndChild(); ImGui.SameLine();
            ImGui.BeginChild("flow-inspector", new Vec2(0, 0), ImGuiChildFlags.Borders);
            Inspector(world, graph, true); ImGui.EndChild();
        }
        private readonly BehaviorViewUiState viewState;
        private string message = "";
        private ref string selectedGraph => ref viewState.SelectedGraph;
        private ref string selectedNode => ref viewState.SelectedNode;
        private ref string targetId => ref viewState.TargetId;
        private ref string dragging => ref viewState.Dragging;
        private ref string linking => ref viewState.Linking;
        private EditorEdit layoutEdit;
        private ref string targetSearch => ref viewState.TargetSearch;
        private ref Vec2 pan => ref viewState.Pan;
        private ref float zoom => ref viewState.Zoom;
        private CompiledBehaviorModel compiled => editorState.Compiled;
        private string error => editorState.Error;
        private readonly Dictionary<string, float> inputSamples = new();
        private float sampleNative = 1, sampleState, sampleAmount, sampleDelta = .5f, sampleTime;
        private float sampleCapacity = 25, sampleAvailable = 100, sampleFrom, sampleTo = 1, sampleSurface = 1;
        private static readonly string[] Names = { "Állandó", "Beépített eredmény", "Időlépés", "Szimulációs idő", "Állapot", "Mennyiség",
            "Összeadás", "Kivonás", "Szorzás", "Osztás", "Minimum", "Maximum", "Nagyobb", "Kisebb", "Feltételes választás", "Abszolút érték", "Negálás",
            "Kapacitás", "Elérhető készlet", "Kiinduló csempe", "Célcsempe", "Burkolattípus", "Készlet / világmező", "Objektum tulajdonsága", "Exponenciális válasz", "Hatvány / gyökválasz", "Görbe" };
        internal BehaviorModel Document => editorState.Capture();
        internal void SetAvailability(string id, bool disabled, double? value = null) => graphs.SetAvailability(id, disabled, value);
        internal void LoadDraft(BehaviorModel value) { editorState.LoadDraft(value); ResetSelection(); }
        internal void ResetSelection()
        {
            selectedGraph = model.Graphs.FirstOrDefault()?.Id;
            layoutEdit = null; selectedNode = selectedController = selectedState = dragging = linking = framedGraph = null;
        }
        private void ReconcileSelection()
        {
            if (!model.Graphs.Any(g => g.Id == selectedGraph)) selectedGraph = model.Graphs.FirstOrDefault()?.Id;
            var graph = model.Graphs.Find(g => g.Id == selectedGraph);
            if (graph?.Nodes.Any(n => n.Id == selectedNode) != true) selectedNode = graph?.Output;
            var controller = model.Controllers.Find(c => c.Id == selectedController);
            if (controller == null) selectedController = selectedState = null;
            else if (!controller.States.Any(s => s.Id == selectedState)) selectedState = controller.InitialState;
            layoutEdit = null; dragging = linking = framedGraph = null;
        }
        internal void SelectHook(BehaviorHook hook) => SelectGraph(graphs.Ensure(hook).Id);
        internal void Load(BehaviorModel value)
        {
            editorState.Load(value); ResetSelection();
            selectedController = model.Graphs.Count == 0 ? model.Controllers.FirstOrDefault()?.Id : null;
            selectedState = model.Controllers.Find(c => c.Id == selectedController)?.InitialState;
        }
        internal void CommitEdit()
        {
            var edit = layoutEdit; layoutEdit = null; dragging = null;
            edit?.Commit(); editorState.CommitEdit();
        }
        private void CancelLayoutEdit()
        {
            var edit = layoutEdit; layoutEdit = null; dragging = null; edit?.Cancel();
        }
        internal bool Undo() => editorState.Undo();
        internal bool Redo() => editorState.Redo();
        private void Validate() => editorState.Validate();
        internal void DrawEmbedded(GameWorld world)
        {
            Validate();
            if (ImGui.Button("Visszavonás##embedded")) Undo();
            ImGui.SameLine(); if (ImGui.Button("Újra##embedded")) Redo();
            if (error != null) ImGui.TextWrapped(error);
            var controller = model.Controllers.Find(c => c.Id == selectedController);
            var graph = model.Graphs.Find(g => g.Id == selectedGraph);
            if (controller == null && graph != null) {
                ImGui.TextWrapped(EcologyResponseProgram.IsPureKind(graph.Kind)
                    ? "Te választod a forrásokat és a kapcsolatokat. Új csomópont: objektum tulajdonsága, majd objektum és mező választása. A pontokat összekötheted; a kimenet adja a játékban használt választ. Jobb gomb: mozgatás, görgő: nagyítás."
                    : "Kezdés: jelölj ki egy csomópontot, majd módosítsd jobb oldalt. A szorzó új, összekötött csomópontokat ad az eredményhez; 1 = változatlan, 1,1 = +10%, 0,9 = -10%, a szabály korlátain belül.");
                if (ImGui.Button("Folyamatnézet")) { processLayout = true; fitGraphRequested = false; framedGraph = null; }
                ImGui.SameLine();
                if (ImGui.Button("Szabad elrendezés")) { processLayout = false; framedGraph = null; }
                ImGui.SameLine();
                if (ImGui.Button("Teljes gráf")) { fitGraphRequested = true; framedGraph = null; }
                ImGui.SameLine();
                ImGui.BeginDisabled(graph.Nodes.Count > BehaviorGraphEditor.NodeLimit - 2 || !graph.Nodes.Any(n => n.Id == graph.Output));
                if (ImGui.Button("Hatás hozzáadása")) ImGui.OpenPopup("add-output-effect");
                if (ImGui.BeginPopup("add-output-effect")) {
                    if (ImGui.Selectable("Szorzó az eredményhez")) AddOutputMultiplier();
                    if (ImGui.Selectable("Görbe az eredményhez")) AddOutputCurve();
                    ImGui.EndPopup();
                }
                ImGui.EndDisabled();
            }
            float inspectorWidth = Math.Min(330, ImGui.GetContentRegionAvail().X * .42f);
            ImGui.BeginChild("embedded-behavior-canvas", new Vec2(-inspectorWidth - 8, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);
            if (ImGui.GetContentRegionAvail().X > 0 && ImGui.GetContentRegionAvail().Y > 0) {
                if (controller != null) ControllerCanvas(controller, world);
                else if (graph != null) Canvas(graph);
                else ImGui.TextWrapped("Válassz egy szabálykötést a szerkesztéshez.");
            }
            ImGui.EndChild(); ImGui.SameLine();
            ImGui.BeginChild("embedded-behavior-inspector", new Vec2(0, 0), ImGuiChildFlags.Borders);
            if (controller != null) ControllerInspector(controller, world);
            else if (graph != null) Inspector(world, graph);
            ImGui.EndChild();
        }
        /// <summary>
        /// Makes a quantity influence a hook's result: result × max(0, 1 + strength × (quantity − reference)).
        /// Creates the type graph when missing; the new constants stay editable nodes of the graph.
        /// </summary>
        internal void AddInfluence(BehaviorHook hook, string key, string name, double reference, double strength)
        {
            SelectHook(hook);
            var graph = model.Graphs.Find(g => g.Id == selectedGraph);
            selectedNode = graphs.AddInfluence(graph, key, name, reference, strength) ?? selectedNode;
        }
        internal void AddOutputCurve()
        {
            var graph = model.Graphs.Find(g => g.Id == selectedGraph);
            if (graph == null) return;
            var node = graphs.AddOutputCurve(graph);
            if (node != null) { selectedNode = node.Id; framedGraph = null; }
        }
        internal void AddOutputMultiplier()
        {
            if (selectedController != null) return;
            var graph = model.Graphs.Find(g => g.Id == selectedGraph);
            var output = graph?.Nodes.Find(n => n.Id == graph.Output);
            if (output == null) return;
            var scale = graphs.AddOutputMultiplier(graph);
            if (scale == null) return;
            selectedNode = scale.Id;
            pan = new(20 - output.X * zoom, 20 - output.Y * zoom);
        }
        private bool processLayout = true;
        // Layout is derived from the authored edges; it never introduces another rule model.
        private static System.Collections.Generic.Dictionary<string, Vec2> ProcessPositions(BehaviorGraph graph)
        {
            var positions = new System.Collections.Generic.Dictionary<string, Vec2>();
            var depths = new System.Collections.Generic.Dictionary<string, int>();
            var visiting = new System.Collections.Generic.HashSet<string>();
            int Depth(BehaviorNode node) {
                if (depths.TryGetValue(node.Id, out int known)) return known;
                if (!visiting.Add(node.Id)) return 0;
                int depth = 0;
                foreach (string id in new[] { node.A, node.B, node.C }.Take(BehaviorModel.Inputs(node.Operation))) {
                    var input = graph.Nodes.Find(n => n.Id == id);
                    if (input != null) depth = Math.Max(depth, Depth(input) + 1);
                }
                visiting.Remove(node.Id);
                return depths[node.Id] = depth;
            }
            foreach (var node in graph.Nodes) Depth(node);
            int resultColumn = Math.Max(2, depths.Values.DefaultIfEmpty(0).Max());
            foreach (var column in graph.Nodes.GroupBy(n => n.Id == graph.Output ? resultColumn : depths[n.Id])) {
                int row = 0;
                foreach (var node in column) positions[node.Id] = new Vec2(column.Key * 238, 36 + row++ * 88);
            }
            return positions;
        }
        private bool fitGraphRequested;
        private string framedGraph;
        private void Canvas(BehaviorGraph graph)
        {
            var origin = ImGui.GetCursorScreenPos(); var size = ImGui.GetContentRegionAvail(); var io = ImGui.GetIO();
            if (size.X <= 0 || size.Y <= 0) return; // A clipped/narrow child has no drawable canvas this frame.
            var layout = processLayout ? ProcessPositions(graph) : null;
            Vec2 Location(BehaviorNode node) => layout != null ? layout[node.Id] : new Vec2(node.X, node.Y);
            const float nodeWidth = 174, nodeHeight = 70;
            if (framedGraph != graph.Id && graph.Nodes.Count > 0) {
                float left = graph.Nodes.Min(n => Location(n).X), top = graph.Nodes.Min(n => Location(n).Y);
                var bounds = new Vec2(graph.Nodes.Max(n => Location(n).X) - left + nodeWidth, graph.Nodes.Max(n => Location(n).Y) - top + nodeHeight);
                float fitted = Math.Clamp(Math.Min((size.X - 32) / bounds.X, (size.Y - 32) / bounds.Y), .15f, 1);
                zoom = fitGraphRequested || fitted >= .65f ? fitted : .85f;
                pan = zoom > fitted ? new Vec2(16, 24) - new Vec2(left, top) * zoom : (size - bounds * zoom) / 2 - new Vec2(left, top) * zoom;
                fitGraphRequested = false; framedGraph = graph.Id;
            }
            ImGui.InvisibleButton("behavior-map", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
            bool hover = ImGui.IsItemHovered();
            if (hover && ImGui.IsMouseDragging(ImGuiMouseButton.Right)) pan += io.MouseDelta;
            if (hover && io.MouseWheel != 0) {
                float next = Math.Clamp(zoom * (1 + io.MouseWheel * .1f), .15f, 1.8f);
                pan = io.MousePos - origin - (io.MousePos - origin - pan) * next / zoom; zoom = next;
            }
            ImGui.GetWindowDrawList().AddRectFilled(origin, origin + size, EditorStyle.Canvas, 5);
            var draw = ImGui.GetWindowDrawList(); draw.PushClipRect(origin, origin + size, true);
            var connected = ResponseRuleDescription.ConnectedNodes(graph).Select(n => n.Id).ToHashSet();
            Vec2 Pos(BehaviorNode n) => origin + pan + Location(n) * zoom;
            if (processLayout && graph.Nodes.Count > 0) {
                float right = layout.Values.Max(p => p.X);
                foreach (var column in layout.Values.Select(p => p.X).Distinct()) {
                    string title = column == 0 ? "FORRÁS" : column == right ? "EREDMÉNY" : "KAPCSOLAT · SZÁMÍTÁS";
                    draw.AddText(origin + pan + new Vec2(column, 8) * zoom, EditorStyle.TextMuted, title);
                }
            }
            foreach (var n in graph.Nodes) {
                string[] ports = { n.A, n.B, n.C };
                for (int i = 0; i < BehaviorModel.Inputs(n.Operation); i++) {
                    var from = graph.Nodes.Find(x => x.Id == ports[i]); if (from == null) continue;
                    Vec2 a = Pos(from) + new Vec2(nodeWidth, 32) * zoom, b = Pos(n) + new Vec2(0, 32 + i * 14) * zoom;
                    uint wire = EditorStyle.U(graph.Disabled || !connected.Contains(n.Id) ? HudTheme.Muted : from.Operation == BehaviorOperation.Input ? EditorStyle.InputKind : HudTheme.Good);
                    EditorStyle.Wire(draw, a, b - new Vec2(6, 0) * zoom, wire, 1.8f * zoom, graph.Disabled || !connected.Contains(n.Id), EditorStyle.Canvas, zoom);
                }
            }
            foreach (var n in graph.Nodes) {
                Vec2 p = Pos(n), box = new Vec2(nodeWidth, nodeHeight) * zoom;
                bool over = hover && io.MousePos.X >= p.X && io.MousePos.Y >= p.Y && io.MousePos.X <= p.X + box.X && io.MousePos.Y <= p.Y + box.Y;
                bool portClicked = false;
                Vec2 output = p + new Vec2(nodeWidth, 32) * zoom;

                if (hover && Vec2.Distance(io.MousePos, output) < 10 * zoom && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { linking = n.Id; portClicked = true; }
                for (int port = 0; port < BehaviorModel.Inputs(n.Operation); port++) {
                    Vec2 input = p + new Vec2(0, 32 + port * 14) * zoom;

                    if (hover && linking != null && Vec2.Distance(io.MousePos, input) < 10 * zoom && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                        graphs.SetInput(graph, n, port, linking);
                        linking = null; portClicked = true;
                    }
                }
                if (over && !portClicked && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { selectedNode = dragging = n.Id; }
                if (dragging == n.Id && ImGui.IsMouseDragging(ImGuiMouseButton.Left) && io.MouseDelta != Vec2.Zero) {
                    layoutEdit ??= editorState.BeginEdit("gráf elrendezése");
                    layoutEdit.Apply(() => {
                        if (processLayout) {
                            foreach (var item in graph.Nodes) { item.X = layout[item.Id].X; item.Y = layout[item.Id].Y; }
                            processLayout = false;
                        }
                        n.X += io.MouseDelta.X / zoom; n.Y += io.MouseDelta.Y / zoom;
                    });
                }
                bool result = graph.Output == n.Id;
                bool inactive = graph.Disabled || !connected.Contains(n.Id);
                bool sourceNode = BehaviorModel.Inputs(n.Operation) == 0 && n.Operation != BehaviorOperation.Constant;
                uint tone = inactive ? EditorStyle.TextMuted : result ? EditorStyle.Selection
                    : sourceNode ? EditorStyle.U(EditorStyle.InputKind) : EditorStyle.GridMajor;
                string source = n.Operation is BehaviorOperation.Input or BehaviorOperation.NamedValue ? SourceDisplay?.Invoke(n.Key) ?? n.Name : null;
                string head = source == null ? result ? graph.Name : n.Operation == BehaviorOperation.Constant ? "Állandó érték" : n.Name : source.Split(" · ")[0];
                string middle = source == null ? n.Operation == BehaviorOperation.Constant ? n.Value.ToString("G5") : n.Operation == BehaviorOperation.Curve && RuleCurve.TryParse(n.Key, out var shape) ? RuleCurve.Find(shape.Family)?.Name ?? "Görbe" : Names[(int)n.Operation]
                    : source.Contains(" · ") ? source[(source.IndexOf(" · ", StringComparison.Ordinal) + 3)..] : source;
                if (result && graph.Disabled) middle = "Fix érték: " + (graph.DisabledValue ?? 0).ToString("G5");
                string bottom = result ? graph.Disabled ? "FIX EREDMÉNY" : "EREDMÉNY" : sourceNode ? connected.Contains(n.Id) ? "Forrás" : "Nincs bekötve" : "Számítás";
                EditorStyle.GraphBox(draw, p, box, n.Id == selectedNode ? EditorStyle.Selection : tone,
                    head, source != null ? EditorStyle.TextMuted : EditorStyle.TextBright, middle,
                    source != null ? EditorStyle.TextBright : EditorStyle.U(inactive ? HudTheme.Muted : HudTheme.Good),
                    bottom, inactive ? EditorStyle.TextMuted : result ? EditorStyle.Selection : source != null ? EditorStyle.U(EditorStyle.InputKind) : EditorStyle.TextMuted, over || n.Id == selectedNode, zoom);
                uint portTone = inactive ? EditorStyle.TextMuted : result ? EditorStyle.Selection : source != null ? EditorStyle.U(EditorStyle.InputKind) : EditorStyle.TextBright;
                EditorStyle.Port(draw, output, portTone, graph.Nodes.Any(o => o.A == n.Id || o.B == n.Id || o.C == n.Id), zoom);
                for (int port = 0; port < BehaviorModel.Inputs(n.Operation); port++)
                    EditorStyle.Port(draw, p + new Vec2(0, 32 + port * 14) * zoom, portTone, true, zoom);
                if (over) ImGui.SetTooltip(source ?? n.Name);

            }
            var linkFrom = graph.Nodes.Find(n => n.Id == linking);
            if (linkFrom != null) draw.AddLine(Pos(linkFrom) + new Vec2(nodeWidth, 32) * zoom, io.MousePos, EditorStyle.U(HudTheme.Info), 2);
            if (ImGui.IsKeyPressed(ImGuiKey.Escape) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)) linking = null;
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) CancelLayoutEdit();
            else if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) CommitEdit();
            draw.PopClipRect();
        }
        /// <summary>Fields fill the panel up to a fixed label column, so labels on the right stay readable at the editor's text size.</summary>
        private void Inspector(GameWorld world, BehaviorGraph graph, bool equation = false)
        {
            ImGui.PushItemWidth(-EditorStyle.LabelWidth());
            try { InspectorBody(world, graph, equation); } finally { ImGui.PopItemWidth(); }
        }
        private void InspectorBody(GameWorld world, BehaviorGraph graph, bool equation)
        {
            var hook = availableHooks.Single(h => h.Id == graph.Hook && h.Kind == graph.Kind);
            ImGui.TextWrapped(hook.Schedule + " · " + hook.Unit);
            string name = graph.Name;
            if (ImGui.InputText("Név", ref name, 120)) { graphs.Edit(graph, g => g.Name = name); }
            bool specific = graph.ObjectId.HasValue;
            if (hook.SupportsObjects && ImGui.Checkbox("Egyedi objektum", ref specific)) { graphs.Edit(graph, g => g.ObjectId = specific ? 0UL : null); targetId = graph.ObjectId?.ToString() ?? ""; }
            if (specific) {
                ImGui.InputText("Objektum keresése", ref targetSearch, 120);
                if (!ImGui.IsAnyItemActive()) targetId = graph.ObjectId.Value.ToString();
                if (ImGui.InputText("Objektumazonosító", ref targetId, 24) && ulong.TryParse(targetId, out ulong id)) { graphs.Edit(graph, g => g.ObjectId = id); }
                if (ImGui.BeginCombo("Világ elemei", "Választás a tesztvilágból")) {
                    foreach (var target in (ObjectSource?.Invoke(graph.Kind) ?? world.BehaviorTargets(graph.Kind)).Where(t => targetSearch.Length == 0 || t.Label.Contains(targetSearch, StringComparison.OrdinalIgnoreCase)).Take(200))
                        if (ImGui.Selectable(target.Label)) { graphs.Edit(graph, g => g.ObjectId = target.Id); targetId = target.Id.ToString(); }
                    ImGui.EndCombo();
                }
                ImGui.TextWrapped("Az ID a célvilágra vonatkozik. Új elemhez külön kötés kell; a típusszintű gráf automatikusan érvényes az új elemekre is.");
            }
            if (!equation) {
                if (ImGui.Button("Másolás a szabálytárba")) { if (StoreGraphTemplate != null) StoreGraphTemplate(graph); else message = "A sablonokat a közös Szabályműhely kezeli."; }
                ImGui.SameLine(); if (graphs.CanDelete(graph) && ImGui.Button("Gráf törlése")) { graphs.Delete(graph); selectedGraph = null; return; }
            }
            ImGui.Separator();
            if (ImGui.BeginCombo("Új csomópont", "Művelet választása")) {
                foreach (var op in Enum.GetValues<BehaviorOperation>())
                    if (graphs.CanAddNode(graph, op))
                    if (ImGui.Selectable(Names[(int)op])) {
                        selectedNode = graphs.AddNode(graph, op, Names[(int)op]).Id;
                    }
                ImGui.EndCombo();
            }
            SelectPort("Kimenet", graph.Output, graph, value => graphs.SetOutput(graph, value));
            var node = graph.Nodes.Find(n => n.Id == selectedNode);
            if (node != null) {
                string nodeName = node.Name;
                if (ImGui.InputText("Csomópont neve", ref nodeName, 120)) { graphs.EditNode(graph, node, n => n.Name = nodeName); }
                if (node.Operation == BehaviorOperation.Constant) {
                    if (EditNumber != null) EditNumber("Érték", node.Value, value => { graphs.EditNode(graph, node, n => n.Value = value); });
                    else {
                    double value = node.Value;
                    if (ImGui.InputDouble("Érték", ref value, 0, 0, "%.12g")) { graphs.EditNode(graph, node, n => n.Value = value); }
                    float slide = (float)value;
                    if (ImGui.SliderFloat("Érték csúszkával", ref slide, (float)Math.Min(0, value * 2), (float)Math.Max(1, value * 2), "%.4g")) { graphs.EditNode(graph, node, n => n.Value = slide); }
                    }
                }
                if (node.Operation == BehaviorOperation.Curve) {
                    try {
                        var curve = BehaviorCurves.Read(node.Key);
                        if (EditCurve != null && EditCurve("node-curve-" + node.Id, curve)) { graphs.EditNode(graph, node, n => n.Key = curve.ToFormula()); }
                        else if (EditCurve == null) ImGui.TextWrapped("A görbét a közös Szabályok felületen szerkesztheted.");
                    } catch (System.IO.InvalidDataException e) { ImGui.TextWrapped(e.Message); }
                }
                if (node.Operation == BehaviorOperation.Input) {
                    var fields = hook.Inputs ?? Array.Empty<string>();
                    if (PickField != null) PickField("Forrás tulajdonsága", node.Key, fields, value => { graphs.EditNode(graph, node, n => n.Key = value); });
                    else if (ImGui.BeginCombo("Forrás tulajdonsága", node.Key)) {
                        foreach (string field in fields) if (ImGui.Selectable(field, node.Key == field)) { graphs.EditNode(graph, node, n => n.Key = field); }
                        ImGui.EndCombo();
                    }
                }
                if (node.Operation == BehaviorOperation.NamedValue) {
                    if (PickField != null) PickField("Forrás a világból", node.Key, namedKeys.OrderBy(k => k).ToArray(), value => { graphs.EditNode(graph, node, n => n.Key = value); });
                    else if (ImGui.BeginCombo("Készlet / világmező", node.Key)) {
                        foreach (string key in namedKeys.OrderBy(k => k))
                            if (ImGui.Selectable(key, node.Key == key)) { graphs.EditNode(graph, node, n => n.Key = key); }
                        ImGui.EndCombo();
                    }
                }
                int count = BehaviorModel.Inputs(node.Operation);
                if (count > 0) SelectPort(node.Operation == BehaviorOperation.Select ? "Feltétel" : "Első bemenet", node.A, graph, value => graphs.SetInput(graph, node, 0, value));
                if (count > 1) SelectPort(node.Operation == BehaviorOperation.Select ? "Ha igaz" : "Második bemenet", node.B, graph, value => graphs.SetInput(graph, node, 1, value));
                if (count > 2) SelectPort("Ha hamis", node.C, graph, value => graphs.SetInput(graph, node, 2, value));
                if (ImGui.Button("Csomópont törlése")) { graphs.DeleteNode(graph, node); selectedNode = null; }
            }
            ImGui.Separator(); ImGui.TextUnformatted("Próbaszámítás");
            bool Uses(BehaviorOperation op) => graph.Nodes.Any(n => n.Operation == op);
            if (Uses(BehaviorOperation.Native)) Sample("Beépített eredmény", "native", ref sampleNative);
            if (hook.Kind != "world") {
                if (Uses(BehaviorOperation.State)) Sample(hook.State, "state", ref sampleState);
                if (Uses(BehaviorOperation.Amount)) Sample(hook.Amount, "amount", ref sampleAmount);
                if (Uses(BehaviorOperation.Delta)) Sample("Időlépés", "delta", ref sampleDelta);
                if (Uses(BehaviorOperation.Time)) Sample("Idő", "time", ref sampleTime);
                if (hook.Kind is "truck" or "forwarder" or "processor") {
                    Sample("Kapacitás (m³)", "capacity", ref sampleCapacity); Sample("Elérhető készlet (m³)", "available", ref sampleAvailable);
                    if (hook.Id.StartsWith("route.")) {
                        Sample("Kiinduló csempe-ID", "from", ref sampleFrom); Sample("Célcsempe-ID", "to", ref sampleTo);
                        Sample("Burkolat: 0 terep, 1 aszfalt, 2 makadám, 3 nyom", "surface", ref sampleSurface);
                    }
                }
            }
            foreach (string input in graph.Nodes.Where(n => n.Operation == BehaviorOperation.Input).Select(n => n.Key).Distinct()) {
                float sample = inputSamples.GetValueOrDefault(input, 1);
                Sample("Forrás próbája: " + (SourceDisplay?.Invoke(input) ?? input), "input-" + input, ref sample); inputSamples[input] = sample;
            }
            Validate();
            if (compiled != null) {
                double value = compiled.Evaluate(graph.Hook, graph.Kind, graph.ObjectId ?? ulong.MaxValue,
                    new(sampleNative, sampleDelta, sampleTime, sampleState, sampleAmount, sampleCapacity, sampleAvailable, sampleFrom, sampleTo, sampleSurface),
                    (hook.Inputs ?? Array.Empty<string>()).Select(f => (double)inputSamples.GetValueOrDefault(f, 1)).ToArray());
                ImGui.TextWrapped($"Eredmény: {value:G6} {hook.Unit}; korlát: {hook.Min:G4}–{hook.Max:G4}");
                if (compiled.LastError != null) ImGui.TextWrapped(compiled.LastError);
            }
        }
        private void SelectPort(string label, string selected, BehaviorGraph graph, Action<string> set)
        {
            if (!ImGui.BeginCombo(label, graph.Nodes.Find(n => n.Id == selected)?.Name ?? "Hiányzó bemenet")) return;
            foreach (var node in graph.Nodes) if (ImGui.Selectable(node.Name + "##" + node.Id, node.Id == selected)) { set(node.Id); }
            ImGui.EndCombo();
        }
        private static void Sample(string label, string id, ref float value)
        { ImGui.TextWrapped(label); ImGui.SetNextItemWidth(-1); ImGui.InputFloat("##sample-" + id, ref value); }
        private void Try(Action action) { try { action(); } catch (Exception e) { message = e.Message; } }
    }
}
