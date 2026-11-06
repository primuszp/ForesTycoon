#nullable enable
using System;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor.Core;

internal enum InfluenceThresholdKind { Constant, Field, Curve }

/// <summary>Edits the saved influence directly; the canvas and inspector share these commands and document history.</summary>
internal sealed class RuleInfluenceEditor(EditorDocument owner)
{
    internal const int TermLimit = 16, FactorLimit = 8, ConditionLimit = 8;

    internal void EditEffect(WorldRule rule, RuleEffect effect, Action<RuleEffect> edit)
        => owner.History.Execute("hatás módosítása", () => { Require(rule, effect); edit(effect); owner.Invalidate(); });

    internal void EditTerm(WorldRule rule, RuleEffect effect, InfluenceTerm term, Action<InfluenceTerm> edit)
        => owner.History.Execute("hatás módosítása", () => { Require(rule, effect, term); edit(term); owner.Invalidate(); });

    internal void EditCondition(WorldRule rule, RuleEffect effect, InfluenceTerm term, InfluenceCondition condition, Action<InfluenceCondition> edit)
        => owner.History.Execute("hatás módosítása", () => { Require(rule, effect, term, condition); edit(condition); owner.Invalidate(); });

    internal void EditFactor(WorldRule rule, RuleEffect effect, InfluenceTerm term, InfluenceFactor factor, Action<InfluenceFactor> edit)
        => owner.History.Execute("hatás módosítása", () => {
            Require(rule, effect, term);
            if (!term.Factors.Contains(factor) && !term.Conditions.Any(c => ReferenceEquals(c.ValueCurve, factor)))
                throw new InvalidOperationException("A szorzó már nem az aktuális kapcsolat része.");
            edit(factor); owner.Invalidate();
        });

    internal void SetMode(WorldRule rule, RuleEffect effect, string mode, string flowName)
        => owner.History.Execute("hatás módosítása", () => {
            Require(rule, effect);
            if (mode is not ("add" or "set" or "flow" or "approach")) throw new ArgumentException("Ismeretlen hatásmód.", nameof(mode));
            if (effect.Mode == mode) return;
            if (mode == "flow") {
                effect.To = ""; effect.FlowName = flowName;
                foreach (var term in effect.Influence.Terms) term.Decrease = false;
            }
            effect.Mode = mode;
            effect.Influence.PerYear = rule.Schedule is not ("passage" or "growth") && mode is "add" or "flow";
            effect.Influence.PerHour = false;
            owner.Invalidate();
        });

    internal InfluenceTerm? AddTerm(WorldRule rule, RuleEffect effect)
        => owner.History.Execute<InfluenceTerm?>("hatás módosítása", () => {
            Require(rule, effect);
            if (effect.Influence.Terms.Count >= TermLimit) return null;
            var term = new InfluenceTerm { Decrease = effect.Mode is not ("action" or "approach"), Enabled = effect.Mode != "action" };
            effect.Influence.Terms.Add(term); owner.Invalidate(); return term;
        });

    /// <returns>The connected term's index, or null when the target is absent or full.</returns>
    internal int? ConnectSource(WorldRule rule, string source, int effectIndex, int termIndex, string name)
        => owner.History.Execute<int?>("hatás módosítása", () => {
            owner.RuleBook.Require(rule);
            if (effectIndex < 0 || effectIndex >= rule.Effects.Count) return null;
            var effect = rule.Effects[effectIndex];
            if (effect.Influence == null) return null;
            if (termIndex >= 0) {
                if (termIndex >= effect.Influence.Terms.Count) return null;
                return AddFactor(rule, effect, effect.Influence.Terms[termIndex], source) == null ? null : termIndex;
            }
            if (effect.Influence.Terms.Count >= TermLimit) return null;
            // A connected action starts enabled; an empty action term starts disabled until configured.
            effect.Influence.Terms.Add(new() { Name = name, Decrease = effect.Mode is not ("action" or "approach"),
                Factors = new() { new() { Source = source } } });
            owner.Invalidate(); return effect.Influence.Terms.Count - 1;
        });

    internal void DeleteTerm(WorldRule rule, RuleEffect effect, InfluenceTerm term)
        => owner.History.Execute("hatás módosítása", () => { Require(rule, effect, term); effect.Influence.Terms.Remove(term); owner.Invalidate(); });

    internal InfluenceFactor? AddFactor(WorldRule rule, RuleEffect effect, InfluenceTerm term, string source)
        => owner.History.Execute<InfluenceFactor?>("hatás módosítása", () => {
            Require(rule, effect, term);
            if (term.Factors.Count >= FactorLimit) return null;
            var factor = new InfluenceFactor { Source = source };
            term.Factors.Add(factor); owner.Invalidate(); return factor;
        });

    internal void DeleteFactor(WorldRule rule, RuleEffect effect, InfluenceTerm term, InfluenceFactor factor)
        => owner.History.Execute("hatás módosítása", () => {
            Require(rule, effect, term);
            if (!term.Factors.Contains(factor)) throw new InvalidOperationException("A szorzó már nem az aktuális kapcsolat része.");
            term.Factors.Remove(factor); owner.Invalidate();
        });

    internal InfluenceCondition? AddCondition(WorldRule rule, RuleEffect effect, InfluenceTerm term, string field)
        => owner.History.Execute<InfluenceCondition?>("hatás módosítása", () => {
            Require(rule, effect, term);
            if (term.Conditions.Count >= ConditionLimit) return null;
            var condition = new InfluenceCondition { Field = field };
            term.Conditions.Add(condition); owner.Invalidate(); return condition;
        });

    internal void DeleteCondition(WorldRule rule, RuleEffect effect, InfluenceTerm term, InfluenceCondition condition)
        => owner.History.Execute("hatás módosítása", () => { Require(rule, effect, term, condition); term.Conditions.Remove(condition); owner.Invalidate(); });

    internal void SetConditionField(WorldRule rule, RuleEffect effect, InfluenceTerm term, InfluenceCondition condition, string field, RuleFieldOption[] options)
        => owner.History.Execute("hatás módosítása", () => {
            Require(rule, effect, term, condition);
            condition.Field = field; FitCondition(condition, options); owner.Invalidate();
        });

    internal bool NormalizeCondition(WorldRule rule, RuleEffect effect, InfluenceTerm term, InfluenceCondition condition, RuleFieldOption[] options)
    {
        Require(rule, effect, term, condition);
        if (!FitCondition(condition, options, apply: false)) return false;
        return owner.History.Execute("hatás módosítása", () => {
            FitCondition(condition, options); owner.Invalidate(); return true;
        });
    }

    private static bool FitCondition(InfluenceCondition condition, RuleFieldOption[] options, bool apply = true)
    {
        bool equality = condition.Comparison is "equal" or "notEqual";
        if (options.Length == 0) {
            if (!equality) return false;
            if (apply) condition.Comparison = "atLeast";
            return true;
        }
        string comparison = equality ? condition.Comparison : "equal";
        double value = options.Any(o => o.Value == condition.Value) ? condition.Value : options[^1].Value;
        bool changed = condition.Comparison != comparison || condition.ValueField.Length != 0 || condition.ValueCurve != null || condition.Value != value;
        if (apply && changed) { condition.Comparison = comparison; condition.ValueField = ""; condition.ValueCurve = null; condition.Value = value; }
        return changed;
    }

    internal bool SetThreshold(WorldRule rule, RuleEffect effect, InfluenceTerm term, InfluenceCondition condition, InfluenceThresholdKind kind, string[] readable)
        => owner.History.Execute<bool>("hatás módosítása", () => {
            Require(rule, effect, term, condition);
            if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (kind != InfluenceThresholdKind.Constant && readable.Length == 0) return false;
            condition.ValueField = kind == InfluenceThresholdKind.Field ? readable.FirstOrDefault(f => f != condition.Field) ?? readable[0] : "";
            condition.ValueCurve = kind == InfluenceThresholdKind.Curve ? new() { Source = readable.Contains("shadeTolerance") ? "shadeTolerance" : readable[0] } : null;
            owner.Invalidate(); return true;
        });

    private void Require(WorldRule rule, RuleEffect effect)
    {
        owner.RuleBook.Require(rule, effect);
        if (effect.Influence == null) throw new InvalidOperationException("A hatásnak nincs szerkeszthető kapcsolata.");
    }
    private void Require(WorldRule rule, RuleEffect effect, InfluenceTerm term)
    {
        Require(rule, effect);
        if (!effect.Influence.Terms.Contains(term)) throw new InvalidOperationException("A kapcsolat már nem az aktuális hatás része.");
    }
    private void Require(WorldRule rule, RuleEffect effect, InfluenceTerm term, InfluenceCondition condition)
    {
        Require(rule, effect, term);
        if (!term.Conditions.Contains(condition)) throw new InvalidOperationException("A feltétel már nem az aktuális kapcsolat része.");
    }

}
