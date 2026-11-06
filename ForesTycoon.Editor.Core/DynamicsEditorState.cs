#nullable enable
using System;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor.Core;

/// <summary>Owns stocks, flows and their history. A bound equation edits the flow's actual rate graph.</summary>
internal sealed class DynamicsEditorState
{
    private readonly EditorBuffer<DynamicsModel> buffer = new(new(),
        model => new RuleProject { Dynamics = model }.ToJson(), json => RuleProject.FromJson(json).Dynamics);
    private string? boundFlow;
    private EditorEdit? equationEdit;
    internal DynamicsModel Draft => buffer.Value;
    private readonly EditorReadCache<DynamicsStock, DynamicsStockView> stocks = new(value => new(value));
    private readonly EditorReadCache<DynamicsFlow, DynamicsFlowView> flows = new(value => new(value));
    internal EditorReadList<DynamicsStockView> Stocks { get; }
    internal EditorReadList<DynamicsFlowView> Flows { get; }
    internal System.Collections.Generic.HashSet<string> NamedValues() => DynamicsRuntime.Keys(Draft, SimulationWorld.RuleWorldFields);
    internal BehaviorEditorState Equation { get; } = new();
    internal string? BoundFlow => boundFlow;
    internal event Action? Changing;
    internal event Action? Replaced;

    internal DynamicsEditorState()
    {
        Stocks = new(() => Draft.Stocks.Count, index => stocks.View(Draft.Stocks[index])!);
        Flows = new(() => Draft.Flows.Count, index => flows.View(Draft.Flows[index])!);
        buffer.Changing += () => Changing?.Invoke();
        buffer.Replaced += () => {
            BindFlow(Draft.Flows.Any(f => f.Id == boundFlow) ? boundFlow : Draft.Flows.FirstOrDefault()?.Id);
            Replaced?.Invoke();
        };
        Equation.Changing += Remember;
        Equation.Replaced += AttachEquation;
        Equation.EditStarted += () => equationEdit = buffer.BeginEdit("áramlási gráf módosítása");
        Equation.EditFinished += cancelled => {
            var edit = equationEdit; equationEdit = null;
            if (cancelled) edit?.Cancel(); else edit?.Commit();
        };
    }

    internal DynamicsModel Capture() => buffer.Capture();
    internal void Load(DynamicsModel value)
    {
        boundFlow = null;
        equationEdit = null; Equation.LoadDraft(new());
        buffer.Load(value);
    }
    internal void Remember() => buffer.Remember();
    internal void EditStock(DynamicsStock stock, Action<DynamicsStock> change)
    {
        if (!Draft.Stocks.Contains(stock)) throw new InvalidOperationException("A készlet már nem a dokumentum része.");
        buffer.Execute("készlet módosítása", _ => change(stock));
    }
    internal void EditFlow(DynamicsFlow flow, Action<DynamicsFlow> change)
    {
        if (!Draft.Flows.Contains(flow)) throw new InvalidOperationException("Az áramlás már nem a dokumentum része.");
        Equation.CommitEdit();
        buffer.Execute("áramlás módosítása", _ => change(flow));
    }
    internal bool IsStockReferenced(DynamicsStock stock) => Draft.Flows.Any(flow => flow.From == stock.Id || flow.To == stock.Id
        || flow.Rate.Nodes.Any(node => node.Key == "stock." + stock.Id));
    internal void DeleteStock(DynamicsStock stock)
    {
        if (!Draft.Stocks.Contains(stock)) throw new InvalidOperationException("A készlet már nem a dokumentum része.");
        if (IsStockReferenced(stock)) throw new InvalidOperationException("A készletre áramlás hivatkozik.");
        Equation.CommitEdit();
        buffer.Execute("készlet törlése", model => model.Stocks.Remove(stock));
    }
    internal bool Undo() { Equation.CommitEdit(); return buffer.Undo(); }
    internal bool Redo() { Equation.CommitEdit(); return buffer.Redo(); }

    internal void BindFlow(string? id)
    {
        Equation.CommitEdit();
        var flow = Draft.Flows.Find(f => f.Id == id);
        boundFlow = flow?.Id;
        if (flow == null) { Equation.LoadDraft(new()); return; }
        Equation.Configure(new[] { DynamicsRuntime.FlowHook(flow) }, DynamicsRuntime.Keys(Draft, SimulationWorld.RuleWorldFields), _ => 0);
        Equation.LoadDraft(new BehaviorModel { Graphs = new() { flow.Rate } });
    }

    private void AttachEquation()
    {
        var flow = Draft.Flows.Find(f => f.Id == boundFlow);
        var graph = Equation.Draft.Graphs.FirstOrDefault();
        if (flow != null && graph != null) flow.Rate = graph;
    }

    internal string CreateStock()
    {
        Equation.CommitEdit();
        Remember();
        var stock = new DynamicsStock { Id = "s" + Guid.NewGuid().ToString("N")[..8] };
        Draft.Stocks.Add(stock); return stock.Id;
    }

    internal string CreateFlow(string? targetStock)
    {
        if (Draft.Stocks.Count == 0) throw new InvalidOperationException("Az áramláshoz előbb készletet hozz létre.");
        Equation.CommitEdit();
        Remember();
        string id = "f" + Guid.NewGuid().ToString("N")[..8];
        var flow = new DynamicsFlow { Id = id, To = Draft.Stocks.Any(s => s.Id == targetStock) ? targetStock! : Draft.Stocks[0].Id,
            Rate = new BehaviorGraph { Id = "rate." + id, Name = "Áramlási ráta", Hook = "flow." + id, Kind = "dynamics", Output = "rate",
                Nodes = new() { new() { Id = "rate", Name = "Ráta", Operation = BehaviorOperation.Constant, Value = 1, X = 40, Y = 60 } } } };
        Draft.Flows.Add(flow); BindFlow(id); return id;
    }

    internal void DeleteFlow(string id)
    {
        if (!Draft.Flows.Any(f => f.Id == id)) return;
        Equation.CommitEdit();
        Remember(); Draft.Flows.RemoveAll(f => f.Id == id);
        if (boundFlow == id) BindFlow(null);
    }
}
