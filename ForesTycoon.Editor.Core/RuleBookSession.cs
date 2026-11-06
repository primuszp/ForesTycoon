#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor.Core;

/// <summary>Rule authoring port: read-only live handles for queries, owned DTOs only inside validated edit commands.</summary>
internal sealed class RuleBookSession
{
    private readonly RuleBookEditor editor;
    private readonly EditorDocument document;
    private long analyzedVersion = -1;
    private IReadOnlyList<RuleFieldUse>? fieldUses;
    private readonly EditorReadCache<WorldRule, RuleView> worldRules;
    private readonly EditorReadCache<RuleEffect, RuleEffectView> ruleEffects;
    private readonly EditorReadCache<RuleInfluence, RuleInfluenceView> ruleInfluences;
    private readonly EditorReadCache<InfluenceTerm, InfluenceTermView> influenceTerms;
    private readonly EditorReadCache<InfluenceCondition, InfluenceConditionView> influenceConditions;
    private readonly EditorReadCache<InfluenceFactor, InfluenceFactorView> influenceFactors;
    private readonly EditorReadCache<RuleFieldDefinition, RuleFieldView> ruleFieldDefinitions;
    internal EditorReadList<RuleView> Entries { get; }
    internal EditorReadList<RuleFieldView> CustomFields { get; }
    internal InfluenceCommands Influences { get; }
    internal RuleBookSession(EditorDocument document)
    {
        this.document = document; editor = document.RuleBook;
        worldRules = new(model => new(model, this));
        ruleEffects = new(model => new(model, this));
        ruleInfluences = new(model => new(model, this));
        influenceTerms = new(model => new(model, this));
        influenceConditions = new(model => new(model, this));
        influenceFactors = new(model => new(model, this));
        ruleFieldDefinitions = new(model => new(model, this));
        Entries = new(() => document.Rules.Count, index => View(document.Rules[index])!);
        CustomFields = new(() => document.Fields.Count, index => View(document.Fields[index])!);
        Influences = new(this, editor.Influences);
    }
    internal RuleView? View(WorldRule? value) => worldRules.View(value);
    private WorldRule Resolve(RuleView value) => worldRules.Model(value);
    internal RuleEffectView? View(RuleEffect? value) => ruleEffects.View(value);
    private RuleEffect Resolve(RuleEffectView value) => ruleEffects.Model(value);
    internal RuleInfluenceView? View(RuleInfluence? value) => ruleInfluences.View(value);
    private RuleInfluence Resolve(RuleInfluenceView value) => ruleInfluences.Model(value);
    internal InfluenceTermView? View(InfluenceTerm? value) => influenceTerms.View(value);
    private InfluenceTerm Resolve(InfluenceTermView value) => influenceTerms.Model(value);
    internal InfluenceConditionView? View(InfluenceCondition? value) => influenceConditions.View(value);
    private InfluenceCondition Resolve(InfluenceConditionView value) => influenceConditions.Model(value);
    internal InfluenceFactorView? View(InfluenceFactor? value) => influenceFactors.View(value);
    private InfluenceFactor Resolve(InfluenceFactorView value) => influenceFactors.Model(value);
    internal RuleFieldView? View(RuleFieldDefinition? value) => ruleFieldDefinitions.View(value);
    private RuleFieldDefinition Resolve(RuleFieldView value) => ruleFieldDefinitions.Model(value);
    internal IReadOnlyList<FormulaName> ExpressionNames(string scope, IEnumerable<string> stocks) => RuleEngine.Names(scope, editor.CustomFields, stocks);
    internal string[] ReadableFields(RuleScope scope, IEnumerable<string> stocks) => scope.Fields.Select(field => field.Name)
        .Concat(editor.CustomFields.Where(field => field.Scope == scope.Id).Select(field => field.Name))
        .Concat(scope.Id == "world" ? Array.Empty<string>() : SimulationWorld.RuleWorldFields)
        .Concat(stocks.Select(id => "stock." + id)).Concat(GameTuning.Specs.Select(spec => "tuning." + spec.Id)).Distinct().ToArray();
    internal string[] WritableFields(RuleScope scope) => scope.Fields.Where(field => field.Write != null).Select(field => field.Name)
        .Concat(editor.CustomFields.Where(field => field.Scope == scope.Id).Select(field => field.Name)).Distinct().ToArray();
    internal IReadOnlyList<RuleFieldUse> FieldUses()
    {
        if (fieldUses == null || analyzedVersion != document.Version) {
            fieldUses = RuleFieldAnalysis.Build(RuleFieldCatalog.Objects, editor.Entries, RuleFieldCatalog.KeyDisplay); analyzedVersion = document.Version;
        }
        return fieldUses;
    }
    internal int IndexOf(RuleView value) => editor.IndexOf(Resolve(value));
    internal void EditEffect(RuleView rule, RuleEffectView effect, Action<RuleEffect> change) => editor.EditEffect(Resolve(rule), Resolve(effect), change);
    internal RuleView CreateEmpty() => View(editor.CreateEmpty())!;
    internal RuleView CreateForTarget(string scope, string target, string mode, string name) => View(editor.CreateForTarget(scope, target, mode, name))!;
    internal RuleView AddStarter(WorldRule starter) => View(editor.AddStarter(starter))!;
    internal RuleView Copy(RuleView rule) => View(editor.Copy(Resolve(rule)))!;
    internal void Delete(RuleView rule) => editor.Delete(Resolve(rule));
    internal bool RestoreVisualRule(RuleView rule) => editor.RestoreVisualRule(Resolve(rule));
    internal void Edit(RuleView rule, Action<WorldRule> edit) => editor.Edit(Resolve(rule), edit);
    internal void SetScope(RuleView rule, string scope) => editor.SetScope(Resolve(rule), scope);
    internal string[] SchedulesFor(RuleView rule) => editor.SchedulesFor(Resolve(rule));
    internal void SetSchedule(RuleView rule, string schedule) => editor.SetSchedule(Resolve(rule), schedule);
    internal void MoveToGroup(RuleView rule, string group) => editor.MoveToGroup(Resolve(rule), group);
    internal void ClearGroups(IEnumerable<RuleView> rules) => editor.ClearGroups(rules.Select(Resolve));
    internal RuleFieldView AddField(string scope, string name, double initial, string unit) => View(editor.AddField(scope, name, initial, unit))!;
    internal void DeleteField(RuleFieldView field) => editor.DeleteField(Resolve(field));
    internal RuleEffectView AddCalculation(RuleView rule) => View(editor.AddCalculation(Resolve(rule)))!;
    internal RuleEffectView EnsureVisualTarget(RuleView rule, string target) => View(editor.EnsureVisualTarget(Resolve(rule), target))!;
    internal void DeleteEffect(RuleView rule, RuleEffectView effect) => editor.DeleteEffect(Resolve(rule), Resolve(effect));
    internal void ClearCondition(RuleView rule) => editor.ClearCondition(Resolve(rule));
    internal void SetCalculation(RuleView rule, RuleEffectView? effect, BehaviorGraph graph) => editor.SetCalculation(Resolve(rule), effect == null ? null : Resolve(effect), graph);

    internal sealed class InfluenceCommands(RuleBookSession session, RuleInfluenceEditor editor)
    {
        internal void EditEffect(RuleView rule, RuleEffectView effect, Action<RuleEffect> edit) => editor.EditEffect(session.Resolve(rule), session.Resolve(effect), edit);
        internal void EditTerm(RuleView rule, RuleEffectView effect, InfluenceTermView term, Action<InfluenceTerm> edit) => editor.EditTerm(session.Resolve(rule), session.Resolve(effect), session.Resolve(term), edit);
        internal void EditCondition(RuleView rule, RuleEffectView effect, InfluenceTermView term, InfluenceConditionView condition, Action<InfluenceCondition> edit) => editor.EditCondition(session.Resolve(rule), session.Resolve(effect), session.Resolve(term), session.Resolve(condition), edit);
        internal void EditFactor(RuleView rule, RuleEffectView effect, InfluenceTermView term, InfluenceFactorView factor, Action<InfluenceFactor> edit) => editor.EditFactor(session.Resolve(rule), session.Resolve(effect), session.Resolve(term), session.Resolve(factor), edit);
        internal void SetMode(RuleView rule, RuleEffectView effect, string mode, string flowName) => editor.SetMode(session.Resolve(rule), session.Resolve(effect), mode, flowName);
        internal InfluenceTermView? AddTerm(RuleView rule, RuleEffectView effect) => session.View(editor.AddTerm(session.Resolve(rule), session.Resolve(effect)));
        internal int? ConnectSource(RuleView rule, string source, int effectIndex, int termIndex, string name) => editor.ConnectSource(session.Resolve(rule), source, effectIndex, termIndex, name);
        internal void DeleteTerm(RuleView rule, RuleEffectView effect, InfluenceTermView term) => editor.DeleteTerm(session.Resolve(rule), session.Resolve(effect), session.Resolve(term));
        internal InfluenceFactorView? AddFactor(RuleView rule, RuleEffectView effect, InfluenceTermView term, string source) => session.View(editor.AddFactor(session.Resolve(rule), session.Resolve(effect), session.Resolve(term), source));
        internal void DeleteFactor(RuleView rule, RuleEffectView effect, InfluenceTermView term, InfluenceFactorView factor) => editor.DeleteFactor(session.Resolve(rule), session.Resolve(effect), session.Resolve(term), session.Resolve(factor));
        internal InfluenceConditionView? AddCondition(RuleView rule, RuleEffectView effect, InfluenceTermView term, string field) => session.View(editor.AddCondition(session.Resolve(rule), session.Resolve(effect), session.Resolve(term), field));
        internal void DeleteCondition(RuleView rule, RuleEffectView effect, InfluenceTermView term, InfluenceConditionView condition) => editor.DeleteCondition(session.Resolve(rule), session.Resolve(effect), session.Resolve(term), session.Resolve(condition));
        internal void SetConditionField(RuleView rule, RuleEffectView effect, InfluenceTermView term, InfluenceConditionView condition, string field, RuleFieldOption[] options) => editor.SetConditionField(session.Resolve(rule), session.Resolve(effect), session.Resolve(term), session.Resolve(condition), field, options);
        internal bool NormalizeCondition(RuleView rule, RuleEffectView effect, InfluenceTermView term, InfluenceConditionView condition, RuleFieldOption[] options) => editor.NormalizeCondition(session.Resolve(rule), session.Resolve(effect), session.Resolve(term), session.Resolve(condition), options);
        internal bool SetThreshold(RuleView rule, RuleEffectView effect, InfluenceTermView term, InfluenceConditionView condition, InfluenceThresholdKind kind, string[] readable) => editor.SetThreshold(session.Resolve(rule), session.Resolve(effect), session.Resolve(term), session.Resolve(condition), kind, readable);
    }
}
