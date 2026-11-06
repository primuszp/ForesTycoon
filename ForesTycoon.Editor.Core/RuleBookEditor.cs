#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor.Core;

/// <summary>Authors the rule book in execution order. The view's document history records these edits with scene changes.</summary>
internal sealed class RuleBookEditor(EditorDocument owner)
{
    internal RuleInfluenceEditor Influences { get; } = new(owner);
    private List<WorldRule> Rules => owner.Rules;
    private List<RuleFieldDefinition> Fields => owner.Fields;
    private List<string> Removed => owner.RemovedStarters;
    internal IReadOnlyList<WorldRule> Entries => Rules.AsReadOnly();
    internal IReadOnlyList<RuleFieldDefinition> CustomFields => Fields.AsReadOnly();
    internal int IndexOf(WorldRule rule) => Rules.IndexOf(rule);

    internal void EditEffect(WorldRule rule, RuleEffect effect, Action<RuleEffect> change)
        => owner.History.Execute("hatás módosítása", () => {
            Require(rule, effect); change(effect); owner.Invalidate();
        });

    internal WorldRule CreateEmpty()
        => owner.History.Execute<WorldRule>("szabálykönyv módosítása", () => {
            int number = Rules.Count + 1;
            while (Rules.Any(r => r.Name == $"Szabály {number}")) number++;
            var scope = RuleEngine.Scopes.First(s => s.Fields.Any(f => f.Write != null));
            return AddNew(new WorldRule { Id = NewId(), Name = $"Szabály {number}", Scope = scope.Id, Enabled = false,
                Effects = { new() { Target = scope.Fields.First(f => f.Write != null).Name, Mode = "add", Influence = new() } } });
        });

    internal WorldRule CreateForTarget(string scope, string target, string mode, string name)
        => owner.History.Execute<WorldRule>("szabálykönyv módosítása", () => {
            RequireScope(scope);
            if (!RuleEffect.Modes.Contains(mode)) throw new ArgumentException("Ismeretlen hatásmód.", nameof(mode));
            string unique = name; int number = 2;
            while (Rules.Any(r => r.Name == unique)) unique = name + " " + number++;
            return AddNew(new WorldRule { Id = NewId(), Name = unique, Scope = scope, Enabled = false,
                Effects = { mode == "action" ? new() { Target = target, Mode = "action" } : new() { Target = target, Mode = mode, Influence = new() } } });
        });

    internal WorldRule AddStarter(WorldRule starter)
        => owner.History.Execute<WorldRule>("szabálykönyv módosítása", () => {
            var copy = RuleEngine.Clone(new() { starter })[0];
            if (Rules.Any(r => r.Id == copy.Id)) {
                string prefix = copy.Id;
                do { copy.Id = prefix + "-" + Guid.NewGuid().ToString("N")[..4]; } while (Rules.Any(r => r.Id == copy.Id));
            }
            Removed.Remove(copy.Template);
            return Add(copy);
        });

    internal WorldRule Copy(WorldRule rule)
        => owner.History.Execute<WorldRule>("szabálykönyv módosítása", () => {
            Require(rule);
            var copy = RuleEngine.Clone(new() { rule })[0];
            copy.Id = NewId(); copy.Name += " (másolat)"; copy.Enabled = false;
            return Add(copy);
        });

    internal void Delete(WorldRule rule)
        => owner.History.Execute("szabálykönyv módosítása", () => {
            Require(rule); Rules.Remove(rule);
            if (rule.Template.Length > 0 && !Rules.Any(r => r.Template == rule.Template) && !Removed.Contains(rule.Template)) Removed.Add(rule.Template);
            owner.Invalidate();
        });

    internal bool RestoreVisualRule(WorldRule rule)
        => owner.History.Execute<bool>("szabálykönyv módosítása", () => {
            Require(rule);
            var starter = RuleEngine.StarterRules().Find(r => r.Id == rule.Template);
            if (starter == null) return false;
            // Restore the visual editor's authored fields without changing identity, group or run order.
            rule.Effects = starter.Effects; rule.Description = starter.Description;
            rule.Condition = starter.Condition; rule.ConditionGraph = starter.ConditionGraph;
            rule.Enabled = true; rule.Scope = starter.Scope;
            owner.Invalidate(); return true;
        });

    internal void Edit(WorldRule rule, Action<WorldRule> edit)
        => owner.History.Execute("szabálykönyv módosítása", () => {
            Require(rule); edit(rule); owner.Invalidate();
        });

    internal void SetScope(WorldRule rule, string scope)
        => owner.History.Execute("szabálykönyv módosítása", () => {
            Require(rule); RequireScope(scope);
            ConfigureScope(rule, scope); owner.Invalidate();
        });

    private static void ConfigureScope(WorldRule rule, string scope)
    {
        rule.Scope = scope;
        rule.Schedule = scope == "passage" ? "passage" : rule.Schedule is "wildlife" or "population" or "month" or "birth"
            ? rule.Schedule : scope == "tree" && rule.Schedule == "growth" ? "growth" : "step";
        if (rule.Schedule == "passage") foreach (var effect in rule.Effects)
            if (effect.Influence != null) effect.Influence.PerYear = effect.Influence.PerHour = false;
    }

    internal string[] SchedulesFor(WorldRule rule) => rule.Scope == "passage" ? new[] { "passage" }
        : WorldRule.Schedules.Where(s => s != "passage" && (s != "growth" || rule.Scope == "tree")).ToArray();

    internal void SetSchedule(WorldRule rule, string schedule)
        => owner.History.Execute("szabálykönyv módosítása", () => {
            Require(rule);
            if (!SchedulesFor(rule).Contains(schedule)) throw new ArgumentException("Az időzítés nem érhető el ebben a hatókörben.", nameof(schedule));
            rule.Schedule = schedule; owner.Invalidate();
        });

    internal void MoveToGroup(WorldRule rule, string group) => Edit(rule, r => r.Group = group);
    internal void ClearGroups(IEnumerable<WorldRule> rules)
        => owner.History.Execute("szabálykönyv módosítása", () => {
            var selected = rules.ToArray();
            foreach (var rule in selected) Require(rule);
            if (selected.Length == 0) return;
            foreach (var rule in selected) rule.Group = "";
            owner.Invalidate();
        });

    internal RuleFieldDefinition AddField(string scope, string name, double initial, string unit)
        => owner.History.Execute<RuleFieldDefinition>("szabálykönyv módosítása", () => {
            RequireScope(scope);
            var field = new RuleFieldDefinition { Name = name.Trim(), Scope = scope, Initial = initial, Unit = unit };
            field.Validate();
            if (Fields.Any(f => f.Name == field.Name)) throw new InvalidOperationException("Ez a név már foglalt.");
            Fields.Add(field); owner.Invalidate(); return field;
        });

    internal void DeleteField(RuleFieldDefinition field)
        => owner.History.Execute("szabálykönyv módosítása", () => {
            if (!Fields.Contains(field)) throw new InvalidOperationException("A mező már nem az aktuális szabálykönyv része.");
            // References remain visible in the draft until the author repairs them.
            Fields.Remove(field); owner.Invalidate();
        });

    internal RuleEffect AddCalculation(WorldRule rule)
        => owner.History.Execute<RuleEffect>("szabálykönyv módosítása", () => {
            Require(rule);
            var scope = RequireScope(rule.Scope);
            string target = scope.Fields.FirstOrDefault(f => f.Write != null)?.Name ?? Fields.FirstOrDefault(f => f.Scope == rule.Scope)?.Name ?? "";
            var effect = new RuleEffect { Target = target, Mode = "set", Formula = "0" };
            rule.Effects.Add(effect); owner.Invalidate(); return effect;
        });

    internal RuleEffect EnsureVisualTarget(WorldRule rule, string target)
        => owner.History.Execute<RuleEffect>("szabálykönyv módosítása", () => {
            Require(rule);
            var existing = rule.Effects.Find(e => e.Target == target);
            if (existing != null) return existing;
            bool action = RequireScope(rule.Scope).Actions.Any(a => a.Name == target);
            var effect = new RuleEffect { Target = target, Mode = action ? "action" : "add", Influence = new() { PerYear = !action && rule.Schedule != "passage" } };
            rule.Effects.Add(effect); owner.Invalidate(); return effect;
        });

    internal void DeleteEffect(WorldRule rule, RuleEffect effect)
        => owner.History.Execute("szabálykönyv módosítása", () => {
            Require(rule, effect); rule.Effects.Remove(effect); owner.Invalidate();
        });

    internal void ClearCondition(WorldRule rule)
        => owner.History.Execute("szabálykönyv módosítása", () => {
            Require(rule);
            if (rule.ConditionGraph == null && rule.Condition.Length == 0) return;
            rule.ConditionGraph = null; rule.Condition = ""; owner.Invalidate();
        });

    internal void SetCalculation(WorldRule rule, RuleEffect? effect, BehaviorGraph graph)
        => owner.History.Execute("szabálykönyv módosítása", () => {
            Require(rule);
            if (effect != null) Require(rule, effect);
            var copy = new BehaviorModel { Graphs = new() { graph } }.Clone().Graphs[0];
            if (effect == null) { rule.ConditionGraph = copy; rule.Condition = ""; }
            else { effect.Computation = copy; effect.Formula = ""; effect.Influence = null; }
            owner.Invalidate();
        });

    private WorldRule Add(WorldRule rule) { Rules.Add(rule); owner.Invalidate(); return rule; }
    private WorldRule AddNew(WorldRule rule) { ConfigureScope(rule, rule.Scope); return Add(rule); }
    private string NewId()
    {
        string id;
        do { id = "rule-" + Guid.NewGuid().ToString("N"); } while (Rules.Any(r => r.Id == id));
        return id;
    }
    private static RuleScope RequireScope(string scope) => RuleEngine.Scope(scope) ?? throw new ArgumentException("Ismeretlen hatókör.", nameof(scope));
    internal void Require(WorldRule rule)
    {
        if (!Rules.Contains(rule)) throw new InvalidOperationException("A szabály már nem az aktuális szabálykönyv része.");
    }
    internal void Require(WorldRule rule, RuleEffect effect)
    {
        Require(rule);
        if (!rule.Effects.Contains(effect)) throw new InvalidOperationException("A hatás már nem az aktuális szabály része.");
    }

}
