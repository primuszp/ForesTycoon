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

/// <summary>A number of the object. Writable when the interface property has a setter.</summary>
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
}

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

/// <summary>Something a rule may do to the object; a parameterless method returning whether the world's structure changed.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RuleActionAttribute : Attribute
{
    public RuleActionAttribute(string help) { Help = help; }
    public string Name { get; set; }
    public string Display { get; set; }
    public string Help { get; }
}

public sealed record RuleFieldSchema(string Name, string Display, string Help, string Unit, double Min, double Max, bool Writable,
    string DeclaredBy, PropertyInfo Property)
{
    public RuleCauseAttribute[] Causes => Property.GetCustomAttributes<RuleCauseAttribute>().ToArray();
}
public sealed record RuleRelationSchema(string Name, string Display, string Help, string Target, PropertyInfo Property);
public sealed record RuleActionSchema(string Name, string Display, string Help, string DeclaredBy, MethodInfo Method);

/// <summary>One discovered kind of object: its place in the hierarchy and everything a rule can read, write and do.</summary>
public sealed class RuleEntitySchema
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
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

    public static RuleSchema Discover(IEnumerable<Type> types)
    {
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
            foreach (var level in lineage) {
                foreach (var p in level.GetProperties()) {
                    if (p.GetCustomAttribute<RuleFieldAttribute>() is { } f)
                        fields.Add(new(f.Name ?? Camel(p.Name), f.Display ?? Words(p.Name), f.Help, f.Unit, f.Min, f.Max, p.CanWrite, ids[level], p));
                    if (p.GetCustomAttribute<RuleRelationAttribute>() is { } r && ids.TryGetValue(p.PropertyType, out string target))
                        relations.Add(new(r.Name ?? Camel(p.Name), r.Display ?? Words(p.Name), r.Help, target, p));
                }
                foreach (var m in level.GetMethods())
                    if (m.GetCustomAttribute<RuleActionAttribute>() is { } act && m.GetParameters().Length == 0 && m.ReturnType == typeof(bool))
                        actions.Add(new(act.Name ?? Camel(m.Name), act.Display ?? Words(m.Name), act.Help, ids[level], m));
            }
            list.Add(new() { Id = a.Id, Name = a.Display ?? Words(type.Name.StartsWith("I") ? type.Name[1..] : type.Name), Element = a.Element, Help = a.Help, Abstract = a.Abstract, Parents = parents,
                Type = type, Fields = fields.ToArray(), Relations = relations.ToArray(), Actions = actions.ToArray() });
        }
        return new(list);
    }

    /// <summary>WaterSupply → waterSupply: the code name, as written in formulas.</summary>
    public static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
    /// <summary>WaterSupply → “Water supply”: the code name, as shown to the designer.</summary>
    public static string Words(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));

    private static int Depth(Type t) => t.GetInterfaces().Count(i => i.GetCustomAttribute<RuleEntityAttribute>() != null);
}
