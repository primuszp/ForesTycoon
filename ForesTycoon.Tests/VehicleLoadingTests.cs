using OpenTK.Mathematics;
namespace ForesTycoon.Tests;
public class VehicleLoadingTests
{
    private static VehicleSystem Traffic(TimberCargoSystem cargo) => new(cargo, _ => new VehicleRoadRoute(
        new[] { new Vector3(0,0,0), new Vector3(10,0,0) }, new[] { Vector2.Zero, Vector2.Zero }));
    [Fact]
    public void EmptyTruckWaitsThenLoadsVisiblyWithoutMoving()
    {
        var cargo = new TimberCargoSystem(); var traffic = Traffic(cargo); var truck = traffic.Spawn(new[] { 0,1 });
        traffic.Update(2);
        Assert.Equal(VehicleTransportState.Waiting, truck.TransportState);
        Assert.Equal(0, truck.RoutePosition);
        cargo.AddHarvested(12); traffic.Update(0.1);
        Assert.Equal(VehicleTransportState.Loading, truck.TransportState);
        Assert.Equal(12, truck.CargoAmount); Assert.Equal(0, cargo.Available);
        traffic.Update(1.5);
        Assert.InRange(truck.VisualCargoFill, 0.20f, 0.30f);
        Assert.Equal(0, truck.RoutePosition);
        traffic.Update(2);
        Assert.Equal(VehicleTransportState.Hauling, truck.TransportState);
        Assert.Equal(truck.CargoFill, truck.VisualCargoFill);
    }
    [Fact]
    public void DeliveryWaitsForUnloadingAndRemovalReturnsReservedCargo()
    {
        var cargo = new TimberCargoSystem(); cargo.AddHarvested(25);
        var traffic = Traffic(cargo); var truck = traffic.Spawn(new[] { 0,1 });
        for(int i=0;i<900 && truck.TransportState!=VehicleTransportState.Unloading;i++) traffic.Update(1.0/30);
        Assert.Equal(VehicleTransportState.Unloading, truck.TransportState);
        Assert.Equal(0, cargo.Delivered);
        traffic.Update(1);
        Assert.Equal(0, cargo.Delivered); Assert.Equal(25, truck.CargoAmount);
        traffic.Update(2.1);
        Assert.Equal(25, cargo.Delivered); Assert.Equal(0, truck.CargoAmount);
        Assert.Equal(VehicleTransportState.Returning, truck.TransportState);
        var other = new TimberCargoSystem(); other.AddHarvested(7);
        var waiting = Traffic(other); waiting.Spawn(new[] { 0,1 }); waiting.Update(1);
        waiting.RemoveInvalidRoutes(_=>false);
        Assert.Equal(7, other.Available); Assert.Equal(0, other.Delivered);
    }
    [Fact]
    public void BodySteeringBuildsGraduallyAcrossStraightToCurveTransition()
    {
        var road = new VehicleRoadRoute(new[] {new Vector3(0,0,0),new Vector3(10,0,0),new Vector3(10,10,0)},
            new[] {Vector2.Zero,Vector2.Zero,Vector2.Zero});
        float before = road.BodyCurvature(0.4,4);
        Assert.InRange(before, 0.001f, 0.2f);
        Assert.Equal(0, road.BodyCurvature(0.1,4), 4);
        float previous = road.BodyCurvature(0.2,4);
        for(double d=0.21;d<1.8;d+=0.01) {
            float next=road.BodyCurvature(d,4);
            Assert.True(float.IsFinite(next));
            Assert.InRange(Math.Abs(next-previous),0,0.035f);
            previous=next;
        }
    }
}
