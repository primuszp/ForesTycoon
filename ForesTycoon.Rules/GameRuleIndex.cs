using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon.Rules
{
    /// <summary>
    /// Builds dependency and search indexes once per catalog load. Layout and notes may change;
    /// rebuild the index after changing rule identifiers, modules, descriptions or field bindings.
    /// </summary>
    public sealed class GameRuleIndex
    {
        private readonly Dictionary<string, GameRuleDefinition> rules;
        private readonly Dictionary<string, IReadOnlyList<GameRuleDefinition>> modules, readers, writers;
        private readonly Dictionary<string, string> searchText;
        public IReadOnlyList<string> Modules { get; }
        public IReadOnlyList<GameRuleDefinition> Rules { get; }
        public IReadOnlyList<RuleConnection> Connections { get; }
        public IReadOnlyList<RuleConnection> ModuleConnections { get; }

        public GameRuleIndex(GameRuleCatalog catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            catalog.Validate();
            Rules = Array.AsReadOnly(catalog.Rules.ToArray());
            rules = catalog.Rules.ToDictionary(r => r.Id, StringComparer.Ordinal);
            modules = Group(catalog.Rules.Select(r => (r.Module, r)));
            readers = Group(catalog.Rules.SelectMany(r => r.Reads.Select(field => (field, r))));
            writers = Group(catalog.Rules.SelectMany(r => r.Writes.Select(field => (field, r))));
            Modules = Array.AsReadOnly(catalog.Rules.Select(r => r.Module).Distinct(StringComparer.Ordinal).ToArray());
            Connections = Array.AsReadOnly(catalog.Connections());
            ModuleConnections = Array.AsReadOnly(Connections.Select(c => new RuleConnection(rules[c.From].Module, rules[c.To].Module, c.Field))
                .Where(c => c.From != c.To).DistinctBy(c => (c.From, c.To)).ToArray());
            searchText = catalog.Rules.ToDictionary(r => r.Id,
                r => string.Join(" ", new[] { r.Name, r.Id, r.Description }.Concat(r.Reads).Concat(r.Writes)), StringComparer.Ordinal);
        }

        public GameRuleDefinition Find(string id) => id != null && rules.TryGetValue(id, out var rule) ? rule : null;
        public IReadOnlyList<GameRuleDefinition> InModule(string module) => Lookup(modules, module);
        public IReadOnlyList<GameRuleDefinition> Readers(string field) => Lookup(readers, field);
        public IReadOnlyList<GameRuleDefinition> Writers(string field) => Lookup(writers, field);
        public GameRuleDefinition[] Search(string module, string query) =>
            (string.IsNullOrEmpty(module) ? Rules : InModule(module))
            .Where(r => string.IsNullOrEmpty(query) || searchText[r.Id].Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();

        private static IReadOnlyList<GameRuleDefinition> Lookup(Dictionary<string, IReadOnlyList<GameRuleDefinition>> index, string key) =>
            key != null && index.TryGetValue(key, out var result) ? result : Array.Empty<GameRuleDefinition>();
        private static Dictionary<string, IReadOnlyList<GameRuleDefinition>> Group(IEnumerable<(string Key, GameRuleDefinition Rule)> entries) =>
            entries.GroupBy(p => p.Key, StringComparer.Ordinal).ToDictionary(g => g.Key,
                g => (IReadOnlyList<GameRuleDefinition>)Array.AsReadOnly(g.Select(p => p.Rule).ToArray()), StringComparer.Ordinal);
    }
}
