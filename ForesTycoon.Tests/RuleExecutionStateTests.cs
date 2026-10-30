namespace ForesTycoon.Tests;

public class RuleExecutionStateTests
{
    [Fact]
    public void PreviewAndRestoreKeepTheSameNamedInputsDuringASuspendedRule()
    {
        using var world = new SimulationWorld(TerrainSettings.Default.WithNodeSize(9, 42));
        double live = 10;
        var amounts = new double[2];
        RuleScope Scope(double[] state) => new("probe", "Probe", "row", _ => new[] { 0, 1 }, _ => 2,
            new[] { new RuleScopeField("amount", "Amount", (_, id) => state[id], (_, id, value) => state[id] = value) },
            Array.Empty<RuleScopeAction>());
        var rules = new List<WorldRule> { new() { Id = "probe", Name = "Probe", Scope = "probe",
            Effects = { new() { Target = "amount", Mode = "set", Formula = "world.balance" } } } };
        RuleEngine Engine(double[] state) => new(rules, new(), Array.Empty<string>(), _ => live, scopes: new[] { Scope(state) });
        var engine = Engine(amounts);
        engine.Begin(world, 0, 0);
        while (amounts[0] == 0) engine.Continue(world, 1);
        Assert.True(engine.StepPending);
        var saved = engine.CaptureStep()!;
        live = 30;
        Assert.Equal(30, Assert.Single(engine.PreviewInfluences(world, "probe", 1, 0)).After);
        Assert.Equal(10, engine.CaptureStep()!.Named["world.balance"]);
        var restoredAmounts = (double[])amounts.Clone();
        var restored = Engine(restoredAmounts);
        restored.RestoreStep(world, saved);
        while (engine.StepPending) engine.Continue(world, 1);
        while (restored.StepPending) restored.Continue(world, 7);
        Assert.Null(engine.LastError);
        Assert.Null(restored.LastError);
        Assert.Equal(new double[] { 10, 10 }, amounts);
        Assert.Equal(amounts, restoredAmounts);
    }

    [Fact]
    public void PreviewDoesNotReplaceThePendingStepsNamedSnapshot()
    {
        double live = 10;
        var values = new RuleNamedValues(_ => live);
        int balance = values.Slot("world.balance");
        values.Slot("world.time");
        values.BeginStep();
        Assert.Equal(10, values.Read(balance, "world.balance", preview: false, parallel: false));
        live = 30;
        Assert.Equal(30, values.Read(balance, "world.balance", preview: true, parallel: false));
        Assert.Equal(10, values.Read(balance, "world.balance", preview: false, parallel: false));
        Assert.Equal(new Dictionary<string, double> { ["world.balance"] = 10 }, values.Capture());
        values.BeginStep();
        Assert.Empty(values.Capture());
        Assert.Equal(30, values.Read(balance, "world.balance", preview: false, parallel: false));
    }

    [Fact]
    public void RestoredNamedValuesFollowKeysAndDoNotRereadTheLiveWorld()
    {
        var original = new RuleNamedValues(_ => 17);
        int balance = original.Slot("world.balance");
        original.BeginStep();
        original.Read(balance, "world.balance", preview: false, parallel: false);
        var saved = original.Capture();
        var restored = new RuleNamedValues(_ => throw new InvalidOperationException("The saved value must be reused."));
        restored.Slot("world.time");
        int relocated = restored.Slot("world.balance");
        restored.BeginStep();
        restored.Restore(saved);
        saved["world.balance"] = 99;
        Assert.Equal(17, restored.Read(relocated, "world.balance", preview: false, parallel: false));
        Assert.Equal(17, restored.Read(relocated, "world.balance", preview: false, parallel: true));
        Assert.Single(restored.Capture());
    }

    [Fact]
    public void ParallelQueriesShareOneNamedReadAndPublishACompleteSnapshot()
    {
        int reads = 0;
        var values = new RuleNamedValues(_ => { Interlocked.Increment(ref reads); return 17; });
        int slot = values.Slot("world.hoursPerYear");
        values.BeginStep();
        var observed = new double[1024];
        Parallel.For(0, observed.Length, i => observed[i] = values.Read(slot, "world.hoursPerYear", preview: false, parallel: true));
        Assert.Equal(1, reads);
        Assert.All(observed, value => Assert.Equal(17, value));
        Assert.Equal(17, values.Capture()["world.hoursPerYear"]);
    }

    [Fact]
    public void AFailedNamedReadDoesNotPoisonTheStepsSnapshot()
    {
        int reads = 0;
        var values = new RuleNamedValues(_ => ++reads == 1 ? throw new ArithmeticException("Temporary read failure.") : 17);
        int slot = values.Slot("world.balance");
        values.BeginStep();
        Assert.Throws<ArithmeticException>(() => values.Read(slot, "world.balance", preview: false, parallel: false));
        Assert.Empty(values.Capture());
        Assert.Equal(17, values.Read(slot, "world.balance", preview: false, parallel: false));
        Assert.Equal(17, values.Read(slot, "world.balance", preview: false, parallel: false));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void FlowPublicationKeepsCompiledOrderAndAccumulatesSharedNamesExactlyOnce()
    {
        var ledger = new RuleFlowLedger();
        int rain = ledger.Register("rain");
        int drainage = ledger.Register("drainage");
        Assert.Equal(rain, ledger.Register("rain"));
        ledger.Prepare();
        ledger.BeginStep();
        ledger.Record(drainage, 3);
        ledger.Record(rain, 2);
        ledger.Record(drainage, 4);
        Assert.Empty(ledger.Totals);
        ledger.Publish();
        Assert.Equal(new[] { "rain", "drainage" }, ledger.Totals.Keys);
        Assert.Equal(2, ledger.Totals["rain"]);
        Assert.Equal(7, ledger.Totals["drainage"]);
        ledger.Publish();
        ledger.BeginStep();
        ledger.Record(rain, .5);
        ledger.Publish();
        Assert.Equal(2.5, ledger.Totals["rain"]);
        Assert.Equal(7, ledger.Totals["drainage"]);
    }

    [Fact]
    public void RemovedFlowTotalsSurviveRestoreAndKeepTheirPublicationOrder()
    {
        var ledger = new RuleFlowLedger();
        int rain = ledger.Register("rain");
        int drainage = ledger.Register("drainage");
        ledger.Prepare();
        var saved = new Dictionary<string, double> { ["removed"] = 17, ["drainage"] = 3 };
        ledger.Restore(saved);
        saved["removed"] = 99;
        ledger.BeginStep();
        ledger.Record(rain, 2);
        ledger.Record(drainage, 4);
        ledger.Publish();
        var captured = ledger.Capture()!;
        Assert.Equal(new[] { "removed", "drainage", "rain" }, captured.Keys);
        Assert.Equal(17, captured["removed"]);
        Assert.Equal(7, captured["drainage"]);
        captured["rain"] = 100;
        Assert.Equal(2, ledger.Totals["rain"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void NonTransfersDoNotCreateEmptyLedgerEntries(double moved)
    {
        var ledger = new RuleFlowLedger();
        int slot = ledger.Register("rain");
        ledger.Prepare();
        ledger.BeginStep();
        ledger.Record(slot, moved);
        ledger.Publish();
        Assert.Empty(ledger.Totals);
        Assert.Null(ledger.Capture());
    }
}
