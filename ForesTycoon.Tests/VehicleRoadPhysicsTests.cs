using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class VehicleRoadPhysicsTests
{
    private static VehicleRoadRoute Straight(float grade = 0, float crossfall = 0) => new(
        new[] { new Vector3(0, 0, 0), new Vector3(10, 0, 10 * grade), new Vector3(20, 0, 20 * grade) },
        new[] { new Vector2(grade, crossfall), new Vector2(grade, crossfall), new Vector2(grade, crossfall) });

    [Fact]
    public void Pose_FollowsGradeInBothTravelDirectionsAndCrossfall()
    {
        var road = Straight(0.25f, 0.1f);
        road.GetPose(0.7, out var center, out var forward, out var left, out var up);
        Assert.Equal(0.25f, forward.Z / forward.X, 4);
        Assert.True(left.Z > 0);
        Assert.Equal(0f, Vector3.Dot(forward, up), 4);
        Assert.Equal(1f, up.Length, 4);
        Assert.InRange(center.Z, 1.75f, 1.78f);
        road.GetPose(3.3, out var reverseCenter, out var reverse, out _, out var reverseUp);
        Assert.True(reverse.Z < 0);
        Assert.True((forward + reverse).Length < 0.001f);
        Assert.True((center - reverseCenter).Length < 0.001f);
        Assert.True((up - reverseUp).Length < 0.001f);
    }

    [Fact]
    public void Corner_FollowsRoadQuarterCircleAndHasContinuousTangent()
    {
        var road = new VehicleRoadRoute(new[] { Vector3.Zero, new Vector3(10, 0, 0), new Vector3(10, 10, 0) }, new Vector2[3]);
        Assert.Equal(new Vector3(5, 0, 0), road.Sample(0.5));
        Assert.True((road.Sample(1.5) - new Vector3(10, 5, 0)).Length < 0.0001f);
        Assert.Equal(5f, (road.Sample(1) - new Vector3(5, 5, 0)).Length, 4);
        var before = (road.Sample(0.5) - road.Sample(0.499)).Normalized();
        var after = (road.Sample(0.501) - road.Sample(0.5)).Normalized();
        Assert.True(Vector3.Dot(before, after) > 0.999f);
        Assert.True(road.TargetSpeed(0.3, 1.5, 0) < 1.5);
    }

    [Fact]
    public void GradeAndLoad_AffectSpeedWithoutRunawayDownhill()
    {
        var road = Straight(0.25f);
        // Climbing speed comes from the dynamics; descending, the loaded driver holds back.
        Assert.True(road.Grade(0.5) > 0);
        Assert.InRange(road.TargetSpeed(3.5, 1.5, 1), 0.6, 1.5);
        Assert.True(road.TargetSpeed(3.5, 1.5, 1) < road.TargetSpeed(3.5, 1.5, 0));
        var empty = new Vehicle(1, new[] { 0, 1, 2 }, 1.5, roadRoute: road);
        var loaded = new Vehicle(2, new[] { 0, 1, 2 }, 1.5, roadRoute: road);
        loaded.Load(25);
        empty.Update(0.5); loaded.Update(0.5);
        Assert.InRange(empty.CurrentSpeed, 0.01, 1.49);
        Assert.True(loaded.RoutePosition < empty.RoutePosition);
        Assert.True(loaded.CurrentSpeed < empty.CurrentSpeed);
    }

    [Fact]
    public void TerminalStop_DeliversOnceAndRestartsWithoutOvershoot()
    {
        var cargo = new TimberCargoSystem(); cargo.AddHarvested(40);
        var system = new VehicleSystem(cargo, _ => Straight());
        var truck = system.Spawn(new[] { 0, 1, 2 });
        int steps = 0;
        while (cargo.Delivered == 0 && steps++ < 1000) system.Update(1.0 / 30);
        Assert.True(steps < 1000);
        Assert.Equal(2, truck.RoutePosition);
        Assert.Equal(0, truck.CurrentSpeed);
        Assert.Equal(25, cargo.Delivered);
        system.Update(1.0 / 30);
        Assert.True(truck.RoutePosition > 2);
        Assert.Equal(25, cargo.Delivered);
        Assert.Equal(40, cargo.Available + cargo.Delivered + truck.CargoAmount);
    }

    [Fact]
    public void LegacyPhysics_PreservesJournalDeliveryTiming()
    {
        var cargo = new TimberCargoSystem(); cargo.AddHarvested(25);
        var system = new VehicleSystem(cargo, _ => Straight()) { UseRoadPhysics = false };
        var truck = system.Spawn(new[] { 0, 1, 2 }, 1);
        system.Update(2);
        Assert.Equal(2, truck.RoutePosition);
        Assert.Equal(25, cargo.Delivered);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void InvalidTimeIsRejected(double delta)
    {
        var truck = new Vehicle(1, new[] { 0, 1, 2 }, 1, roadRoute: Straight());
        Assert.Throws<ArgumentOutOfRangeException>(() => truck.Update(delta));
        Assert.Equal(0, truck.RoutePosition);
    }
}
