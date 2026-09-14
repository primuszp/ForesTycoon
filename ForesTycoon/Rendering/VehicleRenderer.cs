using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    static class VehicleRenderer
    {
        private static readonly Color CabColor = Color.FromArgb(218, 66, 45);
        private static readonly Color CabSideColor = Color.FromArgb(165, 42, 32);
        private static readonly Color CargoColor = Color.FromArgb(213, 193, 145);
        private static readonly Color CargoSideColor = Color.FromArgb(157, 137, 98);
        private static readonly Color WindowColor = Color.FromArgb(72, 112, 132);
        private static readonly Color ChassisColor = Color.FromArgb(57, 61, 62);
        private static readonly Color TireColor = Color.FromArgb(30, 31, 32);
        private static readonly Color BumperColor = Color.FromArgb(190, 193, 190);

        public static void Draw(VehicleSystem vehicles, Terrain terrain, float interpolationAlpha)
        {
            if (vehicles.Count == 0) return;

            DynamicPrimitiveBatch.Draw(PrimitiveType.Quads, () =>
            {
                foreach (Vehicle vehicle in vehicles.Vehicles)
                {
                    double routePosition = vehicle.InterpolatedRoutePosition(interpolationAlpha);
                    if (vehicle.RoadRoute != null)
                    {
                        vehicle.RoadRoute.GetPose(routePosition, out Vector3 center, out Vector3 forward,
                            out Vector3 left, out Vector3 up);
                        DrawTruck(new VehicleTransform(center, forward, left, up), vehicle.CargoFill,
                            (float)(routePosition * vehicle.RoadRoute.TileLength / 0.23));
                        continue;
                    }
                    vehicle.GetSegment(vehicle.InterpolatedRoutePosition(interpolationAlpha),
                        out int fromTile, out int toTile, out float amount);
                    if (!terrain.TryGetRoadTileCenter(fromTile, out Vector3 from)
                        || !terrain.TryGetRoadTileCenter(toTile, out Vector3 to)) continue;

                    Vector3 direction = to - from;
                    float yaw = MathF.Atan2(direction.Y, direction.X);
                    float horizontalLength = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
                    float pitch = MathF.Atan2(direction.Z, horizontalLength);
                    Vector3 position = Vector3.Lerp(from, to, amount);
                    position.Z += 0.025f;
                    DrawTruck(new VehicleTransform(position, yaw, pitch), vehicle.CargoFill);
                }
            });
        }

        private static void DrawTruck(VehicleTransform transform, float cargoFill, float wheelAngle = 0)
        {
            // Local +X is the front of the truck.
            DrawBox(transform, new Vector3(0f, 0f, 0.22f), new Vector3(2.9f, 0.82f, 0.22f), ChassisColor, ChassisColor);
            DrawBox(transform, new Vector3(-0.62f, 0f, 0.40f), new Vector3(1.85f, 1.18f, 0.12f), ChassisColor, ChassisColor);
            // Steel bolsters and stakes hold longitudinal logs; an empty return shows the rack.
            for (int rack = 0; rack < 3; rack++)
            {
                float x = -1.38f + rack * 0.70f;
                DrawBox(transform, new Vector3(x, 0, 0.48f), new Vector3(0.10f, 1.28f, 0.10f), BumperColor, ChassisColor);
                for (int side = -1; side <= 1; side += 2)
                    DrawBox(transform, new Vector3(x, side * 0.61f, 0.91f), new Vector3(0.07f, 0.07f, 0.92f), BumperColor, ChassisColor);
            }
            int logs = cargoFill <= 0 ? 0 : (int)MathF.Ceiling(Math.Clamp(cargoFill, 0, 1) * 9);
            int log = 0;
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 4 - row && log < logs; column++, log++)
                {
                    float y = (column - (3 - row) * 0.5f) * 0.285f;
                    Cylinder(transform, new Vector3(-0.64f, y, 0.665f + row * 0.245f),
                        1.86f + (log % 3) * 0.035f, 0.14f, false, 0,
                        Color.FromArgb(91 + log % 3 * 9, 66 + log % 3 * 6, 38), CargoColor);
                }
            DrawBox(transform, new Vector3(0.38f, 0, 0.94f), new Vector3(0.08f, 1.2f, 1.05f), CabSideColor, ChassisColor);
            DrawBox(transform, new Vector3(0.87f, 0f, 0.69f), new Vector3(0.92f, 1.12f, 1.02f), CabColor, CabSideColor);
            DrawBox(transform, new Vector3(1.35f, 0f, 0.73f), new Vector3(0.06f, 0.86f, 0.38f), WindowColor, WindowColor);
            DrawBox(transform, new Vector3(1.49f, 0f, 0.27f), new Vector3(0.14f, 1.18f, 0.20f), BumperColor, BumperColor);
            for (int side = -1; side <= 1; side += 2)
            {
                DrawBox(transform, new Vector3(0.93f, side * 0.565f, 0.91f), new Vector3(0.54f, 0.02f, 0.39f), WindowColor, WindowColor);
                DrawBox(transform, new Vector3(1.37f, side * 0.40f, 0.40f), new Vector3(0.05f, 0.22f, 0.12f), Color.LightGoldenrodYellow, Color.LightGoldenrodYellow);
                DrawBox(transform, new Vector3(-1.56f, side * 0.46f, 0.40f), new Vector3(0.04f, 0.15f, 0.10f), CabColor, CabColor);
                DrawWheel(transform, -1.14f, side * 0.62f, wheelAngle);
                DrawWheel(transform, -0.61f, side * 0.62f, wheelAngle);
                DrawWheel(transform, 0.91f, side * 0.62f, wheelAngle);
            }
        }

        private static void DrawWheel(VehicleTransform transform, float x, float y, float angle)
        {
            Cylinder(transform, new Vector3(x, y, 0.23f), 0.22f, 0.23f, true, angle, TireColor, TireColor);
            Cylinder(transform, new Vector3(x, y, 0.23f), 0.235f, 0.11f, true, angle, BumperColor, BumperColor);
        }

        private static void Cylinder(VehicleTransform transform, Vector3 center, float length, float radius,
            bool wheel, float angle, Color bark, Color end)
        {
            const int sides = 10;
            for (int i = 0; i < sides; i++)
            {
                float a = angle + i * (MathF.Tau / sides), b = angle + (i + 1) * (MathF.Tau / sides);
                Vector3 ra = wheel ? new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) : new Vector3(0, MathF.Cos(a), MathF.Sin(a));
                Vector3 rb = wheel ? new Vector3(MathF.Cos(b), 0, MathF.Sin(b)) : new Vector3(0, MathF.Cos(b), MathF.Sin(b));
                Vector3 axis = (wheel ? -Vector3.UnitY : Vector3.UnitX) * (length * 0.5f);
                Vector3 p = center - axis, q = center + axis;
                float shade = 0.72f + 0.28f * Math.Max(0, ra.Z);
                DynamicPrimitiveBatch.Color3(Color.FromArgb((int)(bark.R * shade), (int)(bark.G * shade), (int)(bark.B * shade)));
                EmitQuad(transform, p + ra * radius, p + rb * radius, q + rb * radius, q + ra * radius);
                DynamicPrimitiveBatch.Color3(end);
                EmitQuad(transform, p, p + rb * radius, p + ra * radius, p);
                EmitQuad(transform, q, q + ra * radius, q + rb * radius, q);
                if (!wheel)
                {
                    // Small darker heartwood disks on the exposed cut ends.
                    DynamicPrimitiveBatch.Color3(CargoSideColor);
                    Vector3 offset = axis.Normalized() * 0.002f;
                    EmitQuad(transform, p - offset, p - offset + rb * radius * 0.53f, p - offset + ra * radius * 0.53f, p - offset);
                    EmitQuad(transform, q + offset, q + offset + ra * radius * 0.53f, q + offset + rb * radius * 0.53f, q + offset);
                }
            }
        }

        private static void EmitQuad(VehicleTransform t, Vector3 a, Vector3 b, Vector3 c, Vector3 d) =>
            Quad(t, a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z, d.X, d.Y, d.Z);

        private static void DrawBox(VehicleTransform transform, Vector3 center, Vector3 size, Color topColor, Color sideColor)
        {
            float x0 = center.X - size.X * 0.5f, x1 = center.X + size.X * 0.5f;
            float y0 = center.Y - size.Y * 0.5f, y1 = center.Y + size.Y * 0.5f;
            float z0 = center.Z - size.Z * 0.5f, z1 = center.Z + size.Z * 0.5f;

            DynamicPrimitiveBatch.Color3(topColor);
            Quad(transform, x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1);

            DynamicPrimitiveBatch.Color3(sideColor);
            Quad(transform, x0, y0, z0, x1, y0, z0, x1, y0, z1, x0, y0, z1);
            Quad(transform, x1, y1, z0, x0, y1, z0, x0, y1, z1, x1, y1, z1);
            Quad(transform, x0, y1, z0, x0, y0, z0, x0, y0, z1, x0, y1, z1);
            Quad(transform, x1, y0, z0, x1, y1, z0, x1, y1, z1, x1, y0, z1);
        }

        private static void Quad(VehicleTransform transform,
            float ax, float ay, float az, float bx, float by, float bz,
            float cx, float cy, float cz, float dx, float dy, float dz)
        {
            DynamicPrimitiveBatch.Vertex3(transform.Apply(ax, ay, az)); DynamicPrimitiveBatch.Vertex3(transform.Apply(bx, by, bz));
            DynamicPrimitiveBatch.Vertex3(transform.Apply(cx, cy, cz)); DynamicPrimitiveBatch.Vertex3(transform.Apply(dx, dy, dz));
        }

        private readonly struct VehicleTransform
        {
            private readonly Vector3 position;
            private readonly Vector3 forward, left, up;

            public VehicleTransform(Vector3 position, Vector3 forward, Vector3 left, Vector3 up)
            {
                this.position = position;
                this.forward = forward;
                this.left = left;
                this.up = up;
            }

            public VehicleTransform(Vector3 position, float yawRadians, float pitchRadians)
            {
                this.position = position;
                forward = new Vector3(MathF.Cos(yawRadians) * MathF.Cos(pitchRadians), MathF.Sin(yawRadians) * MathF.Cos(pitchRadians), MathF.Sin(pitchRadians));
                left = new Vector3(-MathF.Sin(yawRadians), MathF.Cos(yawRadians), 0);
                up = Vector3.Cross(forward, left);
            }

            public Vector3 Apply(float x, float y, float z)
            {
                return position + forward * x + left * y + up * z;
            }
        }
    }
}
