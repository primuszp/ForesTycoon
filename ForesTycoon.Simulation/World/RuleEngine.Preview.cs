using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ForesTycoon;

internal sealed partial class RuleEngine
{
    internal sealed record InfluenceContribution(string Name, double Change);
    internal sealed record InfluencePreview(string Target, double Before, double After, InfluenceContribution[] Contributions);
    internal IReadOnlyList<InfluencePreview> PreviewInfluences(SimulationWorld world, string ruleId, int id, double years)
    {
        var rule = compiled.Single(r => r.Source.Id == ruleId);
        previewing = true;
        try { return PreviewInfluences(world, rule, id, years); }
        finally { previewing = false; }
    }
    private List<InfluencePreview> PreviewInfluences(SimulationWorld world, CompiledRule rule, int id, double years)
    {
        var inputs = new double[rule.Inputs.Length];
        foreach (int i in rule.Reads) inputs[i] = rule.Scope.Fields[i].Read(world, id);
        for (int i = rule.Scope.Fields.Length; i < inputs.Length; i++) inputs[i] = ReadCustom(world, rule.Inputs[i], id);
        var snapshots = new Dictionary<(RuleScope Scope, int Id, int Field), double>();
        RuleScope Detached(RuleScope original) => original with { Fields = original.Fields.Select((field, index) => field with {
            Read = (_, element) => snapshots.TryGetValue((original, element, index), out double saved) ? saved : field.Read(world, element),
            Write = (_, element, value) => {
                double before = snapshots.TryGetValue((original, element, index), out double saved) ? saved : field.Read(world, element);
                snapshots[(original, element, index)] = field.LocalStock is {} stock
                    ? stock.Normalize(before, Math.Clamp(value, field.Min, field.Max), stock.Capacity(world, element))
                    : Math.Clamp(value, field.Min, field.Max);
            }
        }).ToArray() };
        var detachedScope = Detached(rule.Scope);
        var stocks = new Stocks(detachedScope, id, inputs, rule.CustomDefinitions);
        string conditionError = null;
        bool applies = rule.Source.Enabled && (rule.Condition == null || rule.Condition.Evaluate(years, world.ReadRuleValue("world.time"), inputs, out conditionError) != 0);
        if (conditionError != null) throw new InvalidDataException(conditionError);
        var rows = new List<InfluencePreview>();
        for (int e = 0; e < rule.Effects.Length; e++) {
            var effect = rule.Effects[e];
            if (!applies || !EffectAvailable(world, rule.Scope, id, effect)) {
                double unchanged = effect.Field >= 0 ? inputs[effect.Field] : 0;
                rows.Add(new(effect.Field < 0 && effect.Flow != null ? effect.Source.To : effect.Source.Target, unchanged, unchanged,
                    rule.Source.Effects[e].Influence?.Terms.Select(t => new InfluenceContribution(t.Name, 0)).ToArray() ?? Array.Empty<InfluenceContribution>()));
                continue;
            }
            if (effect.Source.Mode == "action") {
                string actionError = null;
                double trigger = effect.Value == null ? 1 : effect.Value.Evaluate(years, world.ReadRuleValue("world.time"), inputs, out actionError);
                if (actionError != null) throw new InvalidDataException(actionError);
                rows.Add(new(effect.Source.Target, 0, rule.Source.Enabled && trigger != 0 ? 1 : 0, Array.Empty<InfluenceContribution>()));
                if (rule.Source.Enabled && trigger != 0) break;
                continue;
            }
            if (effect.Flow != null) {
                // A flow is previewed on its source: what it would hold after giving the amount (an inflow from outside: the amount).
                double amount = Math.Max(0, effect.Value.Evaluate(years, world.ReadRuleValue("world.time"), inputs, out string flowError));
                if (flowError != null) throw new InvalidDataException(flowError);
                var flowParts = new List<InfluenceContribution>();
                for (int p = 0; p < (effect.Parts?.Length ?? 0); p++) {
                    double change = effect.Parts[p].Evaluate(years, world.ReadRuleValue("world.time"), inputs, out flowError);
                    if (flowError != null) throw new InvalidDataException(flowError);
                    flowParts.Add(new(rule.Source.Effects[e].Influence.Terms[p].Name, rule.Source.Enabled ? change : 0));
                }
                if (!rule.Source.Enabled) amount = 0;
                double held = effect.Field >= 0 ? stocks.Read(effect.Field) : 0;
                var flow = effect.Flow.SpreadScope == null ? effect.Flow : effect.Flow with { SpreadScope = Detached(effect.Flow.SpreadScope) };
                double moved = Transfer(world, detachedScope, id, ref stocks, flow, amount);
                foreach (int index in rule.Reads) inputs[index] = stocks.Read(index);
                if (effect.Field >= 0) rows.Add(new(effect.Source.Target, held, stocks.Read(effect.Field), flowParts.ToArray()));
                else rows.Add(new(effect.Source.To, 0, moved, flowParts.ToArray()));
                continue;
            }
            double before = inputs[effect.Field];
            double value = effect.Value.Evaluate(years, world.ReadRuleValue("world.time"), inputs, out string error);
            if (error != null) throw new InvalidDataException(error);
            var field = effect.Field < rule.Scope.Fields.Length ? rule.Scope.Fields[effect.Field] : null;
            var customField = field == null ? Fields.Find(f => f.Name == effect.Source.Target) : null;
            double after = Math.Clamp(NextValue(effect.Source, before, value, years), field?.Min ?? customField?.Min ?? -1e12, field?.Max ?? customField?.Max ?? 1e12);
            inputs[effect.Field] = after;
            var parts = new List<InfluenceContribution>();
            // Inputs are the pre-effect snapshot when explaining each contribution.
            inputs[effect.Field] = before;
            for (int p = 0; p < (effect.Parts?.Length ?? 0); p++) {
                double change = effect.Parts[p].Evaluate(years, world.ReadRuleValue("world.time"), inputs, out error);
                if (error != null) throw new InvalidDataException(error);
                parts.Add(new(rule.Source.Effects[e].Influence.Terms[p].Name, rule.Source.Enabled ? change : 0));
            }
            stocks.Write(effect.Field, after);
            inputs[effect.Field] = stocks.Read(effect.Field);
            rows.Add(new(effect.Source.Target, before, inputs[effect.Field], parts.ToArray()));
        }
        return rows;
    }
}
