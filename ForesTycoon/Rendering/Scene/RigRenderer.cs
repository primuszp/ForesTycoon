using System;
using System.Drawing;
using OpenTK.Mathematics;

namespace ForesTycoon;

internal sealed record RigModelFrames(Matrix4 Tractor, Matrix4 Trailer, bool HasTrailer, RigVisualPose Visual);

/// <summary>Presentation of the simulation's configured axles. Wheels never infer steering from a road polyline.</summary>
internal static class RigRenderer
{
    internal static Matrix4 Frame(RigUnitPose unit, TerrainMap map, float scale = Engine.WorldScale.MetresToWorld)
    {
        float x = (float)unit.Axle.X * scale, y = (float)unit.Axle.Y * scale;
        map.TryGetDrivingSurfaceZ(x, y, out float z);
        Vector3 forward = new((float)Math.Cos(unit.Yaw), (float)Math.Sin(unit.Yaw), 0);
        Vector3 left = new(-forward.Y, forward.X, 0);
        const float span = .5f;
        if (map.TryGetDrivingSurfaceZ(x + forward.X * span, y + forward.Y * span, out float front) &&
            map.TryGetDrivingSurfaceZ(x - forward.X * span, y - forward.Y * span, out float rear)) forward.Z = (front - rear) / (2 * span);
        if (map.TryGetDrivingSurfaceZ(x + left.X * span, y + left.Y * span, out float side) &&
            map.TryGetDrivingSurfaceZ(x - left.X * span, y - left.Y * span, out float other)) left.Z = (side - other) / (2 * span);
        forward.Normalize(); Vector3 up = Vector3.Cross(forward, left).Normalized(); left = Vector3.Cross(up, forward).Normalized();
        return new(new Vector4(forward, 0), new Vector4(left, 0), new Vector4(up, 0), new Vector4(x, y, z + .025f, 1));
    }

    internal static RigModelFrames TruckFrames(VehicleKinematics rig, ITruckModel model, TerrainMap map, float legacyScale)
        => TruckFrames(rig, model, legacyScale, (x, y) => { map.TryGetDrivingSurfaceZ(x, y, out float z); return z; });

    internal static RigModelFrames TruckFrames(VehicleKinematics rig, ITruckModel model, float legacyScale, Func<float, float, float> height)
    {
        float rear = model.AxleMidpoint - model.Wheelbase * .5f;
        float longScale = model.Wheelbase * legacyScale / 4;
        var appearance = new RigAppearance(legacyScale, longScale, model.WheelTrack * legacyScale / 2,
            model.WheelRadius * legacyScale / .5f,
            model.Articulated ? (model.Kingpin - model.TrailerAxle) * legacyScale / 6 : longScale,
            model.Articulated ? (model.Kingpin - rear) * legacyScale + 1.5f * longScale : 0);
        var visual = new RigVisualPose(rig, appearance);
        return TruckGroundAlignment.Align(rig, model, appearance, visual, height);
    }

    internal static void Wheels(VehicleKinematics rig, TerrainMap map, RigVisualPose visualPose, int visibleUnits = int.MaxValue,
        Func<float, float, float> height = null)
    {
        var wheels = visualPose.Wheels;
        var visual = RenderDevice.Visuals; var previous = visual?.Kind ?? SurfaceKind.Vehicle;
        if (visual != null) visual.Kind = SurfaceKind.Rubber;
        try {
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () => {
                for (int j = 0; j < wheels.Length; j++) {
                    var w = wheels[j]; var axle = rig.Profile.Axles[j / 2];
                    if (axle.Unit >= visibleUnits) continue;
                    float x = (float)w.Position.X, y = (float)w.Position.Y;
                    map.TryGetDrivingSurfaceZ(x, y, out float z);
                    if (height != null) z = height(x, y);
                    float r = (float)axle.Radius * visualPose.Appearance.RadiusScale, halfWidth = r * .3f;
                    var frame = Matrix4.CreateRotationY((float)w.Roll) * Matrix4.CreateRotationZ((float)w.Yaw) * Matrix4.CreateTranslation(x, y, z + r + .025f);
                    Vector3 P(float a, float side, float size = 1) => Vector3.TransformPosition(new Vector3(MathF.Cos(a) * r * size, side, MathF.Sin(a) * r * size), frame);
                    for (int i = 0; i < 16; i++) {
                        float a = i * MathF.Tau / 16, b = (i + 1) * MathF.Tau / 16;
                        DynamicPrimitiveBatch.Color4(i % 2 == 0 ? Color.FromArgb(38, 39, 41) : Color.FromArgb(51, 52, 54));
                        DynamicPrimitiveBatch.Vertex3(P(a, -halfWidth)); DynamicPrimitiveBatch.Vertex3(P(a, halfWidth));
                        DynamicPrimitiveBatch.Vertex3(P(b, halfWidth)); DynamicPrimitiveBatch.Vertex3(P(b, -halfWidth));
                        foreach (float side in new[] { -halfWidth, halfWidth }) {
                            DynamicPrimitiveBatch.Color4(Color.FromArgb(42, 43, 45));
                            DynamicPrimitiveBatch.Vertex3(P(a, side)); DynamicPrimitiveBatch.Vertex3(P(b, side));
                            DynamicPrimitiveBatch.Vertex3(P(b, side, .38f)); DynamicPrimitiveBatch.Vertex3(P(a, side, .38f));
                            DynamicPrimitiveBatch.Color4(Color.FromArgb(138, 139, 137));
                            DynamicPrimitiveBatch.Vertex3(P(a, side, .38f)); DynamicPrimitiveBatch.Vertex3(P(b, side, .38f));
                            DynamicPrimitiveBatch.Vertex3(Vector3.TransformPosition(new Vector3(0, side, 0), frame));
                            DynamicPrimitiveBatch.Vertex3(Vector3.TransformPosition(new Vector3(0, side, 0), frame));
                        }
                    }
                }
            });
        } finally { if (visual != null) visual.Kind = previous; }
    }
}
