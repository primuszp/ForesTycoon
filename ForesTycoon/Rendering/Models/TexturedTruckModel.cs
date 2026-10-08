using System;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>What the vehicle renderer needs from a log truck model, whichever format it comes in.</summary>
    internal interface ITruckModel : IDisposable
    {
        float Radius { get; }
        float Width { get; }
        float Wheelbase { get; }
        float AxleMidpoint { get; }
        void Draw(Matrix4 transform, float cargoFill, float wheelAngle, float curvature = 0, float scale = 1,
            Matrix4? suspension = null, float outlineWidth = 0);
    }

    /// <summary>
    /// The textured log truck produced by tools/convert_licensed_truck.py (game frame, +X forward, +Z up).
    /// Drawn through the textured model renderer: "wheel_*" nodes sit at their centres and spin (front ones also
    /// steer), "cargo_*" nodes appear bottom-up with the load, and "body" follows the suspension.
    /// </summary>
    internal sealed class TexturedTruckModel : ITruckModel
    {
        private enum Kind : byte { Body, FrontWheel, RearWheel, Cargo }

        private readonly AnimatedGlbModel model;
        private readonly AnimatedModelRenderer renderer;
        private readonly AnimatedGlbModel.Pose pose;
        private readonly Matrix4[] rest;
        private readonly Kind[] kinds;
        private readonly int cargoCount;
        private readonly int[] cargoOrder;

        public float Radius { get; }
        public float Width { get; }
        public float Wheelbase { get; }
        public float AxleMidpoint { get; }

        /// <summary>Shading settings of the current frame; set by the vehicle renderer.</summary>
        internal static IShadingSettings Settings { get; set; }

        internal TexturedTruckModel(string path)
        {
            model = AnimatedGlbModel.Load(path);
            renderer = new AnimatedModelRenderer(model);
            pose = model.CreatePose();
            pose.Evaluate(null, 0);
            rest = (Matrix4[])pose.World.Clone();
            kinds = new Kind[model.Nodes.Length];
            var cargo = new System.Collections.Generic.List<int>();
            float front = 0, rear = 0; int frontCount = 0, rearCount = 0;
            for (int i = 0; i < kinds.Length; i++)
            {
                string name = model.Nodes[i].Name ?? "";
                kinds[i] = name.StartsWith("wheel_front", StringComparison.Ordinal) ? Kind.FrontWheel
                    : name.StartsWith("wheel_", StringComparison.Ordinal) ? Kind.RearWheel
                    : name.StartsWith("cargo_", StringComparison.Ordinal) ? Kind.Cargo : Kind.Body;
                float x = rest[i].Row3.X;
                if (kinds[i] == Kind.FrontWheel) { front += x; frontCount++; }
                else if (kinds[i] == Kind.RearWheel) { rear += x; rearCount++; }
                else if (kinds[i] == Kind.Cargo) cargo.Add(i);
            }
            if (frontCount == 0 || rearCount == 0) throw new System.IO.InvalidDataException("Truck model has no axles.");
            cargoOrder = cargo.ToArray(); // the converter writes logs bottom-up
            cargoCount = cargoOrder.Length;
            front /= frontCount; rear /= rearCount;
            Wheelbase = front - rear; AxleMidpoint = (front + rear) * 0.5f;
            float minY = float.MaxValue, maxY = float.MinValue, radius = 0;
            foreach (var mesh in model.Meshes)
                for (int v = 0; v < mesh.Vertices.Length; v += 16)
                {
                    var p = Vector3.TransformPosition(new Vector3(mesh.Vertices[v], mesh.Vertices[v + 1], mesh.Vertices[v + 2]), rest[mesh.Node]);
                    minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); radius = Math.Max(radius, p.Length);
                }
            Width = maxY - minY; Radius = radius;
            if (!float.IsFinite(Width) || Width <= 0) throw new System.IO.InvalidDataException("Invalid truck width.");
        }

        public void Draw(Matrix4 transform, float cargoFill, float wheelAngle, float curvature = 0, float scale = 1,
            Matrix4? suspension = null, float outlineWidth = 0)
        {
            int visible = (int)MathF.Ceiling(Math.Clamp(cargoFill, 0, 1) * cargoCount);
            var body = suspension ?? Matrix4.Identity;
            for (int i = 0; i < kinds.Length; i++)
            {
                switch (kinds[i])
                {
                    case Kind.FrontWheel:
                    case Kind.RearWheel:
                        float steer = kinds[i] == Kind.FrontWheel
                            ? VehicleVisualMotion.Steering(curvature, Wheelbase * scale, rest[i].Row3.Y * scale) : 0;
                        pose.World[i] = Matrix4.CreateRotationY(-wheelAngle) * Matrix4.CreateRotationZ(steer) * rest[i];
                        break;
                    default:
                        pose.World[i] = rest[i] * body;
                        break;
                }
            }
            // Logs beyond the current load collapse to a point instead of being drawn.
            for (int k = visible; k < cargoCount; k++) pose.World[cargoOrder[k]] = Matrix4.CreateScale(0);
            var settings = Settings ?? new GraphicsSettings();
            renderer.Draw(pose, transform, settings, outlineWidth * scale, sourceMaterial: true);
        }

        public void Dispose() => renderer.Dispose();
    }
}
