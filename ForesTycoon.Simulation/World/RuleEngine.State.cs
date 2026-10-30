using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ForesTycoon;

internal sealed record RuleBookCheckpoint(List<WorldRule> Rules, List<RuleFieldDefinition> Fields, Dictionary<string, double[]> Values,
    List<string> RemovedStarters = null, Dictionary<string, double> FlowTotals = null, int ProjectVersion = 2);
/// <summary>A rule step under way: its time step, where it stands (rule, element) and the named values it has read so far.</summary>
internal sealed record RuleStepCheckpoint(double Years, double Time, int Rule, int Element, Dictionary<string, double> Named,
    string Schedule = "step", int EventElement = 0, string EventScope = null, int[] EventElements = null);

internal sealed partial class RuleEngine
{
    /// <summary>Custom field values survive a rule book change for fields kept with the same name and scope.</summary>
    internal void Preserve(RuleEngine previous)
    {
        if (previous == null) return;
        foreach (var f in Fields)
            if (previous.Fields.Exists(p => p.Name == f.Name && p.Scope == f.Scope) && previous.values.TryGetValue(f.Name, out var v))
                values[f.Name] = (double[])v.Clone();
    }

    private double[] Values(SimulationWorld world, RuleFieldDefinition field)
    {
        var scope = FindScope(field.Scope); int size = (scope.StateSize ?? scope.Size)(world);
        if (!values.TryGetValue(field.Name, out var array) || array.Length != size) {
            var previous = array; array = new double[size]; Array.Fill(array, field.Initial);
            if (previous != null) Array.Copy(previous, array, Math.Min(previous.Length, size)); values[field.Name] = array;
        }
        return array;
    }

    internal double ReadCustom(SimulationWorld world, string name, int id)
    {
        var f = Fields.Find(x => x.Name == name);
        if (f == null) return double.NaN;
        var scope = FindScope(f.Scope);
        if (id < 0 || id >= scope.Size(world)) return double.NaN;
        id = scope.StateIndex(world, id);
        if (previewing) {
            int size = (scope.StateSize ?? scope.Size)(world);
            if (id < 0 || id >= size) return double.NaN;
            return values.TryGetValue(name, out var saved) && id < saved.Length ? saved[id] : f.Initial;
        }
        var v = Values(world, f);
        return id >= 0 && id < v.Length ? v[id] : double.NaN;
    }

    /// <summary>The step under way, for a checkpoint; null when none is.</summary>
    internal RuleStepCheckpoint CaptureStep()
    {
        return !stepPending ? null : new(stepYears, stepTime, stepRule, stepElement, namedInputs.Capture(),
            stepSchedule, eventElement, eventScope, eventElements?.Order().ToArray());
    }

    /// <summary>Takes up a step saved by <see cref="CaptureStep"/>: it continues where it stood, with the named values it had read.</summary>
    internal void RestoreStep(SimulationWorld world, RuleStepCheckpoint saved) => SimulationWork.Complete(RestoreStepWork(world, saved));

    internal IEnumerable<int> RestoreStepWork(SimulationWorld world, RuleStepCheckpoint saved)
    {
        if (saved == null) yield break;
        if (flowQueries.Pending) throw new InvalidOperationException("Join query jobs before restoring an existing rule engine.");
        CheckpointGuard.Require(double.IsFinite(saved.Years) && saved.Years >= 0 && double.IsFinite(saved.Time) && saved.Rule >= 0
            && saved.Rule <= compiled.Length && saved.Element >= 0 && saved.Named != null && saved.Named.Values.All(double.IsFinite), "rule step");
        int limit = 0;
        if (saved.Rule < compiled.Length) {
            var scope = compiled[saved.Rule].Scope;
            if (saved.Schedule == "passage") limit = 1;
            else {
                if (scope.PrepareElements != null) foreach (int unit in scope.PrepareElements(world)) yield return unit;
                var elements = scope.PreparedElements?.Invoke(world) ?? scope.Elements(world);
                var selected = saved.EventElements == null ? null : new HashSet<int>(saved.EventElements);
                foreach (int id in elements) {
                    if (selected == null || selected.Contains(scope.StateIndex(world, id))) limit++;
                    yield return 1;
                }
            }
        }
        CheckpointGuard.Require(saved.Element <= limit, "rule step element");
        stepPending = false;
        Begin(world, saved.Years, saved.Time, saved.Schedule, saved.EventElement, saved.EventScope, saved.EventElements);
        stepRule = saved.Rule; stepElement = saved.Element;
        namedInputs.Restore(saved.Named);
    }

    /// <summary>Starter rules the author deleted; saved with the rules so they stay deleted.</summary>
    internal List<string> RemovedStarters { get; init; } = new();
    // The complete authored book survives a save, even if it currently equals the shipped project.
    internal RuleBookCheckpoint Capture() => new(Clone(Rules), CloneFields(Fields), values.ToDictionary(p => p.Key, p => (double[])p.Value.Clone(), StringComparer.Ordinal),
            RemovedStarters.ToList(), flowLedger.Capture(), RuleProject.CurrentVersion);

    internal void RestoreFlows(Dictionary<string, double> saved) => flowLedger.Restore(saved);

    internal void Restore(Dictionary<string, double[]> saved)
    {
        if (saved == null) return;
        foreach (var (name, v) in saved) {
            CheckpointGuard.Require(Fields.Exists(f => f.Name == name) && v != null && v.All(double.IsFinite), "rule field values");
            values[name] = (double[])v.Clone();
        }
    }

    internal static List<WorldRule> Clone(List<WorldRule> rules) => JsonSerializer.Deserialize<List<WorldRule>>(JsonSerializer.Serialize(rules));
    internal static List<RuleFieldDefinition> CloneFields(List<RuleFieldDefinition> fields) => JsonSerializer.Deserialize<List<RuleFieldDefinition>>(JsonSerializer.Serialize(fields));
}
