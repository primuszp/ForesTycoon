using System;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor
{
    internal sealed partial class BehaviorEditorView
    {
        private string selectedController, selectedState;
        private static readonly string[] ActionNames = { "Automatikus munkavégzés", "Várakozás", "Hazatérés kérése", "Csak felrakodás", "Csak lerakodás", "Csak haladás", "Indulás a rakománnyal" };
        private static readonly string[] TriggerNames = { "Mindig", "Állapotban eltelt idő (s)", "Rakomány legalább (m³)", "Rakomány kisebb (m³)",
            "Gép állapota", "Belépés gépállapotba", "Feladatot kapott", "Feladat befejeződött", "Elromlott", "Megjavult" };
        private static readonly string[] MachineStateNames = { "Parkol", "Halad", "Kitermel", "Rakodik", "Lerakodik" };
        private static readonly string[] TruckStateNames = { "Telephelyen", "Forráshoz tart", "Rakodik", "Rakománnyal halad", "Lerakodik", "Üresen visszatér", "Hazafelé tart", "Készletre vár", "Útvonal megszakadt" };
        internal void SelectController(string kind)
        {
            var controller = model.Controllers.Find(c => c.Kind == kind && c.ObjectId == null);
            if (controller == null) { Remember(); controller = BehaviorController.TimedPause(kind); model.Controllers.Add(controller); }
            selectedController = controller.Id; selectedState = controller.InitialState;
        }
        private void ControllerList()
        {
            ImGui.Separator(); ImGui.TextUnformatted("Gépvezérlés · állapotgráf");
            foreach (string kind in new[] { "forwarder", "processor", "truck" }) {
                if (ImGui.Button("Új " + kind + " vezérlés")) {
                    Remember(); var controller = BehaviorController.TimedPause(kind);
                    model.Controllers.Add(controller); selectedController = controller.Id; selectedState = controller.InitialState;
                }
            }
            if (ImGui.Button("Teherautó szállítási ciklus")) {
                Remember(); var controller = BehaviorController.TransportCycle("truck");
                model.Controllers.Add(controller); selectedController = controller.Id; selectedState = controller.InitialState;
            }
            if (ImGui.Button("Forwarder szállítási ciklus")) {
                Remember(); var controller = BehaviorController.TransportCycle("forwarder");
                model.Controllers.Add(controller); selectedController = controller.Id; selectedState = controller.InitialState;
            }
            foreach (var controller in model.Controllers)
                if (ImGui.Selectable($"{controller.Name} · {controller.Kind} {(controller.ObjectId.HasValue ? "#" + controller.ObjectId : "típus")}##ctl{controller.Id}", selectedController == controller.Id)) {
                    selectedController = controller.Id; selectedState = controller.InitialState;
                }
        }
        private void ControllerCanvas(BehaviorController controller, GameWorld world)
        {
            var origin = ImGui.GetCursorScreenPos(); var size = ImGui.GetContentRegionAvail(); var io = ImGui.GetIO();
            ImGui.InvisibleButton("controller-map", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
            bool hover = ImGui.IsItemHovered();
            if (hover && ImGui.IsMouseDragging(ImGuiMouseButton.Right)) pan += io.MouseDelta;
            if (hover && io.MouseWheel != 0) {
                float next = Math.Clamp(zoom * (1 + io.MouseWheel * .1f), .3f, 1.8f);
                pan = io.MousePos - origin - (io.MousePos - origin - pan) * next / zoom; zoom = next;
            }
            var draw = ImGui.GetWindowDrawList(); EditorStyle.Grid(draw, origin, size, pan, zoom);
            draw.PushClipRect(origin, origin + size, true);
            Vec2 Pos(BehaviorState state) => origin + pan + new Vec2(state.X, state.Y) * zoom;
            foreach (var state in controller.States)
                foreach (var group in state.Transitions.Select((transition, index) => (transition.Target, Priority: index + 1)).GroupBy(t => t.Target)) {
                    var target = controller.States.Find(s => s.Id == group.Key); if (target == null) continue;
                    Vec2 a, b, direction;
                    var delta = Pos(target) - Pos(state);
                    if (Math.Abs(delta.Y) > Math.Abs(delta.X)) {
                        float sign = delta.Y > 0 ? 1 : -1;
                        a = Pos(state) + new Vec2(105, sign > 0 ? 100 : 0) * zoom;
                        b = Pos(target) + new Vec2(105, sign > 0 ? 0 : 100) * zoom; direction = new Vec2(0, sign);
                    } else {
                        float sign = delta.X >= 0 ? 1 : -1;
                        a = Pos(state) + new Vec2(sign > 0 ? 210 : 0, 45) * zoom;
                        b = Pos(target) + new Vec2(sign > 0 ? 0 : 210, 45) * zoom; direction = new Vec2(sign, 0);
                    }
                    Vec2 normal = new(-direction.Y, direction.X);
                    bool selected = state.Id == selectedState;
                    uint colour = selected ? EditorStyle.U(HudTheme.Info) : EditorStyle.U(EditorStyle.Fade(HudTheme.Muted, .55f));
                    EditorStyle.BezierArrow(draw, a, a + (direction * 65 + normal * 25) * zoom,
                        b - (direction * 65 - normal * 25) * zoom, b, 10 * zoom, colour, selected ? 2 : 1, direction);
                    if (selected) draw.AddText((a + b) * .5f + normal * 22 * zoom, EditorStyle.U(HudTheme.Parchment), string.Join(", ", group.Select(t => t.Priority)));
                }
            var active = world.BehaviorStates.Where(s => s.Controller == controller.Id).Select(s => s.State).ToHashSet();
            foreach (var state in controller.States) {
                var p = Pos(state); var box = new Vec2(210, 100) * zoom;
                bool over = hover && io.MousePos.X >= p.X && io.MousePos.Y >= p.Y && io.MousePos.X <= p.X + box.X && io.MousePos.Y <= p.Y + box.Y;
                if (over && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { Remember(); selectedState = dragging = state.Id; }
                if (dragging == state.Id && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) { state.X += io.MouseDelta.X / zoom; state.Y += io.MouseDelta.Y / zoom; }
                draw.AddRectFilled(p, p + box, EditorStyle.NodeBodyHover, 5);
                draw.AddRect(p, p + box, selectedState == state.Id ? EditorStyle.Selection : EditorStyle.U(HudTheme.Muted), 5, ImDrawFlags.None, active.Contains(state.Id) ? 3 : 1);
                draw.PushClipRect(p + new Vec2(6, 4), p + box - new Vec2(6, 4), true);
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * zoom, p + new Vec2(8, 8) * zoom, EditorStyle.U(HudTheme.Parchment), state.Name);
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * zoom, p + new Vec2(8, 36) * zoom, EditorStyle.U(HudTheme.Info), ActionNames[(int)state.Action]);
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * zoom, p + new Vec2(8, 67) * zoom, EditorStyle.U(HudTheme.AmberAccent),
                    (controller.InitialState == state.Id ? "KEZDŐ " : "") + (active.Contains(state.Id) ? " · FUT" : ""));
                draw.PopClipRect();
            }
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) dragging = null;
            draw.PopClipRect();
        }
        private void ControllerInspector(BehaviorController controller, GameWorld world)
        {
            ImGui.TextWrapped("Az állapot művelete minden géplépésben fut. Az első teljesülő átmenet nyer; lépésenként legfeljebb egy váltás történik.");
            string name = controller.Name;
            if (ImGui.InputText("Vezérlés neve", ref name, 120)) { Remember(); controller.Name = name; }
            bool specific = controller.ObjectId.HasValue;
            if (ImGui.Checkbox("Egyedi gép", ref specific)) { Remember(); controller.ObjectId = specific ? 0UL : null; }
            if (specific) {
                if (!ImGui.IsAnyItemActive()) targetId = controller.ObjectId.Value.ToString();
                if (ImGui.InputText("Gépazonosító", ref targetId, 24) && ulong.TryParse(targetId, out ulong value)) { Remember(); controller.ObjectId = value; }
                if (ImGui.BeginCombo("Gépek", "Választás a világból")) {
                    foreach (var target in world.BehaviorTargets(controller.Kind))
                        if (ImGui.Selectable(target.Label)) { Remember(); controller.ObjectId = target.Id; }
                    ImGui.EndCombo();
                }
            }
            if (ImGui.Button("Vezérlés másolása")) {
                Remember(); var copy = model.Clone().Controllers.Single(c => c.Id == controller.Id);
                copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " másolat"; model.Controllers.Add(copy); selectedController = copy.Id;
            }
            if (ImGui.Button("Vezérlés törlése")) { Remember(); model.Controllers.Remove(controller); selectedController = null; return; }
            StateChoice("Kezdőállapot", controller, controller.InitialState, value => controller.InitialState = value);
            if (ImGui.Button("Új állapot")) {
                Remember(); var added = new BehaviorState { Id = Guid.NewGuid().ToString("N")[..8], Name = "Új állapot", X = 250, Y = 60 + controller.States.Count * 100 };
                controller.States.Add(added); selectedState = added.Id;
            }
            var state = controller.States.Find(s => s.Id == selectedState);
            if (state != null) {
                ImGui.Separator(); name = state.Name;
                if (ImGui.InputText("Állapot neve", ref name, 120)) { Remember(); state.Name = name; }
                int action = (int)state.Action;
                if (ImGui.Combo("Művelet", ref action, ActionNames, controller.Kind == "processor" ? 3 : ActionNames.Length)) { Remember(); state.Action = (BehaviorAction)action; }
                ImGui.TextWrapped(state.Action == BehaviorAction.ReturnHome ? "A gép befejezi a biztonságos rakodási ciklust, leadja a rakományt, majd hazatér."
                    : state.Action == BehaviorAction.Wait ? "Mozgás és munkavégzés szünetel. A rakomány, a fogott rönk és a javítás megmarad."
                    : state.Action == BehaviorAction.DepartLoaded ? "Rakománnyal elindul a célhoz; forwardernél megvárja a rönkciklus végét."
                    : state.Action >= BehaviorAction.LoadOnly ? "Csak ezt a munkafázist engedi. Átmenettel válts a következő fázisra; a gráf nem helyezi át a járművet."
                    : "A kijelölt feladat beépített mozgási és rakodási lépéseit hajtja végre.");
                if (ImGui.Button("Állapot törlése") && controller.States.Count > 1) {
                    Remember(); controller.States.Remove(state);
                    foreach (var other in controller.States) other.Transitions.RemoveAll(t => t.Target == state.Id);
                    if (controller.InitialState == state.Id) controller.InitialState = controller.States[0].Id;
                    selectedState = controller.InitialState; return;
                }
                ImGui.TextWrapped("Átmenetek prioritási sorrendben. Egy átmeneten belül minden feltételnek teljesülnie kell.");
                for (int i = 0; i < state.Transitions.Count; i++) {
                    ImGui.PushID(i); var transition = state.Transitions[i];
                    ImGui.Separator(); ImGui.TextUnformatted($"{i + 1}. átmenet");
                    StateChoice("Célállapot", controller, transition.Target, value => transition.Target = value);
                    for (int j = 0; j < transition.Conditions.Count; j++) {
                        ImGui.PushID(j); var condition = transition.Conditions[j]; int trigger = (int)condition.Trigger;
                        if (ImGui.Combo("Feltétel", ref trigger, TriggerNames, TriggerNames.Length)) { Remember(); condition.Trigger = (BehaviorTrigger)trigger; condition.Value = 0; }
                        if (condition.Trigger is BehaviorTrigger.MachineState or BehaviorTrigger.EnteredMachineState) {
                            int value = (int)condition.Value;
                            var names = controller.Kind == "truck" ? TruckStateNames : MachineStateNames;
                            if (ImGui.Combo("Gépállapot", ref value, names, names.Length)) { Remember(); condition.Value = value; }
                        } else if (condition.Trigger is BehaviorTrigger.Elapsed or BehaviorTrigger.CargoAtLeast or BehaviorTrigger.CargoBelow) {
                            double value = condition.Value;
                            if (ImGui.InputDouble("Küszöb", ref value)) { Remember(); condition.Value = value; }
                        }
                        bool remove = ImGui.SmallButton("Feltétel törlése") && transition.Conditions.Count > 1;
                        ImGui.PopID();
                        if (remove) { Remember(); transition.Conditions.RemoveAt(j); break; }
                    }
                    if (ImGui.SmallButton("ÉS feltétel")) { Remember(); transition.Conditions.Add(new()); }
                    bool up = ImGui.SmallButton("Előrébb") && i > 0; ImGui.SameLine();
                    bool removeTransition = ImGui.SmallButton("Átmenet törlése"); ImGui.PopID();
                    if (up) { Remember(); (state.Transitions[i - 1], state.Transitions[i]) = (state.Transitions[i], state.Transitions[i - 1]); break; }
                    if (removeTransition) { Remember(); state.Transitions.RemoveAt(i); break; }
                }
                if (ImGui.Button("Új átmenet")) { Remember(); state.Transitions.Add(new() { Target = controller.States.First(s => s.Id != state.Id || controller.States.Count == 1).Id }); }
            }
            ImGui.Separator(); ImGui.TextUnformatted("Futó példányok");
            foreach (var runtime in world.BehaviorStates.Where(s => s.Controller == controller.Id))
                ImGui.TextWrapped($"#{runtime.ObjectId}: {controller.States.Find(s => s.Id == runtime.State)?.Name ?? runtime.State} · {runtime.Elapsed:F2} s");
            ImGui.TextWrapped("Alkalmazáskor a megváltoztatott vezérlés kezdőállapotból indul. A változatlan vezérlés futó állapota megmarad.");
        }
        private void StateChoice(string label, BehaviorController controller, string selected, Action<string> set)
        {
            if (!ImGui.BeginCombo(label, controller.States.Find(s => s.Id == selected)?.Name ?? "Hiányzó állapot")) return;
            foreach (var state in controller.States)
                if (ImGui.Selectable(state.Name + "##" + state.Id, state.Id == selected)) { Remember(); set(state.Id); }
            ImGui.EndCombo();
        }
    }
}
