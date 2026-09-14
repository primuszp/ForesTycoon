using System;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    // Immutable road geometry, captured at spawn. Road foundations keep their heights
    // after terrain edits; deleting a route tile removes its vehicles.
    sealed class VehicleRoadRoute
    {
        private readonly Vector3[] centers;
        private readonly Vector2[] gradients;
        public int Last => centers.Length - 1;
        public float TileLength { get; }

        public VehicleRoadRoute(Vector3[] centers, Vector2[] gradients)
        {
            if (centers == null || centers.Length < 2 || gradients?.Length != centers.Length)
                throw new ArgumentException("Road geometry needs matching centers and gradients.");
            this.centers = (Vector3[])centers.Clone();
            this.gradients = (Vector2[])gradients.Clone();
            TileLength = (centers[1] - centers[0]).Xy.Length;
            if (TileLength < 0.001f) throw new ArgumentException("Road centers must be distinct.");
        }

        public Vector3 Sample(double directedPosition)
        {
            // Extend the terminal road plane for axle sampling, without extending travel.
            int i = Math.Clamp((int)Math.Floor(directedPosition + 0.5), 0, Last);
            float t = (float)(directedPosition - i + 0.5);
            Vector3 c = centers[i];
            Vector3 previous = i > 0 ? centers[i - 1] : c * 2 - centers[1];
            Vector3 next = i < Last ? centers[i + 1] : c * 2 - centers[Last - 1];
            Vector3 entry = (previous + c) * 0.5f, exit = (c + next) * 0.5f;
            Vector3 p;
            if (IsCorner(i))
            {
                Vector3 pivot = entry + exit - c;
                float angle = t * MathF.PI * 0.5f;
                p = pivot + (entry - pivot) * MathF.Cos(angle) + (exit - pivot) * MathF.Sin(angle);
            }
            else p = Vector3.Lerp(entry, exit, t);
            p.Z = c.Z + gradients[i].X * (p.X - c.X) + gradients[i].Y * (p.Y - c.Y);
            return p;
        }

        public bool IsCorner(int i) => i > 0 && i < Last &&
            Vector2.Dot((centers[i] - centers[i - 1]).Xy.Normalized(),
                (centers[i + 1] - centers[i]).Xy.Normalized()) < 0.5f;

        public static double Directed(double position, int last, out int sign)
        {
            double cycle = position % (last * 2.0);
            if (cycle < 0) cycle += last * 2.0;
            sign = cycle < last ? 1 : -1;
            return sign > 0 ? cycle : last * 2.0 - cycle;
        }

        public void GetPose(double position, out Vector3 center, out Vector3 forward, out Vector3 left, out Vector3 up)
        {
            double d = Directed(position, Last, out int sign);
            double halfWheelbase = 0.95 / TileLength;
            Vector3 rear = Sample(d - sign * halfWheelbase);
            Vector3 front = Sample(d + sign * halfWheelbase);
            forward = (front - rear).Normalized();
            left = new Vector3(-forward.Y, forward.X, 0).Normalized();
            int i = Math.Clamp((int)Math.Floor(d + 0.5), 0, Last);
            left.Z = gradients[i].X * left.X + gradients[i].Y * left.Y;
            up = Vector3.Cross(forward, left).Normalized();
            left = Vector3.Cross(up, forward).Normalized();
            center = Sample(d);
            center.Z = (rear.Z + front.Z) * 0.5f;
            center += up * 0.025f;
        }

        public double TargetSpeed(double position, double cruise, float load)
        {
            double d = Directed(position, Last, out int sign);
            Vector3 tangent = Sample(d + sign * 0.02) - Sample(d - sign * 0.02);
            double grade = tangent.Z / Math.Max(0.001, tangent.Xy.Length);
            double target = cruise * Math.Clamp(1 - grade * (1.4 + load), 0.3, 1.12);
            int tile = (int)Math.Floor(d + 0.5);
            if (IsCorner(tile) || IsCorner(tile + sign)) target = Math.Min(target, cruise * 0.48);
            return target;
        }
    }
}
