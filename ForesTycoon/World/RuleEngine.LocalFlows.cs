using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Engine;

namespace ForesTycoon;

internal sealed partial class RuleEngine
{
    internal bool PlanLocalFlows { get; set; } = true;
    internal long PlannedFlowRows { get; private set; }
    private sealed class LocalFlowPlan
    {
        internal readonly double[] Inputs, Capacities;
        internal readonly List<FlowOperation> Operations;
        internal string Error;
        internal LocalFlowPlan(CompiledRule rule) {
            Inputs = new double[rule.Inputs.Length]; Capacities = new double[rule.Scope.Fields.Length];
            Operations = new(rule.Effects.Length * 3);
        }
    }
    private readonly record struct FlowOperation(bool Ledger, int Index, double Value);
    private sealed class LocalFlowBlock
    {
        internal readonly LocalFlowPlan[] Plans = new LocalFlowPlan[1024];
        internal int First, Count;
    }
    private readonly LocalFlowBlock[] localFlowBlocks = new LocalFlowBlock[8];
    private readonly SlicedQueryBatch flowQueries = new();
    private readonly object namedReadLock = new();
    [ThreadStatic] private static bool parallelFlowEvaluation;
    internal IEnumerable<int> FinishQueryWork() => flowQueries.JoinWork();

    // These are execution capabilities, not a second water model: readers stay on the owner and
    // both paths use Transfer and the stock's canonical normalization. Cross-object flows stay ordered.
    private static bool CanPlanLocalFlows(CompiledRule rule)
    {
        bool Stock(int field) => field < 0 || field < rule.Scope.Fields.Length && rule.Scope.Fields[field].LocalStock != null;
        bool Pure(Expression expression) => expression == null || expression.NamedKeys.All(key => key == "world.hoursPerYear");
        return rule.CustomDefinitions.Length == 0 && rule.Reads.All(i => rule.Scope.Fields[i].LocalRead)
            && Pure(rule.Condition) && rule.Effects.Length > 0 && rule.Effects.All(effect =>
                effect.Action == null && effect.Flow is { Spread: null } flow && Stock(flow.From) && Stock(flow.To) && Pure(effect.Value));
    }

    private IEnumerable<int> RunLocalFlowsWork(GameWorld world, CompiledRule rule, int[] elements, double years, double time)
    {
        int[] stocks = rule.Effects.SelectMany(effect => new[] { effect.Flow.From, effect.Flow.To }).Where(i => i >= 0).Distinct().ToArray();
        int next = stepElement, head = 0, active = 0, slots = SlicedQueryBatch.WorkerCount;
        while (next < elements.Length || active > 0) {
            if (active == slots || next == elements.Length) {
                foreach (int unit in flowQueries.JoinSlotWork(head)) yield return 1;
                var ready = localFlowBlocks[head];
                if (stepElement != ready.First) throw new InvalidOperationException("Flow plans must publish in element order.");
                for (int p = 0; p < ready.Count; p++) {
                    yield return 1;
                    int id = elements[stepElement]; var plan = ready.Plans[p];
                    foreach (var operation in plan.Operations)
                        if (operation.Ledger) RecordFlow(operation.Index, operation.Value);
                        else rule.Scope.Fields[operation.Index].Write(world, id, operation.Value);
                    if (plan.Error != null) LastError = plan.Error;
                    stepElement++; PlannedFlowRows++;
                }
                head = (head + 1) % slots; active--; continue;
            }
            int slot = (head + active) % slots;
            var block = localFlowBlocks[slot] ??= new(); block.First = next; block.Count = Math.Min(1024, elements.Length - next);
            for (int p = 0; p < block.Count; p++) {
                var plan = block.Plans[p];
                if (plan == null || plan.Inputs.Length != rule.Inputs.Length) block.Plans[p] = plan = new(rule);
                plan.Error = null; plan.Operations.Clear(); int id = elements[next + p];
                for (int r = 0; r < rule.Reads.Length; r++) plan.Inputs[rule.Reads[r]] = rule.Readers[r](world, id);
                foreach (int field in stocks) {
                    if (!rule.Used[field]) plan.Inputs[field] = rule.Scope.Fields[field].Read(world, id);
                    plan.Capacities[field] = rule.Scope.Fields[field].LocalStock.Capacity(world, id);
                }
                yield return 1;
            }
            // Capture the next block while this fixed-size job runs, instead of imposing a barrier per 1024 rows.
            flowQueries.Start(slot, () => ComputeLocalFlowBlock(rule, block, years, time));
            next += block.Count; active++; yield return 1;
        }
    }
    private static void ComputeLocalFlowBlock(CompiledRule rule, LocalFlowBlock block, double years, double time)
    {
        bool previous = parallelFlowEvaluation; parallelFlowEvaluation = true;
        try {
            for (int p = 0; p < block.Count; p++) {
                var plan = block.Plans[p]; string error = null;
                if (rule.Condition != null && rule.Condition.Evaluate(years, time, plan.Inputs, out error) == 0) {
                    plan.Error = error; continue;
                }
                if (error != null) { plan.Error = error; continue; }
                var stock = new Stocks(rule.Scope, plan);
                foreach (var effect in rule.Effects) {
                    double value = effect.Value == null ? 1 : effect.Value.Evaluate(years, time, plan.Inputs, out error);
                    if (error != null) { plan.Error = rule.Source.Name + ": " + error; break; }
                    double moved = Transfer(null, rule.Scope, 0, ref stock, effect.Flow, value);
                    if (moved > 0) plan.Operations.Add(new(true, effect.Flow.Slot, moved));
                }
            }
        } finally { parallelFlowEvaluation = previous; }
    }
}
