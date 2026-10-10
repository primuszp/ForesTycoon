using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForesTycoon.Rules
{
    public enum GameRuleExecution { NativeCode, EditableGraph, Presentation, EditableController, EditableExpressions }
    public sealed record RuleSource(string File, string Symbol);
    /// <summary>A number of a rule. With a <see cref="Key"/> it is tunable within [Min, Max]; without one it is information only.</summary>
    public sealed record RuleParameter(string Name, double Value, string Unit, string Key = null, double Min = 0, double Max = 0, double Default = 0)
    {
        [JsonIgnore] public bool Tunable => !string.IsNullOrEmpty(Key);
        [JsonIgnore] public bool Changed => Tunable && Value != Default;
    }
    public sealed record RuleConnection(string From, string To, string Field);
    public sealed class GameRuleDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Module { get; set; } = "";
        public string Scope { get; set; } = "";
        public string Schedule { get; set; } = "";
        public GameRuleExecution Execution { get; set; }
        public string Description { get; set; } = "";
        public string Formula { get; set; } = "";
        public string Notes { get; set; } = "";
        public string[] Reads { get; set; } = Array.Empty<string>();
        public string[] Writes { get; set; } = Array.Empty<string>();
        public List<RuleSource> Sources { get; set; } = new();
        public List<RuleParameter> Parameters { get; set; } = new();
        public float X { get; set; }
        public float Y { get; set; }
    }

    /// <summary>Source-grounded inventory of native processes; connections document data access, not execution order.</summary>
    public sealed class GameRuleCatalog
    {
        public string Schema { get; set; } = "forest-current-rules";
        public int Version { get; set; } = 1;
        public string Name { get; set; } = "ForesTycoon jelenlegi szabályrendszere";
        public List<GameRuleDefinition> Rules { get; set; } = new();
        public List<string> Gaps { get; set; } = new();
        public RuleModel RoadTrafficModel { get; set; } = RuleModel.Default();
        public RuleConnection[] Connections()
        {
            var readers = new Dictionary<string, List<GameRuleDefinition>>(StringComparer.Ordinal);
            foreach (var rule in Rules)
                foreach (string field in rule.Reads)
                {
                    if (!readers.TryGetValue(field, out var list)) readers.Add(field, list = new());
                    list.Add(rule);
                }
            var connections = new List<RuleConnection>();
            var unique = new HashSet<RuleConnection>();
            foreach (var producer in Rules)
                foreach (string field in producer.Writes)
                    if (readers.TryGetValue(field, out var consumers))
                        foreach (var consumer in consumers)
                            if (consumer.Id != producer.Id)
                            {
                                var connection = new RuleConnection(producer.Id, consumer.Id, field);
                                if (unique.Add(connection)) connections.Add(connection);
                            }
            return connections.ToArray();
        }
        public void Validate()
        {
            if (Schema != "forest-current-rules" || Version != 1 || string.IsNullOrWhiteSpace(Name) || Rules == null || Rules.Count == 0 || Rules.Count > 512 || Gaps == null)
                throw new InvalidDataException("Érvénytelen játékszabály-katalógus.");
            var ids = new HashSet<string>();
            var tuningKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in Rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id) || string.IsNullOrWhiteSpace(rule.Name)
                    || string.IsNullOrWhiteSpace(rule.Module) || string.IsNullOrWhiteSpace(rule.Scope) || string.IsNullOrWhiteSpace(rule.Schedule)
                    || rule.Description == null || rule.Formula == null || rule.Notes == null || !Enum.IsDefined(rule.Execution)
                    || rule.Reads == null || rule.Writes == null || rule.Reads.Concat(rule.Writes).Any(string.IsNullOrWhiteSpace)
                    || rule.Reads.Distinct().Count() != rule.Reads.Length || rule.Writes.Distinct().Count() != rule.Writes.Length
                    || rule.Sources == null || rule.Sources.Count == 0 || rule.Sources.Any(s => s == null || string.IsNullOrWhiteSpace(s.File) || string.IsNullOrWhiteSpace(s.Symbol))
                    || rule.Parameters == null || rule.Parameters.Any(p => p == null || string.IsNullOrWhiteSpace(p.Name) || !double.IsFinite(p.Value) || string.IsNullOrWhiteSpace(p.Unit)
                        || (p.Tunable && !(p.Min <= p.Value && p.Value <= p.Max)))
                    || !float.IsFinite(rule.X) || !float.IsFinite(rule.Y)) throw new InvalidDataException("Érvénytelen szabály: " + rule?.Id);
                foreach (var parameter in rule.Parameters.Where(p => p.Tunable))
                    if (!tuningKeys.Add(parameter.Key)) throw new InvalidDataException("Ismétlődő hangolási kulcs: " + parameter.Key);
            }
            if (RoadTrafficModel == null || Gaps.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Hiányzó útkopási modell vagy érvénytelen hiánylista.");
            var compiled = new CompiledRoadRule(RoadTrafficModel); compiled.ValidateRange();
        }
        private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true, WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        public string ToJson() { Validate(); return JsonSerializer.Serialize(this, Options); }

        /// <summary>The tunable numbers that differ from the game's defaults, by key — what the world applies.</summary>
        public Dictionary<string, double> TuningOverrides() => Rules.SelectMany(r => r.Parameters).Where(p => p.Changed)
            .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
        public static GameRuleCatalog FromJson(string json)
        {
            var catalog = JsonSerializer.Deserialize<GameRuleCatalog>(json, Options) ?? throw new InvalidDataException("Üres katalógus.");
            catalog.Validate(); return catalog;
        }
    }
}
