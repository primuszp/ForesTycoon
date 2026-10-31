using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon;

/// <summary>
/// Runs the rule book: every enabled rule, in order, on every element of its scope. Per element the condition is
/// evaluated, then the effects in order; a later effect sees the earlier writes of the same element. Expressions are the
/// editor's formula language, compiled to the bounded behavior graphs (no IO, no C#). Deterministic: fixed element order.
/// A step or passage may suspend between bounded work units (<see cref="Begin"/>, <see cref="Continue"/>).
/// The pending logical tick or command retains ownership; other simulation work cannot overtake it.
/// </summary>
internal sealed partial class RuleEngine
{
    // ── Scopes: the building blocks a rule can address ──────────────────
    // Every concrete kind of the object model (RuleEntities.cs) with its view; no scope is written by hand.
    internal static readonly RuleScope[] Scopes = SimulationWorld.DiscoveredScopes();
    internal static RuleScope Scope(string id) => Array.Find(Scopes, s => s.Id == id);


    private readonly CompiledRule[] compiled;
    private readonly Dictionary<string, double[]> values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, double>> keyedValues = new(StringComparer.Ordinal);
    private sealed class FieldValues(double initial, double[] dense = null, Dictionary<int, double> keyed = null)
    {
        internal double this[int key] {
            get => keyed == null ? dense[key] : keyed.GetValueOrDefault(key, initial);
            set { if (keyed == null) dense[key] = value; else if (value == initial) keyed.Remove(key); else keyed[key] = value; }
        }
    }
    private readonly RuleFlowLedger flowLedger = new();
    /// <summary>The flow ledger: what each named flow has moved so far, in its source's unit.</summary>
    internal IReadOnlyDictionary<string, double> FlowTotals => flowLedger.Totals;
    internal List<WorldRule> Rules { get; }
    internal List<RuleFieldDefinition> Fields { get; }
    private readonly Dictionary<string, string> scheduleErrors = new(StringComparer.Ordinal);
    internal string LastError {
        get => scheduleErrors.Values.LastOrDefault();
        private set { if (value == null) scheduleErrors.Remove(stepSchedule); else scheduleErrors[stepSchedule] = value; }
    }

    private readonly RuleNamedValues namedInputs;
    private bool previewing;
    private double ReadNamed(string key) => namedInputs.Read(namedInputs.Slot(key), key, preview: previewing, parallel: parallelFlowEvaluation);
    private Func<double> BindNamed(string key)
    {
        int slot = namedInputs.Slot(key);
        return () => namedInputs.Read(slot, key, preview: previewing, parallel: parallelFlowEvaluation);
    }

    // Mutable scratch space belongs to this execution, rather than to the compiled rule description.
    private sealed class RuleWorkBuffers(int inputs, int customFields)
    {
        internal readonly double[] Inputs = new double[inputs];
        internal readonly FieldValues[] CustomValues = new FieldValues[customFields];
    }
    private readonly RuleWorkBuffers[] ruleBuffers;

    private readonly Dictionary<string, RuleScope> objectScopes;
    private RuleScope FindScope(string id) => objectScopes.GetValueOrDefault(id);
    private bool NeedsPotentialEvaporation => compiled.Any(r => r.Source.Enabled && r.Source.Schedule == "step" && r.Scope.Id == "tile"
        && r.Reads.Any(i => r.Inputs[i] == "potentialEvaporation"));
    private static bool ReadsTileClimate(CompiledRule rule) => rule.Scope.Id == "tile"
        && rule.Reads.Any(i => rule.Inputs[i] is "rain" or "snowfall" or "temperature" or "potentialEvaporation");

    /// <summary>One whole step of <paramref name="years"/>; returns true when an action changed the network (routes need a refresh).</summary>
    internal bool Run(SimulationWorld world, double years, double time, string schedule = "step")
    {
        ForesTycoon.Engine.SimulationWork.RequireSynchronousContext();
        Begin(world, years, time, schedule);
        bool changed = false;
        while (StepPending) changed |= Continue(world, 256);
        return changed;
    }

    // ── A step spread over world ticks ───────────────────────────────────
    private IEnumerator<int> execution;
    internal bool NetworkChanged { get; private set; }
    private bool stepPending; private double stepYears, stepTime; private int stepRule, stepElement;
    private string stepSchedule = "step"; private int eventElement;
    private string eventScope;
    private Type eventType;
    private HashSet<int> eventElements;
    /// <summary>A step has begun and not all of its work is done yet.</summary>
    internal bool StepPending => stepPending;

    /// <summary>Starts a step of <paramref name="years"/>; <see cref="Continue"/> does the work. The previous step must be finished.</summary>
    internal void Begin(SimulationWorld world, double years, double time, string schedule = "step", int element = 0,
        string eventScope = null, int[] eventElements = null)
    {
        if (!double.IsFinite(years) || years < 0 || !double.IsFinite(time) || Array.IndexOf(WorldRule.Schedules, schedule) < 0) throw new ArgumentException("Invalid rule interval or schedule.");
        if (stepPending) throw new InvalidOperationException("Az előző szabálylépés még fut.");
        execution?.Dispose(); execution = null; NetworkChanged = false;
        stepSchedule = schedule; LastError = null; namedInputs.BeginStep();
        eventElement = element;
        this.eventScope = eventScope;
        eventType = eventScope == null ? null : FindScope(eventScope)?.ObjectType ?? SimulationWorld.RuleObjects.Find(eventScope)?.Type
            ?? throw new ArgumentException("Ismeretlen esemény-objektumcsalád: " + eventScope);
        this.eventElements = eventElements == null ? null : new(eventElements);
        stepPending = true; stepYears = years; stepTime = time; stepRule = stepElement = 0;
    }

    /// <summary>
    /// Continues the step with at most <paramref name="budget"/> preparation/element work units, in its own order.
    /// Every scope can suspend. Returns true when an action
    /// changed the network. Nothing happens when no step is pending.
    /// </summary>
    internal bool Continue(SimulationWorld world, int budget)
    {
        if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        if (!stepPending) return false;
        execution ??= RunRulesWork(world, stepYears, stepTime).GetEnumerator();
        NetworkChanged = false;
        for (int i = 0; i < budget; i++) {
            bool pending = AdvanceWorkUnit(); flowLedger.Publish();
            if (!pending) { execution.Dispose(); execution = null; break; }
        }
        flowLedger.Publish();
        return NetworkChanged;
    }

    internal IEnumerable<int> ContinueWork(SimulationWorld world)
    {
        if (!stepPending) yield break;
        execution ??= RunRulesWork(world, stepYears, stepTime).GetEnumerator();
        NetworkChanged = false;
        // A bounded batch amortizes nested iterator dispatch without a synchronous finish path.
        // Publish at every inner unit as well: first-use ledger order must not depend on the batch size.
        bool pending = true;
        while (pending) {
            int completed = 0;
            while (completed < Math.Min(8, SimulationWork.MaximumUnits) && (pending = AdvanceWorkUnit())) {
                flowLedger.Publish(); completed++;
            }
            flowLedger.Publish();
            if (completed > 0) yield return completed;
        }
        execution.Dispose(); execution = null;
        flowLedger.Publish();
    }

    private bool AdvanceWorkUnit()
    {
        if (Profile == null) return ForesTycoon.Engine.SimulationWork.MoveNext(execution);
        string id = stepRule < compiled.Length ? compiled[stepRule].Source.Id : null;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        bool more = ForesTycoon.Engine.SimulationWork.MoveNext(execution);
        if (id != null) Profile[id] = Profile.GetValueOrDefault(id) + System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return more;
    }

    private IEnumerable<int> PrepareValuesWork(SimulationWorld world, RuleFieldDefinition field)
    {
        if (UsesStableKeys(field)) {
            foreach (int unit in PrepareKeyedValuesWork(world, field)) yield return unit;
            yield break;
        }
        var scope = FindScope(field.Scope); int size = (scope.StateSize ?? scope.Size)(world);
        if (values.TryGetValue(field.Name, out var previous) && previous.Length == size) yield break;
        var array = new double[size];
        for (int first = 0; first < size; first += 1024) {
            int count = Math.Min(1024, size - first);
            Array.Fill(array, field.Initial, first, count);
            if (previous != null && first < previous.Length) Array.Copy(previous, first, array, first, Math.Min(count, previous.Length - first));
            yield return 1;
        }
        values[field.Name] = array;
    }

    private IEnumerable<int> RunRulesWork(SimulationWorld world, double years, double time)
    {
        bool climatePrepared = false;
        HashSet<RuleScope> preparedScopes = stepSchedule is "passage" or "growth" ? null : new();
        flowLedger.BeginStep();
        for (; stepRule < compiled.Length; stepRule++, stepElement = 0) {
            var rule = compiled[stepRule];
            // The metadata scan is bounded by the 256-rule book limit. A growth patch must not
            // suspend once for every unrelated water/month/passage rule in the same book.
            if (rule.Source.Schedule != stepSchedule) continue;
            yield return 1;
            if (!rule.Source.Enabled) continue;
            var scope = rule.Scope;
            if (eventType != null && (scope.ObjectType == null || !eventType.IsAssignableFrom(scope.ObjectType))) continue;
            if (stepSchedule is not ("passage" or "growth") && scope.PrepareElements != null && preparedScopes.Add(scope)) foreach (int work in scope.PrepareElements(world)) yield return work;
            var elements = stepSchedule == "passage" ? new[] { eventElement } : scope.PreparedElements?.Invoke(world) ?? scope.Elements(world);
            if (eventElements != null) {
                var selected = new List<int>();
                foreach (int id in elements) { if (eventElements.Contains(scope.StateIndex(world, id))) selected.Add(id); yield return 1; }
                elements = selected.ToArray();
            }
            if (!climatePrepared && stepSchedule == "step" && stepElement < elements.Length && ReadsTileClimate(rule)) {
                // Prime the first row in its original read order, preserving which query error is reported first.
                foreach (var read in rule.Readers) read(world, elements[stepElement]);
                foreach (int unit in world.Environment.PrepareRuleClimateWork(NeedsPotentialEvaporation)) yield return unit;
                climatePrepared = true;
            }
            if (PlanLocalFlows && elements.Length >= 4096 && world.Environment.VegetationInputsCurrent && CanPlanLocalFlows(rule)) {
                foreach (int unit in RunLocalFlowsWork(world, rule, elements, years, time)) yield return unit;
                continue;
            }
            var customDefs = rule.CustomDefinitions;
            var custom = ruleBuffers[stepRule].CustomValues;
            for (int c = 0; c < custom.Length; c++) {
                foreach (int unit in PrepareValuesWork(world, customDefs[c])) yield return unit;
                var f = customDefs[c];
                custom[c] = UsesStableKeys(f) ? new(f.Initial, keyed: keyedValues[f.Name]) : new(f.Initial, dense: values[f.Name]);
            }
            var inputs = ruleBuffers[stepRule].Inputs;
            int[] reads = rule.Reads; var readers = rule.Readers; var effects = rule.Effects;
            for (; stepElement < elements.Length; stepElement++) {
                yield return 1;
                int id = elements[stepElement];
                for (int r = 0; r < reads.Length; r++) inputs[reads[r]] = readers[r](world, id);
                for (int i = 0; i < custom.Length; i++) inputs[scope.Fields.Length + i] = custom[i][scope.StateIndex(world, id)];
                string error = null;
                if (rule.Condition != null && rule.Condition.Evaluate(years, time, inputs, out error) == 0) { if (error != null) LastError = error; continue; }
                if (error != null) { LastError = error; continue; }
                foreach (var effect in effects) {
                    if (!EffectAvailable(world, scope, id, effect)) continue;
                    double value = effect.Value == null ? 1 : effect.Value.Evaluate(years, time, inputs, out error);
                    if (error != null) { LastError = $"{rule.Source.Name}: {error}"; break; }
                    if (effect.Action != null) {
                        if (value == 0) continue;
                        NetworkChanged |= effect.Action.Apply(world, id);
                        preparedScopes?.Clear();
                        break; // the element may be gone; its later effects do not run
                    }
                    if (effect.Flow != null) { Move(world, scope, id, inputs, custom, customDefs, effect.Flow, value); continue; }
                    double next = NextValue(effect.Source, inputs[effect.Field], value, years);
                    if (effect.Field < scope.Fields.Length) {
                        scope.Fields[effect.Field].Write(world, id, next);
                        inputs[effect.Field] = scope.Fields[effect.Field].Read(world, id); // the world may clamp it
                    } else {
                        int c = effect.Field - scope.Fields.Length;
                        next = Math.Clamp(next, customDefs[c].Min, customDefs[c].Max);
                        custom[c][scope.StateIndex(world, id)] = next; inputs[effect.Field] = next;
                    }
                }
            }
        }
        stepPending = false;
    }

    private static double NextValue(RuleEffect effect, double before, double value, double years) => effect.Mode switch {
        "add" => before + value,
        "approach" => before + Math.Clamp(value - before, -effect.ApproachPerYear * years, effect.ApproachPerYear * years),
        _ => value
    };

    private static bool EffectAvailable(SimulationWorld world, RuleScope scope, int id, CompiledEffect effect)
    {
        bool Has(int field) => field < 0 || field >= scope.Fields.Length || scope.Fields[field].Available?.Invoke(world, id) != false;
        return Has(effect.Field) && (effect.Flow == null || Has(effect.Flow.To));
    }

    /// <summary>When set, the milliseconds each rule took are added up here, by rule ID (benchmarks).</summary>
    internal Dictionary<string, double> Profile { get; set; }


}
