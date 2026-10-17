using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

public class GraphHealthPlanTests
{
    private static BehaviorGraph Graph() => new() {
        Id = "health.test", Name = "Health test", Hook = "forest.shading", Kind = "competitionPair", Output = "choose",
        Nodes = new() {
            new() { Id = "gate", Name = "Gate", Operation = BehaviorOperation.Input, Key = "tree.height" },
            new() { Id = "coefficient", Name = "Coefficient", Operation = BehaviorOperation.Input, Key = "neighbor.height" },
            new() { Id = "health", Name = "Health", Operation = BehaviorOperation.Input, Key = "neighbor.health" },
            new() { Id = "multiply", Name = "Product", Operation = BehaviorOperation.Multiply, A = "coefficient", B = "health" },
            new() { Id = "zero", Name = "Zero", Operation = BehaviorOperation.Constant, Value = 0 },
            new() { Id = "choose", Name = "Choice", Operation = BehaviorOperation.Select, A = "gate", B = "multiply", C = "zero" }
        }
    };

    [Fact]
    public void PreparedCoefficientsMatchTheAuthoredGraphAndKeepLazyErrors()
    {
        var graph = Graph(); var hook = EcologyResponseProgram.Hooks.Single(h => h.Id == graph.Hook);
        var plan = GraphHealthPlan.TryCreate(graph, hook);
        var evaluate = new CompiledBehaviorModel(new() { Graphs = new() { graph } }, new[] { hook }).Evaluator(hook.Id, hook.Kind);
        var random = new Random(48); var inputs = new double[12];
        for (int i = 0; i < 600; i++) {
            inputs[0] = i % 3; inputs[5] = random.NextDouble() * 20;
            double coefficient = plan.Prepare(inputs);
            for (int j = 0; j < 5; j++) {
                inputs[8] = random.NextDouble();
                Assert.Equal(evaluate(default, inputs), plan.Evaluate(coefficient, inputs[3], inputs[8]));
            }
        }
        inputs[0] = 0; inputs[5] = double.PositiveInfinity;
        Assert.Equal(0, plan.Evaluate(plan.Prepare(inputs), 0, double.PositiveInfinity));
        inputs[0] = 1; inputs[5] = 2;
        double prepared = plan.Prepare(inputs); inputs[8] = double.MaxValue;
        Assert.Equal(Assert.Throws<ArithmeticException>(() => evaluate(default, inputs)).Message,
            Assert.Throws<ArithmeticException>(() => plan.Evaluate(prepared, inputs[3], inputs[8])).Message);
        inputs[8] = double.NaN;
        Assert.Equal(Assert.Throws<ArithmeticException>(() => evaluate(default, inputs)).Message,
            Assert.Throws<ArithmeticException>(() => plan.Evaluate(prepared, inputs[3], inputs[8])).Message);
    }

    [Fact]
    public void UnsupportedHealthExpressionsStayOnTheOriginalGraphEvaluator()
    {
        var graph = Graph(); graph.Nodes.Single(n => n.Id == "multiply").Operation = BehaviorOperation.Power;
        Assert.Null(GraphHealthPlan.TryCreate(graph, EcologyResponseProgram.Hooks.Single(h => h.Id == graph.Hook)));
    }

    [Fact]
    public void PreparingDelegatesDoesNotReadWorldValues()
    {
        var graph = new BehaviorGraph { Id = "read", Name = "Read", Hook = "test", Kind = "world", Output = "read",
            Nodes = new() { new() { Id = "read", Name = "Read", Operation = BehaviorOperation.NamedValue, Key = "world.test" } } };
        int reads = 0;
        var program = new CompiledBehaviorModel(new() { Graphs = new() { graph } },
            new[] { new BehaviorHook("test", "Test", "world", "", 0, 10, "", "", "", false) },
            new HashSet<string> { "world.test" }, _ => { reads++; return 4; });
        program.Prepare(); Assert.Equal(0, reads);
        Assert.Equal(4, program.Evaluator("test", "world")(default, null)); Assert.Equal(1, reads);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ConstantPreparationRetainsLazyChoicesAndRuntimeFailures(int gate)
    {
        var graph = new BehaviorGraph { Id = "constant", Name = "Constant", Hook = "test", Kind = "test", Output = "choice",
            Nodes = new() {
                new() { Id = "gate", Name = "Gate", Operation = BehaviorOperation.Constant, Value = gate },
                new() { Id = "one", Name = "One", Operation = BehaviorOperation.Constant, Value = 1 },
                new() { Id = "zero", Name = "Zero", Operation = BehaviorOperation.Constant, Value = 0 },
                new() { Id = "bad", Name = "Bad", Operation = BehaviorOperation.Divide, A = "one", B = "zero" },
                new() { Id = "input", Name = "Input", Operation = BehaviorOperation.Input, Key = "x" },
                new() { Id = "choice", Name = "Choice", Operation = BehaviorOperation.Select, A = "gate", B = "bad", C = "input" }
            } };
        var hook = new BehaviorHook("test", "Test", "test", "", -100, 100, "", "", "", false, new[] { "x" });
        var model = new CompiledBehaviorModel(new() { Graphs = new() { graph } }, new[] { hook });
        var evaluate = model.Evaluator("test", "test");
        string? before = gate == 1 ? Assert.Throws<ArithmeticException>(() => evaluate(default, new[] { .25 })).Message : null;
        model.Prepare();
        if (gate == 1) Assert.Equal(before, Assert.Throws<ArithmeticException>(() => evaluate(default, new[] { .25 })).Message);
        else Assert.Equal(.25, evaluate(default, new[] { .25 }));
    }

    [Fact]
    public void NativeDagCompilationMatchesInterpreterValuesAndLazyErrors()
    {
        var random = new Random(784);
        var operations = new[] { BehaviorOperation.Add, BehaviorOperation.Subtract, BehaviorOperation.Multiply,
            BehaviorOperation.Divide, BehaviorOperation.Minimum, BehaviorOperation.Maximum, BehaviorOperation.Less,
            BehaviorOperation.Greater, BehaviorOperation.Select, BehaviorOperation.Power, BehaviorOperation.Exp };
        var hook = new BehaviorHook("test", "Test", "test", "", -1e9, 1e9, "", "", "", false, new[] { "x", "y" });
        for (int graphIndex = 0; graphIndex < 60; graphIndex++) {
            var graph = new BehaviorGraph { Id = "dag", Name = "DAG", Hook = "test", Kind = "test", Nodes = new() {
                new() { Id = "n0", Name = "Zero", Operation = BehaviorOperation.Constant, Value = 0 },
                new() { Id = "n1", Name = "One", Operation = BehaviorOperation.Constant, Value = 1 },
                new() { Id = "n2", Name = "X", Operation = BehaviorOperation.Input, Key = "x" },
                new() { Id = "n3", Name = "Y", Operation = BehaviorOperation.Input, Key = "y" }
            } };
            for (int n = 4; n < 36; n++) graph.Nodes.Add(new() {
                Id = "n" + n, Name = "Node " + n, Operation = operations[random.Next(operations.Length)],
                A = "n" + random.Next(n), B = "n" + random.Next(n), C = "n" + random.Next(n)
            });
            graph.Output = "n35";
            var model = new CompiledBehaviorModel(new() { Graphs = new() { graph } }, new[] { hook });
            var evaluate = model.Evaluator("test", "test");
            var inputs = Enumerable.Range(0, 40).Select(n => new[] {
                n == 0 ? double.NaN : n == 1 ? -0d : random.NextDouble() * 4 - 2,
                n == 2 ? double.PositiveInfinity : random.NextDouble() * 4 - 2 }).ToArray();
            (long Bits, string? Error) Run(double[] values) {
                try { return (BitConverter.DoubleToInt64Bits(evaluate(default, values)), null); }
                catch (ArithmeticException error) { return (0, error.Message); }
            }
            var expected = inputs.Select(Run).ToArray();
            model.Prepare();
            Assert.Equal(expected, inputs.Select(Run).ToArray());
            var parallel = new (long Bits, string? Error)[inputs.Length];
            Parallel.For(0, inputs.Length, n => parallel[n] = Run(inputs[n]));
            Assert.Equal(expected, parallel);
        }
    }

}
