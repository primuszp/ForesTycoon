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

    [Fact]
    public void DirectClonePreservesSerializedDocumentWithoutSharingAnyNodes()
    {
        var original = RuleModel.Default();
        original.Name = "Nedves út"; original.Nodes.Reverse(); original.Nodes[0].B = "traffic";
        var copy = original.Clone();
        Assert.Equal(original.ToJson(), copy.ToJson());
        Assert.NotSame(original.Nodes, copy.Nodes);
        for (int i = 0; i < copy.Nodes.Count; i++) Assert.NotSame(original.Nodes[i], copy.Nodes[i]);
        copy.Nodes[0].Value = 25; copy.Nodes.RemoveAt(1);
        Assert.Equal(5, original.Nodes.Count); Assert.Equal(1, original.Nodes[0].Value);
    }

    [Fact]
    public void DraftCacheReusesCalculationOnLayoutChangesAndRecompilesOnParameterChanges()
    {
        var draft = RuleModel.Default(); var compiler = new RoadRuleDraftCompiler();
        var first = compiler.Compile(draft);
        draft.Nodes[0].X += 100; draft.Nodes[0].Y -= 15;
        Assert.Same(first, compiler.Compile(draft));
        draft.Nodes.Single(n => n.Id == "scale").Value = 2;
        var second = compiler.Compile(draft);
        Assert.NotSame(first, second);
        Assert.Equal(.0015f, first.Evaluate(new(.0015, 1, 0)));
        Assert.Equal(.003f, second.Evaluate(new(.0015, 1, 0)));
        Assert.Same(second, compiler.Compile(draft.Clone()));
    }

    [Fact]
    public void DraftCacheDiscardsStaleCompiledModelOnBrokenLinksAndRecovers()
    {
        var draft = RuleModel.Default(); var compiler = new RoadRuleDraftCompiler();
        Assert.NotNull(compiler.Compile(draft));
        draft.Nodes.Single(n => n.Id == "base").A = "wear";
        Assert.Null(compiler.Compile(draft));
        Assert.Contains("képletkör", compiler.ValidationMessage);
        Assert.Null(compiler.Compile(draft));
        draft.Nodes.Single(n => n.Id == "base").A = "traffic";
        Assert.NotNull(compiler.Compile(draft));
        draft.Nodes[0].X = float.NaN;
        Assert.Null(compiler.Compile(draft));
        draft.Nodes[0].X = 15;
        Assert.NotNull(compiler.Compile(draft));
        draft.Nodes.Add(new() { Id = "broken", Name = "Hiányzó", Operation = RuleOperation.Multiply });
        Assert.Null(compiler.Compile(draft));
    }

    [Fact]
    public void InvalidNativeCatalogAndDraftInputsReportValidationErrors()
    {
        var catalog = CurrentGameRules.Build(); catalog.RoadTrafficModel = null!;
        Assert.Throws<InvalidDataException>(catalog.Validate);
        catalog = CurrentGameRules.Build(); catalog.Gaps.Add(null!);
        Assert.Throws<InvalidDataException>(catalog.Validate);
        var draft = RuleModel.Default(); draft.Nodes[0].Value = double.PositiveInfinity;
        Assert.Throws<InvalidDataException>(() => new CompiledRoadRule(draft));
        draft = RuleModel.Default(); draft.Nodes = null!;
        Assert.Null(new RoadRuleDraftCompiler().Compile(draft));
    }

    [Theory]
    [InlineData(16000)]
    [InlineData(36000)]
    [InlineData(64000)]
    public void SharedNativeTrafficInputsPreserveExistingFloatArithmetic(float mass)
    {
        Assert.Equal(.0015f * mass / 36000f, RoadTrafficParameters.Load(mass));
        var catalog = CurrentGameRules.Build();
        var parameters = catalog.Rules.Single(r => r.Id == "road.trafficWear").Parameters;
        Assert.Contains(parameters, p => p.Value == RoadTrafficParameters.ReferenceWear);
        Assert.Contains(parameters, p => p.Value == RoadTrafficParameters.ReferenceMass);
        Assert.Equal(.2f, RoadTrafficParameters.SurfaceFactor(RoadPaving.Asphalt));
        Assert.Equal(1f, RoadTrafficParameters.SurfaceFactor(RoadPaving.Macadam));
    }
}
