using System;
using ForesTycoon;
using Xunit;

public class VehicleKinematicsTests
{
    private static RigProfile Truck(double hitch = 0, double trailer = 8) => new(SteeringGeometry.Axle, 4, 0, 1.1, .5,
        [new("rear", 0, 0, 2, .5), new("front", 0, 4, 2, .5, true), new("trailer", 1, 0, 2, .5)],
        [new(hitch, trailer, 1.3)]);

    [Fact]
    public void EditorGeometrySelectsAxleOrFrameSteeringAndKeepsThePhysicalAxleCounts()
    {
        var tuning = GameTuning.Default;
        var processor = VehicleGeometryProfiles.Machine(tuning, ForestMachineKind.Harvester);
        var forwarder = VehicleGeometryProfiles.Machine(tuning, ForestMachineKind.Forwarder);
        Assert.Equal(SteeringGeometry.Axle, processor.Steering);
        Assert.Equal(2, processor.Axles.Length); Assert.Contains(processor.Axles, a => a.Steered);
        Assert.Equal(SteeringGeometry.Articulated, forwarder.Steering); Assert.Equal(4, forwarder.Axles.Length);
        var custom = GameTuning.FromOverrides(new System.Collections.Generic.Dictionary<string, double> {
            ["ProcessorSteeringModel"] = 1, ["ProcessorFrontAxles"] = 2, ["ProcessorRearAxles"] = 2,
            ["TruckTrailerLayout"] = 2, ["TruckRearAxles"] = 3, ["TruckTrailerAxles"] = 1 });
        processor = VehicleGeometryProfiles.Machine(custom, ForestMachineKind.Harvester);
        Assert.Equal(SteeringGeometry.Articulated, processor.Steering); Assert.Equal(4, processor.Axles.Length);
        var truck = VehicleGeometryProfiles.Truck(custom); truck.Validate();
        Assert.Equal(2, truck.Trailers.Length); Assert.Equal(6, truck.Axles.Length);
        Assert.Throws<ArgumentException>(() => GameTuning.FromOverrides(new System.Collections.Generic.Dictionary<string, double> { ["TruckRearAxles"] = 1.5 }));
    }

    [Fact]
    public void PhysicalTruckChangesToReverseWithoutFlippingItsBodyAndRestoresTheSameWheelState()
    {
        var road = new VehicleRoadRoute([new(0, 0, 0), new(5, 0, 0), new(10, 0, 0)], [default, default, default]);
        var vehicle = new Vehicle(1, [0, 1, 2], .4, roadRoute: road, roadPhysics: false) { Spec = GameTuning.Default.Truck, CargoStopsEnabled = true };
        for (int i = 0; i < 300 && vehicle.RoutePosition < 2; i++) vehicle.Update(1d / 30);
        Assert.Equal(2, vehicle.RoutePosition); double yaw = vehicle.Kinematics.Units[0].Yaw;
        vehicle.Update(1d / 30);
        Assert.InRange(Math.Abs(VehicleKinematics.Wrap(vehicle.Kinematics.Units[0].Yaw - yaw)), 0, .01);
        Assert.Equal(-1, vehicle.SteeringDriver.Capture().DriveGear);
        var checkpoint = vehicle.Capture(); var copy = Vehicle.Restore(checkpoint, 3); copy.Spec = GameTuning.Default.Truck;
        for (int i = 0; i < 60; i++) { vehicle.Update(1d / 30); copy.Update(1d / 30); }
        Assert.Equal(vehicle.RoutePosition, copy.RoutePosition);
        Assert.Equal(vehicle.Kinematics.Capture(), copy.Kinematics.Capture(), RigStateComparer.Instance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void PassiveTrailerConvergesToTheForestRoadCircularPath(double hitch)
    {
        var rig = new VehicleKinematics(Truck(hitch), default, 0);
        double steer = Math.Atan(4.0 / 20);
        rig = VehicleKinematics.Restore(rig.Capture() with { Steer = steer });
        for (int i = 0; i < 500; i++) Assert.True(rig.Advance(2, steer, 1));
        var centre = new PlanPoint(0, 20);
        double frontRadius = Math.Sqrt(20 * 20 + 4 * 4);
        double expected = ForestRoadSweptPath.RearRadius(frontRadius, 4, Math.Abs(hitch), 0, 8);
        Assert.InRange(Math.Abs((rig.Units[1].Axle - centre).Length - expected), 0, .002);
        var hitchAt = rig.Units[0].Axle + PlanPoint.Forward(rig.Units[0].Yaw) * hitch;
        Assert.InRange(Math.Abs((hitchAt - rig.Units[1].Axle).Length - 8), 0, 1e-10);
    }

    [Fact]
    public void AxleSteeringHasAckermannAnglesAndDifferentWheelRolls()
    {
        var rig = new VehicleKinematics(Truck(), default, 0);
        Assert.True(rig.Advance(2, .3, 1));
        var wheels = rig.Wheels();
        Assert.True(VehicleKinematics.Wrap(wheels[2].Yaw - rig.Units[0].Yaw) > VehicleKinematics.Wrap(wheels[3].Yaw - rig.Units[0].Yaw));
        Assert.NotEqual(wheels[0].Roll, wheels[1].Roll);
        Assert.Equal(rig.Units[0].Yaw, wheels[0].Yaw);
    }

    [Fact]
    public void EverySteeredWheelRespectsItsMechanicalLimitIncludingTheInsideWheel()
    {
        var profile = Truck() with { MaxSteer = .6 };
        var rig = new VehicleKinematics(profile, default, 0);
        Assert.True(rig.Advance(1, 2, 2));
        var wheels = rig.Wheels();
        Assert.InRange(Math.Abs(VehicleKinematics.Wrap(wheels[2].Yaw - rig.Units[0].Yaw)), .5999, .60000001);
        Assert.InRange(Math.Abs(VehicleKinematics.Wrap(wheels[3].Yaw - rig.Units[0].Yaw)), 0, .6);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(2.2, 4.8)]
    public void ActiveJointMaintainsLengthsAndHasTheExpectedSteadyTurningRadius(double front, double rear)
    {
        var profile = new RigProfile(SteeringGeometry.Articulated, front, rear, .8, .4,
            [new("front", 0, 0, 2.5, .7), new("rear", 1, 0, 2.5, .7)], []);
        var rig = new VehicleKinematics(profile, default, 0);
        for (int i = 0; i < 10; i++) Assert.True(rig.Advance(1, .6, 1));
        var before = rig.Capture();
        double radius = (rear + front * Math.Cos(.6)) / Math.Sin(.6);
        var centre = before.Units[0].Axle + PlanPoint.Left(before.Units[0].Yaw) * radius;
        Assert.True(rig.Advance(1, .6, 10));
        Assert.InRange(Math.Abs((rig.Units[0].Axle - centre).Length - radius), 0, 1e-4);
        double rearRadius = (rig.Units[1].Axle - centre).Length;
        if (front == rear) Assert.InRange(Math.Abs(rearRadius - radius), 0, 1e-4);
        else Assert.True(rearRadius < radius);
        var state = rig.Capture();
        Assert.Equal(state, VehicleKinematics.Restore(state).Capture(), RigStateComparer.Instance);
    }

    [Fact]
    public void ReversingKeepsBodyHeadingAndWheelSteeringWithinHalfATurn()
    {
        var rig = new VehicleKinematics(Truck(), default, 0);
        Assert.True(rig.Advance(-1, 0, 1));
        Assert.Equal(0, rig.Units[0].Yaw);
        Assert.InRange(rig.Units[0].Axle.X, -1.00001, -.99999);
        foreach (var wheel in rig.Wheels(-1)) Assert.InRange(Math.Abs(wheel.Yaw), 0, 1e-10);
    }

    [Fact]
    public void SaveRestoreContinuesTheSameTrailerAndWheelTrajectoryAndRejectsBrokenHitches()
    {
        var first = new VehicleKinematics(Truck(-2), default, .2);
        Assert.True(first.Advance(2, .3, 2));
        var saved = first.Capture(); var second = VehicleKinematics.Restore(saved);
        Assert.True(first.Advance(2, -.2, 3)); Assert.True(second.Advance(2, -.2, 3));
        Assert.Equal(first.Capture(), second.Capture(), RigStateComparer.Instance);
        var broken = (RigUnitPose[])saved.Units.Clone(); broken[1] = broken[1] with { Axle = default };
        Assert.Throws<ArgumentException>(() => VehicleKinematics.Restore(saved with { Units = broken }));
        // Captures do not expose the running state's mutable arrays.
        saved.Units[0] = default;
        Assert.NotEqual(default, first.Units[0]);
    }

    [Fact]
    public void RenderInterpolationKeepsHitchesClosedAndDoesNotModifySimulationState()
    {
        var rig = new VehicleKinematics(Truck(-2), default, .1); var before = rig.Capture();
        rig.Advance(2, .3, 1); var state = rig.Capture();
        var visual = rig.Interpolated(before, .5);
        _ = VehicleKinematics.Restore(visual.Capture());
        Assert.Equal(state, rig.Capture(), RigStateComparer.Instance);
        Assert.NotEqual(rig.Units[0], visual.Units[0]);
    }

    private sealed class RigStateComparer : System.Collections.Generic.IEqualityComparer<RigState>
    {
        internal static readonly RigStateComparer Instance = new();
        public bool Equals(RigState? a, RigState? b) => a != null && b != null && a.Steer == b.Steer &&
            System.Linq.Enumerable.SequenceEqual(a.Units, b.Units) && System.Linq.Enumerable.SequenceEqual(a.Rolls, b.Rolls);
        public int GetHashCode(RigState value) => value.Steer.GetHashCode();
    }

    public static TheoryData<string, int, double> ConfiguredRigs => new() {
        { "TruckTrailerLayout=0", 0, 25 }, { "TruckTrailerLayout=1", 0, 25 }, { "TruckTrailerLayout=2", 0, 25 },
        { "TruckTrailerLayout=1;TruckRearAxles=1;TruckTrailerAxles=3;TruckTrailerLength=7.7;TruckHitchOffset=0.3", 0, 25 },
        { "ForwarderRearAxles=3", 1, 25 }, { "ProcessorSteeringModel=0", 2, 25 }, { "ProcessorSteeringModel=1;ProcessorFrontAxles=2;ProcessorRearAxles=2", 2, 25 },
    };

    /// <summary>The gameplay controller, not the textbook construction: pure pursuit with the same limits the fleet drives under.</summary>
    [Theory]
    [MemberData(nameof(ConfiguredRigs))]
    public void EveryConfiguredRigFollowsARoadBendWithClosedJointsAndNoBlockage(string overrides, int kind, double radius)
    {
        var tuning = GameTuning.FromOverrides(overrides.Split(';').Select(p => p.Split('=')).ToDictionary(p => p[0], p => double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)));
        var profile = kind == 0 ? VehicleGeometryProfiles.Truck(tuning) : VehicleGeometryProfiles.Machine(tuning, kind == 1 ? ForestMachineKind.Forwarder : ForestMachineKind.Harvester);
        var road = new StudyRoad(StudyManoeuvre.SCurve, radius, 30, 12);
        var driver = new RigPathFollower(new VehicleKinematics(profile, road.Sample(0).Point, 0));
        double position = 0; const double speed = 2, step = 1d / 30;
        for (int tick = 0; tick < 12000 && position < road.Length; tick++) {
            position = driver.Advance(s => road.Sample(s).Point, position, road.Length, 1, speed * step, step, manoeuvreRoom: 6);
            Assert.Null(driver.BlockedReason);
            Assert.InRange(Math.Abs(road.SignedOffset(driver.Rig.Units[0].Axle)), 0, 1.2);
            if (tick % 30 == 0) VehicleKinematics.Restore(driver.Rig.Capture());
        }
        Assert.Equal(road.Length, position, 3);
    }
}
