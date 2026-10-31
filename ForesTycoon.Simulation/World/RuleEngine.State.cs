using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ForesTycoon;

internal sealed record RuleBookCheckpoint(List<WorldRule> Rules, List<RuleFieldDefinition> Fields, Dictionary<string, double[]> Values,
    List<string> RemovedStarters = null, Dictionary<string, double> FlowTotals = null, int ProjectVersion = 2,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Dictionary<string, Dictionary<int, double>> KeyedValues = null);
/// <summary>A rule step under way: its time step, where it stands (rule, element) and the named values it has read so far.</summary>
internal sealed record RuleStepCheckpoint(double Years, double Time, int Rule, int Element, Dictionary<string, double> Named,
    string Schedule = "step", int EventElement = 0, string EventScope = null, int[] EventElements = null);

internal sealed partial class RuleEngine
{
    /// <summary>Custom field values survive a rule book change for fields kept with the same name and scope.</summary>
    internal IEnumerable<int> PreserveWork(RuleEngine previous, SimulationWorld world)
    {
        if (previous == null) yield break;
        foreach (var f in Fields) {
            var old = previous.Fields.Find(p => p.Name == f.Name && p.Scope == f.Scope);
            if (old == null) continue;
            if (previous.values.TryGetValue(f.Name, out var v)) {
                var copy = new double[v.Length];
                for (int first = 0; first < v.Length; first += 1024) {
                    Array.Copy(v, first, copy, first, Math.Min(1024, v.Length - first)); yield return 1;
                }
                values[f.Name] = copy;
            }
            if (previous.keyedValues.TryGetValue(f.Name, out var keyed)) {
                var copy = new Dictionary<int, double>();
                foreach (int key in LiveStableKeys(world, f)) {
                    double value = keyed.GetValueOrDefault(key, old.Initial);
                    if (value != f.Initial) copy.Add(key, value);
                    yield return 1;
                }
                keyedValues[f.Name] = copy;
            }
        }
    }

    private static bool UsesStableKeys(RuleFieldDefinition field) => field.Scope is "tree" or "truck";
    private readonly Dictionary<string, (object Owner, ulong Version, ulong Epoch)> keyedMembership = new(StringComparer.Ordinal);

    private static IEnumerable<int> LiveStableKeys(SimulationWorld world, RuleFieldDefinition field)
    {
        if (field.Scope == "tree") {
            foreach (var patch in world.Forest.IndividualTrees.Patches)
                for (int i = 0; i < patch.Value.Count; i++) yield return checked((int)patch.Value.Trees[i].Id);
        } else {
            foreach (var vehicle in world.VehicleState.Vehicles) yield return vehicle.Id;
        }
    }

    // Sparse values contain only live identities that differ from the initial value. Never allocate by the largest ID.
    private IEnumerable<int> PrepareKeyedValuesWork(SimulationWorld world, RuleFieldDefinition field)
    {
        var scope = FindScope(field.Scope);
        var store = world.Forest.IndividualTrees;
        (object Owner, ulong Version, ulong Epoch) stamp = field.Scope == "tree"
            ? (store, store.TopologyVersion, store.StorageEpoch) : (world.VehicleState, world.VehicleState.MembershipVersion, 0UL);
        if (keyedMembership.TryGetValue(field.Name, out var known) && known == stamp) yield break;
        keyedValues.TryGetValue(field.Name, out var previous);
        values.TryGetValue(field.Name, out var legacy);
        var live = new Dictionary<int, double>();
        void Add(int key, int oldIndex) {
            double value = previous?.GetValueOrDefault(key, field.Initial)
                ?? (legacy != null && (uint)oldIndex < (uint)legacy.Length ? legacy[oldIndex] : field.Initial);
            if (value != field.Initial) live.Add(key, value);
        }
        if (field.Scope == "tree") {
            // Read identities from the store; rebuilding the scope view here would replace a bound growth patch.
            foreach (int key in LiveStableKeys(world, field)) { Add(key, key); yield return 1; }
        } else {
            foreach (int id in scope.Elements(world)) { Add(scope.StateIndex(world, id), id); yield return 1; }
        }
        keyedValues[field.Name] = live; values.Remove(field.Name);
        keyedMembership[field.Name] = stamp;
    }

    // Convert old dense checkpoints before any actor can be removed or its list reordered.
    internal IEnumerable<int> MigrateCustomValuesWork(SimulationWorld world)
    {
        foreach (var field in Fields)
            if (UsesStableKeys(field) && values.ContainsKey(field.Name))
                foreach (int unit in PrepareKeyedValuesWork(world, field)) yield return unit;
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
        int element = id;
        id = scope.StateIndex(world, id);
        if (UsesStableKeys(f)) {
            if (keyedValues.TryGetValue(name, out var keyed)) return keyed.GetValueOrDefault(id, f.Initial);
            int oldIndex = f.Scope == "truck" ? element : id;
            return values.TryGetValue(name, out var legacy) && (uint)oldIndex < (uint)legacy.Length ? legacy[oldIndex] : f.Initial;
        }
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
            RemovedStarters.ToList(), flowLedger.Capture(), RuleProject.CurrentVersion,
            keyedValues.Count == 0 ? null : keyedValues.ToDictionary(p => p.Key,
                p => p.Value.OrderBy(v => v.Key).ToDictionary(v => v.Key, v => v.Value), StringComparer.Ordinal));

    internal void RestoreFlows(Dictionary<string, double> saved) => flowLedger.Restore(saved);

    internal void Restore(Dictionary<string, double[]> saved)
    {
        if (saved == null) return;
        foreach (var (name, v) in saved) {
            CheckpointGuard.Require(Fields.Exists(f => f.Name == name) && v != null && v.All(double.IsFinite), "rule field values");
            values[name] = (double[])v.Clone();
        }
    }

    internal void RestoreKeyed(Dictionary<string, Dictionary<int, double>> saved)
    {
        if (saved == null) return;
        foreach (var (name, state) in saved) {
            var field = Fields.Find(f => f.Name == name);
            CheckpointGuard.Require(field != null && UsesStableKeys(field) && !values.ContainsKey(name) && state != null
                && state.All(p => p.Key >= 0 && double.IsFinite(p.Value)), "keyed rule field values");
            keyedValues[name] = new(state);
        }
    }

    internal static List<WorldRule> Clone(List<WorldRule> rules) => JsonSerializer.Deserialize<List<WorldRule>>(JsonSerializer.Serialize(rules));
    internal static List<RuleFieldDefinition> CloneFields(List<RuleFieldDefinition> fields) => JsonSerializer.Deserialize<List<RuleFieldDefinition>>(JsonSerializer.Serialize(fields));
}
