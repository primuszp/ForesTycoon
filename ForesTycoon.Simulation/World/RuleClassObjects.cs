using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ForesTycoon.Rules;

namespace ForesTycoon;

partial class SimulationWorld
{
    private IEnumerable<int> CreatedObjectsWork(string family, int[] ids) {
        return ObjectRuleWork(0, "birth", family, ids);
    }
    private IEnumerable<int> PopulationRuleWork(double seconds) {
        return ObjectRuleWork(seconds, "population");
    }
    private IEnumerable<int> WildlifeRuleWork(double seconds) {
        return ObjectRuleWork(seconds, "wildlife");
    }
    private IEnumerable<int> ObjectRuleWork(double seconds, string schedule, string family = null, int[] ids = null) {
        ruleEngine.Begin(this, seconds, forest.ForestYear, schedule, eventScope: family, eventElements: ids);
        foreach (int unit in ruleEngine.ContinueWork(this)) yield return unit;
        bool networkChanged = ruleEngine.NetworkChanged;
        foreach (int unit in forest.CompleteRuleEditsWork(forest.ForestYear)) yield return unit;
        if (networkChanged || ruleEngine.NetworkChanged) {
            Logistics?.TrailsChanged();
            foreach (int unit in vehicles.RemoveInvalidRoutesWork(map.IsNetworkTile, map.AreNetworkNeighbours)) yield return unit;
        }
    }

    internal static Dictionary<string, double> CaptureObjectFields(object value, RuleSchema schema = null)
    {
        var entity = (schema ?? RuleObjects).Entities.Single(e => e.Type == value.GetType());
        return entity.Fields.Where(f => f.Writable).ToDictionary(f => f.Name, f => Getter(f.Property)(value), StringComparer.Ordinal);
    }
    internal static void RestoreObjectFields(object value, IReadOnlyDictionary<string, double> fields, RuleSchema schema = null)
    {
        if (fields == null) return;
        var entity = (schema ?? RuleObjects).Entities.Single(e => e.Type == value.GetType());
        foreach (var entry in fields) {
            var field = entity.Fields.SingleOrDefault(f => f.Name == entry.Key && f.Writable);
            if (field == null || !double.IsFinite(entry.Value) || entry.Value < field.Min || entry.Value > field.Max
                || field.Kind != RuleFieldKind.Number && !field.Options.Any(o => o.Value == entry.Value))
                throw new System.IO.InvalidDataException(entity.Name + ": hibás vagy már nem létező mentett mező: " + entry.Key);
            Setter(field.Property)(value, entry.Value);
        }
    }
    [RuleObjectSource]
    internal IEnumerable<WildlifeSystem.Animal> RuleAnimals {
        get { foreach (var animal in wildlife.Animals) { animal.World = this; animal.System = wildlife; if (!animal.Removed) yield return animal; } }
    }
    [RuleObjectSource]
    internal IEnumerable<WildlifeSystem.ForageCell> RuleForage => wildlife.ForageObjects;
    [RuleObjectSource]
    internal IEnumerable<WildlifeSystem.AnimalType> RuleAnimalTypes => wildlife.AnimalTypes;

    internal ITile RuleTile(int id) { var tile = new TileView(); tile.Attach(this); tile.Bind(this, id); return tile; }

    private sealed class ClassObjects(Type type, PropertyInfo[] sources)
    {
        internal readonly Dictionary<int, object> Objects = new();
        internal int[] Elements = Array.Empty<int>();
        internal int Size;
        private readonly SortedSet<int> ordered = new();
        private readonly HashSet<int> seen = new();
        private readonly List<int> removed = new();
        private bool membershipChanged;
        internal void Refresh(SimulationWorld world)
        {
            foreach (int _ in RefreshWork(world)) { }
        }
        internal IEnumerable<int> RefreshWork(SimulationWorld world)
        {
            seen.Clear(); removed.Clear(); Size = 0;
            // Keep dirtiness across interrupted refreshes until the element snapshot is published.
            foreach (var source in sources)
                foreach (var item in (IEnumerable)source.GetValue(world)) {
                    yield return 1;
                    if (!type.IsInstanceOfType(item)) continue;
                    if (item is not IRuleInstance identity || identity.RuleId < 0 || identity.RuleId >= 1_000_000)
                        throw new InvalidOperationException(type.Name + ": a szabályobjektumnak stabil, 0–999999 azonosító szükséges.");
                    if (!seen.Add(identity.RuleId)) throw new InvalidOperationException(type.Name + ": ismétlődő objektumazonosító: " + identity.RuleId);
                    Objects[identity.RuleId] = item;
                    membershipChanged |= ordered.Add(identity.RuleId);
                    Size = Math.Max(Size, identity.RuleId + 1);
                }
            foreach (int id in ordered) {
                if (!seen.Contains(id)) removed.Add(id);
                yield return 1;
            }
            foreach (int id in removed) { Objects.Remove(id); ordered.Remove(id); membershipChanged = true; yield return 1; }
            if (membershipChanged) {
                var elements = new int[ordered.Count]; int index = 0;
                foreach (int id in ordered) { elements[index++] = id; yield return 1; }
                Elements = elements;
                membershipChanged = false;
            }
        }
    }
    private readonly Dictionary<Type, ClassObjects> classRuleObjects = new();

    private static RuleScope ClassScope(RuleSchema schema, RuleEntitySchema entity)
    {
        var sources = typeof(SimulationWorld).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<RuleObjectSourceAttribute>() != null &&
                p.PropertyType.GetInterfaces().Append(p.PropertyType).Any(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                    && t.GetGenericArguments()[0].IsAssignableFrom(entity.Type))).ToArray();
        if (sources.Length == 0) throw new InvalidOperationException(entity.Name + ": nincs a típust szolgáltató [RuleObjectSource] gyűjtemény.");
        ClassObjects Bound(SimulationWorld w) {
            if (!w.classRuleObjects.TryGetValue(entity.Type, out var binding)) w.classRuleObjects.Add(entity.Type, binding = new(entity.Type, sources));
            return binding;
        }
        object Target(SimulationWorld w, int id) {
            var binding = Bound(w);
            if (!binding.Objects.TryGetValue(id, out var target)) { binding.Refresh(w); target = binding.Objects.GetValueOrDefault(id); }
            return target ?? throw new InvalidOperationException(entity.Name + ": a szabályobjektum nem található: " + id);
        }
        var fields = new List<RuleScopeField>();
        foreach (var f in entity.Fields) {
            var read = Getter(f.Property); var write = f.Writable ? Setter(f.Property) : null;
            fields.Add(new(f.Name, f.Display + ": " + Help(f), (w, id) => read(Target(w, id)), write == null ? null
                : (w, id, value) => write(Target(w, id), Math.Clamp(value, f.Min, f.Max))) { Min = f.Min, Max = f.Max });
        }
        foreach (var summary in entity.Summaries) {
            var read = Summary(summary); fields.Add(new(summary.Name, summary.Display, (w, id) => read(Target(w, id))));
        }
        foreach (var relation in entity.Relations) {
            var follow = ObjectGetter(relation.Property); var related = schema.Find(relation.Target);
            foreach (var field in related.Fields) {
                var read = Getter(field.Property); var write = field.Writable ? Setter(field.Property) : null;
                fields.Add(new(relation.Name + "." + field.Name, relation.Display + " › " + field.Display,
                    (w, id) => follow(Target(w, id)) is { } target ? read(target) : 0,
                    write == null ? null : (w, id, value) => { if (follow(Target(w, id)) is { } target) write(target, Math.Clamp(value, field.Min, field.Max)); }) {
                    Min = field.Min, Max = field.Max, Available = (w, id) => follow(Target(w, id)) != null });
            }
            foreach (var summary in related.Summaries) {
                var read = Summary(summary); fields.Add(new(relation.Name + "." + summary.Name, relation.Display + " › " + summary.Display,
                    (w, id) => follow(Target(w, id)) is { } target ? read(target) : 0));
            }
        }
        var actions = entity.Actions.Select(a => new RuleScopeAction(a.Name, a.Help, (w, id) => (bool)a.Method.Invoke(Target(w, id), null))).ToArray();
        var collections = entity.Collections.Where(c => c.Addressable).Select(c => new RuleScopeCollection(c.Name, c.Target,
            (w, id) => ((IEnumerable)c.Property.GetValue(Target(w, id))).Cast<object>().Select(item => item switch {
                IRuleInstance instance => instance.RuleId,
                IWorldObject view => checked((int)view.Index),
                _ => throw new InvalidOperationException(c.Display + ": a kapcsolat céljának nincs szabályazonosítója.")
            }).ToArray())).ToArray();
        return new(entity.Id, entity.Name, entity.Element, w => { var b = Bound(w); b.Refresh(w); return b.Elements; },
            w => { var b = Bound(w); b.Refresh(w); return b.Size; }, fields.ToArray(), actions) {
                PrepareElements = w => Bound(w).RefreshWork(w), PreparedElements = w => Bound(w).Elements,
                Entity = entity.Id, ObjectType = entity.Type, Collections = collections,
                Label = (w, id) => Target(w, id) is IRuleDisplay display ? display.RuleLabel : entity.Name + " #" + id };
    }
}
