using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal sealed class DynamicsEditorView
{
    private DynamicsModel model = new();
    private string selectedStock, selectedFlow, message = "";
    private readonly BehaviorEditorView equation = new();
    internal BehaviorEditorView EquationEditor => equation;
    private readonly Stack<string> undo = new(), redo = new();
    internal DynamicsModel Document { get { SynchronizeEquation(); return new RuleProject { Dynamics = model }.Clone().Dynamics; } }
    internal DynamicsModel Draft => model;
    internal void Load(DynamicsModel value) {
        model = new RuleProject { Dynamics = value }.Clone().Dynamics;
        selectedFlow = null; selectedStock = model.Stocks.FirstOrDefault()?.Id; undo.Clear(); redo.Clear();
        if (model.Flows.Count > 0) SelectFlow(model.Flows[0]);
    }
    private void Remember() { undo.Push(new RuleProject { Dynamics = model }.ToJson()); redo.Clear(); }
    private void History(Stack<string> from, Stack<string> to)
    {
        SynchronizeEquation();
        if (from.Count == 0) return;
        to.Push(new RuleProject { Dynamics = model }.ToJson()); model = RuleProject.FromJson(from.Pop()).Dynamics; selectedFlow = null;
    }
    internal string CreateStock()
    {
        SynchronizeEquation(); Remember(); var stock = new DynamicsStock { Id = "s" + Guid.NewGuid().ToString("N")[..8] };
        model.Stocks.Add(stock); selectedStock = stock.Id; selectedFlow = null; return stock.Id;
    }
    internal string CreateFlow()
    {
        SynchronizeEquation(); if (model.Stocks.Count == 0) throw new InvalidOperationException("Az áramláshoz előbb készletet hozz létre.");
        Remember(); string id = "f" + Guid.NewGuid().ToString("N")[..8];
        var flow = new DynamicsFlow { Id = id, To = model.Stocks.Any(s => s.Id == selectedStock) ? selectedStock : model.Stocks[0].Id,
            Rate = new BehaviorGraph { Id = "rate." + id, Name = "Áramlási ráta", Hook = "flow." + id, Kind = "dynamics", Output = "rate",
                Nodes = new() { new() { Id = "rate", Name = "Ráta", Operation = BehaviorOperation.Constant, Value = 1, X = 40, Y = 60 } } } };
        model.Flows.Add(flow); SelectFlow(flow); return id;
    }
    internal void SelectItem(string id, bool flow) { SynchronizeEquation(); if (flow) { var item = model.Flows.Find(f => f.Id == id); if (item != null) SelectFlow(item); } else { selectedStock = id; selectedFlow = null; } }
    internal void Draw(GameWorld world, Func<string, double> read, Func<string, string[]> consumers = null, Func<string, bool> visibleStock = null, Func<string, bool> visibleFlow = null)
    {
        SynchronizeEquation();
        if (visibleStock == null) { if (ImGui.Button("Új készlet")) CreateStock(); ImGui.SameLine(); }
        if (visibleFlow == null) { if (ImGui.Button("Új áramlás") && model.Stocks.Count > 0) CreateFlow(); ImGui.SameLine(); }
        if (ImGui.Button("Vissza##dynamics")) History(undo, redo);
        ImGui.SameLine(); if (ImGui.Button("Újra##dynamics")) History(redo, undo);
        ImGui.TextWrapped("Készlet(t+dt) = készlet(t) + beáramlás - kiáramlás. A ráták szimuláció-másodpercenként értendők. Az átadások egyszerre, készlet- és kapacitáskorláttal történnek.");
        try { _ = new DynamicsRuntime(model, GameWorld.RuleWorldFields, read); message = "Érvényes dinamika"; }
        catch (Exception e) { message = e.Message; }
        ImGui.TextWrapped(message);
        ImGui.BeginChild("dynamics-list", new Vec2(260, 0), ImGuiChildFlags.Borders);
        ImGui.TextUnformatted("Készletek");
        foreach (var stock in model.Stocks.Where(s => visibleStock == null || visibleStock(s.Id)))
            if (ImGui.Selectable(stock.Name + "##" + stock.Id, stock.Id == selectedStock)) { SynchronizeEquation(); selectedStock = stock.Id; }
        var selected = model.Stocks.Find(s => s.Id == selectedStock);
        if (selected != null) {
            ImGui.Separator(); ImGui.TextWrapped("Hivatkozás: stock." + selected.Id);
            string name = selected.Name, unit = selected.Unit;
            if (ImGui.InputText("Név##stock", ref name, 120)) { Remember(); selected.Name = name; }
            if (ImGui.InputText("Egység", ref unit, 40)) { Remember(); selected.Unit = unit; }
            double initial = selected.Initial, min = selected.Minimum, max = selected.Maximum;
            if (ImGui.InputDouble("Kezdőérték", ref initial)) { Remember(); selected.Initial = initial; }
            if (ImGui.InputDouble("Minimum", ref min)) { Remember(); selected.Minimum = min; }
            if (ImGui.InputDouble("Maximum", ref max)) { Remember(); selected.Maximum = max; }
            double observed = read("stock." + selected.Id);
            ImGui.TextWrapped(double.IsFinite(observed) ? $"Próbaszámítás bemenete: {observed:G7}" : "A választott világforrásban nincs készletmegfigyelés.");
            string[] dependencies = consumers?.Invoke(selected.Id) ?? model.Flows.Where(f => f.From == selected.Id || f.To == selected.Id).Select(f => f.Name).ToArray();
            if (dependencies.Length > 0) ImGui.TextWrapped("Használja: " + string.Join(", ", dependencies));
            ImGui.BeginDisabled(dependencies.Length > 0);
            if (ImGui.Button("Készlet törlése")) { Remember(); model.Stocks.Remove(selected); selectedStock = null; }
            ImGui.EndDisabled();
        }
        ImGui.Separator(); ImGui.TextUnformatted("Áramlások");
        foreach (var flow in model.Flows.Where(f => visibleFlow == null || visibleFlow(f.Id))) {
            if (ImGui.Selectable(flow.Name + "##" + flow.Id, selectedFlow == flow.Id)) SelectFlow(flow);
            ImGui.TextWrapped(EndpointName(flow.From) + " -> " + EndpointName(flow.To));
        }
        ImGui.EndChild(); ImGui.SameLine();
        ImGui.BeginChild("dynamics-equation", new Vec2(0, 0));
        var active = model.Flows.Find(f => f.Id == selectedFlow);
        if (active != null) {
            if (ImGui.BeginTable("flow-transfer-context", 3, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.RowBg)) {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("FORRÁS"); ImGui.TextWrapped(EndpointName(active.From));
                ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted("ÁTADÁS / RÁTA"); ImGui.TextWrapped(active.Name);
                ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted("CÉLKÉSZLET"); ImGui.TextWrapped(EndpointName(active.To)); ImGui.EndTable();
            }
            string name = active.Name;
            ImGui.SetNextItemWidth(-1); if (ImGui.InputText("##flow-name", ref name, 120)) { Remember(); active.Name = name; }
            Endpoint("Forrás", active.From, value => active.From = value);
            ImGui.SameLine(); Endpoint("Cél", active.To, value => active.To = value);
            if (ImGui.Button("Áramlás törlése")) { Remember(); model.Flows.Remove(active); selectedFlow = null; }
            else {
                equation.Configure(new[] { DynamicsRuntime.FlowHook(active) }, DynamicsRuntime.Keys(model, GameWorld.RuleWorldFields), read);
                equation.DrawEquation(world); SynchronizeEquation();
            }
        } else {
            ImGui.TextWrapped("Válassz áramlást a ráta képletgráfjának szerkesztéséhez. A készletcsomópontokon keresztül késleltetett visszacsatolás építhető, és a viselkedésgráfok ugyanazokat a készleteket olvassák.");
            if (selected != null) {
                EditorStyle.Section("Kapcsolódó áramlások");
                foreach (var flow in model.Flows.Where(f => f.From == selected.Id || f.To == selected.Id || f.Rate.Nodes.Any(n => n.Key == "stock." + selected.Id)))
                    if (ImGui.Selectable(flow.Name + " · " + EndpointName(flow.From) + " -> " + EndpointName(flow.To) + "##dependency" + flow.Id)) SelectFlow(flow);
            }
        }
        ImGui.EndChild();
    }
    internal void DrawSelected(GameWorld world, string key, Func<string, double> read, Func<string, string, string[], Action<string>, bool> pick = null)
    {
        SynchronizeEquation();
        if (key.StartsWith("stock.", StringComparison.Ordinal)) key = "stock:" + key[6..];
        if (key.StartsWith("stock:", StringComparison.Ordinal)) {
            var stock = model.Stocks.Find(s => s.Id == key[6..]);
            if (stock == null) { ImGui.TextWrapped("A készlet már nem létezik."); return; }
            string name = stock.Name, unit = stock.Unit;
            if (ImGui.InputText("Készlet neve", ref name, 120)) { Remember(); stock.Name = name; }
            if (ImGui.InputText("Mértékegység", ref unit, 40)) { Remember(); stock.Unit = unit; }
            double initial = stock.Initial, min = stock.Minimum, max = stock.Maximum;
            if (ImGui.InputDouble("Kezdőérték", ref initial)) { Remember(); stock.Initial = initial; }
            if (ImGui.InputDouble("Minimum", ref min)) { Remember(); stock.Minimum = min; }
            if (ImGui.InputDouble("Maximum", ref max)) { Remember(); stock.Maximum = max; }
            ImGui.TextWrapped($"A világban: {read("stock." + stock.Id):G6} {stock.Unit}");
            bool referenced = model.Flows.Any(f => f.From == stock.Id || f.To == stock.Id || f.Rate.Nodes.Any(n => n.Key == "stock." + stock.Id));
            ImGui.BeginDisabled(referenced);
            if (ImGui.Button("Készlet törlése")) { Remember(); model.Stocks.Remove(stock); ImGui.EndDisabled(); return; }
            ImGui.EndDisabled();
            if (referenced) ImGui.TextWrapped("Törlés előtt bontsd a készletre hivatkozó áramlásokat.");
            ImGui.TextWrapped("Kapcsolódó áramlások:");
            foreach (var flow in model.Flows.Where(f => f.From == stock.Id || f.To == stock.Id))
                ImGui.TextWrapped(EndpointName(flow.From) + " → " + flow.Name + " → " + EndpointName(flow.To));
            return;
        }
        var active = model.Flows.Find(f => f.Id == key[5..]);
        if (active == null) { ImGui.TextWrapped("Az áramlás már nem létezik."); return; }
        if (selectedFlow != active.Id) SelectFlow(active);
        if (ImGui.Button("Áramlás törlése")) { Remember(); model.Flows.Remove(active); selectedFlow = null; return; }
        string flowName = active.Name;
        if (ImGui.InputText("Áramlás neve", ref flowName, 120)) { Remember(); active.Name = flowName; }
        Endpoint("Forráskészlet", active.From, v => active.From = v); ImGui.SameLine();
        Endpoint("Célkészlet", active.To, v => active.To = v);
        ImGui.TextWrapped("Fix tick elején fut; a gráf másodpercenkénti átadást ad. Az áramlások az előző készletállapotból számolnak. Kevés készlet esetén arányosan osztoznak rajta; a sorrendjük nem ad elsőbbséget.");
        equation.Configure(new[] { DynamicsRuntime.FlowHook(active) }, DynamicsRuntime.Keys(model, GameWorld.RuleWorldFields), read);
        equation.PickField = pick; equation.DrawEquation(world); SynchronizeEquation();
    }

    private string EndpointName(string id) => id.Length == 0 ? "Környezet" : model.Stocks.Find(s => s.Id == id)?.Name ?? id;
    private void Endpoint(string label, string id, Action<string> set)
    {
        ImGui.SetNextItemWidth(200);
        if (!ImGui.BeginCombo(label, EndpointName(id))) return;
        if (ImGui.Selectable("Környezet", id.Length == 0)) { Remember(); set(""); }
        foreach (var stock in model.Stocks)
            if (ImGui.Selectable(stock.Name + "##" + stock.Id, stock.Id == id)) { Remember(); set(stock.Id); }
        ImGui.EndCombo();
    }
    private void SelectFlow(DynamicsFlow flow)
    {
        SynchronizeEquation();
        selectedFlow = flow.Id;
        equation.Configure(new[] { DynamicsRuntime.FlowHook(flow) }, DynamicsRuntime.Keys(model, GameWorld.RuleWorldFields), _ => 0);
        equation.LoadDraft(new BehaviorModel { Graphs = new() { flow.Rate } });
    }
    private void SynchronizeEquation()
    {
        var flow = model.Flows.Find(f => f.Id == selectedFlow); if (flow == null) return;
        var rate = equation.Document.Graphs.FirstOrDefault(); if (rate == null) return;
        if (new BehaviorModel { Graphs = new() { rate } }.ToJson() == new BehaviorModel { Graphs = new() { flow.Rate } }.ToJson()) return;
        Remember(); flow.Rate = rate;
    }
}
