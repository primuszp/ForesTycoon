using System;
using System.Collections.Generic;
using System.Linq;

namespace ForesTycoon;

internal enum SteeringGeometry { Axle, Articulated }
internal readonly record struct PlanPoint(double X, double Y)
{
    public static PlanPoint operator +(PlanPoint a, PlanPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static PlanPoint operator -(PlanPoint a, PlanPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static PlanPoint operator *(PlanPoint a, double b) => new(a.X * b, a.Y * b);
    internal double Length => Math.Sqrt(X * X + Y * Y);
    internal double Dot(PlanPoint b) => X * b.X + Y * b.Y;
    internal static PlanPoint Forward(double yaw) => new(Math.Cos(yaw), Math.Sin(yaw));
    internal static PlanPoint Left(double yaw) => new(-Math.Sin(yaw), Math.Cos(yaw));
}

/// <summary>Axle coordinates are metres relative to its unit's equivalent axle; +X forward, +Y left.</summary>
internal sealed record RigAxle(string Id, int Unit, double X, double Track, double Radius, bool Steered = false);
/// <summary>A passive unit. HitchX is measured on the preceding unit, Length from hitch to its axle group.</summary>
internal sealed record RigTrailer(double HitchX, double Length, double MaxAngle);
internal sealed record RigProfile(SteeringGeometry Steering, double FrontLength, double RearLength,
    double MaxSteer, double SteerRate, RigAxle[] Axles, RigTrailer[] Trailers)
{
    internal void Validate()
    {
        static bool Positive(double v) => double.IsFinite(v) && v > 0;
        if (!Enum.IsDefined(Steering) || !Positive(FrontLength) || FrontLength > 20 ||
            !double.IsFinite(RearLength) || RearLength < 0 || RearLength > 20 ||
            Steering == SteeringGeometry.Articulated && RearLength == 0 ||
            !Positive(MaxSteer) || MaxSteer >= Math.PI / 2 || !Positive(SteerRate) || SteerRate > 10 ||
            Axles == null || Axles.Length is < 2 or > 24 || Trailers == null || Trailers.Length > 4)
            throw new ArgumentException("Invalid vehicle steering geometry.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int units = (Steering == SteeringGeometry.Articulated ? 2 : 1) + Trailers.Length;
        foreach (var a in Axles)
            if (a == null || string.IsNullOrWhiteSpace(a.Id) || !ids.Add(a.Id) || a.Unit < 0 || a.Unit >= units ||
                !double.IsFinite(a.X) || Math.Abs(a.X) > 20 || !Positive(a.Track) || a.Track > 5 || !Positive(a.Radius) || a.Radius > 2)
                throw new ArgumentException("Invalid vehicle axle.");
        for (int i = 0; i < units; i++)
            if (!Axles.Any(a => a.Unit == i)) throw new ArgumentException("Every vehicle unit needs an axle group.");
        if (Steering == SteeringGeometry.Axle && !Axles.Any(a => a.Unit == 0 && a.Steered && Math.Abs(a.X) > .01))
            throw new ArgumentException("Axle steering needs a steered axle away from the reference axle.");
        foreach (var t in Trailers)
            if (t == null || !double.IsFinite(t.HitchX) || Math.Abs(t.HitchX) > 20 || !Positive(t.Length) || t.Length > 25 ||
                !Positive(t.MaxAngle) || t.MaxAngle >= Math.PI / 2)
                throw new ArgumentException("Invalid vehicle hitch.");
    }
    internal RigProfile Copy() => this with { Axles = (RigAxle[])Axles.Clone(), Trailers = (RigTrailer[])Trailers.Clone() };
    internal double MinimumRadius => Steering == SteeringGeometry.Axle ?
        Axles.Where(a => a.Unit == 0 && a.Steered).Max(a => Math.Abs(a.X) / Math.Tan(MaxSteer) + a.Track * .5) :
        (RearLength + FrontLength * Math.Cos(MaxSteer)) / Math.Sin(MaxSteer);
    // The inner Ackermann wheel reaches its mechanical stop before the equivalent bicycle wheel does.
    internal double EquivalentSteerLimit => Steering == SteeringGeometry.Axle ? Math.Atan(FrontLength / MinimumRadius) : MaxSteer;
}

internal readonly record struct RigUnitPose(PlanPoint Axle, double Yaw);
internal readonly record struct RigWheelPose(string AxleId, bool Left, PlanPoint Position, double Yaw, double Roll);
internal sealed record RigState(RigProfile Profile, RigUnitPose[] Units, double Steer, double[] Rolls, int DriveGear = 0, long DriveLeg = 0);

/// <summary>Low-speed planar no-slip model. No renderer, tile scale or road sampling participates in these constraints.</summary>
internal sealed class VehicleKinematics
{
    internal RigProfile Profile { get; }
    private RigUnitPose[] units;
    private double[] rolls;
    internal double Steer { get; private set; }
    internal IReadOnlyList<RigUnitPose> Units => units;
    internal bool Jackknifed { get; private set; }
    private VehicleKinematics(RigState state)
    {
        Profile = state.Profile; units = state.Units; rolls = state.Rolls; Steer = state.Steer;
    }
    internal static VehicleKinematics FromPresentation(RigState state) => new(state);
    internal RigState CapturePresentation() => new(Profile, (RigUnitPose[])units.Clone(), Steer, (double[])rolls.Clone());

    internal VehicleKinematics(RigProfile profile, PlanPoint axle, double yaw)
    {
        profile.Validate(); Profile = profile.Copy();
        if (!double.IsFinite(axle.X) || !double.IsFinite(axle.Y) || !double.IsFinite(yaw)) throw new ArgumentException("Invalid vehicle pose.");
        units = new RigUnitPose[(profile.Steering == SteeringGeometry.Articulated ? 2 : 1) + profile.Trailers.Length];
        rolls = new double[profile.Axles.Length * 2];
        units[0] = new(axle, Wrap(yaw));
        int start = 1;
        if (profile.Steering == SteeringGeometry.Articulated) {
            units[1] = new(axle - PlanPoint.Forward(yaw) * (profile.FrontLength + profile.RearLength), Wrap(yaw)); start++;
        }
        for (int i = 0; i < profile.Trailers.Length; i++) {
            var t = profile.Trailers[i]; var before = units[start + i - 1];
            units[start + i] = new(before.Axle + PlanPoint.Forward(yaw) * (t.HitchX - t.Length), Wrap(yaw));
        }
    }

    /// <summary>Signed m/s; steering command is wheel angle or active frame angle, radians. Returns false at a hitch limit.</summary>
    internal bool Advance(double speed, double command, double dt)
    {
        if (!double.IsFinite(speed) || !double.IsFinite(command) || !double.IsFinite(dt) || dt < 0 || dt > 60)
            throw new ArgumentOutOfRangeException(nameof(dt));
        command = Math.Clamp(command, -Profile.EquivalentSteerLimit, Profile.EquivalentSteerLimit);
        while (dt > 1e-12) {
            double h = Math.Min(dt, Math.Min(1.0 / 60, .05 / Math.Max(.01, Math.Abs(speed))));
            double nextSteer = Steer + Math.Clamp(command - Steer, -Profile.SteerRate * h, Profile.SteerRate * h);
            double rate = (nextSteer - Steer) / h;
            var first = Derivative(units, speed, Steer, rate);
            var mid = new RigUnitPose[units.Length];
            for (int i = 0; i < units.Length; i++) mid[i] = new(units[i].Axle + first.V[i] * (h * .5), units[i].Yaw + first.W[i] * (h * .5));
            var derivative = Derivative(mid, speed, (Steer + nextSteer) * .5, rate);
            var next = new RigUnitPose[units.Length];
            next[0] = new(units[0].Axle + derivative.V[0] * h, Wrap(units[0].Yaw + derivative.W[0] * h));
            int start = 1;
            if (Profile.Steering == SteeringGeometry.Articulated) {
                double yaw = Wrap(next[0].Yaw - nextSteer);
                var joint = next[0].Axle - PlanPoint.Forward(next[0].Yaw) * Profile.FrontLength;
                next[1] = new(joint - PlanPoint.Forward(yaw) * Profile.RearLength, yaw); start++;
            }
            for (int i = 0; i < Profile.Trailers.Length; i++) {
                int j = start + i; var t = Profile.Trailers[i];
                double yaw = Wrap(units[j].Yaw + derivative.W[j] * h);
                if (Math.Abs(Wrap(yaw - next[j - 1].Yaw)) > t.MaxAngle) { Jackknifed = true; return false; }
                var hitch = next[j - 1].Axle + PlanPoint.Forward(next[j - 1].Yaw) * t.HitchX;
                next[j] = new(hitch - PlanPoint.Forward(yaw) * t.Length, yaw);
            }
            for (int i = 0; i < Profile.Axles.Length; i++) {
                var a = Profile.Axles[i]; int j = a.Unit;
                var f = PlanPoint.Forward(mid[j].Yaw); var n = PlanPoint.Left(mid[j].Yaw);
                for (int side = 0; side < 2; side++) {
                    double y = (side == 0 ? 1 : -1) * a.Track * .5;
                    var velocity = derivative.V[j] + (n * a.X - f * y) * derivative.W[j];
                    double u = velocity.Dot(f), w = velocity.Dot(n);
                    double angle = a.Steered ? HalfTurn(Math.Atan2(w, u)) : 0;
                    double rolling = u * Math.Cos(angle) + w * Math.Sin(angle);
                    rolls[i * 2 + side] = Wrap(rolls[i * 2 + side] + rolling * h / a.Radius);
                }
            }
            units = next; Steer = nextSteer; dt -= h; Jackknifed = false;
        }
        return true;
    }

    private (PlanPoint[] V, double[] W) Derivative(RigUnitPose[] pose, double speed, double steer, double rate)
    {
        var v = new PlanPoint[pose.Length]; var w = new double[pose.Length];
        v[0] = PlanPoint.Forward(pose[0].Yaw) * speed;
        int start = 1;
        if (Profile.Steering == SteeringGeometry.Axle) w[0] = speed * Math.Tan(steer) / Profile.FrontLength;
        else {
            w[0] = (speed * Math.Sin(steer) + Profile.RearLength * rate) /
                (Profile.RearLength + Profile.FrontLength * Math.Cos(steer));
            w[1] = w[0] - rate;
            v[1] = v[0] - PlanPoint.Left(pose[0].Yaw) * (Profile.FrontLength * w[0])
                - PlanPoint.Left(pose[1].Yaw) * (Profile.RearLength * w[1]); start++;
        }
        for (int i = 0; i < Profile.Trailers.Length; i++) {
            int j = start + i; var t = Profile.Trailers[i];
            var hitchVelocity = v[j - 1] + PlanPoint.Left(pose[j - 1].Yaw) * (t.HitchX * w[j - 1]);
            w[j] = hitchVelocity.Dot(PlanPoint.Left(pose[j].Yaw)) / t.Length;
            v[j] = PlanPoint.Forward(pose[j].Yaw) * hitchVelocity.Dot(PlanPoint.Forward(pose[j].Yaw));
        }
        return (v, w);
    }

    internal RigWheelPose[] Wheels(double speed = 1)
    {
        var d = Derivative(units, speed, Steer, 0); var result = new RigWheelPose[rolls.Length];
        for (int i = 0; i < Profile.Axles.Length; i++) {
            var a = Profile.Axles[i]; var unit = units[a.Unit];
            var f = PlanPoint.Forward(unit.Yaw); var n = PlanPoint.Left(unit.Yaw);
            for (int side = 0; side < 2; side++) {
                double y = (side == 0 ? 1 : -1) * a.Track * .5;
                var velocity = d.V[a.Unit] + (n * a.X - f * y) * d.W[a.Unit];
                double angle = a.Steered ? HalfTurn(Math.Atan2(velocity.Dot(n), velocity.Dot(f))) : 0;
                result[i * 2 + side] = new(a.Id, side == 0, unit.Axle + f * a.X + n * y, Wrap(unit.Yaw + angle), rolls[i * 2 + side]);
            }
        }
        return result;
    }

    internal RigState Capture() => new(Profile.Copy(), (RigUnitPose[])units.Clone(), Steer, (double[])rolls.Clone());
    internal VehicleKinematics Interpolated(RigState previous, double alpha, double maximumAlpha = 1)
    {
        if (previous == null || previous.Units.Length != units.Length || previous.Rolls.Length != rolls.Length) return this;
        alpha = Math.Clamp(alpha, 0, maximumAlpha);
        double Angle(double a, double b) => Wrap(a + Wrap(b - a) * alpha);
        var result = new VehicleKinematics(new RigState(Profile, new RigUnitPose[units.Length], 0, new double[rolls.Length]));
        result.Steer = Math.Clamp(previous.Steer + (Steer - previous.Steer) * alpha, -Profile.EquivalentSteerLimit, Profile.EquivalentSteerLimit);
        result.units[0] = new(previous.Units[0].Axle * (1 - alpha) + units[0].Axle * alpha, Angle(previous.Units[0].Yaw, units[0].Yaw));
        int start = 1;
        if (Profile.Steering == SteeringGeometry.Articulated) {
            double yaw = Wrap(result.units[0].Yaw - result.Steer);
            var joint = result.units[0].Axle - PlanPoint.Forward(result.units[0].Yaw) * Profile.FrontLength;
            result.units[1] = new(joint - PlanPoint.Forward(yaw) * Profile.RearLength, yaw); start++;
        }
        for (int i = 0; i < Profile.Trailers.Length; i++) {
            int j = start + i; var t = Profile.Trailers[i];
            double yaw = Angle(previous.Units[j].Yaw, units[j].Yaw);
            yaw = Wrap(result.units[j - 1].Yaw + Math.Clamp(Wrap(yaw - result.units[j - 1].Yaw), -t.MaxAngle, t.MaxAngle));
            var hitch = result.units[j - 1].Axle + PlanPoint.Forward(result.units[j - 1].Yaw) * t.HitchX;
            result.units[j] = new(hitch - PlanPoint.Forward(yaw) * t.Length, yaw);
        }
        for (int i = 0; i < rolls.Length; i++) result.rolls[i] = Angle(previous.Rolls[i], rolls[i]);
        return result;
    }
    internal static VehicleKinematics Restore(RigState state)
    {
        if (state == null || state.Profile == null || state.Units == null || state.Rolls == null || state.DriveGear is < -1 or > 1 || state.DriveLeg < 0) throw new ArgumentException("Missing vehicle geometry state.");
        state.Profile.Validate();
        var rig = new VehicleKinematics(state.Profile, default, 0);
        if (state.Units.Length != rig.units.Length || state.Rolls.Length != rig.rolls.Length || !double.IsFinite(state.Steer) || Math.Abs(state.Steer) > state.Profile.EquivalentSteerLimit + 1e-12 ||
            state.Units.Any(p => !double.IsFinite(p.Axle.X) || !double.IsFinite(p.Axle.Y) || !double.IsFinite(p.Yaw)) || state.Rolls.Any(r => !double.IsFinite(r)))
            throw new ArgumentException("Invalid vehicle geometry state.");
        rig.units = (RigUnitPose[])state.Units.Clone(); rig.rolls = (double[])state.Rolls.Clone(); rig.Steer = state.Steer;
        // Reject disconnected saved joints rather than projecting corrupt data to a different physical position.
        int start = state.Profile.Steering == SteeringGeometry.Articulated ? 2 : 1;
        if (start == 2) {
            var frontJoint = rig.units[0].Axle - PlanPoint.Forward(rig.units[0].Yaw) * state.Profile.FrontLength;
            var rearJoint = rig.units[1].Axle + PlanPoint.Forward(rig.units[1].Yaw) * state.Profile.RearLength;
            if ((frontJoint - rearJoint).Length > 1e-5 || Math.Abs(Wrap(rig.units[0].Yaw - rig.units[1].Yaw - rig.Steer)) > 1e-6)
                throw new ArgumentException("Disconnected saved active joint.");
        }
        for (int i = 0; i < state.Profile.Trailers.Length; i++) {
            int j = start + i; var t = state.Profile.Trailers[i];
            var hitch = rig.units[j - 1].Axle + PlanPoint.Forward(rig.units[j - 1].Yaw) * t.HitchX;
            var axle = hitch - PlanPoint.Forward(rig.units[j].Yaw) * t.Length;
            if ((axle - rig.units[j].Axle).Length > 1e-5 || Math.Abs(Wrap(rig.units[j].Yaw - rig.units[j - 1].Yaw)) > t.MaxAngle)
                throw new ArgumentException("Disconnected saved passive joint.");
        }
        return rig;
    }

    internal static double Wrap(double angle) => Math.IEEERemainder(angle, 2 * Math.PI);
    private static double HalfTurn(double angle) => Math.IEEERemainder(angle, Math.PI);
}

/// <summary>Steady circular-path oracle from the forest-road text, front axle radius R1, all dimensions in metres.</summary>
internal static class ForestRoadSweptPath
{
    internal static double RearRadius(double frontRadius, double wheelbase, double rearHitch, double drawbar, double trailerWheelbase)
    {
        double[] values = [frontRadius, wheelbase, rearHitch, drawbar, trailerWheelbase];
        if (values.Any(v => !double.IsFinite(v) || v < 0) || frontRadius == 0) throw new ArgumentException("Invalid turning dimensions.");
        double square = frontRadius * frontRadius - wheelbase * wheelbase + rearHitch * rearHitch - drawbar * drawbar - trailerWheelbase * trailerWheelbase;
        return square < 0 ? double.NaN : Math.Sqrt(square);
    }
}
