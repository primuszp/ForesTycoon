using OpenTK.Mathematics;

namespace ForesTycoon.Tests;

public class VehiclePresentationTests
{
    private sealed class Model : ITruckModel
    {
        public float Radius => 3;
        public float Width => .6f;
        public float Wheelbase => .8f;
        public float AxleMidpoint => 1;
        public float Kingpin => .6f;
        public float TrailerAxle => -1;
        public float WheelTrack => .4f;
        public float WheelRadius => .12f;
        public void Draw(Matrix4 transform, float cargoFill, float wheelAngle, float curvature = 0, float scale = 1,
            Matrix4? suspension = null, float outlineWidth = 0, float articulation = 0, float trailerPitch = 0, RigModelFrames? rigFrames = null) { }
        public void Dispose() { }
    }

    [Fact]
    public void AxleAndHitchEditsDoNotResizeTheExistingTruckOrTrailerBodies()
    {
        var map = new TerrainMap(TerrainSettings.Default.WithNodeSize(17, 42), (_, _) => 4);
        var profile = VehicleGeometryProfiles.Truck(GameTuning.Default);
        var model = new Model(); float legacyScale = 3.1f * DioramaScale.TruckLaneFill / model.Width;
        var rig = new VehicleKinematics(profile, default, .3);
        var first = RigRenderer.TruckFrames(rig, model, map, legacyScale);
        var custom = GameTuning.FromOverrides(new Dictionary<string, double> {
            ["TruckWheelbase"] = 8, ["TruckTrack"] = 3, ["TruckTrailerLength"] = 12, ["TruckHitchOffset"] = -3 });
        var second = RigRenderer.TruckFrames(new(VehicleGeometryProfiles.Truck(custom), default, .3), model, map, legacyScale);
        foreach (var frame in new[] { first.Tractor, first.Trailer, second.Tractor, second.Trailer }) {
            Assert.Equal(legacyScale, frame.Row0.Xyz.Length, 5);
            Assert.Equal(legacyScale, frame.Row1.Xyz.Length, 5);
            Assert.Equal(legacyScale, frame.Row2.Xyz.Length, 5);
        }
        float configuredHitch = model.Kingpin + (-3 + 1.5f) * (model.Wheelbase / 4);
        Vector3 tractorHitch = Vector3.TransformPosition(new Vector3(configuredHitch, 0, model.WheelRadius * 2), second.Tractor);
        Vector3 trailerHitch = Vector3.TransformPosition(new Vector3(model.Kingpin, 0, model.WheelRadius * 2), second.Trailer);
        Assert.InRange((tractorHitch - trailerHitch).Length, 0, .0001f);
    }

    [Fact]
    public void ABindingEqualToTheProfilePutsEveryWheelAndHitchOnTheModelWithoutMovingThePath()
    {
        var custom = GameTuning.FromOverrides(new Dictionary<string, double> {
            ["TruckWheelbase"] = 5.5, ["TruckTrack"] = 2.4, ["TruckWheelRadius"] = .6, ["TruckHitchOffset"] = -.8, ["TruckTrailerLength"] = 9, ["TruckAxleSpacing"] = 1.5,
            ["TruckModelWheelbase"] = 5.5, ["TruckModelTrack"] = 2.4, ["TruckModelWheelRadius"] = .6, ["TruckModelHitch"] = -.8, ["TruckModelTrailerLength"] = 9 });
        var model = new Model(); const float scale = 3;
        var rig = new VehicleKinematics(VehicleGeometryProfiles.Truck(custom), default, 0);
        var frame = RigRenderer.TruckFrames(rig, model, scale, (_, _) => 0, VehicleGeometryProfiles.TruckBinding(custom));
        var wheels = frame.Visual.Wheels; var units = frame.Visual.Units;
        float rear = model.AxleMidpoint - model.Wheelbase / 2;
        int front = Array.FindIndex(rig.Profile.Axles, a => a.Steered) * 2;
        Vector3 Model(float x, float y = 0) => Vector3.TransformPosition(new Vector3(x, y, 0), frame.Tractor);
        Assert.Equal(Model(rear + model.Wheelbase).X, wheels[front].Position.X, 3);
        Assert.Equal(Model(rear + model.Wheelbase, model.WheelTrack / 2).Y, wheels[front].Position.Y, 3);
        Vector3 hitch = Vector3.TransformPosition(new Vector3(model.Kingpin, 0, model.WheelRadius * 2), frame.Tractor);
        Assert.Equal(hitch.X, frame.Visual.Hitches[0].X, 3);
        Vector3 trailerAxle = Vector3.TransformPosition(new Vector3(model.TrailerAxle, 0, 0), frame.Trailer);
        Assert.Equal(trailerAxle.X, units[1].Axle.X, 3);
        Assert.Equal(frame.Visual.Appearance.RadiusScale * .6f, model.WheelRadius * scale, 4);
        Assert.Equal(rig.Units[1], new VehicleKinematics(VehicleGeometryProfiles.Truck(custom), default, 0).Units[1]);
    }

    [Fact]
    public void TheDefaultBindingKeepsTheShippedAppearanceAndCustomBindingsAreSavedTuning()
    {
        var model = new Model(); var rig = new VehicleKinematics(VehicleGeometryProfiles.Truck(GameTuning.Default), default, .3);
        var implicitBinding = RigRenderer.TruckFrames(rig, model, 3, (_, _) => 0);
        var explicitBinding = RigRenderer.TruckFrames(rig, model, 3, (_, _) => 0, GameTuning.Default.Truck.ModelBinding);
        Assert.Equal(implicitBinding.Tractor, explicitBinding.Tractor); Assert.Equal(implicitBinding.Trailer, explicitBinding.Trailer);
        Assert.Equal(implicitBinding.Visual.Appearance, new RigAppearance(3, model.Wheelbase * 3 / 4, model.WheelTrack * 3 / 2, model.WheelRadius * 3 / .5f,
            (model.Kingpin - model.TrailerAxle) * 3 / 6, (model.Kingpin - (model.AxleMidpoint - model.Wheelbase / 2)) * 3 + 1.5f * (model.Wheelbase * 3 / 4)));
        var tuned = GameTuning.FromOverrides(new Dictionary<string, double> { ["ForwarderModelFrontLength"] = 2.6 });
        Assert.Equal(2.6, VehicleGeometryProfiles.MachineBinding(tuned, ForestMachineKind.Forwarder).FrontLength);
        Assert.Equal(2.2, GameTuning.Default[Tune.ForwarderModelFrontLength], 9);
    }

    [Fact]
    public void TheRepositoryTruckSurveyFindsItsAxlesWithoutAGraphicsContext()
    {
        var survey = VehicleModelSurvey.Truck();
        Assert.False(survey.Jointed);
        Assert.Equal(2, survey.Axles(0).Length);
        Assert.Equal(survey.Wheels.Max(w => w.X), survey.Axles(0)[0]);
        Assert.InRange(survey.Track(0), .5, 1.2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TractorAndTrailerStaySupportedAcrossACrestAndASagWithoutStretching(int terrain)
    {
        var model = new Model(); const float scale = 3;
        float Height(float x, float y) => (terrain == 0 ? Math.Max(0, x * .3f) : terrain == 1 ? 2 - Math.Abs(x) * .3f : Math.Abs(x) * .3f) + y * .12f;
        var rig = new VehicleKinematics(VehicleGeometryProfiles.Truck(GameTuning.Default), default, 0);
        var frame = RigRenderer.TruckFrames(rig, model, scale, Height);
        Vector3 Coupler(Matrix4 transform) => Vector3.TransformPosition(new Vector3(model.Kingpin, 0, model.WheelRadius * 2), transform);
        Assert.InRange((Coupler(frame.Tractor) - Coupler(frame.Trailer)).Length, 0, .0001f);
        var rear = Vector3.TransformPosition(new Vector3(model.TrailerAxle, 0, model.WheelRadius), frame.Trailer);
        float wheelRadius = model.WheelRadius * scale;
        Assert.InRange(Math.Abs(rear.Z - Height(rear.X, rear.Y) - wheelRadius - .025f), 0, .002f);
        Assert.InRange(Math.Abs(frame.Tractor.Row0.Z - frame.Trailer.Row0.Z), .1f, 3);
        Assert.Equal(scale, frame.Tractor.Row0.Xyz.Length, 5); Assert.Equal(scale, frame.Trailer.Row0.Xyz.Length, 5);
        var frontRear = Vector3.TransformPosition(new Vector3(model.AxleMidpoint - model.Wheelbase / 2, 0, model.WheelRadius), frame.Tractor);
        Assert.InRange(Math.Abs(frontRear.Z - Height(frontRear.X, frontRear.Y) - wheelRadius - .025f), 0, .002f);
    }

    [Theory]
    [InlineData(2.5f, 5)]
    [InlineData(3, 5)]
    [InlineData(2.5f, 7)]
    public void TrailRutsHaveExactlyTheDisplayedWheelTrack(float trackMetres, float tileWidth)
    {
        var profile = VehicleGeometryProfiles.Machine(GameTuning.Default, ForestMachineKind.Forwarder);
        var axles = profile.Axles.Select(a => a with { Track = trackMetres }).ToArray();
        var rig = new VehicleKinematics(profile with { Axles = axles }, default, .4);
        var appearance = new RigAppearance(.42f, .42f, .42f, .42f);
        var pose = new RigVisualPose(rig, appearance);
        double wheelSpacing = (pose.Wheels[0].Position - pose.Wheels[1].Position).Length;
        float rutSpacing = Terrain.RutGaugeFraction(trackMetres * appearance.TrackScale, tileWidth) * 2 * tileWidth;
        Assert.Equal(wheelSpacing, rutSpacing, 5);
    }
}
