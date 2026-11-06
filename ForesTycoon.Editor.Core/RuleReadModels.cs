#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor.Core;

/// <summary>A live read-only projection. Neither the collection nor its elements expose an owned DTO.</summary>
internal sealed class EditorReadList<T>(Func<int> count, Func<int, T> item) : IReadOnlyList<T>
{
    public int Count => count();
    public T this[int index] => item(index);
    public IEnumerator<T> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    internal int FindIndex(Predicate<T> match) { for (int i = 0; i < Count; i++) if (match(this[i])) return i; return -1; }
    internal T? Find(Predicate<T> match) { int index = FindIndex(match); return index < 0 ? default : this[index]; }
    internal int IndexOf(T value) { for (int i = 0; i < Count; i++) if (EqualityComparer<T>.Default.Equals(this[i], value)) return i; return -1; }
}

/// <summary>Weak identity maps preserve selection handles without retaining deleted documents.</summary>
internal sealed class EditorReadCache<TModel, TView> where TModel : class where TView : class
{
    private readonly ConditionalWeakTable<TModel, TView> views = new();
    private readonly ConditionalWeakTable<TView, TModel> models = new();
    private readonly Func<TModel, TView> create;
    private readonly ConditionalWeakTable<TModel, TView>.CreateValueCallback factory;
    internal EditorReadCache(Func<TModel, TView> create) { this.create = create; factory = CreateView; }
    private TView CreateView(TModel model) { var view = create(model); models.Add(view, model); return view; }
    internal TView? View(TModel? model) => model == null ? null : views.GetValue(model, factory);
    internal TModel Model(TView view) => models.TryGetValue(view, out var model) ? model
        : throw new InvalidOperationException("A kijelölés másik szerkesztési munkamenethez tartozik.");
}

/// <summary>Read-only WorldRule handle. Capture returns an independent, mutable transfer object.</summary>
internal sealed class RuleView
{
    private readonly WorldRule model;
    public string Id => model.Id;
    public string Name => model.Name;
    public string Description => model.Description;
    public string Group => model.Group;
    public bool Enabled => model.Enabled;
    public string Template => model.Template;
    public string Scope => model.Scope;
    public string Schedule => model.Schedule;
    public string Condition => model.Condition;
    public BehaviorGraph? ConditionGraph => model.ConditionGraph == null ? null : new BehaviorModel { Graphs = new() { model.ConditionGraph } }.Clone().Graphs[0];
    public EditorReadList<RuleEffectView> Effects { get; }
    internal RuleView(WorldRule model, RuleBookSession session)
    {
        this.model = model;
        Effects = new(() => model.Effects.Count, index => session.View(model.Effects[index])!);
    }
    internal WorldRule Capture() => RuleEngine.Clone(new() { model })[0];
}

/// <summary>Read-only RuleEffect handle. Capture returns an independent, mutable transfer object.</summary>
internal sealed class RuleEffectView
{
    private readonly RuleEffect model;
    public string Target => model.Target;
    public string To => model.To;
    public string FlowName => model.FlowName;
    public string Spread => model.Spread;
    public string Level => model.Level;
    public string Mode => model.Mode;
    public double ApproachPerYear => model.ApproachPerYear;
    public string Formula => model.Formula;
    public RuleInfluenceView? Influence => session.View(model.Influence);
    public BehaviorGraph? Computation => model.Computation == null ? null : new BehaviorModel { Graphs = new() { model.Computation } }.Clone().Graphs[0];
    private readonly RuleBookSession session;
    internal RuleEffectView(RuleEffect model, RuleBookSession session)
    {
        this.model = model; this.session = session;
    }
    internal RuleEffect Capture() => RuleEngine.Clone(new() { new() { Effects = new() { model } } })[0].Effects[0];
    internal RuleEffect Executable() => Capture().Executable();
}

/// <summary>Read-only RuleInfluence handle. Capture returns an independent, mutable transfer object.</summary>
internal sealed class RuleInfluenceView
{
    private readonly RuleInfluence model;
    public string Combine => model.Combine;
    public bool PerYear => model.PerYear;
    public bool PerHour => model.PerHour;
    public EditorReadList<InfluenceTermView> Terms { get; }
    internal RuleInfluenceView(RuleInfluence model, RuleBookSession session)
    {
        this.model = model;
        Terms = new(() => model.Terms.Count, index => session.View(model.Terms[index])!);
    }
}

/// <summary>Read-only InfluenceTerm handle. Capture returns an independent, mutable transfer object.</summary>
internal sealed class InfluenceTermView
{
    private readonly InfluenceTerm model;
    public string Name => model.Name;
    public bool Enabled => model.Enabled;
    public bool Decrease => model.Decrease;
    public double Amount => model.Amount;
    public string AmountField => model.AmountField;
    public double AmountScale => model.AmountScale;
    public EditorReadList<InfluenceFactorView> Factors { get; }
    public EditorReadList<InfluenceConditionView> Conditions { get; }
    internal InfluenceTermView(InfluenceTerm model, RuleBookSession session)
    {
        this.model = model;
        Factors = new(() => model.Factors.Count, index => session.View(model.Factors[index])!);
        Conditions = new(() => model.Conditions.Count, index => session.View(model.Conditions[index])!);
    }
    internal InfluenceTerm Capture() => RuleEngine.Clone(new() { new() { Effects = new() { new() { Influence = new() { Terms = new() { model } } } } } })[0].Effects[0].Influence.Terms[0];
}

/// <summary>Read-only InfluenceCondition handle. Capture returns an independent, mutable transfer object.</summary>
internal sealed class InfluenceConditionView
{
    private readonly InfluenceCondition model;
    public string Field => model.Field;
    public string Comparison => model.Comparison;
    public double Value => model.Value;
    public string ValueField => model.ValueField;
    public InfluenceFactorView? ValueCurve => session.View(model.ValueCurve);
    private readonly RuleBookSession session;
    internal InfluenceConditionView(InfluenceCondition model, RuleBookSession session)
    {
        this.model = model; this.session = session;
    }
    internal InfluenceCondition Capture() => JsonSerializer.Deserialize<InfluenceCondition>(JsonSerializer.Serialize(model))!;
}

/// <summary>Read-only InfluenceFactor handle. Capture returns an independent, mutable transfer object.</summary>
internal sealed class InfluenceFactorView
{
    private readonly InfluenceFactor model;
    public string Source => model.Source;
    public string Shape => model.Shape;
    public double InputLow => model.InputLow;
    public double InputHigh => model.InputHigh;
    public double OutputLow => model.OutputLow;
    public double OutputHigh => model.OutputHigh;
    public IReadOnlyList<double>? Breakpoints => model.Breakpoints?.AsReadOnly();
    internal InfluenceFactorView(InfluenceFactor model, RuleBookSession session)
    {
        this.model = model;
    }
    internal RuleCurve ToCurve() => model.ToCurve();
    internal InfluenceFactor Capture() => JsonSerializer.Deserialize<InfluenceFactor>(JsonSerializer.Serialize(model))!;
}

/// <summary>Read-only RuleFieldDefinition handle. Capture returns an independent, mutable transfer object.</summary>
internal sealed class RuleFieldView
{
    private readonly RuleFieldDefinition model;
    public string Name => model.Name;
    public string Scope => model.Scope;
    public string Description => model.Description;
    public string Unit => model.Unit;
    public double Initial => model.Initial;
    public double Min => model.Min;
    public double Max => model.Max;
    internal RuleFieldView(RuleFieldDefinition model, RuleBookSession session)
    {
        this.model = model;
    }
}
