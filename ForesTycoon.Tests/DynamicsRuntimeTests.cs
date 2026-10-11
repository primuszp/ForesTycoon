using ForesTycoon.Rules;

namespace ForesTycoon.Tests;

public class DynamicsRuntimeTests
{
    internal static DynamicsFlow Flow(string id, string from, string to, double rate) => new() {
        Id = id, Name = id, From = from, To = to,
        Rate = new() { Id = id, Hook = "flow." + id, Kind = "dynamics", Output = "rate",
            Nodes = new() { new() { Id = "rate", Name = "rate", Operation = BehaviorOperation.Constant, Value = rate } } }
    };
    private static DynamicsRuntime Compile(DynamicsModel model) => new(model, Array.Empty<string>(), _ => throw new ArithmeticException());
    [Fact]
    public void CompetingTransfersConserveAndRespectBothEndpointLimits()
    {
        var model = new DynamicsModel { Stocks = new() {
            new() { Id = "a", Initial = 10, Maximum = 20, Unit = "m3" },
            new() { Id = "b", Maximum = 3, Unit = "m3" }, new() { Id = "c", Maximum = 20, Unit = "m3" }
        }, Flows = new() { Flow("ab", "a", "b", 10), Flow("ac", "a", "c", 10) } };
        var runtime = Compile(model); runtime.Step(1, 0);
        Assert.Equal(2, runtime.Values["a"]); Assert.Equal(3, runtime.Values["b"]); Assert.Equal(5, runtime.Values["c"]);
        Assert.Equal(10, runtime.Values.Values.Sum());
        model.Flows.Reverse(); var reordered = Compile(model); reordered.Step(1, 0);
        Assert.Equal(runtime.Capture().OrderBy(p => p.Key), reordered.Capture().OrderBy(p => p.Key));
    }
    [Fact]
    public void FeedbackReadsPreviousTickAndWorldInputsAreLive()
    {
        var flow = Flow("feedback", "", "x", 0);
        flow.Rate.Nodes = new() {
            new() { Id = "stock", Name = "stock", Operation = BehaviorOperation.NamedValue, Key = "stock.x" },
            new() { Id = "world", Name = "world", Operation = BehaviorOperation.NamedValue, Key = "world.rain" },
            new() { Id = "rate", Name = "sum", Operation = BehaviorOperation.Add, A = "stock", B = "world" }
        };
        double rain = 1;
        var runtime = new DynamicsRuntime(new() { Stocks = new() { new() { Id = "x", Initial = 2, Maximum = 100 } }, Flows = new() { flow } }, new[] { "world.rain" }, _ => rain);
        runtime.Step(1, 0); Assert.Equal(5, runtime.Values["x"]);
        rain = 2; runtime.Step(1, 1); Assert.Equal(12, runtime.Values["x"]);
        flow.Rate.Nodes[0].Key = "missing"; // compiled snapshot is independent
        runtime.Step(1, 2); Assert.Equal(26, runtime.Values["x"]);
    }
    [Fact]
    public void FailureKeepsWholeTickStateAndCanRecover()
    {
        var bad = Flow("bad", "", "x", 0);
        bad.Rate.Nodes.Add(new() { Id = "div", Name = "divide", Operation = BehaviorOperation.Divide, A = "rate", B = "rate" }); bad.Rate.Output = "div";
        var runtime = Compile(new() { Stocks = new() { new() { Id = "x", Initial = 2 } }, Flows = new() { Flow("good", "", "x", 1), bad } });
        runtime.Step(1, 0); Assert.Equal(2, runtime.Values["x"]); Assert.Contains("Nullával", runtime.LastError);
    }
    [Fact]
    public void InvalidUnitsMissingReferencesAndSnapshotsAreRejected()
    {
        var model = new DynamicsModel { Stocks = new() { new() { Id = "a", Unit = "m3" }, new() { Id = "b", Unit = "kg" } }, Flows = new() { Flow("ab", "a", "b", 1) } };
        Assert.Throws<InvalidDataException>(() => Compile(model));
        model.Stocks[1].Unit = "m3"; model.Flows[0].Rate.Nodes[0].Operation = BehaviorOperation.NamedValue; model.Flows[0].Rate.Nodes[0].Key = "stock.missing";
        Assert.Throws<InvalidDataException>(() => Compile(model));
        model.Flows.Clear(); var runtime = Compile(model);
        Assert.Throws<InvalidDataException>(() => runtime.Restore(new Dictionary<string, double> { ["a"] = 0, ["b"] = double.NaN }));
        Assert.Equal(0, runtime.Values["a"]);
    }
}
