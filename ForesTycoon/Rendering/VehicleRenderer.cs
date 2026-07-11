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
            foreach (Vehicle vehicle in vehicles.Vehicles)
            {
                vehicle.GetSegment(vehicle.InterpolatedRoutePosition(interpolationAlpha),
                    out int fromTile, out int toTile, out float amount);
                if (!terrain.TryGetRoadTileCenter(fromTile, out Vector3 from)
                    || !terrain.TryGetRoadTileCenter(toTile, out Vector3 to)) continue;

                Vector3 direction = to - from;
                float yaw = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;
                float horizontalLength = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
                float pitch = MathF.Atan2(direction.Z, horizontalLength) * 180f / MathF.PI;
                Vector3 position = Vector3.Lerp(from, to, amount);
                position.Z += 0.12f;
                DrawTruck(position, yaw, pitch);
            }
        }

        private static void DrawTruck(Vector3 position, float yawDegrees, float pitchDegrees)
        {
            GL.PushMatrix();
            GL.Translate(position.X, position.Y, position.Z);
            GL.Rotate(yawDegrees, 0f, 0f, 1f);
            // OpenGL's positive local-Y rotation lowers +X, hence the negative sign.
            // This aligns the truck's longitudinal axis with uphill/downhill road grade.
            GL.Rotate(-pitchDegrees, 0f, 1f, 0f);

            // Local +X is the front of the truck.
            DrawBox(new Vector3(0f, 0f, 0.22f), new Vector3(2.9f, 0.82f, 0.22f), ChassisColor, ChassisColor);
            DrawBox(new Vector3(-0.62f, 0f, 0.86f), new Vector3(1.55f, 1.18f, 1.35f), CargoColor, CargoSideColor);
            DrawBox(new Vector3(0.87f, 0f, 0.69f), new Vector3(0.92f, 1.12f, 1.02f), CabColor, CabSideColor);
            DrawBox(new Vector3(1.35f, 0f, 0.73f), new Vector3(0.06f, 0.86f, 0.38f), WindowColor, WindowColor);
            DrawBox(new Vector3(1.49f, 0f, 0.27f), new Vector3(0.14f, 1.18f, 0.20f), BumperColor, BumperColor);

            // Four block-style wheels, intentionally matching the low-poly/isometric look.
            DrawWheel(-0.93f, -0.61f);
            DrawWheel(-0.93f, 0.61f);
            DrawWheel(0.91f, -0.61f);
            DrawWheel(0.91f, 0.61f);

            GL.PopMatrix();
        }

        private static void DrawWheel(float x, float y) =>
            DrawBox(new Vector3(x, y, 0.20f), new Vector3(0.43f, 0.20f, 0.40f), TireColor, TireColor);

        private static void DrawBox(Vector3 center, Vector3 size, Color topColor, Color sideColor)
        {
            float x0 = center.X - size.X * 0.5f, x1 = center.X + size.X * 0.5f;
            float y0 = center.Y - size.Y * 0.5f, y1 = center.Y + size.Y * 0.5f;
            float z0 = center.Z - size.Z * 0.5f, z1 = center.Z + size.Z * 0.5f;

            ImmediateRenderer.Draw(PrimitiveType.Quads, () =>
            {
                GL.Color3(topColor);
                Quad(x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1);

                GL.Color3(sideColor);
                Quad(x0, y0, z0, x1, y0, z0, x1, y0, z1, x0, y0, z1);
                Quad(x1, y1, z0, x0, y1, z0, x0, y1, z1, x1, y1, z1);
                Quad(x0, y1, z0, x0, y0, z0, x0, y0, z1, x0, y1, z1);
                Quad(x1, y0, z0, x1, y1, z0, x1, y1, z1, x1, y0, z1);
            });
        }

        private static void Quad(
            float ax, float ay, float az, float bx, float by, float bz,
            float cx, float cy, float cz, float dx, float dy, float dz)
        {
            GL.Vertex3(ax, ay, az); GL.Vertex3(bx, by, bz);
            GL.Vertex3(cx, cy, cz); GL.Vertex3(dx, dy, dz);
        }
    }
}
