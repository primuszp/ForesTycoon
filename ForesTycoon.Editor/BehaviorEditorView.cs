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
        private BehaviorModel model = new();
        private readonly Stack<string> undo = new(), redo = new();
        private string selectedGraph, selectedNode, message = "", targetId = "", dragging, linking;
        private string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForesTycoon", "rules", "behaviors.json");
        private int hookIndex;
        private Vec2 pan = new(20, 20);
        private float zoom = .85f;
        private string validatedJson;
        private CompiledBehaviorModel compiled;
        private string error;
        private float sampleNative = 1, sampleState, sampleAmount, sampleDelta = .5f, sampleTime;
        private float sampleCapacity = 25, sampleAvailable = 100, sampleFrom, sampleTo = 1, sampleSurface = 1;
        private readonly FixedStepClock clock = new() { IsPaused = true };
        internal void Advance(GameWorld world, double elapsed) => clock.Advance(Math.Min(elapsed, .25), world.Update);
        private static readonly string[] Names = { "Állandó", "Beépített eredmény", "Időlépés", "Szimulációs idő", "Állapot", "Mennyiség",
            "Összeadás", "Kivonás", "Szorzás", "Osztás", "Minimum", "Maximum", "Nagyobb", "Kisebb", "Feltételes választás", "Abszolút érték", "Negálás",
            "Kapacitás", "Elérhető készlet", "Kiinduló csempe", "Célcsempe", "Burkolattípus" };
        internal BehaviorModel Document => model.Clone();
        internal void SelectHook(BehaviorHook hook)
        {
            selectedController = null;
            hookIndex = Array.IndexOf(WorldBehaviorPolicy.Hooks, hook);
            var graph = model.Graphs.Find(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null);
            if (graph == null) {
                Remember(); graph = new() { Id = Guid.NewGuid().ToString("N"), Name = hook.Name, Hook = hook.Id, Kind = hook.Kind };
                model.Graphs.Add(graph);
            }
            selectedGraph = graph.Id; selectedNode = graph.Output;
        }
        internal void Load(BehaviorModel value, string file = null)
        {
            _ = new CompiledBehaviorModel(value, WorldBehaviorPolicy.Hooks);
            model = value.Clone(); model.Version = 3; selectedGraph = model.Graphs.FirstOrDefault()?.Id; selectedNode = null;
            selectedController = model.Graphs.Count == 0 ? model.Controllers.FirstOrDefault()?.Id : null;
            selectedState = model.Controllers.Find(c => c.Id == selectedController)?.InitialState;
            if (model.Graphs.Count > 0) hookIndex = Array.FindIndex(WorldBehaviorPolicy.Hooks, h => h.Id == model.Graphs[0].Hook && h.Kind == model.Graphs[0].Kind);
            undo.Clear(); redo.Clear(); validatedJson = null;
            if (file != null) path = file;
        }
        private void Remember() { undo.Push(model.ToJson()); redo.Clear(); }
        private void History(Stack<string> from, Stack<string> to)
        {
            if (from.Count == 0) return;
            to.Push(model.ToJson()); model = BehaviorModel.FromJson(from.Pop());
            selectedGraph = model.Graphs.Any(g => g.Id == selectedGraph) ? selectedGraph : model.Graphs.FirstOrDefault()?.Id;
        }
        private void Validate()
        {
            string json = model.ToJson(); if (json == validatedJson) return;
            validatedJson = json;
            try { compiled = new CompiledBehaviorModel(model, WorldBehaviorPolicy.Hooks); error = null; }
            catch (Exception e) { compiled = null; error = e.Message; }
        }
        internal void Draw(GameWorld world)
        {
            Validate();
            ImGui.SetNextItemWidth(390); ImGui.InputText("Fájl##behavior", ref path, 1024); ImGui.SameLine();
            if (ImGui.Button("Mentés##behavior")) Try(() => {
                if (compiled == null) throw new InvalidDataException(error);
                string file = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file + ".tmp", model.ToJson()); File.Move(file + ".tmp", file, true); message = "Viselkedések és objektumkötések mentve.";
            });
            ImGui.SameLine(); if (ImGui.Button("Megnyitás##behavior")) Try(() => Load(BehaviorModel.FromJson(File.ReadAllText(path)), path));
            ImGui.SameLine(); if (ImGui.Button("Vissza##behavior")) History(undo, redo);
            ImGui.SameLine(); if (ImGui.Button("Újra##behavior")) History(redo, undo);
            if (EditorStyle.PrimaryButton("Viselkedések alkalmazása a tesztvilágban", compiled != null, "Naplózott parancs: a következő események már ezeket a gráfokat használják."))
                Try(() => { world.QueueBehaviors(model); message = "Viselkedésváltás naplózva."; });
            ImGui.SameLine(); if (ImGui.Button("Aktív viselkedések átvétele")) { Remember(); model = world.BehaviorDocument; selectedGraph = model.Graphs.FirstOrDefault()?.Id; }
            if (ImGui.Button(clock.IsPaused ? "Tesztvilág indítása" : "Szünet")) clock.IsPaused = !clock.IsPaused;
            ImGui.SameLine(); if (ImGui.Button("Egy szimulációs lépés")) { world.ExecutePendingCommands(); world.Update(clock.StepSeconds); }
            ImGui.SameLine(); ImGui.TextUnformatted($"Tick: {world.SimulationTick} · {world.ObserveRuleField("water.snow")}");
            ImGui.TextWrapped(error ?? message);
            if (world.BehaviorError != null) ImGui.TextWrapped(world.BehaviorError);
            ImGui.BeginChild("behavior-bindings", new Vec2(270, 0), ImGuiChildFlags.Borders);
            ImGui.TextWrapped("Világkötések · az egyedi kötés felülírja a típusszintű gráfot.");
            ImGui.TextWrapped("Képletgráfok és gépvezérlési állapotgráfok. Az alacsony szintű rakodás és útkeresés beépített művelet.");
            if (ImGui.BeginCombo("Folyamat", WorldBehaviorPolicy.Hooks[hookIndex].Name)) {
                for (int i = 0; i < WorldBehaviorPolicy.Hooks.Length; i++)
                    if (ImGui.Selectable(WorldBehaviorPolicy.Hooks[i].Name, hookIndex == i)) hookIndex = i;
                ImGui.EndCombo();
            }
            if (ImGui.Button("Új típusszintű gráf")) {
                Remember(); var hook = WorldBehaviorPolicy.Hooks[hookIndex];
                var graph = new BehaviorGraph { Id = Guid.NewGuid().ToString("N"), Name = hook.Name, Hook = hook.Id, Kind = hook.Kind };
                model.Graphs.Add(graph); selectedGraph = graph.Id; selectedNode = "native"; selectedController = null;
            }
            ImGui.Separator();
            foreach (var g in model.Graphs)
                if (ImGui.Selectable($"{g.Name} · {(g.ObjectId.HasValue ? "#" + g.ObjectId : g.Kind)}##{g.Id}", selectedGraph == g.Id && selectedController == null)) { selectedGraph = g.Id; selectedNode = null; selectedController = null; }
            ControllerList();
            ImGui.EndChild(); ImGui.SameLine();
            var selected = model.Graphs.Find(g => g.Id == selectedGraph);
            ImGui.BeginChild("behavior-canvas", new Vec2(-350, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            var controller = model.Controllers.Find(c => c.Id == selectedController);
            if (controller != null) ControllerCanvas(controller, world);
            else if (selected != null) Canvas(selected); else ImGui.TextWrapped("Válassz folyamatot, majd hozz létre egy gráfot. A beépített eredmény változatlanul továbbadása megőrzi az eredeti viselkedést.");
            ImGui.EndChild(); ImGui.SameLine();
            ImGui.BeginChild("behavior-inspector", new Vec2(0, 0), ImGuiChildFlags.Borders);
            if (controller != null) ControllerInspector(controller, world);
            else if (selected != null) Inspector(world, selected);
            ImGui.EndChild();
        }
        private void Canvas(BehaviorGraph graph)
        {
            var origin = ImGui.GetCursorScreenPos(); var size = ImGui.GetContentRegionAvail(); var io = ImGui.GetIO();
            ImGui.InvisibleButton("behavior-map", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
            bool hover = ImGui.IsItemHovered();
            if (hover && ImGui.IsMouseDragging(ImGuiMouseButton.Right)) pan += io.MouseDelta;
            if (hover && io.MouseWheel != 0) {
                float next = Math.Clamp(zoom * (1 + io.MouseWheel * .1f), .3f, 1.8f);
                pan = io.MousePos - origin - (io.MousePos - origin - pan) * next / zoom; zoom = next;
            }
            EditorStyle.Grid(ImGui.GetWindowDrawList(), origin, size, pan, zoom);
            var draw = ImGui.GetWindowDrawList(); draw.PushClipRect(origin, origin + size, true);
            Vec2 Pos(BehaviorNode n) => origin + pan + new Vec2(n.X, n.Y) * zoom;
            foreach (var n in graph.Nodes) {
                string[] ports = { n.A, n.B, n.C };
                for (int i = 0; i < BehaviorModel.Inputs(n.Operation); i++) {
                    var from = graph.Nodes.Find(x => x.Id == ports[i]); if (from == null) continue;
                    Vec2 a = Pos(from) + new Vec2(190, 32) * zoom, b = Pos(n) + new Vec2(0, 32 + i * 14) * zoom;
                    draw.AddBezierCubic(a, a + new Vec2(60, 0) * zoom, b - new Vec2(60, 0) * zoom, b, EditorStyle.U(HudTheme.Info), 2);
                }
            }
            foreach (var n in graph.Nodes) {
                Vec2 p = Pos(n), box = new Vec2(190, 85) * zoom;
                bool over = hover && io.MousePos.X >= p.X && io.MousePos.Y >= p.Y && io.MousePos.X <= p.X + box.X && io.MousePos.Y <= p.Y + box.Y;
                bool portClicked = false;
                Vec2 output = p + new Vec2(190, 32) * zoom;
                draw.AddCircleFilled(output, 5 * zoom, EditorStyle.U(HudTheme.AmberAccent));
                if (hover && Vec2.Distance(io.MousePos, output) < 10 * zoom && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { linking = n.Id; portClicked = true; }
                for (int port = 0; port < BehaviorModel.Inputs(n.Operation); port++) {
                    Vec2 input = p + new Vec2(0, 32 + port * 14) * zoom;
                    draw.AddCircleFilled(input, 5 * zoom, EditorStyle.U(HudTheme.Info));
                    if (hover && linking != null && Vec2.Distance(io.MousePos, input) < 10 * zoom && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                        Remember(); if (port == 0) n.A = linking; else if (port == 1) n.B = linking; else n.C = linking;
                        linking = null; portClicked = true;
                    }
                }
                if (over && !portClicked && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { Remember(); selectedNode = dragging = n.Id; }
                if (dragging == n.Id && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) { n.X += io.MouseDelta.X / zoom; n.Y += io.MouseDelta.Y / zoom; }
                draw.AddRectFilled(p, p + box, EditorStyle.NodeBodyHover, 5);
                draw.AddRect(p, p + box, n.Id == selectedNode ? EditorStyle.Selection : EditorStyle.U(HudTheme.Muted), 5);
                draw.PushClipRect(p + new Vec2(6, 4), p + box - new Vec2(6, 4), true);
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * zoom, p + new Vec2(8, 8) * zoom, EditorStyle.U(HudTheme.Parchment), n.Name);
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * zoom, p + new Vec2(8, 32) * zoom, EditorStyle.U(HudTheme.Info), Names[(int)n.Operation]);
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * zoom, p + new Vec2(8, 57) * zoom, EditorStyle.U(HudTheme.AmberAccent),
                    graph.Output == n.Id ? "KIMENET" : n.Operation == BehaviorOperation.Constant ? n.Value.ToString("G5") : n.Id);
                draw.PopClipRect();
            }
            var linkFrom = graph.Nodes.Find(n => n.Id == linking);
            if (linkFrom != null) draw.AddLine(Pos(linkFrom) + new Vec2(190, 32) * zoom, io.MousePos, EditorStyle.U(HudTheme.Info), 2);
            if (ImGui.IsKeyPressed(ImGuiKey.Escape) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)) linking = null;
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) dragging = null;
            draw.PopClipRect();
        }
        private void Inspector(GameWorld world, BehaviorGraph graph)
        {
            var hook = WorldBehaviorPolicy.Hooks.Single(h => h.Id == graph.Hook && h.Kind == graph.Kind);
            ImGui.TextWrapped(hook.Schedule + " · " + hook.Unit);
            string name = graph.Name;
            if (ImGui.InputText("Név", ref name, 120)) { Remember(); graph.Name = name; }
            bool specific = graph.ObjectId.HasValue;
            if (hook.SupportsObjects && ImGui.Checkbox("Egyedi objektum", ref specific)) { Remember(); graph.ObjectId = specific ? 0UL : null; targetId = graph.ObjectId?.ToString() ?? ""; }
            if (specific) {
                if (!ImGui.IsAnyItemActive()) targetId = graph.ObjectId.Value.ToString();
                if (ImGui.InputText("Objektumazonosító", ref targetId, 24) && ulong.TryParse(targetId, out ulong id)) { Remember(); graph.ObjectId = id; }
                if (ImGui.BeginCombo("Világ elemei", "Választás a tesztvilágból")) {
                    foreach (var target in world.BehaviorTargets(graph.Kind).Take(200))
                        if (ImGui.Selectable(target.Label)) { Remember(); graph.ObjectId = target.Id; targetId = target.Id.ToString(); }
                    ImGui.EndCombo();
                }
                ImGui.TextWrapped("Az ID a célvilágra vonatkozik. Új elemhez külön kötés kell; a típusszintű gráf automatikusan érvényes az új elemekre is.");
            }
            if (ImGui.Button("Gráf másolása")) { Remember(); var clone = model.Clone().Graphs.Single(g => g.Id == graph.Id); clone.Id = Guid.NewGuid().ToString("N"); clone.Name += " másolat"; model.Graphs.Add(clone); selectedGraph = clone.Id; }
            ImGui.SameLine(); if (ImGui.Button("Gráf törlése")) { Remember(); model.Graphs.Remove(graph); selectedGraph = null; return; }
            ImGui.Separator();
            if (ImGui.BeginCombo("Új csomópont", "Művelet választása")) {
                foreach (var op in Enum.GetValues<BehaviorOperation>())
                    if (hook.Kind != "world" || op is not (>= BehaviorOperation.Delta and <= BehaviorOperation.Amount or >= BehaviorOperation.Capacity))
                    if (ImGui.Selectable(Names[(int)op])) {
                        Remember(); string id = Guid.NewGuid().ToString("N")[..8];
                        graph.Nodes.Add(new() { Id = id, Name = Names[(int)op], Operation = op, Value = 1,
                            A = graph.Output, B = graph.Output, C = graph.Output, X = 260, Y = 40 + graph.Nodes.Count * 30 }); selectedNode = id;
                    }
                ImGui.EndCombo();
            }
            SelectPort("Kimenet", graph.Output, graph, value => graph.Output = value);
            var node = graph.Nodes.Find(n => n.Id == selectedNode);
            if (node != null) {
                ImGui.TextUnformatted(node.Id); string nodeName = node.Name;
                if (ImGui.InputText("Csomópont neve", ref nodeName, 120)) { Remember(); node.Name = nodeName; }
                if (node.Operation == BehaviorOperation.Constant) {
                    double value = node.Value;
                    if (ImGui.InputDouble("Érték", ref value)) { Remember(); node.Value = value; }
                }
                int count = BehaviorModel.Inputs(node.Operation);
                if (count > 0) SelectPort("A / feltétel", node.A, graph, value => node.A = value);
                if (count > 1) SelectPort("B / igaz", node.B, graph, value => node.B = value);
                if (count > 2) SelectPort("C / hamis", node.C, graph, value => node.C = value);
                if (ImGui.Button("Csomópont törlése")) { Remember(); graph.Nodes.Remove(node); selectedNode = null; }
            }
            ImGui.Separator(); ImGui.TextUnformatted("Próbaszámítás");
            Sample("Beépített eredmény", "native", ref sampleNative);
            if (hook.Kind != "world") {
                Sample(hook.State, "state", ref sampleState); Sample(hook.Amount, "amount", ref sampleAmount);
                Sample("Időlépés", "delta", ref sampleDelta); Sample("Idő", "time", ref sampleTime);
                if (hook.Kind is "truck" or "forwarder" or "processor") {
                    Sample("Kapacitás (m³)", "capacity", ref sampleCapacity); Sample("Elérhető készlet (m³)", "available", ref sampleAvailable);
                    if (hook.Id.StartsWith("route.")) {
                        Sample("Kiinduló csempe-ID", "from", ref sampleFrom); Sample("Célcsempe-ID", "to", ref sampleTo);
                        Sample("Burkolat: 0 terep, 1 aszfalt, 2 makadám, 3 nyom", "surface", ref sampleSurface);
                    }
                }
            }
            Validate();
            if (compiled != null) {
                double value = compiled.Evaluate(graph.Hook, graph.Kind, graph.ObjectId ?? ulong.MaxValue,
                    new(sampleNative, sampleDelta, sampleTime, sampleState, sampleAmount, sampleCapacity, sampleAvailable, sampleFrom, sampleTo, sampleSurface));
                ImGui.TextWrapped($"Eredmény: {value:G6} {hook.Unit}; korlát: {hook.Min:G4}–{hook.Max:G4}");
                if (compiled.LastError != null) ImGui.TextWrapped(compiled.LastError);
            }
        }
        private void SelectPort(string label, string selected, BehaviorGraph graph, Action<string> set)
        {
            if (!ImGui.BeginCombo(label, graph.Nodes.Find(n => n.Id == selected)?.Name ?? "Hiányzó bemenet")) return;
            foreach (var node in graph.Nodes) if (ImGui.Selectable(node.Name + "##" + node.Id, node.Id == selected)) { Remember(); set(node.Id); }
            ImGui.EndCombo();
        }
        private static void Sample(string label, string id, ref float value)
        { ImGui.TextWrapped(label); ImGui.SetNextItemWidth(-1); ImGui.InputFloat("##sample-" + id, ref value); }
        private void Try(Action action) { try { action(); } catch (Exception e) { message = e.Message; } }
    }
}
