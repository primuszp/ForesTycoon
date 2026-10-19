using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ForesTycoon.Rules;

// Reflection objects never cross the game/editor boundary. The revision changes with the discovered public contract.
public sealed record RuleObjectModel(string Revision, RuleObjectType[] Types)
{
    public static RuleObjectModel Describe(RuleSchema schema)
    {
        double? Bound(double value) => double.IsFinite(value) ? value : null;
        var types = schema.Entities.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => new RuleObjectType(
            e.Id, e.Name, e.Element, e.Help, e.Abstract, e.Parents,
            e.Fields.Concat(e.Summaries).Select(f => new RuleObjectField(f.Name, f.Display, f.Help, f.Unit, f.Kind,
                Bound(f.Min), Bound(f.Max), f.Writable, f.DeclaredBy, f.Options)).ToArray(),
            e.Relations.Select(r => new RuleObjectLink(r.Name, r.Display, r.Help, r.Target, false)).ToArray(),
            e.Collections.Select(c => new RuleObjectLink(c.Name, c.Display, c.Help, c.Target, c.Addressable)).ToArray(),
            e.Actions.Select(a => new RuleObjectAction(a.Name, a.Display, a.Help, a.DeclaredBy)).ToArray())).ToArray();
        string revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(types, RuleProject.JsonOptions)))).ToLowerInvariant();
        return new(revision, types);
    }
}
public sealed record RuleObjectType(string Id, string Display, string Element, string Help, bool Abstract, string[] Parents,
    RuleObjectField[] Fields, RuleObjectLink[] Relations, RuleObjectLink[] Collections, RuleObjectAction[] Actions);
public sealed record RuleObjectField(string Name, string Display, string Help, string Unit, RuleFieldKind Kind,
    double? Min, double? Max, bool Writable, string DeclaredBy, RuleFieldOption[] Options);
public sealed record RuleObjectLink(string Name, string Display, string Help, string Target, bool Addressable);
public sealed record RuleObjectAction(string Name, string Display, string Help, string DeclaredBy);
