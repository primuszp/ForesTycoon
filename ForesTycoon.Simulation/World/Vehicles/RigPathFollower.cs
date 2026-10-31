using System;

namespace ForesTycoon;

/// <summary>Pure-pursuit steering of the physical reference axle; progress is projected from its actual position.</summary>
internal sealed class RigPathFollower
{
    internal VehicleKinematics Rig { get; }
    internal string BlockedReason { get; private set; }
    internal double AppliedSpeed { get; private set; }
    internal double TravelledDistance { get; private set; }
    private int gear;
    internal long Leg { get; private set; }
    internal RigPathFollower(VehicleKinematics rig, int gear = 0, long leg = 0) { Rig = rig; this.gear = gear; Leg = leg; }
    internal RigState Capture() => Rig.Capture() with { DriveGear = gear, DriveLeg = Leg };
    internal void NewLeg(long leg = 0) { gear = 0; Leg = leg; BlockedReason = null; }

    internal double Advance(Func<double, PlanPoint> sample, double position, double end, double metresPerPosition,
        double distance, double dt, double maxPosition = double.PositiveInfinity, double manoeuvreRoom = 0)
    {
        TravelledDistance = 0;
        if (distance <= 0 || dt <= 0 || position >= end) return position;
        double remaining = dt;
        double speed = distance / dt;
        while (remaining > 1e-10) {
            double h = Math.Min(remaining, Math.Min(1.0 / 60, .05 / Math.Max(.01, speed)));
            var unit = Rig.Units[0];
            double lookahead = Math.Max(2, Rig.Profile.MinimumRadius * 1.2);
            double targetAt = Math.Min(end, position + lookahead / metresPerPosition);
            PlanPoint target = sample(targetAt), local = target - unit.Axle;
            var f = PlanPoint.Forward(unit.Yaw);
            if (gear == 0) {
                var departure = sample(Math.Min(end, position + .01)) - sample(position);
                gear = departure.Dot(f) >= (manoeuvreRoom > 0 ? -.866 : -.2) * departure.Length ? 1 : -1;
            }
            double sign = gear;
            if (gear < 0 && Rig.Profile.Steering == SteeringGeometry.Articulated) {
                // In reverse the rear frame is the leading unit. Aim its axle ahead of the front axle's
                // target, preserving the same logical reference point and avoiding a position jump.
                var tangent = sample(Math.Min(end, targetAt + .01)) - sample(Math.Max(0, targetAt - .01));
                if (tangent.Length > 1e-8) target += tangent * ((Rig.Profile.FrontLength + Rig.Profile.RearLength) / tangent.Length);
                unit = Rig.Units[1]; local = target - unit.Axle;
            }
            double curvature = 2 * local.Dot(PlanPoint.Left(unit.Yaw)) / Math.Max(.01, local.Dot(local));
            double alignment = sign * local.Dot(PlanPoint.Forward(unit.Yaw)) / Math.Max(.01, local.Length);
            if (alignment < 0 && Math.Abs(curvature) < .01)
                curvature = Math.CopySign(1 / Rig.Profile.MinimumRadius, local.Dot(PlanPoint.Left(unit.Yaw)) + 1e-12);
            // Short departures and sharp bends need time to steer. Without this, a fast machine travels a
            // complete turning radius before its steering actuator can reach the requested angle.
            double turnSpeed = Math.Abs(curvature) > 1e-5 ? Math.Sqrt(1.2 / Math.Abs(curvature)) : speed;
            double actualSpeed = Math.Min(speed, turnSpeed) * Math.Clamp(alignment, .1, 1);
            double command;
            if (Rig.Profile.Steering == SteeringGeometry.Axle) command = Math.Atan(Rig.Profile.FrontLength * curvature);
            else {
                // Frame steering changes yaw even while the vehicle is stationary. Control joint RATE,
                // accounting for this transient term; a steady-circle angle alone causes hunting at starts.
                double velocity = sign * actualSpeed;
                double cosine = Math.Cos(Rig.Steer), sine = Math.Sin(Rig.Steer);
                double yawRate = gear > 0 ? velocity * curvature :
                    velocity * curvature / Math.Max(.05, cosine + Rig.Profile.RearLength * curvature * sine);
                double rate = gear > 0 ?
                    (yawRate * (Rig.Profile.RearLength + Rig.Profile.FrontLength * cosine) - velocity * sine) / Rig.Profile.RearLength :
                    (velocity * sine - yawRate * (Rig.Profile.RearLength + Rig.Profile.FrontLength * cosine)) / (Rig.Profile.FrontLength * cosine);
                command = Rig.Steer + rate * h;
            }
            // Construction clears the complete network cell, including its verge. Keep the reference axle
            // within that corridor after reserving half the track width; long rigs still can reject tight turns.
            double maxOffset = Math.Max(manoeuvreRoom, Math.Max(1, metresPerPosition * .5 - Rig.Profile.Axles[0].Track * .5));
            if ((sample(position) - Rig.Units[0].Axle).Length > maxOffset) {
                AppliedSpeed = 0; BlockedReason = "A jármű kanyarodási geometriájához az út túl szűk; fordulóhely vagy másik út szükséges."; return position;
            }
            if (!Rig.Advance(sign * actualSpeed, command, h)) {
                AppliedSpeed = 0; BlockedReason = "A szerelvény elérte a csukló szöghatárát; fordulóhely vagy másik út szükséges."; return position;
            }
            TravelledDistance += actualSpeed * h;
            // A bounded local projection prevents skipping intersections or jumping to a later parallel road.
            double lo = position, hi = Math.Min(Math.Min(end, maxPosition), position + Math.Max(.05, actualSpeed * h * 2) / metresPerPosition);
            for (int i = 0; i < 18; i++) {
                double a = lo + (hi - lo) / 3, b = hi - (hi - lo) / 3;
                if ((sample(a) - Rig.Units[0].Axle).Length < (sample(b) - Rig.Units[0].Axle).Length) hi = b; else lo = a;
            }
            double candidate = (lo + hi) * .5;
            if ((sample(candidate) - Rig.Units[0].Axle).Length < (sample(position) - Rig.Units[0].Axle).Length) position = candidate;
            if ((sample(end) - Rig.Units[0].Axle).Length < .15 && (end - position) * metresPerPosition < .3) position = end;
            remaining -= h; BlockedReason = null; AppliedSpeed = actualSpeed;
        }
        return position;
    }
}
