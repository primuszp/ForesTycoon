namespace ForesTycoon.Tests;

public class TimberCargoSystemTests
{
    [Fact]
    public void LoadAndDeliver_ConserveTimber()
    {
        TimberCargoSystem cargo = new TimberCargoSystem();
        cargo.AddHarvested(40f);

        float loaded = cargo.Load(25f);
        cargo.Deliver(loaded);

        Assert.Equal(15f, cargo.Available);
        Assert.Equal(25f, cargo.Delivered);
        Assert.Equal(40f, cargo.Available + cargo.Delivered);
    }

    [Fact]
    public void Vehicle_LoadsAtSourceAndDeliversAtRouteEnd()
    {
        TimberCargoSystem cargo = new TimberCargoSystem();
        cargo.AddHarvested(40f);
        VehicleSystem vehicles = new VehicleSystem(cargo);

        Vehicle vehicle = vehicles.Spawn(new[] { 10, 11 }, speedTilesPerSecond: 1.0);
        Assert.Equal(25f, vehicle.CargoAmount);
        Assert.Equal(15f, cargo.Available);

        vehicles.Update(1.0);
        Assert.Equal(0f, vehicle.CargoAmount);
        Assert.Equal(25f, cargo.Delivered);

        vehicles.Update(1.0);
        Assert.Equal(15f, vehicle.CargoAmount);
        Assert.Equal(0f, cargo.Available);
    }

    [Fact]
    public void RemovingBrokenRoute_ReturnsLoadedTimberToStockpile()
    {
        TimberCargoSystem cargo = new TimberCargoSystem();
        cargo.AddHarvested(20f);
        VehicleSystem vehicles = new VehicleSystem(cargo);
        vehicles.Spawn(new[] { 10, 11 });

        vehicles.RemoveInvalidRoutes(tileId => tileId != 11);

        Assert.Equal(20f, cargo.Available);
        Assert.Equal(0, vehicles.Count);
    }
}
