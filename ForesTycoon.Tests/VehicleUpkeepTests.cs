namespace ForesTycoon.Tests;

public class VehicleUpkeepTests
{
    [Fact]
    public void WorkWearsAndRoughGroundWearsMore()
    {
        var smooth = new VehicleUpkeep(1); var rough = new VehicleUpkeep(1);
        for (int i = 0; i < 300; i++) { smooth.Operate(1, 1, out _); rough.Operate(1, 2, out _); }
        Assert.InRange(smooth.Wear, 0.07f, 0.08f);
        Assert.True(rough.Wear > smooth.Wear * 1.9f);
        Assert.True(rough.FuelFactor > smooth.FuelFactor && rough.PaceFactor < smooth.PaceFactor);
    }

    [Fact]
    public void WornVehiclesBreakDownAndTheMechanicRepairsThemForAPrice()
    {
        var upkeep = new VehicleUpkeep(7) { Wear = 0.9f };
        int seconds = 0;
        while (upkeep.Operate(1, 1, out _) && seconds < 100000) seconds++;
        Assert.True(upkeep.Broken, "A worn vehicle never broke down.");
        float worn = upkeep.Wear;
        double cost = 0; int waited = 0;
        while (cost == 0) { upkeep.Operate(1, 1, out cost); waited++; }
        Assert.InRange(waited, 39, 41);
        Assert.Equal(VehicleUpkeep.RepairBaseCost + VehicleUpkeep.RepairWearCost * worn, cost, 3);
        Assert.Equal(worn - VehicleUpkeep.FieldRepairRelief, upkeep.Wear, 4);
        Assert.Equal(1, upkeep.Breakdowns);
    }

    [Fact]
    public void NewVehiclesRarelyBreakAndBreakdownsReplayExactly()
    {
        var fresh = new VehicleUpkeep(3);
        for (int i = 0; i < 600; i++) Assert.True(fresh.Operate(1, 1, out _));
        int BreakAt(uint seed) { var u = new VehicleUpkeep(seed) { Wear = 0.7f }; int t = 0; while (u.Operate(1, 1, out _)) t++; return t; }
        Assert.Equal(BreakAt(11), BreakAt(11));
        Assert.NotEqual(BreakAt(11), BreakAt(12));
    }

    [Fact]
    public void DepotServiceRestoresAtACost()
    {
        var upkeep = new VehicleUpkeep(5) { Wear = 0.5f };
        double cost = 0;
        for (int i = 0; i < 30; i++) cost += upkeep.Service(1);
        Assert.Equal(0, upkeep.Wear, 4);
        Assert.Equal(0.5 * VehicleUpkeep.ServiceCostPerWear, cost, 3);
    }
}
