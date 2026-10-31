namespace ForesTycoon.Tests;

public class RigPresentationTests
{
    [Fact]
    public void UnfinishedCooperativeTicksNeverLeakIntoThePublishedMotion()
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
        using var world = new SimulationWorld(map);
        world.Forest.Clear(); map.BuildRoadTilePath(34, 210, RoadPaving.Macadam);
        world.Logistics.Vehicles.UseCargoStops = false;
        var vehicle = world.Logistics.Vehicles.Spawn(new[] { 34, 50, 66, 82, 98, 114, 130, 146, 162, 178, 194, 210 });
        world.Update(1d / 30);
        Assert.NotNull(vehicle.Presentation.Current);
        var published = vehicle.Presentation.Current;
        var pose = vehicle.Presentation.Sample(.5f).Units[0];
        bool sawUnpublishedMotion = false;
        while (!world.ContinueUpdate(1d / 30, 1)) {
            Assert.Same(published, vehicle.Presentation.Current);
            Assert.Equal(pose, vehicle.Presentation.Sample(.5f).Units[0]);
            sawUnpublishedMotion |= vehicle.Kinematics.Units[0] != published.State.Units[0];
        }
        Assert.True(sawUnpublishedMotion);
        Assert.NotSame(published, vehicle.Presentation.Current);
    }

    [Fact]
    public void LateTicksKeepConstantSpeedMotionContinuousAtSixtyFramesPerSecond()
    {
        var clock = new FixedStepClock(30) { Speed = .25 };
        var rig = new VehicleKinematics(VehicleGeometryProfiles.Truck(GameTuning.Default), default, 0);
        var history = new RigPresentation();
        void Publish() => history.Publish(new(rig.CapturePresentation(), true));
        Publish(); rig.Advance(3, 0, clock.StepSeconds); Publish();
        int slices = 0; double previous = history.Sample(0).Units[0].Axle.X;
        for (int frame = 0; frame < 240; frame++) {
            bool CanComplete(double dt) {
                // A logical tick spans three rendering frames, with the simulation mutated in its first slice.
                if (slices++ == 0) rig.Advance(3, 0, dt);
                if (slices < 3) return false;
                Publish(); slices = 0; return true;
            }
            clock.AdvanceWork(1d / 60, CanComplete, () => false);
            double x = history.Sample(clock.MotionInterpolationAlpha).Units[0].Axle.X;
            Assert.InRange(x - previous, .012499, .012501);
            previous = x;
        }
        Assert.Equal(0, clock.DroppedSimulationSeconds);
    }

    [Fact]
    public void PredictionIsBoundedStopsAtRestAndKeepsTrailerJointsClosed()
    {
        var rig = new VehicleKinematics(VehicleGeometryProfiles.Truck(GameTuning.Default), default, 0);
        var history = new RigPresentation(); history.Publish(new(rig.CapturePresentation(), true));
        rig.Advance(2, .15, .1); history.Publish(new(rig.CapturePresentation(), true));
        var future = history.Sample(2); var capped = history.Sample(100);
        Assert.Equal(future.Units, capped.Units);
        VehicleKinematics.Restore(future.Capture()); // validates every joint and mechanical limit
        history.Publish(new(rig.CapturePresentation(), false));
        Assert.Equal(rig.Units, history.Sample(2).Units);
        Assert.Equal(rig.Capture().Rolls, history.Sample(2).Capture().Rolls);
    }
}
