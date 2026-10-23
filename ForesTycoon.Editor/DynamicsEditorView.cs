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
    private string selectedStock, selectedFlow;
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
