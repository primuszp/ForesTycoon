using System;
using System.Linq;
using OpenTK.Mathematics;

namespace ForesTycoon;

/// <summary>Rigid 3D support geometry: axle contacts set pitch/roll, the trailer inherits the actual fifth-wheel point.
/// Body scales never change. Horizontal wheel positions are projected by the rigid frame before ground contact.</summary>
internal static class TruckGroundAlignment
{
    private const float Lift = .025f;
    private readonly record struct Basis(Vector3 Forward, Vector3 Left, Vector3 Up)
    {
        internal Matrix4 At(Vector3 point) => new(new Vector4(Forward, 0), new Vector4(Left, 0), new Vector4(Up, 0), new Vector4(point, 1));
        internal Vector3 Apply(Vector3 v) => Forward * v.X + Left * v.Y + Up * v.Z;
    }
    private static Basis Orientation(double yaw, float pitch, float roll)
    {
        Vector3 along = new((float)Math.Cos(yaw), (float)Math.Sin(yaw), 0), lateral = new(-along.Y, along.X, 0);
        Vector3 forward = along * MathF.Cos(pitch) + Vector3.UnitZ * MathF.Sin(pitch);
        Vector3 up = Vector3.Cross(forward, lateral);
        return new(forward, lateral * MathF.Cos(roll) + up * MathF.Sin(roll), up * MathF.Cos(roll) - lateral * MathF.Sin(roll));
    }
    private static Vector3 XY(PlanPoint p) => new((float)p.X, (float)p.Y, 0);
    private static float Roll(Vector3 point, double yaw, float track, Func<float, float, float> height, float pitch = 0)
    {
        Vector3 left = new(-(float)Math.Sin(yaw), (float)Math.Cos(yaw), 0);
        var a = point + left * track * .5f; var b = point - left * track * .5f;
        return MathF.Atan2((height(a.X, a.Y) - height(b.X, b.Y)) * MathF.Cos(pitch), Math.Max(.01f, track));
    }
    private static float Contact(Vector3 point, double yaw, float track, Func<float, float, float> height)
    {
        Vector3 left = new(-(float)Math.Sin(yaw), (float)Math.Cos(yaw), 0);
        var a = point + left * track * .5f; var b = point - left * track * .5f;
        return (height(a.X, a.Y) + height(b.X, b.Y)) * .5f;
    }
    private static float SolvePitch(Func<float, float> error)
    {
        float lo = -1.3f, hi = 1.3f, a = error(lo), b = error(hi);
        if (a * b > 0) return Math.Abs(a) < Math.Abs(b) ? lo : hi;
        for (int i = 0; i < 32; i++) {
            float mid = (lo + hi) * .5f, value = error(mid);
            if (MathF.Abs(value) < .000001f) return mid;
            if (Math.Sign(value) == Math.Sign(a)) { lo = mid; a = value; } else hi = mid;
        }
        return (lo + hi) * .5f;
    }

    internal static RigModelFrames Align(VehicleKinematics rig, ITruckModel model, RigAppearance appearance,
        RigVisualPose visual, Func<float, float, float> height)
    {
        float scale = appearance.BodyScale;
        float sourceRear = model.AxleMidpoint - model.Wheelbase * .5f;
        float track = (float)rig.Profile.Axles.First(a => a.Unit == 0).Track * appearance.TrackScale;
        float radius = (float)rig.Profile.Axles.First(a => a.Unit == 0).Radius * appearance.RadiusScale;
        Vector3 rear = XY(visual.Units[0].Axle);
        double yaw = visual.Units[0].Yaw;
        rear.Z = Contact(rear, yaw, track, height) + radius + Lift;
        float wheelbase = (float)rig.Profile.FrontLength * appearance.LongScale;
        float pitch = SolvePitch(p => {
            var front = rear + Orientation(yaw, p, 0).Forward * wheelbase;
            return front.Z - Contact(front, yaw, track, height) - radius - Lift;
        });
        var tractorBasis = Orientation(yaw, pitch, Roll(rear, yaw, track, height, pitch));
        Matrix4 tractor = Matrix4.CreateTranslation(-sourceRear, 0, -model.WheelRadius) * Matrix4.CreateScale(scale) * tractorBasis.At(rear);
        PositionWheels(0, rear, tractorBasis);
        bool hasTrailer = rig.Profile.Trailers.Length > 0;
        Matrix4 trailer = tractor;
        float hitchX = model.Kingpin + (hasTrailer ? ((float)rig.Profile.Trailers[0].HitchX + 1.5f) * appearance.LongScale / scale : 0);
        Vector3 hitch = Vector3.TransformPosition(new Vector3(hitchX, 0, model.KingpinHeight), tractor);
        for (int i = 0; i < rig.Profile.Trailers.Length; i++) {
            int unit = i + 1; var data = rig.Profile.Trailers[i];
            float lengthScale = i == rig.Profile.Trailers.Length - 1 ? appearance.TrailerLengthScale : appearance.LongScale;
            float length = (float)data.Length * lengthScale;
            var axle = rig.Profile.Axles.First(a => a.Unit == unit);
            track = (float)axle.Track * appearance.TrackScale; radius = (float)axle.Radius * appearance.RadiusScale;
            double rearYaw = visual.Units[unit].Yaw;
            var localAxle = new Vector3(-length, 0, (model.WheelRadius - model.KingpinHeight) * scale);
            float roll = 0; Basis basis = default; Vector3 at = default;
            for (int iteration = 0; iteration < 5; iteration++) {
                float rearPitch = SolvePitch(p => {
                    var point = hitch + Orientation(rearYaw, p, roll).Apply(localAxle);
                    return point.Z - Contact(point, rearYaw, track, height) - radius - Lift;
                });
                basis = Orientation(rearYaw, rearPitch, roll); at = hitch + basis.Apply(localAxle);
                roll = Roll(at, rearYaw, track, height, rearPitch);
            }
            visual.Hitches[i] = new(hitch.X, hitch.Y); visual.Units[unit] = new(new(at.X, at.Y), rearYaw);
            PositionWheels(unit, at, basis);
            trailer = Matrix4.CreateTranslation(-model.Kingpin, 0, -model.KingpinHeight) * Matrix4.CreateScale(scale) * basis.At(hitch);
            if (i + 1 < rig.Profile.Trailers.Length) {
                hitch = at + basis.Apply(new Vector3((float)rig.Profile.Trailers[i + 1].HitchX * appearance.LongScale, 0,
                    (model.KingpinHeight - model.WheelRadius) * scale));
            }
        }
        return new(tractor, trailer, hasTrailer, visual);

        void PositionWheels(int unit, Vector3 origin, Basis basis)
        {
            for (int w = 0; w < visual.Wheels.Length; w++) {
                var a = rig.Profile.Axles[w / 2]; if (a.Unit != unit) continue;
                float side = (visual.Wheels[w].Left ? 1 : -1) * (float)a.Track * .5f * appearance.TrackScale;
                var p = origin + basis.Apply(new Vector3((float)a.X * appearance.LongScale, side, 0));
                visual.Wheels[w] = visual.Wheels[w] with { Position = new(p.X, p.Y) };
            }
        }
    }
}
