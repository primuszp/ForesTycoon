using ForesTycoon.Editor;

namespace ForesTycoon.Tests;

public sealed class SystemModelTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper output;
    public SystemModelTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;

    [Fact]
    public void EveryCatalogFieldAndModuleIsClassified()
    {
        var catalog = CurrentGameRules.Build(); var vocabulary = SystemVocabulary.Default;
        var fields = catalog.Rules.SelectMany(r => r.Reads.Concat(r.Writes)).Distinct().ToArray();
        Assert.All(fields, f => Assert.NotNull(vocabulary.Find(f)));
        Assert.All(catalog.Rules, r => Assert.True(vocabulary.SubsystemOfModule(r.Module) >= 0, r.Module));
        Assert.Empty(new SystemModelAnalysis(catalog).Unclassified);
        // Formula keys the game publishes resolve to the quantity they observe.
        Assert.Equal("forest.trees", vocabulary.Observed("world.treeCount")!.Id);
        Assert.All(vocabulary.Variables.Where(v => v.Observe.Length > 0), v => Assert.Contains(v.Observe, GameWorld.RuleWorldFields));
    }

    [Fact]
    public void WorkshopTopicsAreTheVocabularySubsystems()
    {
        Assert.Equal(SystemVocabulary.Default.Subsystems.Select(s => s.Name), RuleWorkshopModel.Topics.Select(t => t.Name));
        Assert.Equal("Gyökérzónavíz", RuleWorkshopModel.FieldName("water.root"));
        Assert.Equal("Faállomány", RuleWorkshopModel.FieldName("world.treeCount"));
    }

    [Fact]
    public void ForestWaterAndRoadTrafficFeedbackLoopsAreFound()
    {
        var model = new SystemModelAnalysis(CurrentGameRules.Build());
        output.WriteLine($"{model.KeyLoops.Count} key loops, influences: {model.Influences.Count}");
        foreach (var group in model.CoupledGroups) output.WriteLine("GROUP " + group.Count + ": " + string.Join(", ", group.Select(model.Name)));
        foreach (var loop in model.KeyLoops) output.WriteLine(model.Span(loop) + ": " + string.Join(" -> ", loop.Variables.Select(model.Name)));
        var all = model.KeyLoops.Concat(model.Variables.SelectMany(v => model.LoopsThroughVariable(v.Id)))
            .Concat(model.Processes.SelectMany(p => model.LoopsThroughProcess(p.Id))).ToArray();
        Assert.NotEmpty(model.KeyLoops);
        Assert.All(all, loop => {
            Assert.Equal(loop.Variables.Count, loop.Steps.Count);
            Assert.Equal(loop.Variables.Count, loop.Variables.Distinct().Count());
            Assert.All(loop.Variables, v => Assert.True(SystemVocabulary.Endogenous(model.Variable(v)!.Kind)));
            for (int i = 0; i < loop.Length; i++)
                Assert.All(loop.Steps[i], p => {
                    var process = model.Process(p)!;
                    Assert.Contains(loop.Variables[i], process.Reads); Assert.Contains(loop.Variables[(i + 1) % loop.Length], process.Writes);
                });
        });
        // Trees form the canopy that draws water; the water budget sets the growth response; growth changes the trees.
        Assert.Contains(model.LoopsThroughVariable("water.growthFactor"), l => new[] { "forest.trees", "forest.canopy" }.All(l.Variables.Contains) && l.Length == 4);
        Assert.Contains("water.root", model.CoupledGroupOf("forest.trees")!);
        // Money feeds back: the credit line gates spending and road repair; roads carry the timber whose sale is income.
        Assert.Contains(model.LoopsThroughVariable("economy.result"), l => l.Variables.Contains("economy.expenses") && l.Length == 2);
        Assert.Contains(model.LoopsThroughVariable("economy.result"), l => l.Variables.Contains("road.condition"));
        Assert.Contains("economy.income", model.CoupledGroupOf("forest.trees")!);
        Assert.Contains("economy.result", model.CoupledGroupOf("forest.trees")!);
        // Vehicles wear the roads they drive on, and the road condition changes how they drive.
        Assert.Contains(model.LoopsThroughProcess("road.trafficWear"), l => l.Variables.Contains("road.condition"));
        Assert.Contains(model.StocksChangedBy("water.drainage"), v => v.Id == "water.root");
        Assert.True(model.ReadsOwnStock("water.drainage"));
        Assert.DoesNotContain(all, l => l.Variables.Any(v => v.StartsWith("visual.")));
        Assert.Equal(model.KeyLoops.Count, model.KeyLoops.Select(l => l.Key).Distinct().Count());
    }

    [Fact]
    public void ProjectStocksAndFormulasJoinTheModel()
    {
        var stock = new SystemVariable { Id = "stock.wearMultiplier", Name = "Útkopási szorzó", Subsystem = "roads", Kind = SystemVariableKind.Stock };
        var flow = new SystemProcess("flow.load", "Terhelés", "roads", new[] { "road.condition" }, new[] { "stock.wearMultiplier" });
        var formula = new SystemProcess("graph.wear", "Kopás × szorzó", "roads", new[] { "stock.wearMultiplier" }, new[] { "road.condition" }, "road.trafficWear");
        var model = new SystemModelAnalysis(CurrentGameRules.Build(), extraProcesses: new[] { flow, formula }, extraVariables: new[] { stock });
        Assert.Contains(model.LoopsThroughVariable("stock.wearMultiplier"), l => l.Length == 2 && l.Variables.Contains("road.condition"));
        Assert.Contains(model.KeyLoops, l => l.Variables.Contains("stock.wearMultiplier"));
    }

    [Fact]
    public void ProjectFormulasAreBoundToTheRuleOfTheirHook()
    {
        var project = GameWorld.DefaultRuleProject();
        var credit = project.Behaviors.Graphs.Single(g => g.Hook == "tuning.CreditLimit");
        credit.Nodes.Add(new BehaviorNode { Id = "balance", Name = "Egyenleg", Operation = BehaviorOperation.NamedValue, Key = "world.balance" });
        credit.Output = "balance";
        project.Dynamics.Stocks.Add(new DynamicsStock { Id = "debt", Name = "Adósság", Maximum = 1e6 });
        project.Dynamics.Flows.Add(new DynamicsFlow { Id = "borrow", Name = "Hitelfelvétel", To = "debt", Rate = new BehaviorGraph {
            Id = "rate", Name = "Ráta", Hook = "flow.borrow", Kind = "dynamics", Output = "x",
            Nodes = { new BehaviorNode { Id = "x", Name = "Kiadás", Operation = BehaviorOperation.NamedValue, Key = "world.expenses" } } } });
        var model = RuleWorkshopModel.SystemModel(CurrentGameRules.Build(), project.Dynamics, project.Behaviors.Graphs, _ => 4);
        var formula = model.Process("graph." + credit.Id)!;
        Assert.Equal("economy.balance", formula.RuleId);
        Assert.Equal(new[] { "economy.result" }, formula.Reads);
        Assert.Equal(new[] { "economy.expenses" }, model.Process("flow.borrow")!.Reads);
        Assert.Equal("economy", model.Variable("stock.debt")!.Subsystem);
        Assert.Contains(model.StocksChangedBy("flow.borrow"), v => v.Id == "stock.debt");
    }
}
