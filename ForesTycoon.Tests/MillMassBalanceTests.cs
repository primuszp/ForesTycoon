namespace ForesTycoon.Tests;

public sealed class MillMassBalanceTests
{
    [Fact]
    public void LifetimeDeliveryTotalRetainsSmallLoadsAndSurvivesRestore()
    {
        var cargo = new TimberCargoSystem(); cargo.Deliver(1000000);
        for (int i = 0; i < 10000; i++) cargo.Deliver(.01f);
        Assert.Equal(1000000d + 10000 * (double).01f, cargo.Delivered, 6);
        var restored = new TimberCargoSystem(); restored.Restore(cargo.Available, cargo.Delivered);
        restored.Deliver(.01f);
        Assert.Equal(cargo.Delivered + (double).01f, restored.Delivered, 6);
    }

    [Fact]
    public void RepeatedSmallProcessingStepsConserveStockAndRemainLoadable()
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(9, 42), (_, _) => 4);
        var forest = new ForestSystem(map, new ForestStand[map.Tiles.Count]);
        var logistics = new ForestryLogistics(map, forest);
        Assert.True(logistics.PlaceMill(10));
        var mill = logistics.Mills.Single(); mill.Received = mill.Stock = 10000;
        for (int tick = 0; tick < 12000; tick++) logistics.Update(1d / 30);
        Assert.True(mill.Processed > 0);
        Assert.InRange(Math.Abs(mill.Received - mill.Stock - mill.Processed), 0, 1e-6);
        var snapshot = logistics.Capture();
        var restored = new ForestryLogistics(map, forest); restored.Restore(snapshot);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(snapshot), System.Text.Json.JsonSerializer.Serialize(restored.Capture()));
        Assert.Throws<InvalidDataException>(() => restored.Restore(snapshot with {
            Mills = new[] { snapshot.Mills[0] with { Processed = snapshot.Mills[0].Processed + 1 } }
        }));
    }
}
