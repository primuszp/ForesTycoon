using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal sealed class DynamicsEditorView
{
    private readonly DynamicsEditorState editorState;
    private DynamicsModel model => editorState.Draft;
    private readonly DynamicsViewUiState viewState;
    private ref string selectedStock => ref viewState.SelectedStock;
    private readonly BehaviorEditorView equation;
    internal DynamicsEditorView(DynamicsEditorState state = null, DynamicsViewUiState ui = null, BehaviorViewUiState equationUi = null)
    {
        editorState = state ?? new(); viewState = ui ?? new();
        equation = new(editorState.Equation, equationUi);
        editorState.Replaced += ResetSelection;
    }
    internal BehaviorEditorView EquationEditor => equation;
    internal DynamicsModel Document => editorState.Capture();
    internal EditorReadList<DynamicsStockView> Stocks => editorState.Stocks;
    internal EditorReadList<DynamicsFlowView> Flows => editorState.Flows;
    internal HashSet<string> NamedValues() => editorState.NamedValues();
    internal void Load(DynamicsModel value) { editorState.Load(value); ResetSelection(); }
    internal void ResetSelection()
    {
        selectedStock = model.Stocks.FirstOrDefault()?.Id;
        equation.ResetSelection();
    }
    internal string CreateStock()
    {
        selectedStock = editorState.CreateStock(); editorState.BindFlow(null); return selectedStock;
    }
    internal string CreateFlow()
    {
        string id = editorState.CreateFlow(selectedStock); equation.ResetSelection(); return id;
    }
    internal void DrawSelected(GameWorld world, string key, Func<string, double> read, Func<string, string, string[], Action<string>, bool> pick = null)
    {
        if (key.StartsWith("stock.", StringComparison.Ordinal)) key = "stock:" + key[6..];
        if (key.StartsWith("stock:", StringComparison.Ordinal)) {
            var stock = model.Stocks.Find(s => s.Id == key[6..]);
            if (stock == null) { ImGui.TextWrapped("A készlet már nem létezik."); return; }
            string name = stock.Name, unit = stock.Unit;
            if (ImGui.InputText("Készlet neve", ref name, 120)) { editorState.EditStock(stock, value => value.Name = name); }
            if (ImGui.InputText("Mértékegység", ref unit, 40)) { editorState.EditStock(stock, value => value.Unit = unit); }
            double initial = stock.Initial, min = stock.Minimum, max = stock.Maximum;
            if (ImGui.InputDouble("Kezdőérték", ref initial)) { editorState.EditStock(stock, value => value.Initial = initial); }
            if (ImGui.InputDouble("Minimum", ref min)) { editorState.EditStock(stock, value => value.Minimum = min); }
            if (ImGui.InputDouble("Maximum", ref max)) { editorState.EditStock(stock, value => value.Maximum = max); }
            ImGui.TextWrapped($"A világban: {read("stock." + stock.Id):G6} {stock.Unit}");
            bool referenced = editorState.IsStockReferenced(stock);
            ImGui.BeginDisabled(referenced);
            if (ImGui.Button("Készlet törlése")) { editorState.DeleteStock(stock); ImGui.EndDisabled(); return; }
            ImGui.EndDisabled();
            if (referenced) ImGui.TextWrapped("Törlés előtt bontsd a készletre hivatkozó áramlásokat.");
            ImGui.TextWrapped("Kapcsolódó áramlások:");
            foreach (var flow in model.Flows.Where(f => f.From == stock.Id || f.To == stock.Id))
                ImGui.TextWrapped(EndpointName(flow.From) + " → " + flow.Name + " → " + EndpointName(flow.To));
            return;
        }
        var active = model.Flows.Find(f => f.Id == key[5..]);
        if (active == null) { ImGui.TextWrapped("Az áramlás már nem létezik."); return; }
        if (editorState.BoundFlow != active.Id) { editorState.BindFlow(active.Id); equation.ResetSelection(); }
        if (ImGui.Button("Áramlás törlése")) { editorState.DeleteFlow(active.Id); return; }
        string flowName = active.Name;
        if (ImGui.InputText("Áramlás neve", ref flowName, 120)) { editorState.EditFlow(active, value => value.Name = flowName); }
        Endpoint("Forráskészlet", active.From, v => editorState.EditFlow(active, flow => flow.From = v)); ImGui.SameLine();
        Endpoint("Célkészlet", active.To, v => editorState.EditFlow(active, flow => flow.To = v));
        ImGui.TextWrapped("Fix tick elején fut; a gráf másodpercenkénti átadást ad. Az áramlások az előző készletállapotból számolnak. Kevés készlet esetén arányosan osztoznak rajta; a sorrendjük nem ad elsőbbséget.");
        equation.Configure(new[] { DynamicsRuntime.FlowHook(active) }, DynamicsRuntime.Keys(model, GameWorld.RuleWorldFields), read);
        equation.PickField = pick; equation.DrawEquation(world);
    }

    private string EndpointName(string id) => id.Length == 0 ? "Környezet" : model.Stocks.Find(s => s.Id == id)?.Name ?? id;
    private void Endpoint(string label, string id, Action<string> set)
    {
        ImGui.SetNextItemWidth(200);
        if (!ImGui.BeginCombo(label, EndpointName(id))) return;
        if (ImGui.Selectable("Környezet", id.Length == 0)) { set(""); }
        foreach (var stock in model.Stocks)
            if (ImGui.Selectable(stock.Name + "##" + stock.Id, stock.Id == id)) { set(stock.Id); }
        ImGui.EndCombo();
    }
}
