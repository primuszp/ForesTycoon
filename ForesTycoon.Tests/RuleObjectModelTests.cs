namespace ForesTycoon.Tests;

/// <summary>The rule object model: kinds of world objects discovered from the game's [RuleEntity] interfaces.</summary>
[Collection("SpeciesBook")]
public sealed class RuleObjectModelTests
{
    private static GameWorld World() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);
    private static void Run(GameWorld world, double seconds) { for (int i = 0; i < seconds * 30; i++) world.Update(1d / 30); }

    [Fact]
    public void TheHierarchyIsDiscoveredWithInheritedFieldsAndRelations()
    {
        var schema = GameWorld.RuleObjects;
        Assert.Equal(new[] { "World object", "Plant", "Tree" }, schema.Lineage("tree").Select(e => e.Name));
        Assert.Equal(new[] { "World object", "Vehicle", "Truck" }, schema.Lineage("truck").Select(e => e.Name));
        var tree = schema.Find("tree");
        Assert.False(tree.Abstract); Assert.True(schema.Find("plant").Abstract);
        // Inherited first, own last; only the plant's health is writable.
        Assert.Equal("plant", tree.Fields.Single(f => f.Name == "health").DeclaredBy);
        Assert.Equal(new[] { "health", "stressYears" }, tree.Fields.Where(f => f.Writable).Select(f => f.Name));
        Assert.Equal("tileEnvironment", Assert.Single(tree.Relations).Target);
        // Concrete kinds become scopes; relation fields are readable as "relation.field".
        var scope = RuleEngine.Scope("tree");
        Assert.Contains(scope.Fields, f => f.Name == "tile.waterSupply" && f.Write == null);
        Assert.Null(RuleEngine.Scope("plant"));
    }

    [Fact]
    public void TheDroughtSampleIsOffByDefaultAndATreeRuleWritesHealthThroughTheModel()
    {
        using var world = World();
        var sample = Assert.Single(world.RuleProjectDocument.Rules, r => r.Id == "tree.drought");
        Assert.False(sample.Enabled); Assert.Equal("tree", sample.Scope);

        var scope = RuleEngine.Scope("tree");
        Assert.True(scope.Elements(world).Length > 0);
        var project = world.RuleProjectDocument; project.Algorithms = null;
        var rule = project.Rules.Single(r => r.Id == "tree.drought");
        // Same rule, a condition every tile meets: the tile relation is read and the tree's health written.
        rule.Enabled = true; rule.Condition = "tile.waterSupply > -1";
        rule.Effects = new() { new() { Target = "health", Mode = "set", Formula = "0,5" } };
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        Run(world, 1);
        Assert.Null(world.RuleBook.LastError);
        var health = scope.Fields.Single(f => f.Name == "health");
        int[] trees = scope.Elements(world);
        Assert.All(trees, id => Assert.Equal(0.5, health.Read(world, id), 5));
    }

    [Theory]
    [InlineData("curve.linear(0,3; 0; 0,2; 1; 0,9)")]
    [InlineData("curve.step(0,3; 0,5; 0,1; 0,8)")]
    [InlineData("curve.smooth(0,3; 0; 0,1; 1; 0,9)")]
    [InlineData("curve.saturate(0,3; 0; 0,1; 1; 0,9)")]
    [InlineData("curve.decay(0,3; 0; 0,9; 1; 0,1)")]
    [InlineData("curve.bell(0,3; 0,5; 0,9; 0,9; 0,1)")]
    [InlineData("curve.points(0,3; 0; 0,1; 0,2; 0,7; 1; 0,4)")]
    public void ACurveRunsInTheGameExactlyAsTheEditorDrawsIt(string formula)
    {
        Assert.True(RuleCurve.TryParse(formula, out var curve));
        Assert.Equal(formula, curve.ToFormula());
        using var world = World();
        var project = world.RuleProjectDocument; project.Algorithms = null;
        project.Rules.Add(new WorldRule { Id = "curve-test", Name = "Curve test", Scope = "tree",
            Effects = { new() { Target = "health", Mode = "set", Formula = formula } } });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        Run(world, 0.6);
        Assert.Null(world.RuleBook.LastError);
        var scope = RuleEngine.Scope("tree");
        Assert.Equal(curve.Evaluate(0.3), scope.Fields.Single(f => f.Name == "health").Read(world, scope.Elements(world)[0]), 5);
    }

    [Fact]
    public void TheCausalMapJoinsCodeLinksAndRuleBookLinksIntoBalancingLoops()
    {
        using var world = World();
        var map = RuleCausalMap.Build(GameWorld.RuleObjects, world.RuleProjectDocument.Rules);
        // The drought sample: a rising curve of the tile's water supply → health moves with it.
        var drought = Assert.Single(map.Edges, e => e.Rule == "tree.drought" && e.From == "tileEnvironment.waterSupply");
        Assert.Equal(("tree.health", 1, false), (drought.To, drought.Sign, drought.Native));
        // The modifier's input influences too, with a sign that depends on the curve.
        Assert.Contains(map.Edges, e => e.Rule == "tree.drought" && e.From == "tree.shadeTolerance" && e.Sign == 0);
        // Declared in code: a cause on the abstract plant lands on the tree.
        Assert.Contains(map.Edges, e => e.Native && e.From == "tree.light" && e.To == "tree.health");
        // Canopy closes → less light → slower growth: self-thinning balances itself.
        Assert.Contains(map.Loops, l => l.Polarity == -1 && l.Edges.Select(e => e.From).OrderBy(k => k)
            .SequenceEqual(new[] { "tileEnvironment.cover", "tree.height", "tree.light" }));
        Assert.All(map.Loops, l => Assert.Equal(l.Edges[0].From, l.Edges[^1].To));
    }

    [Fact]
    public void StumpDecayIsARuleBookRuleAndItsCurveDecidesWhenAStumpGoes()
    {
        using var world = World();
        var rule = Assert.Single(world.RuleProjectDocument.Rules, r => r.Id == "forest.stumps");
        Assert.True(rule.BuiltIn && rule.Enabled); Assert.Equal("stump", rule.Scope);
        Assert.True(world.DiagnosticFellForestBlock(out _));
        var stumps = RuleEngine.Scope("stump");
        int felled = stumps.Elements(world).Length;
        Assert.True(felled > 0);
        // Fresh stumps stay: they are younger than six forest years.
        Run(world, 1);
        Assert.Equal(felled, stumps.Elements(world).Length);
        // Move the threshold below their age: the rule removes every one of them.
        var project = world.RuleProjectDocument; project.Algorithms = null;
        project.Rules.Single(r => r.Id == "forest.stumps").Effects[0].Formula = "curve.step(age; 0; 0; 1)";
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        Run(world, 1);
        Assert.Null(world.RuleBook.LastError);
        Assert.Empty(stumps.Elements(world));
    }

    [Fact]
    public void AnArrowDrawnOnTheMapNamesItsSourceThroughTheRelationAndRunsAsARule()
    {
        var schema = GameWorld.RuleObjects;
        Assert.Equal("tile.rootWater", RuleCausalMap.NameIn(schema, "tree", "tileEnvironment.rootWater"));
        Assert.Equal("age", RuleCausalMap.NameIn(schema, "tree", "tree.age"));
        Assert.Null(RuleCausalMap.NameIn(schema, "tree", "truck.cargo"));
        // What the editor stores for "Root water → Stress years": a rule on the tree with a curve of the relation field.
        using var world = World();
        var project = world.RuleProjectDocument; project.Algorithms = null;
        var curve = new RuleCurve { Input = "tile.rootWater", Points = new() { (0, 0.5), (200, 0) }, Tail = " * lépés" };
        project.Rules.Add(new WorldRule { Id = "drawn", Name = "Root water › Stress years", Scope = "tree",
            Effects = { new() { Target = "stressYears", Mode = "add", Formula = curve.ToFormula() } } });
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        Run(world, 0.6);
        Assert.Null(world.RuleBook.LastError);
        var edge = Assert.Single(RuleCausalMap.Build(schema, world.RuleProjectDocument.Rules).Edges, e => e.Rule == "drawn");
        Assert.Equal(("tileEnvironment.rootWater", "tree.stressYears", -1), (edge.From, edge.To, edge.Sign));
    }
}
