namespace ForesTycoon.Tests;

public class GraphZeroGateTests
{
    private static readonly string[] Fields = { "a", "b", "c" };
    private static readonly BehaviorHook Hook = new("gate", "Gate", "test", "", -1e9, 1e9, "", "", "Test", false, Fields);
    private static BehaviorGraph Graph(string formula) => RuleFormula.Compile(formula,
        new() { Id = "gate", Hook = Hook.Id, Kind = Hook.Kind }, Fields.Select(f => new FormulaName(f, BehaviorOperation.Input, f)).ToArray());

    [Theory]
    [InlineData("ha(a < max(0,01; (b+c)*(b+c)); 7; 0)")]
    [InlineData("ha(abs(a-b) > c; 7; 0)")]
    [InlineData("ha(min(a;b) + c < 0; 7; 0)")]
    [InlineData("ha(-a * b > c; 7; 0)")]
    public void AZeroProofNeverDiscardsANonzeroPointInsideTheInputBox(string formula)
    {
        var graph = Graph(formula); var gate = GraphZeroGate.TryCreate(graph, Fields); Assert.NotNull(gate);
        var evaluate = new CompiledBehaviorModel(new() { Graphs = { graph } }, new[] { Hook }).Evaluator(Hook.Id, Hook.Kind)!;
        var random = new Random(8344); int proved = 0;
        for (int box = 0; box < 300; box++) {
            var bounds = Fields.Select(_ => { double a = random.NextDouble() * 40 - 20; return new GraphInputRange(a, a + random.NextDouble() * 8); }).ToArray();
            if (gate.MayBeNonZero(bounds)) continue;
            proved++;
            // Include every corner as well as interior points, with independent graph evaluation.
            for (int sample = 0; sample < 40; sample++) {
                var values = bounds.Select((b, i) => sample < 8 ? (sample & (1 << i)) == 0 ? b.Min : b.Max : b.Min + random.NextDouble() * (b.Max - b.Min)).ToArray();
                Assert.Equal(0, evaluate(default, values));
            }
        }
        Assert.True(proved > 0);
    }

    [Fact]
    public void BoundaryRoundingUnknownInputsAndOverflowKeepTheOriginalEvaluation()
    {
        var gate = GraphZeroGate.TryCreate(Graph("ha(a < b*b; 7; 0)"), Fields)!;
        double square = .1 * .1;
        Assert.True(gate.MayBeNonZero(new[] { GraphInputRange.Exact(Math.BitDecrement(square)), GraphInputRange.Exact(.1), GraphInputRange.Exact(0) }));
        Assert.True(gate.MayBeNonZero(new[] { GraphInputRange.Exact(1), GraphInputRange.Unknown, GraphInputRange.Exact(0) }));
        Assert.True(gate.MayBeNonZero(new[] { GraphInputRange.Exact(1), GraphInputRange.Exact(double.MaxValue), GraphInputRange.Exact(0) }));
        Assert.False(gate.MayBeNonZero(new[] { new GraphInputRange(4, 10), new GraphInputRange(.1, 1), GraphInputRange.Exact(0) }));
    }

    [Theory]
    [InlineData("ha(1/(a-a); 7; 0)")]
    [InlineData("ha(exp(a) > b; 7; 0)")]
    [InlineData("ha(a > b; 0; 7)")]
    [InlineData("a + b")]
    public void UnsupportedConditionsOrANonzeroElseBranchCannotCreateAZeroGate(string formula)
        => Assert.Null(GraphZeroGate.TryCreate(Graph(formula), Fields));

    [Fact]
    public void AnUnselectedFailingBranchDoesNotBlockAValidZeroProof()
    {
        var graph = Graph("ha(a < b; 1/(c-c); 0)"); var gate = GraphZeroGate.TryCreate(graph, Fields)!;
        var bounds = new[] { new GraphInputRange(10, 20), new GraphInputRange(1, 2), GraphInputRange.Unknown };
        Assert.False(gate.MayBeNonZero(bounds));
        var evaluate = new CompiledBehaviorModel(new() { Graphs = { graph } }, new[] { Hook }).Evaluator(Hook.Id, Hook.Kind)!;
        Assert.Equal(0, evaluate(default, new[] { 10d, 1, 0 }));
    }
}
