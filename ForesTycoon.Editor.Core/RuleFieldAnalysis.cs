#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor.Core;
internal sealed record RuleFieldWrite(int Rule, string Name, string Mode, bool Enabled, string Schedule);
internal sealed record RuleFieldUse(string Key, string Entity, string Field, IReadOnlyList<RuleFieldWrite> Writers,
    IReadOnlyList<(int Rule, string Name, bool Enabled)> Readers, IReadOnlyList<string> Native)
{
    public bool Shared => Writers.Count(w => w.Enabled) > 1;
}
internal static class RuleFieldAnalysis
{
    private sealed record Accumulator(string Key, string Entity, string Field, List<RuleFieldWrite> Writers,
        List<(int Rule, string Name, bool Enabled)> Readers, List<string> Native)
    {
        internal bool Shared => Writers.Count(w => w.Enabled) > 1;
        internal RuleFieldUse Freeze() => new(Key, Entity, Field, Writers.AsReadOnly(), Readers.AsReadOnly(), Native.AsReadOnly());
    }
    private static IEnumerable<string> Sources(InfluenceTerm term)
    {
        yield return term.AmountField;
        foreach (var factor in term.Factors) yield return factor.Source;
        foreach (var condition in term.Conditions) {
            yield return condition.Field; yield return condition.ValueField;
            if (condition.ValueCurve != null) yield return condition.ValueCurve.Source;
        }
    }
    /// <summary>How a rule on <paramref name="scope"/> names a field (“tile.moisture”) as an object-model key (“tile.moisture” of the tile kind).</summary>
    private static string? FieldKey(RuleSchema schema, string scope, string name)
    {
        var entity = schema.Find(scope);
        if (entity == null) return scope + "." + name;
        if (entity.Fields.Any(f => f.Name == name) || entity.Actions.Any(a => a.Name == name)) return scope + "." + name;
        foreach (var r in entity.Relations)
            if (name.StartsWith(r.Name + ".", StringComparison.Ordinal)) return r.Target + "." + name[(r.Name.Length + 1)..];
        return null;
    }

    internal static IReadOnlyList<RuleFieldUse> Build(RuleSchema schema, IReadOnlyList<WorldRule> rules, Func<string, string> display)
    {
        var uses = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        Accumulator? Use(string? key)
        {
            if (key == null) return null;
            if (!uses.TryGetValue(key, out var u)) {
                int dot = key.IndexOf('.');
                uses[key] = u = new(key, key[..dot], key[(dot + 1)..], new(), new(), new());
            }
            return u;
        }
        for (int i = 0; i < rules.Count; i++) {
            var rule = rules[i];
            foreach (var effect in rule.Effects) {
                Use(FieldKey(schema, rule.Scope, effect.Target))?.Writers.Add(new(i, rule.Name, effect.Mode, rule.Enabled, rule.Schedule));
                IEnumerable<string> reads = effect.Influence != null
                    ? effect.Influence.Terms.SelectMany(t => Sources(t))
                    : Array.Empty<string>();
                foreach (string name in reads.Where(n => !string.IsNullOrEmpty(n) && !n.StartsWith("tuning.", StringComparison.Ordinal)).Distinct()) {
                    var use = Use(FieldKey(schema, rule.Scope, name));
                    if (use != null && !use.Readers.Any(r => r.Rule == i)) use.Readers.Add((i, rule.Name, rule.Enabled));
                }
            }
        }
        // Formula rules: the causal map reads every field a formula mentions.
        try {
            var map = RuleCausalMap.Build(schema, rules.Where(r => r.Effects.Any(e => e.Influence == null) || r.Condition.Length > 0).ToList());
            foreach (var edge in map.Edges.Where(e => !e.Native)) {
                int index = rules.ToList().FindIndex(r => r.Id == edge.Rule);
                var use = Use(edge.From);
                if (index >= 0 && use != null && !use.Readers.Any(r => r.Rule == index)) use.Readers.Add((index, rules[index].Name, rules[index].Enabled));
            }
            foreach (var edge in RuleCausalMap.Build(schema, Array.Empty<WorldRule>()).Edges.Where(e => e.Native))
                Use(edge.To)?.Native.Add($"{(edge.Sign > 0 ? "Nő" : edge.Sign < 0 ? "Csökken" : "Változik")} {RuleSentence.Article(display(edge.From))} {display(edge.From)} hatására ({edge.Rule})");
        } catch (Exception) { /* A formula that does not parse simply contributes no reads. */ }
        return uses.Values.OrderByDescending(u => u.Shared).ThenBy(u => schema.Find(u.Entity)?.Name ?? u.Entity, StringComparer.CurrentCulture)
            .ThenBy(u => u.Field, StringComparer.Ordinal).Select(u => u.Freeze()).ToList().AsReadOnly();
    }

}
