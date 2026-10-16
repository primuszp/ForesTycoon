using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ForesTycoon.Rules;

// ── The contract between the game code and the editor ───────────────────
// The game marks its rule-addressable interfaces with these attributes. The editor discovers them by reflection, so
// a new kind of object (or a new field on one) appears in the rule editor without editor code. An interface inherits
// the fields of the interfaces it extends: IForwarder : ITruck : IVehicle gets every vehicle and truck field.

/// <summary>A kind of world object rules can address. Abstract kinds group fields; concrete kinds are rule scopes.</summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class RuleEntityAttribute : Attribute
{
    public RuleEntityAttribute(string id) { Id = id; }
    /// <summary>Stable identifier, the rule's <c>scope</c>.</summary>
    public string Id { get; }
    /// <summary>Shown in the editor; default: the interface name without its I (ITree → “Tree”).</summary>
    public string Display { get; set; }
    /// <summary>What one element is called in a sentence (“fa”, “csempe”).</summary>
    public string Element { get; set; } = "";
    public string Help { get; set; } = "";
    /// <summary>Only groups fields for its descendants; no rule runs on it directly.</summary>
    public bool Abstract { get; set; }
}

/// <summary>
/// A value of the object: a number, a bool (0/1 in formulas) or an enum (its numeric value in formulas, a dropdown in
/// the editor). Writable when the interface property has a setter.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RuleFieldAttribute : Attribute
{
    public RuleFieldAttribute(string help) { Help = help; }
    /// <summary>The name in formulas; default: the property name in camelCase (WaterSupply → waterSupply).</summary>
    public string Name { get; set; }
    /// <summary>Shown in the editor; default: the property name in words (WaterSupply → “Water supply”).</summary>
    public string Display { get; set; }
    public string Help { get; }
    public string Unit { get; set; } = "";
    public double Min { get; set; } = double.NegativeInfinity;
    public double Max { get; set; } = double.PositiveInfinity;
    /// <summary>
    /// Non-zero when an add effect on the field is the pace of a process, not a plain change: +1 when the process raises
    /// the field (decay progress), −1 when it lowers it (ruts smoothing out). The editor then says “speeds up” / “slows down”.
    /// </summary>
    public int Pace { get; set; }
}

/// <summary>How a field's value is read: a number, a yes/no flag or one of named options.</summary>
public enum RuleFieldKind { Number, Bool, Enum }

/// <summary>One value of a bool or enum field: its code name, display and the number formulas see.</summary>
public sealed record RuleFieldOption(string Name, string Display, double Value);

/// <summary>
/// A cause of the field that native code computes: <paramref name="from"/> (“entity.field”) pushes this field up (+1) or
/// down (−1) in rule <paramref name="rule"/>. Rule-book rules need none; the map reads their formulas.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class RuleCauseAttribute : Attribute
{
    public RuleCauseAttribute(string from, int sign, string rule) { From = from; Sign = sign; Rule = rule; }
    public string From { get; }
    public int Sign { get; }
    public string Rule { get; }
    public string Help { get; set; } = "";
}

/// <summary>Another object this one is attached to; its fields are readable as <c>name.field</c> (“csempe.vízellátottság”).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RuleRelationAttribute : Attribute
{
    public RuleRelationAttribute(string help) { Help = help; }
    public string Name { get; set; }
    public string Display { get; set; }
    public string Help { get; }
}

/// <summary>
/// Other objects this one holds or touches, many at a time (the trees on a tile, the tile's neighbours): an
/// <c>IEnumerable</c> of a marked interface. Rules read them summarised: <c>name.count</c> and, for every number of the
/// target, <c>name.avg.field</c>, <c>name.min.field</c>, <c>name.max.field</c> and <c>name.sum.field</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RuleCollectionAttribute : Attribute
{
    public RuleCollectionAttribute(string help) { Help = help; }
    public string Name { get; set; }
    public string Display { get; set; }
    public string Help { get; }
    /// <summary>The members are elements of the target kind itself (a tile's neighbours are tiles), so a flow can write into them.</summary>
    public bool Addressable { get; set; }
}

/// <summary>Something a rule may do to the object; a parameterless method returning whether the world's structure changed.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RuleActionAttribute : Attribute
{
    public RuleActionAttribute(string help) { Help = help; }
    public string Name { get; set; }
    public string Display { get; set; }
    public string Help { get; }
}

/// <summary>
/// Designer-facing text for one schema item, keyed by <c>entity</c>, <c>entity.field</c>, <c>entity.relation</c> or
/// <c>entity.action</c> (the entity that declares it). The code attributes stay English; the editor shows these.
/// </summary>
public sealed record RuleText(string Name, string Help = null)
{
    /// <summary>Overrides the unit (otherwise it is translated by <see cref="RuleTexts.Units"/>).</summary>
    public string Unit { get; init; }
    /// <summary>For an entity: what one element is called in a sentence (“út”).</summary>
    public string Element { get; init; }
    /// <summary>For a 0/1 field: the sentence when it is 1 (“az út makadám”) and when it is 0 (“az út aszfalt”).</summary>
    public string True { get; init; }
    public string False { get; init; }
}

/// <summary>A localization table for the discovered object model.</summary>
public sealed class RuleTexts
{
    public Dictionary<string, RuleText> Items { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Units { get; init; } = new(StringComparer.Ordinal);
    public RuleText Find(string key) => key != null && Items.TryGetValue(key, out var t) ? t : null;
    public string Unit(string unit, RuleText text) => text?.Unit ?? (Units.TryGetValue(unit ?? "", out string u) ? u : unit);
}

public sealed record RuleFieldSchema(string Name, string Display, string Help, string Unit, double Min, double Max, bool Writable,
    string DeclaredBy, PropertyInfo Property)
{
    public RuleCauseAttribute[] Causes => Property.GetCustomAttributes<RuleCauseAttribute>().ToArray();
    /// <summary>The code name as written by the game (“Rut depth”), kept as a secondary hint.</summary>
    public string CodeDisplay { get; init; } = Display;
    /// <summary>Sentences for a 0/1 field; null for other fields.</summary>
    public string True { get; init; }
    public string False { get; init; }
    public RuleFieldKind Kind { get; init; }
    /// <summary>The values of a bool or enum field, in value order; empty for a number.</summary>
    public RuleFieldOption[] Options { get; init; } = Array.Empty<RuleFieldOption>();
    /// <inheritdoc cref="RuleFieldAttribute.Pace"/>
    public int Pace { get; init; }
    /// <summary>For a summary of a collection: the collection, and the summary (count, avg, min, max, sum); null otherwise.</summary>
    public RuleCollectionSchema Collection { get; init; }
    public string Aggregate { get; init; }
}
public sealed record RuleCollectionSchema(string Name, string Display, string Help, string Target, PropertyInfo Property)
{
    /// <inheritdoc cref="RuleCollectionAttribute.Addressable"/>
    public bool Addressable { get; init; }
}
public sealed record RuleRelationSchema(string Name, string Display, string Help, string Target, PropertyInfo Property);
public sealed record RuleActionSchema(string Name, string Display, string Help, string DeclaredBy, MethodInfo Method);

/// <summary>One discovered kind of object: its place in the hierarchy and everything a rule can read, write and do.</summary>
public sealed class RuleEntitySchema
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>The name in the game code (“Road”), a secondary hint next to <see cref="Name"/>.</summary>
    public string CodeName { get; init; } = "";
    public string Element { get; init; } = "";
    public string Help { get; init; } = "";
    public bool Abstract { get; init; }
    /// <summary>The nearest marked base interfaces; usually one.</summary>
    public string[] Parents { get; init; } = Array.Empty<string>();
    public Type Type { get; init; }
    /// <summary>Own and inherited fields, nearest ancestor last.</summary>
    public RuleFieldSchema[] Fields { get; init; } = Array.Empty<RuleFieldSchema>();
    public RuleRelationSchema[] Relations { get; init; } = Array.Empty<RuleRelationSchema>();
    public RuleActionSchema[] Actions { get; init; } = Array.Empty<RuleActionSchema>();
    public RuleCollectionSchema[] Collections { get; init; } = Array.Empty<RuleCollectionSchema>();
    /// <summary>The read-only summaries of <see cref="Collections"/> (“trees.avg.height”), shown and read like fields.</summary>
    public RuleFieldSchema[] Summaries { get; internal set; } = Array.Empty<RuleFieldSchema>();
}

/// <summary>The object model the game exposes to rules, discovered from <see cref="RuleEntityAttribute"/> interfaces.</summary>
public sealed class RuleSchema
{
    private readonly Dictionary<string, RuleEntitySchema> byId = new(StringComparer.Ordinal);
    public IReadOnlyList<RuleEntitySchema> Entities { get; }
    public RuleEntitySchema Find(string id) => id != null && byId.TryGetValue(id, out var e) ? e : null;
    public IEnumerable<RuleEntitySchema> Children(string id) => Entities.Where(e => e.Parents.Contains(id));
    public IEnumerable<RuleEntitySchema> Roots => Entities.Where(e => e.Parents.Length == 0);

    /// <summary>The chain from the root to the entity (“Világobjektum › Növény › Fa”).</summary>
    public IReadOnlyList<RuleEntitySchema> Lineage(string id)
    {
        var chain = new List<RuleEntitySchema>();
        for (var e = Find(id); e != null; e = e.Parents.Length > 0 ? Find(e.Parents[0]) : null) chain.Insert(0, e);
        return chain;
    }

    private RuleSchema(List<RuleEntitySchema> entities)
    {
        Entities = entities;
        foreach (var e in entities)
            if (!byId.TryAdd(e.Id, e)) throw new InvalidOperationException("Ismétlődő szabály-objektum: " + e.Id);
    }

    public static RuleSchema Discover(params Assembly[] assemblies) =>
        Discover(assemblies.SelectMany(a => a.GetTypes()));

    /// <summary>Discovers the object model and shows it with <paramref name="texts"/> where it has an entry.</summary>
    public static RuleSchema Discover(RuleTexts texts, params Assembly[] assemblies) =>
        Discover(assemblies.SelectMany(a => a.GetTypes()), texts);

    public static RuleSchema Discover(IEnumerable<Type> types, RuleTexts texts = null)
    {
        texts ??= new();
        var marked = types.Where(t => t.IsInterface && t.GetCustomAttribute<RuleEntityAttribute>() != null)
            .OrderBy(t => Depth(t)).ThenBy(t => t.GetCustomAttribute<RuleEntityAttribute>().Id, StringComparer.Ordinal).ToList();
        var ids = marked.ToDictionary(t => t, t => t.GetCustomAttribute<RuleEntityAttribute>().Id);
        var list = new List<RuleEntitySchema>();
        foreach (var type in marked) {
            var a = type.GetCustomAttribute<RuleEntityAttribute>();
            // Ancestors first, so a child's own fields follow the inherited ones.
            var lineage = type.GetInterfaces().Where(ids.ContainsKey).OrderBy(Depth).Append(type).ToArray();
            var parents = type.GetInterfaces().Where(ids.ContainsKey)
                .Where(p => !type.GetInterfaces().Any(o => o != p && ids.ContainsKey(o) && p.IsAssignableFrom(o))).Select(p => ids[p]).ToArray();
            var fields = new List<RuleFieldSchema>(); var relations = new List<RuleRelationSchema>(); var actions = new List<RuleActionSchema>();
            var collections = new List<RuleCollectionSchema>();
            foreach (var level in lineage) {
                foreach (var p in level.GetProperties()) {
                    if (p.GetCustomAttribute<RuleFieldAttribute>() is { } f) {
                        string name = f.Name ?? Camel(p.Name), code = f.Display ?? Words(p.Name);
                        var t = texts.Find(ids[level] + "." + name);
                        var (kind, options) = Kind(p.PropertyType, ids[level] + "." + name, texts);
                        double min = options.Length > 0 ? options[0].Value : f.Min, max = options.Length > 0 ? options[^1].Value : f.Max;
                        fields.Add(new(name, t?.Name ?? code, t?.Help ?? f.Help, texts.Unit(f.Unit, t), min, max, p.CanWrite, ids[level], p) {
                            CodeDisplay = code, True = t?.True, False = t?.False, Kind = kind, Options = options, Pace = Math.Sign(f.Pace) });
                    }
                    if (p.GetCustomAttribute<RuleRelationAttribute>() is { } r && ids.TryGetValue(p.PropertyType, out string target)) {
                        string name = r.Name ?? Camel(p.Name); var t = texts.Find(ids[level] + "." + name);
                        relations.Add(new(name, t?.Name ?? r.Display ?? Words(p.Name), t?.Help ?? r.Help, target, p));
                    }
                    if (p.GetCustomAttribute<RuleCollectionAttribute>() is { } c && p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>) && ids.TryGetValue(p.PropertyType.GetGenericArguments()[0], out string of)) {
                        string name = c.Name ?? Camel(p.Name); var t = texts.Find(ids[level] + "." + name);
                        collections.Add(new(name, t?.Name ?? c.Display ?? Words(p.Name), t?.Help ?? c.Help, of, p) { Addressable = c.Addressable });
                    }
                }
                foreach (var m in level.GetMethods())
                    if (m.GetCustomAttribute<RuleActionAttribute>() is { } act && m.GetParameters().Length == 0 && m.ReturnType == typeof(bool)) {
                        string name = act.Name ?? Camel(m.Name); var t = texts.Find(ids[level] + "." + name);
                        actions.Add(new(name, t?.Name ?? act.Display ?? Words(m.Name), t?.Help ?? act.Help, ids[level], m));
                    }
            }
            var text = texts.Find(a.Id);
            string entityCode = a.Display ?? Words(type.Name.StartsWith("I") ? type.Name[1..] : type.Name);
            list.Add(new() { Id = a.Id, Name = text?.Name ?? entityCode, CodeName = entityCode, Element = text?.Element ?? a.Element, Help = text?.Help ?? a.Help, Abstract = a.Abstract, Parents = parents,
                Type = type, Fields = fields.ToArray(), Relations = relations.ToArray(), Actions = actions.ToArray(), Collections = collections.ToArray() });
        }
        var schema = new RuleSchema(list);
        foreach (var e in list) e.Summaries = e.Collections.SelectMany(c => Summaries(c, schema.Find(c.Target), texts)).ToArray();
        return schema;
    }

    /// <summary>The summaries a rule reads from a collection, in the designer's words when <paramref name="texts"/> has them.</summary>
    public static readonly (string Id, string Display)[] Aggregates = { ("avg", "average"), ("min", "smallest"), ("max", "largest"), ("sum", "total") };
    private static IEnumerable<RuleFieldSchema> Summaries(RuleCollectionSchema c, RuleEntitySchema target, RuleTexts texts)
    {
        string Word(string id, string fallback) => texts.Find("aggregate." + id)?.Name ?? fallback;
        yield return new(c.Name + ".count", $"{c.Display}: {Word("count", "count")}", $"{Word("count", "count")}: {c.Help}", "", 0, double.PositiveInfinity, false, target.Id, c.Property) {
            CodeDisplay = c.Name + " count", Collection = c, Aggregate = "count" };
        foreach (var f in target.Fields.Where(f => f.Kind != RuleFieldKind.Enum && f.Name != "index"))
            foreach (var (id, display) in Aggregates) {
                bool sum = id == "sum";
                yield return new(c.Name + "." + id + "." + f.Name, $"{c.Display}: {Word(id, display)} {f.Display.ToLowerInvariant()}",
                    $"{f.Help} ({Word(id, display)}, {c.Display.ToLowerInvariant()})", f.Kind == RuleFieldKind.Bool && !sum ? "0–1" : f.Unit,
                    sum ? Math.Min(0, f.Min) : f.Kind == RuleFieldKind.Bool ? 0 : f.Min, sum ? double.PositiveInfinity : f.Kind == RuleFieldKind.Bool ? 1 : f.Max,
                    false, target.Id, f.Property) { CodeDisplay = $"{c.Name} {id} {f.Name}", Collection = c, Aggregate = id };
            }
    }

    /// <summary>The kind of a field from its property type; option displays come from <c>key.option</c> texts when present.</summary>
    private static (RuleFieldKind, RuleFieldOption[]) Kind(Type type, string key, RuleTexts texts)
    {
        RuleFieldOption Option(string name, string display, double value) => new(name, texts.Find(key + "." + name)?.Name ?? display, value);
        if (type == typeof(bool)) return (RuleFieldKind.Bool, new[] { Option("false", "No", 0), Option("true", "Yes", 1) });
        if (!type.IsEnum) return (RuleFieldKind.Number, Array.Empty<RuleFieldOption>());
        return (RuleFieldKind.Enum, Enum.GetValues(type).Cast<object>()
            .Select(v => Option(Camel(Enum.GetName(type, v)), Words(Enum.GetName(type, v)), Convert.ToDouble(v))).OrderBy(o => o.Value).ToArray());
    }

    /// <summary>WaterSupply → waterSupply: the code name, as written in formulas.</summary>
    public static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
    /// <summary>WaterSupply → “Water supply”: the code name, as shown to the designer.</summary>
    public static string Words(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));

    private static int Depth(Type t) => t.GetInterfaces().Count(i => i.GetCustomAttribute<RuleEntityAttribute>() != null);
}
