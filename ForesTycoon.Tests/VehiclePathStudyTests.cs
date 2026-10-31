namespace ForesTycoon.Tests;

public class VehiclePathStudyTests
{
    private static RigProfile Truck => new(SteeringGeometry.Axle, 4, 0, 1.1, 2,
        [new("truck.rear.0", 0, 0, 2, .5), new("truck.front", 0, 4, 2, .5, true), new("trailer.rear.0", 1, 0, 2, .5)],
        [new(-2, 8, 1.3)]);

    [Fact]
    public void ExportContainsEveryAxleAndWheelAndRemainsValidXmlInHungarianCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try {
            System.Globalization.CultureInfo.CurrentCulture = new("hu-HU");
            var study = new VehiclePathStudy(Truck, new(StudyManoeuvre.SCurve, 20), StudyBody.Defaults(Truck));
            string svg = study.ToSvg(3.5);
            var doc = System.Xml.Linq.XDocument.Parse(svg);
            var ids = doc.Descendants().Select(e => (string?)e.Attribute("id")).ToHashSet();
            for (int a = 0; a < Truck.Axles.Length; a++) {
                Assert.Contains("axle-" + a, ids); Assert.Contains("wheel-left-" + a, ids); Assert.Contains("wheel-right-" + a, ids);
            }
            Assert.Contains("swept-area", ids); Assert.DoesNotContain("NaN", svg); Assert.DoesNotContain("Infinity", svg);
        } finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void TwoStageDrawbarTrailerMatchesAllSteadyRadiiAndEnvelopeIncludesExposedTyres()
    {
        var profile = VehicleGeometryProfiles.Truck(GameTuning.FromOverrides(new Dictionary<string, double> { ["TruckTrailerLayout"] = 2 }));
        Assert.Equal(2, profile.Trailers.Length);
        var bodies = StudyBody.Defaults(profile).Select(b => b with { Width = .2 }).ToArray();
        var study = new VehiclePathStudy(profile, new(StudyManoeuvre.SteadyCircle, 25), bodies);
        Assert.Null(study.Failure);
        foreach (var r in study.Radii) Assert.InRange(Math.Abs(r.Observed - r.Steady), 0, .01);
        Assert.True(study.LeftExtent > .2);
        Assert.All(study.Samples, s => Assert.Equal(profile.Axles.Length * 2 + bodies.Length + 2, s.Footprints.Length));
    }

    [Theory]
    [InlineData(StudyManoeuvre.QuarterTurn)]
    [InlineData(StudyManoeuvre.SCurve)]
    [InlineData(StudyManoeuvre.SteadyCircle)]
    internal void EveryAxleAndWheelHasATraceAndTheFirstAxleFollowsTheGuide(StudyManoeuvre manoeuvre)
    {
        var profile = Truck; var road = new StudyRoad(manoeuvre, 20);
        var study = new VehiclePathStudy(profile, road, StudyBody.Defaults(profile));
        Assert.Null(study.Failure); Assert.Equal(road.Length, study.Samples[^1].Distance);
        Assert.All(study.Samples, sample => {
            Assert.Equal(profile.Axles.Length, sample.Axles.Length); Assert.Equal(profile.Axles.Length * 2, sample.Wheels.Length);
            var front = sample.Units[0].Axle + PlanPoint.Forward(sample.Units[0].Yaw) * profile.FrontLength;
            Assert.InRange((front - road.Sample(sample.Distance).Point).Length, 0, 1e-9);
            var hitch = sample.Units[0].Axle + PlanPoint.Forward(sample.Units[0].Yaw) * profile.Trailers[0].HitchX;
            Assert.InRange(Math.Abs((hitch - sample.Units[1].Axle).Length - 8), 0, 1e-9);
        });
        Assert.True(study.LeftExtent > 1); Assert.True(study.RightExtent > 1); Assert.NotEmpty(study.Sweeps);
    }

    [Fact]
    public void SteadyCircleMatchesTheTextbookRearAxleAndBothWheelRadii()
    {
        var profile = Truck; var road = new StudyRoad(StudyManoeuvre.SteadyCircle, 20);
        var study = new VehiclePathStudy(profile, road, StudyBody.Defaults(profile));
        Assert.Null(study.Failure);
        double rear = ForestRoadSweptPath.RearRadius(20, 4, 2, 0, 8);
        var radius = study.Radii.Single(r => r.Axle == 2 && r.Left == null);
        Assert.Equal(rear, radius.Steady, 9);
        foreach (var r in study.Radii) Assert.InRange(Math.Abs(r.Observed - r.Steady), 0, .005);
    }

    [Fact]
    public void RightAndLeftManoeuvresAreMirrorImagesAndSCurveChangesCurvature()
    {
        var profile = Truck; var left = new StudyRoad(StudyManoeuvre.SCurve, 20); var right = new StudyRoad(StudyManoeuvre.SCurve, 20, right: true);
        var a = new VehiclePathStudy(profile, left, StudyBody.Defaults(profile)); var b = new VehiclePathStudy(profile, right, StudyBody.Defaults(profile));
        Assert.Equal(a.Samples.Length, b.Samples.Length);
        for (int i = 0; i < a.Samples.Length; i += 11) {
            Assert.Equal(a.Samples[i].Wheels[0].Position.X, b.Samples[i].Wheels[1].Position.X, 7);
            Assert.Equal(a.Samples[i].Wheels[0].Position.Y, -b.Samples[i].Wheels[1].Position.Y, 7);
        }
        Assert.True(left.Sample(left.Lead + 1).Curvature > 0);
        Assert.True(left.Sample(left.FirstArcEnd + left.Gap + 1).Curvature < 0);
        Assert.Equal(0, left.Sample(left.Length).Yaw);
    }

    [Fact]
    public void TightTurnsStopWithADiagnosticAndStudyContoursDoNotChangeTheVehicleProfile()
    {
        var profile = Truck; var saved = profile.Copy();
        var bodies = StudyBody.Defaults(profile); bodies[0] = bodies[0] with { Width = 4, Front = 8 };
        var study = new VehiclePathStudy(profile, new(StudyManoeuvre.QuarterTurn, 2), bodies);
        Assert.NotNull(study.Failure); Assert.True(study.Samples[^1].Distance < study.Road.Length);
        Assert.Equal(saved.FrontLength, profile.FrontLength); Assert.Equal(saved.Axles, profile.Axles);
        Assert.All(study.Sweeps, p => Assert.InRange(p.Length, 3, 8));
    }

    [Fact]
    public void ArticulatedForwarderHasAClosedJointAndTheCorrectSteadyRearRadius()
    {
        var profile = VehicleGeometryProfiles.Machine(GameTuning.Default, ForestMachineKind.Forwarder);
        var study = new VehiclePathStudy(profile, new(StudyManoeuvre.SteadyCircle, 20), StudyBody.Defaults(profile));
        Assert.Null(study.Failure);
        var rear = study.Radii.First(r => profile.Axles[r.Axle].Unit == 1 && r.Left == null);
        Assert.InRange(Math.Abs(rear.Observed - rear.Steady), 0, .005);
        Assert.All(study.Samples, sample => {
            var frontJoint = sample.Units[0].Axle - PlanPoint.Forward(sample.Units[0].Yaw) * profile.FrontLength;
            var rearJoint = sample.Units[1].Axle + PlanPoint.Forward(sample.Units[1].Yaw) * profile.RearLength;
            Assert.InRange((frontJoint - rearJoint).Length, 0, 1e-9);
        });
    }
}
