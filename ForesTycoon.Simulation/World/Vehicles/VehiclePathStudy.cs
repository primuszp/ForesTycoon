using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon;

internal enum StudyManoeuvre { QuarterTurn, SCurve, SteadyCircle }
internal readonly record struct StudyGuide(PlanPoint Point, double Yaw, double Curvature);
internal sealed record StudyArc(PlanPoint Centre, double Radius, double Start, double End);

/// <summary>Metric construction line followed by the first axle (or front equivalent bogie).</summary>
internal sealed class StudyRoad
{
    internal StudyManoeuvre Manoeuvre { get; }
    internal double Radius { get; }
    internal double Lead { get; }
    internal double Gap { get; }
    internal int Hand { get; }
    internal double ArcLength => Radius * Math.PI * (Manoeuvre == StudyManoeuvre.SteadyCircle ? 6 : .5);
    internal double FirstArcEnd => Lead + ArcLength;
    internal double Length => Lead * 2 + ArcLength * (Manoeuvre == StudyManoeuvre.SCurve ? 2 : 1) +
        (Manoeuvre == StudyManoeuvre.SCurve ? Gap : 0);
    internal StudyArc[] Arcs { get; }

    internal StudyRoad(StudyManoeuvre manoeuvre, double radius, double lead = 20, double gap = 10, bool right = false)
    {
        if (!Enum.IsDefined(manoeuvre) || !double.IsFinite(radius) || radius < 2 || radius > 200 ||
            !double.IsFinite(lead) || lead < 1 || lead > 100 || !double.IsFinite(gap) || gap < 0 || gap > 100)
            throw new ArgumentException("Hibás pályaméretek.");
        Manoeuvre = manoeuvre; Radius = radius; Lead = lead; Gap = gap; Hand = right ? -1 : 1;
        Arcs = manoeuvre == StudyManoeuvre.SCurve ?
            [new(new(lead, Hand * radius), radius, lead, FirstArcEnd),
             new(new(lead + radius * 2, Hand * (radius + gap)), radius, FirstArcEnd + gap, FirstArcEnd + gap + ArcLength)] :
            [new(new(lead, Hand * radius), radius, lead, FirstArcEnd)];
    }

    internal StudyGuide Sample(double distance)
    {
        distance = Math.Clamp(distance, 0, Length);
        double x, y, yaw, curvature = 0;
        if (distance <= Lead) { x = distance; y = yaw = 0; }
        else if (distance < FirstArcEnd) {
            double angle = (distance - Lead) / Radius;
            x = Lead + Radius * Math.Sin(angle); y = Radius * (1 - Math.Cos(angle)); yaw = angle; curvature = 1 / Radius;
        } else if (Manoeuvre == StudyManoeuvre.SteadyCircle) {
            x = Lead + distance - FirstArcEnd; y = yaw = 0;
        } else if (Manoeuvre == StudyManoeuvre.QuarterTurn || distance <= FirstArcEnd + Gap) {
            x = Lead + Radius; y = Radius + distance - FirstArcEnd; yaw = Math.PI * .5;
        } else if (distance < FirstArcEnd + Gap + ArcLength) {
            double angle = (distance - FirstArcEnd - Gap) / Radius;
            x = Lead + Radius + Radius * (1 - Math.Cos(angle)); y = Radius + Gap + Radius * Math.Sin(angle);
            yaw = Math.PI * .5 - angle; curvature = -1 / Radius;
        } else {
            x = Lead + Radius * 2 + distance - (FirstArcEnd + Gap + ArcLength); y = Radius * 2 + Gap; yaw = 0;
        }
        return new(new(x, Hand * y), Hand * yaw, Hand * curvature);
    }

    internal double SignedOffset(PlanPoint point)
    {
        double best = double.PositiveInfinity, offset = 0;
        void Consider(double at) {
            var p = Sample(at); var delta = point - p.Point;
            if (delta.Length < best) { best = delta.Length; offset = delta.Dot(PlanPoint.Left(p.Yaw)); }
        }
        void Line(double a, double b) {
            var first = Sample(a); double t = (point - first.Point).Dot(PlanPoint.Forward(first.Yaw));
            Consider(a + Math.Clamp(t, 0, b - a));
        }
        Line(0, Lead);
        foreach (var arc in Arcs) {
            double angle = Math.Atan2((point.Y - arc.Centre.Y) * Hand, point.X - arc.Centre.X);
            double local = arc == Arcs[0] ? angle + Math.PI * .5 : Math.PI - angle;
            local %= Math.Tau; if (local < 0) local += Math.Tau;
            Consider(arc.Start + Math.Clamp(local * Radius, 0, arc.End - arc.Start));
            Consider(arc.Start); Consider(arc.End);
        }
        if (Manoeuvre == StudyManoeuvre.SCurve) Line(FirstArcEnd, FirstArcEnd + Gap);
        Line(Length - Lead, Length);
        return offset;
    }
}

internal sealed record StudyBody(double Rear, double Front, double Width)
{
    internal void Validate()
    {
        if (!double.IsFinite(Rear) || !double.IsFinite(Front) || !double.IsFinite(Width) || Rear >= Front ||
            Math.Abs(Rear) > 50 || Math.Abs(Front) > 50 || Width < .2 || Width > 10)
            throw new ArgumentException("A vizsgálati kontúr méretei hibásak.");
    }
    internal static StudyBody[] Defaults(RigProfile profile)
    {
        int active = profile.Steering == SteeringGeometry.Articulated ? 2 : 1;
        return Enumerable.Range(0, active + profile.Trailers.Length).Select(i => {
            var axles = profile.Axles.Where(a => a.Unit == i).ToArray();
            double rear = axles.Min(a => a.X) - 1.3, front = axles.Max(a => a.X) + 1.2;
            if (profile.Steering == SteeringGeometry.Articulated) {
                if (i == 0) rear = Math.Min(rear, -profile.FrontLength);
                if (i == 1) front = Math.Max(front, profile.RearLength);
            }
            if (i >= active) {
                bool dolly = i < active + profile.Trailers.Length - 1;
                front = dolly ? .6 : Math.Max(front, profile.Trailers[i - active].Length);
                if (dolly) rear = -.6;
            }
            return new StudyBody(rear, front, axles.Max(a => a.Track) + .4);
        }).ToArray();
    }
}

internal sealed record StudySample(double Distance, RigUnitPose[] Units, double Steer, PlanPoint[] Axles,
    RigWheelPose[] Wheels, PlanPoint[][] Footprints);
internal sealed record StudyRadius(string Name, int Axle, bool? Left, double Observed, double Steady);

/// <summary>Front-guided, no-slip pursuit construction. This is an offline geometric test, not a gameplay controller.
/// RK2 integrates the same axle/hitch constraints as VehicleKinematics; the guide front point is projected exactly.</summary>
internal sealed partial class VehiclePathStudy
{
    internal RigProfile Profile { get; }
    internal StudyRoad Road { get; }
    internal StudyBody[] Bodies { get; }
    internal StudySample[] Samples { get; }
    internal PlanPoint[][] Sweeps { get; }
    internal StudyRadius[] Radii { get; }
    internal string Failure { get; }
    internal double LeftExtent { get; }
    internal double RightExtent { get; }
    internal const double Step = .05;
    internal double SamplingStep { get; }

    internal VehiclePathStudy(RigProfile profile, StudyRoad road, StudyBody[] bodies, double speed = 1)
    {
        profile.Validate();
        if (speed <= 0 || speed > 10 || !double.IsFinite(speed)) throw new ArgumentException("Hibás vizsgálati sebesség.");
        Profile = profile.Copy(); Road = road; Bodies = bodies.ToArray(); SamplingStep = Math.Max(Step, road.Length / 10000);
        int count = (profile.Steering == SteeringGeometry.Articulated ? 2 : 1) + profile.Trailers.Length;
        if (bodies.Length != count) throw new ArgumentException("Minden járműegységhez szükséges vizsgálati kontúr.");
        foreach (var b in bodies) b.Validate();
        var samples = new List<StudySample>(); var sweeps = new List<PlanPoint[]>();
        double[] yaw = new double[count]; double priorSteer = 0;
        double left = 0, right = 0;
        for (int step = 0; ; step++) {
            double at = Math.Min(road.Length, step * SamplingStep);
            if (road.Length - at < 1e-8) at = road.Length;
            if (step > 0) {
                double from = samples[^1].Distance, ds = at - from;
                var first = Rates(yaw, from); var mid = yaw.Select((a, i) => a + first[i] * ds * .5).ToArray();
                var rate = Rates(mid, from + ds * .5);
                for (int i = 0; i < yaw.Length; i++) yaw[i] = VehicleKinematics.Wrap(yaw[i] + rate[i] * ds);
            }
            var guide = road.Sample(at);
            if (profile.Steering == SteeringGeometry.Articulated) yaw[0] = VehicleKinematics.Wrap(guide.Yaw);
            double steer = VehicleKinematics.Wrap(profile.Steering == SteeringGeometry.Axle ? guide.Yaw - yaw[0] : yaw[0] - yaw[1]);
            if (Math.Abs(steer) > profile.EquivalentSteerLimit) { Failure = "A kormányzási szöghatár miatt ez az ív nem követhető."; break; }
            if (step > 0 && Math.Abs(VehicleKinematics.Wrap(steer - priorSteer)) * speed / (at - samples[^1].Distance) > profile.SteerRate + 1e-5) {
                Failure = "A kormányzás sebességhatára túllépve. Csökkentsd a vizsgálati sebességet vagy növeld az ívsugarat."; break;
            }
            var units = Poses(yaw, guide);
            int active = profile.Steering == SteeringGeometry.Articulated ? 2 : 1;
            bool folded = profile.Trailers.Where((t, i) => Math.Abs(VehicleKinematics.Wrap(yaw[active + i] - yaw[active + i - 1])) > t.MaxAngle).Any();
            if (folded) { Failure = "A pótkocsi elérte a csukló szöghatárát; a vizsgálat itt megállt."; break; }
            var rig = VehicleKinematics.Restore(new(profile, units, steer, new double[profile.Axles.Length * 2]));
            var wheels = rig.Wheels();
            var axles = profile.Axles.Select(a => units[a.Unit].Axle + PlanPoint.Forward(units[a.Unit].Yaw) * a.X).ToArray();
            var footprints = new List<PlanPoint[]>();
            for (int i = 0; i < units.Length; i++) footprints.Add(Rectangle(units[i].Axle, units[i].Yaw, bodies[i].Rear, bodies[i].Front, bodies[i].Width));
            for (int i = 0; i < wheels.Length; i++) {
                var a = profile.Axles[i / 2]; footprints.Add(Rectangle(wheels[i].Position, wheels[i].Yaw, -a.Radius, a.Radius, a.Radius * .6));
            }
            // Thin coupling bars are included even when the body contour excludes a drawbar.
            for (int i = 0; i < profile.Trailers.Length; i++) {
                int j = active + i; var t = profile.Trailers[i];
                footprints.Add(Rectangle(units[j].Axle, units[j].Yaw, 0, t.Length, .15));
            }
            var frame = new StudySample(at, units, steer, axles, wheels, footprints.ToArray());
            for (int i = 0; i < frame.Footprints.Length; i++) {
                var polygon = samples.Count == 0 ? frame.Footprints[i] : Hull(samples[^1].Footprints[i].Concat(frame.Footprints[i]));
                sweeps.Add(polygon);
                foreach (var p in polygon) {
                    double offset = road.SignedOffset(p); left = Math.Max(left, offset); right = Math.Max(right, -offset);
                }
            }
            samples.Add(frame); priorSteer = steer;
            if (at >= road.Length) break;
        }
        Samples = samples.ToArray(); Sweeps = sweeps.ToArray(); LeftExtent = left; RightExtent = right;
        Radii = BuildRadii();
    }

    private double[] Rates(double[] angles, double distance)
    {
        var guide = Road.Sample(distance); var velocity = new PlanPoint[angles.Length]; var w = new double[angles.Length];
        double heading = Profile.Steering == SteeringGeometry.Articulated ? guide.Yaw : angles[0];
        int active = 1;
        if (Profile.Steering == SteeringGeometry.Axle) {
            double delta = VehicleKinematics.Wrap(guide.Yaw - heading);
            velocity[0] = PlanPoint.Forward(heading) * Math.Cos(delta); w[0] = Math.Sin(delta) / Profile.FrontLength;
        } else {
            velocity[0] = PlanPoint.Forward(heading); w[0] = guide.Curvature;
            var jointVelocity = velocity[0] - PlanPoint.Left(heading) * (Profile.FrontLength * w[0]);
            w[1] = jointVelocity.Dot(PlanPoint.Left(angles[1])) / Profile.RearLength;
            velocity[1] = jointVelocity - PlanPoint.Left(angles[1]) * (Profile.RearLength * w[1]); active++;
        }
        for (int i = 0; i < Profile.Trailers.Length; i++) {
            int j = active + i; var t = Profile.Trailers[i];
            var hitchVelocity = velocity[j - 1] + PlanPoint.Left(j == 1 ? heading : angles[j - 1]) * (t.HitchX * w[j - 1]);
            w[j] = hitchVelocity.Dot(PlanPoint.Left(angles[j])) / t.Length;
            velocity[j] = PlanPoint.Forward(angles[j]) * hitchVelocity.Dot(PlanPoint.Forward(angles[j]));
        }
        return w;
    }

    private RigUnitPose[] Poses(double[] angles, StudyGuide guide)
    {
        var units = new RigUnitPose[angles.Length];
        units[0] = new(guide.Point - PlanPoint.Forward(angles[0]) * (Profile.Steering == SteeringGeometry.Axle ? Profile.FrontLength : 0), angles[0]);
        int active = 1;
        if (Profile.Steering == SteeringGeometry.Articulated) {
            var joint = units[0].Axle - PlanPoint.Forward(angles[0]) * Profile.FrontLength;
            units[1] = new(joint - PlanPoint.Forward(angles[1]) * Profile.RearLength, angles[1]); active++;
        }
        for (int i = 0; i < Profile.Trailers.Length; i++) {
            int j = active + i; var t = Profile.Trailers[i];
            var hitch = units[j - 1].Axle + PlanPoint.Forward(angles[j - 1]) * t.HitchX;
            units[j] = new(hitch - PlanPoint.Forward(angles[j]) * t.Length, angles[j]);
        }
        return units;
    }

    private StudyRadius[] BuildRadii()
    {
        var result = new List<StudyRadius>();
        double[] radii = new double[(Profile.Steering == SteeringGeometry.Articulated ? 2 : 1) + Profile.Trailers.Length];
        radii[0] = Profile.Steering == SteeringGeometry.Axle ? Root(Road.Radius * Road.Radius - Profile.FrontLength * Profile.FrontLength) : Road.Radius;
        int active = 1;
        if (Profile.Steering == SteeringGeometry.Articulated) { radii[1] = Root(radii[0] * radii[0] + Profile.FrontLength * Profile.FrontLength - Profile.RearLength * Profile.RearLength); active++; }
        for (int i = 0; i < Profile.Trailers.Length; i++) {
            var t = Profile.Trailers[i]; double before = radii[active + i - 1];
            radii[active + i] = Root(before * before + t.HitchX * t.HitchX - t.Length * t.Length);
        }
        int index = Array.FindLastIndex(Samples, s => s.Distance < Road.FirstArcEnd - .5);
        for (int a = 0; a < Profile.Axles.Length; a++) {
            var axle = Profile.Axles[a]; double r = radii[axle.Unit];
            double Observed(bool? left) => index < 1 || index >= Samples.Length - 1 ? double.PositiveInfinity :
                Circumradius(left.HasValue ? Samples[index - 1].Wheels[a * 2 + (left.Value ? 0 : 1)].Position : Samples[index - 1].Axles[a],
                    left.HasValue ? Samples[index].Wheels[a * 2 + (left.Value ? 0 : 1)].Position : Samples[index].Axles[a],
                    left.HasValue ? Samples[index + 1].Wheels[a * 2 + (left.Value ? 0 : 1)].Position : Samples[index + 1].Axles[a]);
            result.Add(new(AxleName(axle.Id), a, null, Observed(null), Root(r * r + axle.X * axle.X)));
            foreach (bool left in new[] { true, false }) {
                double radial = r - (left ? Road.Hand : -Road.Hand) * axle.Track * .5;
                result.Add(new(AxleName(axle.Id) + (left ? " · bal kerék" : " · jobb kerék"), a, left, Observed(left), Root(radial * radial + axle.X * axle.X)));
            }
        }
        return result.ToArray();
    }

    internal static double Circumradius(PlanPoint a, PlanPoint b, PlanPoint c)
    {
        var ab = b - a; var ac = c - a;
        double cross = Math.Abs(ab.X * ac.Y - ab.Y * ac.X);
        return cross < 1e-12 ? double.PositiveInfinity : ab.Length * (c - b).Length * ac.Length / (2 * cross);
    }
    private static double Root(double square) => square < 0 || !double.IsFinite(square) ? double.NaN : Math.Sqrt(square);
    internal static PlanPoint[] Rectangle(PlanPoint origin, double yaw, double rear, double front, double width)
    {
        PlanPoint P(double x, double y) => origin + PlanPoint.Forward(yaw) * x + PlanPoint.Left(yaw) * y;
        return [P(rear, -width * .5), P(front, -width * .5), P(front, width * .5), P(rear, width * .5)];
    }
    internal static PlanPoint[] Hull(IEnumerable<PlanPoint> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (sorted.Length < 3) return sorted;
        double Cross(PlanPoint a, PlanPoint b, PlanPoint c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        var hull = new List<PlanPoint>();
        foreach (var p in sorted) { while (hull.Count > 1 && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
        int bottom = hull.Count;
        for (int i = sorted.Length - 2; i >= 0; i--) { var p = sorted[i]; while (hull.Count > bottom && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
        hull.RemoveAt(hull.Count - 1); return hull.ToArray();
    }
    internal static string AxleName(string id)
    {
        string name = id.StartsWith("truck.front") ? "Teherautó első" : id.StartsWith("truck.rear") ? "Teherautó hátsó" :
            id.StartsWith("trailer.turntable") ? "Forgózsámoly" : id.StartsWith("trailer.rear") ? "Pótkocsi hátsó" :
            id.StartsWith("front") ? "Első váz" : "Hátsó váz";
        return int.TryParse(id.Split('.')[^1], out int index) ? name + " " + (index + 1) : name;
    }
}
