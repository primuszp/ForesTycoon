namespace ForesTycoon.Tests;

public sealed class EconomyFeedbackTests
{
    private static GameWorld Create() => new(TerrainSettings.Default.WithNodeSize(17, 42), enableRendering: false);

    private static int BuildRoad(GameWorld world, int from = 0)
    {
        for (int id = from; id < world.Map.Tiles.Count - 1; id++) {
            int before = world.RoadCount;
            world.QueueRoadPath(id, id + 1, false); world.ExecutePendingCommands();
            if (world.RoadCount > before) return id + 2;
        }
        throw new InvalidOperationException("Nincs építhető útszakasz.");
    }

    [Fact]
    public void DefaultCreditIsUnlimitedSoExistingPlayIsUnchanged()
    {
        using var world = Create();
        int next = BuildRoad(world); BuildRoad(world, next);
        Assert.True(world.Balance < 0);
        Assert.Equal(Economy.UnlimitedCredit, world.Tuning[Tune.CreditLimit]);
    }

    [Fact]
    public void ExhaustedCreditStopsNewInvestmentUntilIncomeArrives()
    {
        using var world = Create();
        world.QueueTuning(new Dictionary<string, double> { ["CreditLimit"] = 0 }); world.ExecutePendingCommands();
        int next = BuildRoad(world); // The first investment starts from a zero balance.
        Assert.True(world.Balance < 0);
        int roads = world.RoadCount; double expenses = world.Expenses;
        for (int id = next; id < Math.Min(world.Map.Tiles.Count - 1, next + 40); id++) { world.QueueRoadPath(id, id + 1, false); world.ExecutePendingCommands(); }
        Assert.Equal(roads, world.RoadCount); Assert.Equal(expenses, world.Expenses);
        Assert.Equal(Economy.NoFunds, world.Logistics.Status);
    }

    [Fact]
    public void CreditLineCanBeAFormulaOfTheBalance()
    {
        // A credit line that shrinks as debt grows: a balancing loop authored in the editor, executed by the game.
        var project = GameWorld.DefaultRuleProject();
        var graph = project.Behaviors.Graphs.Single(g => g.Hook == "tuning.CreditLimit");
        graph.Nodes.Add(new BehaviorNode { Id = "balance", Name = "Egyenleg", Operation = BehaviorOperation.NamedValue, Key = "world.balance" });
        graph.Nodes.Add(new BehaviorNode { Id = "limit", Name = "Keret", Operation = BehaviorOperation.Constant, Value = 1 });
        graph.Nodes.Add(new BehaviorNode { Id = "credit", Name = "Hitelkeret", Operation = BehaviorOperation.Add, A = "balance", B = "limit" });
        graph.Output = "credit";
        using var world = Create();
        world.QueueRuleProject(project); world.ExecutePendingCommands();
        Assert.Equal(1, world.Tuning[Tune.CreditLimit], 6);
        BuildRoad(world);
        // Spending lowered the balance, and with it the credit line evaluated from the same formula.
        Assert.Equal(Math.Max(0, world.Balance + 1), world.Tuning[Tune.CreditLimit], 6);
    }
}
