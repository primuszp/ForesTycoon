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
                    vehicle.GetSegment(vehicle.InterpolatedRoutePosition(interpolationAlpha),
                        out int fromTile, out int toTile, out float amount);
                    if (!terrain.TryGetRoadTileCenter(fromTile, out Vector3 from)
                        || !terrain.TryGetRoadTileCenter(toTile, out Vector3 to)) continue;

                    Vector3 direction = to - from;
                    float yaw = MathF.Atan2(direction.Y, direction.X);
                    float horizontalLength = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
                    float pitch = MathF.Atan2(direction.Z, horizontalLength);
                    Vector3 position = Vector3.Lerp(from, to, amount);
                    position.Z += 0.12f;
                    DrawTruck(new VehicleTransform(position, yaw, pitch), vehicle.CargoFill);
                }
            });
        }

        private static void DrawTruck(VehicleTransform transform, float cargoFill)
        {
            // Local +X is the front of the truck.
            DrawBox(transform, new Vector3(0f, 0f, 0.22f), new Vector3(2.9f, 0.82f, 0.22f), ChassisColor, ChassisColor);
            DrawBox(transform, new Vector3(-0.62f, 0f, 0.38f), new Vector3(1.65f, 1.18f, 0.28f), CargoSideColor, ChassisColor);
            if (cargoFill > 0.001f)
            {
                float cargoHeight = 0.25f + 0.85f * Math.Clamp(cargoFill, 0f, 1f);
                DrawBox(transform, new Vector3(-0.62f, 0f, 0.52f + cargoHeight * 0.5f),
                    new Vector3(1.50f, 1.08f, cargoHeight), CargoColor, CargoSideColor);
            }
            DrawBox(transform, new Vector3(0.87f, 0f, 0.69f), new Vector3(0.92f, 1.12f, 1.02f), CabColor, CabSideColor);
            DrawBox(transform, new Vector3(1.35f, 0f, 0.73f), new Vector3(0.06f, 0.86f, 0.38f), WindowColor, WindowColor);
            DrawBox(transform, new Vector3(1.49f, 0f, 0.27f), new Vector3(0.14f, 1.18f, 0.20f), BumperColor, BumperColor);

            // Four block-style wheels, intentionally matching the low-poly/isometric look.
            DrawWheel(transform, -0.93f, -0.61f);
            DrawWheel(transform, -0.93f, 0.61f);
            DrawWheel(transform, 0.91f, -0.61f);
            DrawWheel(transform, 0.91f, 0.61f);
        }

        private static void DrawWheel(VehicleTransform transform, float x, float y) =>
            DrawBox(transform, new Vector3(x, y, 0.20f), new Vector3(0.43f, 0.20f, 0.40f), TireColor, TireColor);

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
            private readonly float cosYaw, sinYaw, cosPitch, sinPitch;

            public VehicleTransform(Vector3 position, float yawRadians, float pitchRadians)
            {
                this.position = position;
                cosYaw = MathF.Cos(yawRadians);
                sinYaw = MathF.Sin(yawRadians);
                cosPitch = MathF.Cos(-pitchRadians);
                sinPitch = MathF.Sin(-pitchRadians);
            }

            public Vector3 Apply(float x, float y, float z)
            {
                float pitchedX = cosPitch * x + sinPitch * z;
                float pitchedZ = -sinPitch * x + cosPitch * z;
                return new Vector3(
                    position.X + cosYaw * pitchedX - sinYaw * y,
                    position.Y + sinYaw * pitchedX + cosYaw * y,
                    position.Z + pitchedZ);
            }
        }
    }
}
