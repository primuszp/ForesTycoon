using ForesTycoon;

namespace ForesTycoon.Tests;

public class RuleOptimizationTests
{
    [Fact]
    public void IndexedDependenciesPreserveOriginalOrderAndAllFeedback()
    {
        var catalog = CurrentGameRules.Build();
        var reference = catalog.Rules.SelectMany(producer => producer.Writes.SelectMany(field =>
            catalog.Rules.Where(consumer => consumer.Id != producer.Id && consumer.Reads.Contains(field))
                .Select(consumer => new RuleConnection(producer.Id, consumer.Id, field)))).Distinct().ToArray();
        Assert.Equal(reference, catalog.Connections());
        var index = new GameRuleIndex(catalog);
        Assert.Equal(reference, index.Connections);
        Assert.Equal(reference.Select(c => new RuleConnection(index.Find(c.From).Module, index.Find(c.To).Module, c.Field))
            .Where(c => c.From != c.To).DistinctBy(c => (c.From, c.To)), index.ModuleConnections);
        foreach (var rule in catalog.Rules)
        {
            Assert.Same(rule, index.Find(rule.Id));
            foreach (string field in rule.Reads.Concat(rule.Writes))
            {
                Assert.Equal(catalog.Rules.Where(r => r.Reads.Contains(field)), index.Readers(field));
                Assert.Equal(catalog.Rules.Where(r => r.Writes.Contains(field)), index.Writers(field));
            }
        }
    }

    [Fact]
    public void IndexedSearchSupportsGlobalFieldsModulesAndHungarianNames()
    {
        var catalog = CurrentGameRules.Build(); var index = new GameRuleIndex(catalog);
        foreach (string module in index.Modules)
            Assert.Equal(catalog.Rules.Where(r => r.Module == module), index.InModule(module));
        Assert.Contains(index.Search("", "WATER.ROOT"), r => r.Id == "time.ecology");
        Assert.Single(index.Search("Utak és nyomok", "FORGALMI"));
        Assert.Empty(index.Search("Erdő", "FORGALMI"));
        Assert.Empty(index.Search("missing", ""));
        Assert.Null(index.Find("missing"));
    }


}
