using System.Collections.Generic;
using System.Text.Json;

namespace ForesTycoon.Rules;

public sealed record RuleWorldObject(string Kind, ulong Id, string Label);
public sealed class RuleWorldCatalog
{
    public string AlgorithmRevision { get; set; } = "";
    public string ProjectRevision { get; set; } = "";
    public string Schema { get; set; } = "forest-rule-world";
    public int Version { get; set; } = 1;
    public string Source { get; set; } = "";
    public ulong Tick { get; set; }
    public int Seed { get; set; }
    public string PublishedUtc { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string Error { get; set; }
    public List<RuleWorldObject> Objects { get; set; } = new();
    public RuleObjectModel ObjectModel { get; set; }
    public List<BehaviorHook> Hooks { get; set; } = new();
    public Dictionary<string, double> Fields { get; set; } = new();
    public Dictionary<string, double> Stocks { get; set; } = new();
    public string ToJson() => JsonSerializer.Serialize(this, RuleProject.JsonOptions);
    public static RuleWorldCatalog FromJson(string json) => JsonSerializer.Deserialize<RuleWorldCatalog>(json, RuleProject.JsonOptions);
}
