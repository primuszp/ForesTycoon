using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ForesTycoon.Rules;

/// <summary>A field of an object kind on the map: <c>tree.health</c>.</summary>
public sealed record CausalNode(string Key, string Entity, RuleFieldSchema Field);

/// <summary>
/// One cause → effect link. <see cref="Sign"/> +1: they move together, −1: opposite, 0: depends (not a plain curve).
/// Native links come from [RuleCause] in the game code; rule-book links from the rule's formulas.
/// </summary>
public sealed record CausalEdge(string From, string To, int Sign, string Rule, string RuleName, bool Native, int RuleIndex, string Help);

/// <summary>A feedback loop: the product of its signs makes it reinforcing (+1) or balancing (−1); 0 when a link is unknown.</summary>
public sealed record CausalLoop(CausalEdge[] Edges, int Polarity);

/// <summary>What influences what in the world: the object model's fields as nodes, rules as signed links.</summary>
public sealed class RuleCausalMap
{
    public IReadOnlyList<CausalNode> Nodes { get; }
    public IReadOnlyList<CausalEdge> Edges { get; }
    public IReadOnlyList<CausalLoop> Loops { get; }

    private RuleCausalMap(List<CausalNode> nodes, List<CausalEdge> edges)
    {
        Nodes = nodes; Edges = edges; Loops = FindLoops(nodes, edges);
    }

    /// <param name="extra">Fields to show even without links ("tree.age"), so the designer can draw the first one.</param>
    public static RuleCausalMap Build(RuleSchema schema, IReadOnlyList<WorldRule> rules, IEnumerable<string> extra = null)
    {
        var nodes = new Dictionary<string, CausalNode>(StringComparer.Ordinal);
        var edges = new List<CausalEdge>();
        CausalNode Node(string entity, string field)
        {
            var e = schema.Find(entity); var f = e?.Fields.Concat(e.Summaries).FirstOrDefault(x => x.Name == field);
            if (f == null) return null;
            string key = entity + "." + field;
            if (!nodes.TryGetValue(key, out var n)) nodes[key] = n = new(key, entity, f);
            return n;
        }
        CausalNode Resolve(string key)
        {
            int dot = key.IndexOf('.');
            return dot < 0 ? null : Node(key[..dot], key[(dot + 1)..]);
        }

        // Native causes, declared on the interfaces next to the fields they compute.
        foreach (var e in schema.Entities)
            foreach (var f in e.Fields.Where(f => f.DeclaredBy == e.Id))
                foreach (var c in f.Causes) {
                    // A cause declared on an abstract kind lands on the concrete kind the cause names (IPlant.Health ← tree.light → tree.health).
                    var from = Resolve(c.From); if (from == null) continue;
                    string owner = e.Abstract && schema.Lineage(from.Entity).Any(x => x.Id == e.Id) ? from.Entity : e.Id;
                    var to = Node(owner, f.Name); if (to == null) continue;
                    edges.Add(new(from.Key, to.Key, Math.Sign(c.Sign), c.Rule, c.Rule, true, -1, c.Help));
                }

        // Rule-book rules: every field a formula mentions influences the effect's target.
        for (int i = 0; i < rules.Count; i++) {
            var rule = rules[i]; var scope = schema.Find(rule.Scope);
            if (scope == null || scope.Abstract && scope.Type?.IsClass != true) continue;
            foreach (var effect in rule.EffectiveEffects().Where(x => x.Mode != "action")) {
                var target = Local(scope, effect.Target, schema);
                var to = target == null ? null : Resolve(target); if (to == null) continue;
                RuleCurve.TryParse(effect.Formula, out var curve);
                foreach (string name in Mentioned(scope, rule.Condition + " " + string.Join(" ", (rule.ConditionGraph?.Nodes ?? new()).Concat(effect.Computation?.Nodes ?? new()).Select(n => n.Key)) + " " + (effect.Influence?.Compile() ?? effect.Formula), schema)) {
                    var from = Resolve(name);
                    if (from == null || from.Key == to.Key && effect.Mode == "set") continue;
                    int sign = curve != null && Local(scope, curve.Input, schema) == name && !(curve.Tail ?? "").Contains(curve.Input) ? curve.Sign : 0;
                    edges.Add(new(from.Key, to.Key, sign, rule.Id, rule.Name, false, i, rule.Enabled ? "" : "kikapcsolva"));
                }
            }
        }
        foreach (string key in extra ?? Enumerable.Empty<string>()) Resolve(key);
        return new(nodes.Values.OrderBy(n => n.Entity, StringComparer.Ordinal).ToList(), edges);
    }

    // Names in formulas are scope fields (“health”) or relation fields (“tile.waterSupply”); both become entity.field keys.
    private static readonly Regex Identifier = new(@"[\p{L}_][\p{L}\p{N}_.]*", RegexOptions.Compiled);
    private static IEnumerable<string> Mentioned(RuleEntitySchema scope, string text, RuleSchema schema) =>
        Identifier.Matches(text ?? "").Select(m => Local(scope, m.Value.TrimEnd('.'), schema)).Where(k => k != null).Distinct();

    /// <summary>How a rule on <paramref name="scope"/> names the field <paramref name="key"/>: its own field, or through a relation. Null: unreachable.</summary>
    public static string NameIn(RuleSchema schema, string scope, string key)
    {
        var e = schema.Find(scope); int dot = key.IndexOf('.');
        if (e == null || dot < 0) return null;
        string entity = key[..dot], field = key[(dot + 1)..];
        if (schema.Lineage(scope).Any(x => x.Id == entity) && e.Fields.Concat(e.Summaries).Any(f => f.Name == field)) return field;
        if (entity == "world" && schema.Find("world") is { } world && world.Fields.Where(f => f.DeclaredBy == "world").Concat(world.Summaries).Any(f => f.Name == field)) return key;
        var relation = e.Relations.FirstOrDefault(r => r.Target == entity);
        return relation != null && schema.Find(entity).Fields.Concat(schema.Find(entity).Summaries).Any(f => f.Name == field) ? relation.Name + "." + field : null;
    }

    private static string Local(RuleEntitySchema scope, string name, RuleSchema schema)
    {
        if (scope.Fields.Concat(scope.Summaries).Any(f => f.Name == name)) return scope.Id + "." + name;
        if (name.StartsWith("world.", StringComparison.Ordinal) && schema.Find("world") is { } world &&
            world.Fields.Where(f => f.DeclaredBy == "world").Concat(world.Summaries).Any(f => f.Name == name[6..])) return name;
        foreach (var r in scope.Relations)
            if (name.StartsWith(r.Name + ".", StringComparison.Ordinal) && schema.Find(r.Target) is { } target && target.Fields.Concat(target.Summaries).Any(f => f.Name == name[(r.Name.Length + 1)..]))
                return r.Target + "." + name[(r.Name.Length + 1)..];
        return null;
    }

    /// <summary>Simple cycles, each found once from its smallest node; capped, the map is small.</summary>
    private static CausalLoop[] FindLoops(List<CausalNode> nodes, List<CausalEdge> edges)
    {
        var index = nodes.Select((n, i) => (n.Key, i)).ToDictionary(p => p.Key, p => p.i);
        var outgoing = edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.ToArray());
        var loops = new List<CausalLoop>(); var seen = new HashSet<string>();
        var path = new List<CausalEdge>(); var onPath = new HashSet<string>();
        void Walk(string start, string at)
        {
            if (loops.Count >= 64 || !outgoing.TryGetValue(at, out var next)) return;
            foreach (var e in next) {
                if (index[e.To] < index[start]) continue;
                if (e.To == start) {
                    var cycle = path.Append(e).ToArray();
                    // Parallel links (two rules between the same fields) make the same loop; keep one per field sequence.
                    if (seen.Add(string.Join(">", cycle.Select(c => c.From))))
                        loops.Add(new(cycle, cycle.Any(c => c.Sign == 0) ? 0 : cycle.Aggregate(1, (p, c) => p * c.Sign)));
                } else if (onPath.Add(e.To)) { path.Add(e); Walk(start, e.To); path.RemoveAt(path.Count - 1); onPath.Remove(e.To); }
            }
        }
        foreach (var n in nodes) { onPath.Add(n.Key); Walk(n.Key, n.Key); onPath.Remove(n.Key); }
        return loops.OrderBy(l => l.Edges.Length).ToArray();
    }
}
