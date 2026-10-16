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
        private BehaviorHook[] availableHooks = WorldBehaviorPolicy.Hooks;
        private HashSet<string> namedKeys = new(GameWorld.RuleWorldFields);
        private Func<string, double> namedReader = _ => 0;
        internal Func<string, System.Collections.Generic.IEnumerable<(ulong Id, string Label)>> ObjectSource { get; set; }
        internal Action ApplyProject { get; set; }
        internal Func<string, string, string[], Action<string>, bool> PickField { get; set; }
        internal Func<string, string> SourceDisplay { get; set; }
        internal Action<BehaviorGraph> StoreGraphTemplate { get; set; }
        internal Action<BehaviorController> StoreControllerTemplate { get; set; }
        internal void SelectGraph(string id) { selectedController = null; selectedGraph = id; selectedNode = model.Graphs.Find(g => g.Id == id)?.Output; }
        internal void RestoreDefault(string hook, string kind, ulong? id) {
            Remember(); model.Graphs.RemoveAll(g => g.Hook == hook && g.Kind == kind && g.ObjectId == id);
            if (kind == "site") {
                var graph = EcologyResponseProgram.DefaultGraph(availableHooks.Single(h => h.Id == hook && h.Kind == kind));
                model.Graphs.Add(graph); selectedGraph = graph.Id; selectedNode = graph.Output;
            } else selectedGraph = null;
        }
        internal void UseTemplate(RuleTemplate template, ulong? objectId) { Remember(); AuthoringOperations.Instantiate(model, template, objectId); if (template.Graph != null) SelectGraph(model.Graphs.Last().Id); else SelectControllerById(model.Controllers.Last().Id); }
        internal void Configure(BehaviorHook[] hooks, HashSet<string> keys, Func<string, double> read)
        {
            if (!namedKeys.SetEquals(keys) || !availableHooks.SequenceEqual(hooks)) validatedJson = null;
            availableHooks = hooks; namedKeys = keys; namedReader = read;
            hookIndex = Math.Clamp(hookIndex, 0, Math.Max(0, hooks.Length - 1));
        }
        internal void DrawEquation(GameWorld world)
        {
            Validate();
            if (ImGui.Button("Vissza##equation")) History(undo, redo);
            ImGui.SameLine(); if (ImGui.Button("Újra##equation")) History(redo, undo);
            ImGui.TextWrapped(error ?? "Válassz csomópontot az értéke és kapcsolatainak szerkesztéséhez.");
            var graph = model.Graphs.FirstOrDefault();
            if (graph == null) return;
            float inspectorWidth = Math.Min(300, ImGui.GetContentRegionAvail().X * .43f);
            ImGui.BeginChild("flow-canvas", new Vec2(Math.Max(100, ImGui.GetContentRegionAvail().X - inspectorWidth - 8), 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            Canvas(graph); ImGui.EndChild(); ImGui.SameLine();
            ImGui.BeginChild("flow-inspector", new Vec2(0, 0), ImGuiChildFlags.Borders);
            Inspector(world, graph, true); ImGui.EndChild();
        }
        private readonly Stack<string> undo = new(), redo = new();
        private string selectedGraph, selectedNode, message = "", targetId = "", dragging, linking;
        private string targetSearch = "";
        private string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForesTycoon", "rules", "behaviors.json");
        private int hookIndex;
        private Vec2 pan = new(20, 20);
        private float zoom = .85f;
        private string validatedJson;
        private CompiledBehaviorModel compiled;
        private string error;
        private readonly Dictionary<string, float> inputSamples = new();
        private float sampleNative = 1, sampleState, sampleAmount, sampleDelta = .5f, sampleTime;
        private float sampleCapacity = 25, sampleAvailable = 100, sampleFrom, sampleTo = 1, sampleSurface = 1;
        private readonly SimulationFrameRunner sandboxRunner = CreateSandboxRunner();
        private static SimulationFrameRunner CreateSandboxRunner() { var runner = new SimulationFrameRunner(maximumTicksPerFrame: 2048, maximumWorkMilliseconds: 4); runner.Clock.IsPaused = true; return runner; }
        private FixedStepClock clock => sandboxRunner.Clock;
        private TerrainMap sandboxMap;
        internal void Advance(GameWorld world, double elapsed)
        {
            if (sandboxMap != world.Map) { sandboxMap = world.Map; clock.Reset(world.SimulationTick); }
            sandboxRunner.AdvanceWork(elapsed, () => world.ContinueCommands(), dt => world.ContinueUpdate(dt), () => world.ForegroundPending);
            if (sandboxMap != world.Map) { sandboxMap = world.Map; clock.Reset(world.SimulationTick); }
        }
        internal bool IsPaused => clock.IsPaused;
        internal void TogglePause() => clock.IsPaused = !clock.IsPaused;
        internal void Step(GameWorld world) => clock.RequestTick();
        internal void RequestTime(double seconds) => clock.RequestTime(seconds);
        private static readonly string[] Names = { "Állandó", "Beépített eredmény", "Időlépés", "Szimulációs idő", "Állapot", "Mennyiség",
            "Összeadás", "Kivonás", "Szorzás", "Osztás", "Minimum", "Maximum", "Nagyobb", "Kisebb", "Feltételes választás", "Abszolút érték", "Negálás",
            "Kapacitás", "Elérhető készlet", "Kiinduló csempe", "Célcsempe", "Burkolattípus", "Készlet / világmező", "Objektum tulajdonsága", "Exponenciális válasz", "Hatvány / gyökválasz" };
        internal BehaviorModel Document => model.Clone();
        internal string ScopeSummary {
            get {
                var controller = model.Controllers.Find(c => c.Id == selectedController);
                var graph = model.Graphs.Find(g => g.Id == selectedGraph);
                string kind = controller?.Kind ?? graph?.Kind;
                ulong? id = controller != null ? controller.ObjectId : graph?.ObjectId;
                return kind == null ? "Nincs kiválasztott kötés" : RuleWorkshopModel.KindName(kind) + (id.HasValue ? " · egyedi objektum #" + id : " · típusszintű kötés");
            }
        }
        internal void LoadDraft(BehaviorModel value)
        {
            model = value.Clone(); selectedGraph = model.Graphs.FirstOrDefault()?.Id; selectedNode = null;
            selectedController = null; undo.Clear(); redo.Clear(); validatedJson = null;
        }
        internal void SelectHook(BehaviorHook hook)
        {
            selectedController = null;
            hookIndex = Array.IndexOf(availableHooks, hook);
            var graph = model.Graphs.Find(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null);
            if (graph == null) {
                Remember(); graph = hook.Kind == "site" ? EcologyResponseProgram.DefaultGraph(hook) : new() { Id = Guid.NewGuid().ToString("N"), Name = hook.Name, Hook = hook.Id, Kind = hook.Kind };
                model.Graphs.Add(graph);
            }
            selectedGraph = graph.Id; selectedNode = graph.Output;
        }
        internal void SelectObjectHook(BehaviorHook hook, ulong objectId)
        {
            if (!hook.SupportsObjects) { SelectHook(hook); return; }
            var graph = model.Graphs.Find(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == objectId);
            if (graph == null) {
                Remember();
                graph = model.Clone().Graphs.Find(g => g.Hook == hook.Id && g.Kind == hook.Kind && g.ObjectId == null)
                    ?? new BehaviorGraph { Name = hook.Name, Hook = hook.Id, Kind = hook.Kind };
                graph.Id = Guid.NewGuid().ToString("N"); graph.ObjectId = objectId; graph.Name += " #" + objectId;
                model.Graphs.Add(graph);
            }
            selectedController = null; selectedGraph = graph.Id; selectedNode = graph.Output;
            targetId = objectId.ToString(); hookIndex = Array.IndexOf(availableHooks, hook);
        }
        internal void Load(BehaviorModel value, string file = null)
        {
            _ = new CompiledBehaviorModel(value, availableHooks, namedKeys, key => namedReader(key));
            model = value.Clone(); model.Version = 3; selectedGraph = model.Graphs.FirstOrDefault()?.Id; selectedNode = null;
            selectedController = model.Graphs.Count == 0 ? model.Controllers.FirstOrDefault()?.Id : null;
            selectedState = model.Controllers.Find(c => c.Id == selectedController)?.InitialState;
            if (model.Graphs.Count > 0) hookIndex = Array.FindIndex(availableHooks, h => h.Id == model.Graphs[0].Hook && h.Kind == model.Graphs[0].Kind);
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
            try { compiled = new CompiledBehaviorModel(model, availableHooks, namedKeys, key => namedReader(key)); error = null; }
            catch (Exception e) { compiled = null; error = e.Message; }
        }
        internal void DrawEmbedded(GameWorld world)
        {
            Validate();
            if (ImGui.Button("Visszavonás##embedded")) History(undo, redo);
            ImGui.SameLine(); if (ImGui.Button("Újra##embedded")) History(redo, undo);
            if (error != null) ImGui.TextWrapped(error);
            var controller = model.Controllers.Find(c => c.Id == selectedController);
            var graph = model.Graphs.Find(g => g.Id == selectedGraph);
            if (controller == null && graph != null) {
                ImGui.TextWrapped(graph.Kind == "site"
                    ? "Te választod a forrásokat és a kapcsolatokat. Új csomópont: objektum tulajdonsága, majd objektum és mező választása. A pontokat összekötheted; a kimenet adja a játékban használt választ. Jobb gomb: mozgatás, görgő: nagyítás."
                    : "Kezdés: jelölj ki egy csomópontot, majd módosítsd jobb oldalt. A szorzó új, összekötött csomópontokat ad az eredményhez; 1 = változatlan, 1,1 = +10%, 0,9 = -10%, a szabály korlátain belül.");
                if (ImGui.Button("Teljes gráf")) framedGraph = null;
                ImGui.SameLine();
                ImGui.BeginDisabled(graph.Nodes.Count > 254 || !graph.Nodes.Any(n => n.Id == graph.Output));
                if (ImGui.Button("Szorzó hozzáadása az eredményhez")) AddOutputMultiplier();
                ImGui.EndDisabled();
            }
            float inspectorWidth = Math.Min(330, ImGui.GetContentRegionAvail().X * .42f);
            ImGui.BeginChild("embedded-behavior-canvas", new Vec2(-inspectorWidth - 8, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);
            if (controller != null) ControllerCanvas(controller, world);
            else if (graph != null) Canvas(graph);
            else ImGui.TextWrapped("Válassz egy szabálykötést a szerkesztéshez.");
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
            var output = graph?.Nodes.Find(n => n.Id == graph.Output);
            if (output == null || graph.Nodes.Count > 246) return;
            Remember();
            selectedNode = AuthoringOperations.AddInfluence(graph, key, name, reference, strength);
        }
        /// <summary>Replaces the type-level graph of the graph's hook (a rule formula compiled by the world editor).</summary>
        internal void SetTypeGraph(BehaviorGraph graph)
        {
            Remember();
            model.Graphs.RemoveAll(g => g.Hook == graph.Hook && g.Kind == graph.Kind && g.ObjectId == null);
            model.Graphs.Add(graph); selectedGraph = graph.Id; selectedNode = graph.Output;
        }
        internal void AddOutputMultiplier()
        {
            if (selectedController != null) return;
            var graph = model.Graphs.Find(g => g.Id == selectedGraph);
            var output = graph?.Nodes.Find(n => n.Id == graph.Output);
            if (output == null || graph.Nodes.Count > 254) return;
            Remember();
            var scale = new BehaviorNode { Id = Guid.NewGuid().ToString("N"), Name = "Szorzó · ezt az értéket állítsd", Operation = BehaviorOperation.Constant, Value = 1, X = output.X, Y = output.Y + 140 };
            var product = new BehaviorNode { Id = Guid.NewGuid().ToString("N"), Name = "Szorzott eredmény", Operation = BehaviorOperation.Multiply, A = output.Id, B = scale.Id, X = output.X + 250, Y = output.Y + 60 };
            graph.Nodes.Add(scale); graph.Nodes.Add(product); graph.Output = product.Id; selectedNode = scale.Id;
            pan = new(20 - output.X * zoom, 20 - output.Y * zoom);
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
                Try(() => { if (ApplyProject != null) ApplyProject(); else world.QueueBehaviors(model); message = "Szabályváltás naplózva."; });
            ImGui.SameLine(); if (ImGui.Button("Aktív viselkedések átvétele")) { Remember(); model = world.BehaviorDocument; selectedGraph = model.Graphs.FirstOrDefault()?.Id; }
            if (ImGui.Button(clock.IsPaused ? "Tesztvilág indítása" : "Szünet")) clock.IsPaused = !clock.IsPaused;
            ImGui.SameLine(); if (ImGui.Button("Egy szimulációs lépés")) Step(world);
            ImGui.SameLine(); ImGui.TextUnformatted($"Tick: {world.SimulationTick} · {world.ObserveRuleField("water.snow")}");
            ImGui.TextWrapped(error ?? message);
            if (world.BehaviorError != null) ImGui.TextWrapped(world.BehaviorError);
            ImGui.BeginChild("behavior-bindings", new Vec2(270, 0), ImGuiChildFlags.Borders);
            ImGui.TextWrapped("Világkötések · az egyedi kötés felülírja a típusszintű gráfot.");
            ImGui.TextWrapped("Képletgráfok és gépvezérlési állapotgráfok. Az alacsony szintű rakodás és útkeresés beépített művelet.");
            if (ImGui.BeginCombo("Folyamat", availableHooks[hookIndex].Name)) {
                for (int i = 0; i < availableHooks.Length; i++)
                    if (ImGui.Selectable(availableHooks[i].Name, hookIndex == i)) hookIndex = i;
                ImGui.EndCombo();
            }
            if (ImGui.Button("Típusszintű gráf megnyitása")) SelectHook(availableHooks[hookIndex]);
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
        private string framedGraph;
        private void Canvas(BehaviorGraph graph)
        {
            var origin = ImGui.GetCursorScreenPos(); var size = ImGui.GetContentRegionAvail(); var io = ImGui.GetIO();
            if (framedGraph != graph.Id && graph.Nodes.Count > 0) {
                float left = graph.Nodes.Min(n => n.X), top = graph.Nodes.Min(n => n.Y);
                var bounds = new Vec2(graph.Nodes.Max(n => n.X) - left + 190, graph.Nodes.Max(n => n.Y) - top + 85);
                zoom = Math.Clamp(Math.Min((size.X - 32) / bounds.X, (size.Y - 32) / bounds.Y), .15f, 1);
                pan = (size - bounds * zoom) / 2 - new Vec2(left, top) * zoom; framedGraph = graph.Id;
            }
            ImGui.InvisibleButton("behavior-map", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
            bool hover = ImGui.IsItemHovered();
            if (hover && ImGui.IsMouseDragging(ImGuiMouseButton.Right)) pan += io.MouseDelta;
            if (hover && io.MouseWheel != 0) {
                float next = Math.Clamp(zoom * (1 + io.MouseWheel * .1f), .15f, 1.8f);
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
                    EditorStyle.BezierArrow(draw, a, a + new Vec2(60, 0) * zoom, b - new Vec2(60, 0) * zoom, b, 7 * zoom, EditorStyle.U(HudTheme.Info), 2, Vec2.UnitX);
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
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * zoom, p + new Vec2(8, 8) * zoom, EditorStyle.U(HudTheme.Parchment), n.Operation is BehaviorOperation.Input or BehaviorOperation.NamedValue ? SourceDisplay?.Invoke(n.Key) ?? n.Name : n.Name);
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
        private void Inspector(GameWorld world, BehaviorGraph graph, bool equation = false)
        {
            var hook = availableHooks.Single(h => h.Id == graph.Hook && h.Kind == graph.Kind);
            ImGui.TextWrapped(hook.Schedule + " · " + hook.Unit);
            string name = graph.Name;
            if (ImGui.InputText("Név", ref name, 120)) { Remember(); graph.Name = name; }
            bool specific = graph.ObjectId.HasValue;
            if (hook.SupportsObjects && ImGui.Checkbox("Egyedi objektum", ref specific)) { Remember(); graph.ObjectId = specific ? 0UL : null; targetId = graph.ObjectId?.ToString() ?? ""; }
            if (specific) {
                ImGui.InputText("Objektum keresése", ref targetSearch, 120);
                if (!ImGui.IsAnyItemActive()) targetId = graph.ObjectId.Value.ToString();
                if (ImGui.InputText("Objektumazonosító", ref targetId, 24) && ulong.TryParse(targetId, out ulong id)) { Remember(); graph.ObjectId = id; }
                if (ImGui.BeginCombo("Világ elemei", "Választás a tesztvilágból")) {
                    foreach (var target in (ObjectSource?.Invoke(graph.Kind) ?? world.BehaviorTargets(graph.Kind)).Where(t => targetSearch.Length == 0 || t.Label.Contains(targetSearch, StringComparison.OrdinalIgnoreCase)).Take(200))
                        if (ImGui.Selectable(target.Label)) { Remember(); graph.ObjectId = target.Id; targetId = target.Id.ToString(); }
                    ImGui.EndCombo();
                }
                ImGui.TextWrapped("Az ID a célvilágra vonatkozik. Új elemhez külön kötés kell; a típusszintű gráf automatikusan érvényes az új elemekre is.");
            }
            if (!equation) {
                if (ImGui.Button("Másolás a szabálytárba")) { if (StoreGraphTemplate != null) StoreGraphTemplate(graph); else message = "A sablonokat a közös Szabályműhely kezeli."; }
                ImGui.SameLine(); if (hook.Kind != "site" && ImGui.Button("Gráf törlése")) { Remember(); model.Graphs.Remove(graph); selectedGraph = null; return; }
            }
            ImGui.Separator();
            if (ImGui.BeginCombo("Új csomópont", "Művelet választása")) {
                foreach (var op in Enum.GetValues<BehaviorOperation>())
                    if ((hook.Kind != "site" || EcologyResponseProgram.AllowedOperation(op)) && (hook.Kind != "world" || op is not (>= BehaviorOperation.Delta and <= BehaviorOperation.Amount or >= BehaviorOperation.Capacity and <= BehaviorOperation.Surface)))
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
                    if (ImGui.InputDouble("Érték", ref value, 0, 0, "%.12g")) { Remember(); node.Value = value; }
                    float slide = (float)value;
                    if (ImGui.SliderFloat("Érték csúszkával", ref slide, (float)Math.Min(0, value * 2), (float)Math.Max(1, value * 2), "%.4g")) { Remember(); node.Value = slide; }
                }
                if (node.Operation == BehaviorOperation.Input) {
                    var fields = hook.Inputs ?? Array.Empty<string>();
                    if (PickField != null) PickField("Forrás tulajdonsága", node.Key, fields, value => { Remember(); node.Key = value; });
                    else if (ImGui.BeginCombo("Forrás tulajdonsága", node.Key)) {
                        foreach (string field in fields) if (ImGui.Selectable(field, node.Key == field)) { Remember(); node.Key = field; }
                        ImGui.EndCombo();
                    }
                }
                if (node.Operation == BehaviorOperation.NamedValue) {
                    if (PickField != null) PickField("Forrás a világból", node.Key, namedKeys.OrderBy(k => k).ToArray(), value => { Remember(); node.Key = value; });
                    else if (ImGui.BeginCombo("Készlet / világmező", node.Key)) {
                        foreach (string key in namedKeys.OrderBy(k => k))
                            if (ImGui.Selectable(key, node.Key == key)) { Remember(); node.Key = key; }
                        ImGui.EndCombo();
                    }
                }
                int count = BehaviorModel.Inputs(node.Operation);
                if (count > 0) SelectPort("A / feltétel", node.A, graph, value => node.A = value);
                if (count > 1) SelectPort("B / igaz", node.B, graph, value => node.B = value);
                if (count > 2) SelectPort("C / hamis", node.C, graph, value => node.C = value);
                if (ImGui.Button("Csomópont törlése")) { Remember(); graph.Nodes.Remove(node); selectedNode = null; }
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
            foreach (var node in graph.Nodes) if (ImGui.Selectable(node.Name + "##" + node.Id, node.Id == selected)) { Remember(); set(node.Id); }
            ImGui.EndCombo();
        }
        private static void Sample(string label, string id, ref float value)
        { ImGui.TextWrapped(label); ImGui.SetNextItemWidth(-1); ImGui.InputFloat("##sample-" + id, ref value); }
        private void Try(Action action) { try { action(); } catch (Exception e) { message = e.Message; } }
    }
}
