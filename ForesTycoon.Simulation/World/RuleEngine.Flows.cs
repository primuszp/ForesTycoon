using System;

namespace ForesTycoon;

internal sealed partial class RuleEngine
{
    /// <summary>
    /// Moves up to <paramref name="amount"/> along a flow: never more than the source holds above its minimum, nor more than
    /// the receivers have room for. What leaves one stock arrives in the other, so a closed model conserves its quantity.
    /// </summary>
    private void Move(SimulationWorld world, RuleScope scope, int id, double[] inputs, double[][] custom, RuleFieldDefinition[] customDefs, CompiledFlow flow, double amount)
    {
        var stock = new Stocks(world, scope, id, inputs, custom, customDefs);
        flowLedger.Record(flow.Slot, Transfer(world, scope, id, ref stock, flow, amount));
    }
    private static double Transfer(SimulationWorld world, RuleScope scope, int id, ref Stocks stock, CompiledFlow flow, double amount)
    {
        if (!(amount > 0)) return 0;
        if (flow.From >= 0) amount = Math.Min(amount, Math.Max(0, stock.Read(flow.From) - Math.Max(0, stock.Min(flow.From))));
        double moved = 0;
        if (flow.Spread != null) {
            var buffered = flow.Spread.BufferedMembers;
            // Keep the stack bound independent of extension scopes' declared collection size.
            Span<int> memberBuffer = buffered == null ? Span<int>.Empty
                : buffered.Capacity <= 16 ? stackalloc int[buffered.Capacity] : new int[buffered.Capacity];
            ReadOnlySpan<int> members = buffered == null ? flow.Spread.Members(world, id)
                : memberBuffer[..buffered.Read(world, id, memberBuffer)];
            var into = flow.SpreadScope.Fields[flow.SpreadField];
            double self = flow.Downhill ? scope.Fields[flow.OwnLevel].Read(world, id) : 0, total = 0;
            Span<double> weights = members.Length <= 16 ? stackalloc double[members.Length] : new double[members.Length];
            for (int m = 0; m < members.Length; m++) {
                weights[m] = flow.Downhill ? Math.Max(0, self - flow.SpreadScope.Fields[flow.SpreadLevel].Read(world, members[m])) : 1;
                total += weights[m];
            }
            if (total <= 0) return 0;
            for (int m = 0; m < members.Length; m++) {
                if (weights[m] <= 0) continue;
                double before = into.Read(world, members[m]);
                double share = Math.Min(amount * weights[m] / total, Math.Max(0, into.Max - before));
                into.Write(world, members[m], before + share);
                moved += into.Read(world, members[m]) - before;
            }
        } else if (flow.To >= 0) {
            double before = stock.Read(flow.To);
            stock.Write(flow.To, before + Math.Min(amount, Math.Max(0, stock.Max(flow.To) - before)));
            moved = stock.Read(flow.To) - before;
        } else moved = amount;
        if (moved <= 0) return 0;
        if (flow.From >= 0) stock.Write(flow.From, stock.Read(flow.From) - moved);
        return moved;
    }
    /// <summary>The stocks of one element a flow moves between: its scope fields, then its custom fields.</summary>
    private readonly ref struct Stocks
    {
        private readonly SimulationWorld world; private readonly RuleScope scope; private readonly int id;
        private readonly double[] inputs; private readonly double[][] custom; private readonly RuleFieldDefinition[] customDefs;
        private readonly LocalFlowPlan local;
        private readonly bool detached;
        internal Stocks(SimulationWorld world, RuleScope scope, int id, double[] inputs, double[][] custom, RuleFieldDefinition[] customDefs)
        { this.world = world; this.scope = scope; this.id = id; this.inputs = inputs; this.custom = custom; this.customDefs = customDefs; local = null; detached = false; }
        internal Stocks(RuleScope scope, LocalFlowPlan local)
        { world = null; this.scope = scope; id = 0; inputs = local.Inputs; custom = null; customDefs = null; this.local = local; detached = false; }
        internal Stocks(RuleScope scope, int id, double[] inputs, RuleFieldDefinition[] customDefs)
        { world = null; this.scope = scope; this.id = id; this.inputs = inputs; custom = null; this.customDefs = customDefs; local = null; detached = true; }
        private int Own => scope.Fields.Length;
        internal double Read(int i) => local != null ? inputs[i] : i < Own ? scope.Fields[i].Read(world, id) : detached ? inputs[i] : custom[i - Own][scope.StateIndex(world, id)];
        internal double Min(int i) => i < Own ? scope.Fields[i].Min : customDefs[i - Own].Min;
        internal double Max(int i) => i < Own ? scope.Fields[i].Max : customDefs[i - Own].Max;
        internal void Write(int i, double value)
        {
            if (local != null) {
                var field = scope.Fields[i];
                inputs[i] = field.LocalStock.Normalize(inputs[i], Math.Clamp(value, field.Min, field.Max), local.Capacities[i]);
                local.Operations.Add(new(false, i, value)); return;
            }
            if (i < Own) { scope.Fields[i].Write(world, id, value); inputs[i] = scope.Fields[i].Read(world, id); return; }
            value = Math.Clamp(value, customDefs[i - Own].Min, customDefs[i - Own].Max);
            if (!detached) custom[i - Own][scope.StateIndex(world, id)] = value;
            inputs[i] = value;
        }
    }
}
