using System;

namespace ForesTycoon;

/// <summary>Fixed diorama asset scale, separate from physical metres. Changing axle data never stretches a body mesh.</summary>
internal sealed record RigAppearance(float BodyScale, float LongScale, float TrackScale, float RadiusScale,
    float TrailerLengthScale = 0, float HitchBias = 0, float FrontLengthBias = 0, float RearLengthBias = 0);

internal sealed class RigVisualPose
{
    internal RigAppearance Appearance { get; }
    internal RigUnitPose[] Units { get; }
    internal PlanPoint[] Hitches { get; }
    internal RigWheelPose[] Wheels { get; }

    internal RigVisualPose(VehicleKinematics rig, RigAppearance appearance)
    {
        Appearance = appearance;
        Units = new RigUnitPose[rig.Units.Count];
        Hitches = new PlanPoint[Math.Max(0, Units.Length - 1)];
        Units[0] = new(rig.Units[0].Axle * Engine.WorldScale.MetresToWorld, rig.Units[0].Yaw);
        int start = 1;
        if (rig.Profile.Steering == SteeringGeometry.Articulated) {
            Hitches[0] = Units[0].Axle - PlanPoint.Forward(Units[0].Yaw) *
                (rig.Profile.FrontLength * appearance.LongScale + appearance.FrontLengthBias);
            Units[1] = new(Hitches[0] - PlanPoint.Forward(rig.Units[1].Yaw) *
                (rig.Profile.RearLength * appearance.LongScale + appearance.RearLengthBias), rig.Units[1].Yaw);
            start++;
        }
        for (int i = 0; i < rig.Profile.Trailers.Length; i++) {
            int j = start + i; var trailer = rig.Profile.Trailers[i];
            Hitches[j - 1] = Units[j - 1].Axle + PlanPoint.Forward(Units[j - 1].Yaw) *
                (trailer.HitchX * appearance.LongScale + (i == 0 ? appearance.HitchBias : 0));
            float scale = i == rig.Profile.Trailers.Length - 1 && appearance.TrailerLengthScale > 0 ? appearance.TrailerLengthScale : appearance.LongScale;
            Units[j] = new(Hitches[j - 1] - PlanPoint.Forward(rig.Units[j].Yaw) * (trailer.Length * scale), rig.Units[j].Yaw);
        }
        Wheels = rig.Wheels();
        for (int i = 0; i < Wheels.Length; i++) {
            var axle = rig.Profile.Axles[i / 2]; var unit = Units[axle.Unit];
            Wheels[i] = Wheels[i] with { Position = unit.Axle + PlanPoint.Forward(unit.Yaw) * (axle.X * appearance.LongScale) +
                PlanPoint.Left(unit.Yaw) * ((Wheels[i].Left ? 1 : -1) * axle.Track * .5 * appearance.TrackScale) };
        }
    }
}
