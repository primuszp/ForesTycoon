namespace ForesTycoon.Tests;

public class VehicleTests
{
    [Fact]
    public void Update_MovesAtFixedSimulationSpeed()
    {
        Vehicle vehicle = new Vehicle(1, new[] { 10, 11, 12 }, speedTilesPerSecond: 2.0);

        vehicle.Update(0.25);
        vehicle.GetSegment(vehicle.RoutePosition, out int from, out int to, out float amount);

        Assert.Equal(10, from);
        Assert.Equal(11, to);
        Assert.Equal(0.5f, amount, 3);
    }

    [Fact]
    public void Vehicle_ReversesAtRouteEndWithoutTeleporting()
    {
        Vehicle vehicle = new Vehicle(1, new[] { 10, 11, 12 }, speedTilesPerSecond: 1.0);

        vehicle.Update(2.25);
        vehicle.GetSegment(vehicle.RoutePosition, out int from, out int to, out float amount);

        Assert.Equal(12, from);
        Assert.Equal(11, to);
        Assert.Equal(0.25f, amount, 3);
    }

    [Fact]
    public void RenderInterpolation_UsesPreviousAndCurrentFixedTickState()
    {
        Vehicle vehicle = new Vehicle(1, new[] { 10, 11 }, speedTilesPerSecond: 1.0);
        vehicle.Update(0.5);

        Assert.Equal(0.25, vehicle.InterpolatedRoutePosition(0.5f), 6);
    }

    [Fact]
    public void VehicleSystem_RemovesVehicleWhenItsRoadRouteIsBroken()
    {
        VehicleSystem vehicles = new VehicleSystem();
        vehicles.Spawn(new[] { 10, 11, 12 });

        int removed = vehicles.RemoveInvalidRoutes(tileId => tileId != 11);

        Assert.Equal(1, removed);
        Assert.Equal(0, vehicles.Count);
    }
}
