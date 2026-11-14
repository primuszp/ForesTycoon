using System;
using System.Collections.Generic;
using System.Linq;
using ForesTycoon.Rules;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;
namespace ForesTycoon.Editor;
/// <summary>Module positions and automatic rule placement, rebuilt only on catalog load or explicit layout.</summary>
internal sealed class RuleCatalogLayout
{
    internal sealed record Domain(string Name, Vec4 Colour, string[] Modules);

    // The same subsystems as the workshop and the system model; a module not listed there falls into "Egyéb".
    private static readonly Domain[] Domains = RuleWorkshopModel.Topics
        .Select((t, i) => new Domain(t.Name, EditorStyle.TopicColour(i), t.Modules)).ToArray();
    private static readonly Domain Other = new("Egyéb", new Vec4(0.6f, 0.6f, 0.6f, 1), Array.Empty<string>());

    private GameRuleIndex index;
    internal List<(Domain Domain, string[] Modules)> Groups { get; private set; } = new();
    internal Dictionary<string, Vec2> Modules { get; } = new(StringComparer.Ordinal);
    internal List<(Domain Domain, Vec2 Origin)> DomainsAt { get; } = new();
    internal Dictionary<(string, string), int> Links { get; private set; } = new();
    internal const float ModuleWidth = 210, ModuleHeight = 78, ModuleColumn = 260, ModuleRow = 98;
    private const int DomainsPerRow = 3;
    internal const float RuleWidth = 220, RuleHeight = 92, GhostWidth = 180, GhostHeight = 54, GhostGap = 80;
    internal void Load(GameRuleIndex index)
    {
        this.index = index;
        Groups = Domains.Select(d => (d, d.Modules.Where(m => index.Modules.Contains(m)).ToArray()))
            .Append((Other, index.Modules.Where(m => !Domains.Any(d => d.Modules.Contains(m))).ToArray()))
            .Where(g => g.Item2.Length > 0).ToList();
        // Domains in a grid of three columns, each a heading above its systems.
        Modules.Clear(); DomainsAt.Clear();
        float top = 0;
        for (int first = 0; first < Groups.Count; first += DomainsPerRow)
        {
            int tallest = 0;
            for (int column = first; column < Math.Min(Groups.Count, first + DomainsPerRow); column++)
            {
                var origin = new Vec2((column - first) * ModuleColumn, top);
                DomainsAt.Add((Groups[column].Domain, origin));
                for (int row = 0; row < Groups[column].Modules.Length; row++)
                    Modules[Groups[column].Modules[row]] = origin + new Vec2(0, 34 + row * ModuleRow);
                tallest = Math.Max(tallest, Groups[column].Modules.Length);
            }
            top += 34 + tallest * ModuleRow + 40;
        }
        Links = index.Connections.Select(c => (index.Find(c.From).Module, index.Find(c.To).Module))
            .Where(p => p.Item1 != p.Item2).GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());
        // The code-generated catalog has a plain grid; lay such systems out by their data flow instead.
        foreach (string m in index.Modules)
        {
            var rules = index.InModule(m);
            if (rules.Select((r, i) => r.X == 25 + i % 3 * 245 && r.Y == 30 + i / 3 * 130).All(x => x)) AutoLayout(m);
        }
    }
    /// <summary>Columns by data flow inside the system (producers left, at most two columns), stacked and centred.</summary>
    internal void AutoLayout(string moduleName)
    {
        var rules = index.InModule(moduleName);
        var ids = new HashSet<string>(rules.Select(r => r.Id));
        var inner = index.Connections.Where(c => c.From != c.To && ids.Contains(c.From) && ids.Contains(c.To)).ToArray();
        var depth = rules.ToDictionary(r => r.Id, _ => 0);
        for (int pass = 0; pass < rules.Count; pass++)
            foreach (var c in inner) depth[c.To] = Math.Min(rules.Count, Math.Max(depth[c.To], depth[c.From] + 1));
        // A rule read only by later ones goes left; cycles (feedback) collapse into two columns.
        var columns = rules.GroupBy(r => Math.Min(1, depth[r.Id])).OrderBy(g => g.Key).Select(g => g.ToArray()).ToArray();
        int tallest = columns.Max(col => col.Length);
        for (int x = 0; x < columns.Length; x++)
            for (int y = 0; y < columns[x].Length; y++)
            {
                columns[x][y].X = x * (RuleWidth + 70);
                columns[x][y].Y = (tallest - columns[x].Length) * (RuleHeight + 18) / 2f + y * (RuleHeight + 18);
            }
    }

    internal Domain DomainOf(string moduleName) => Groups.FirstOrDefault(g => g.Modules.Contains(moduleName)).Domain ?? Other;

}
