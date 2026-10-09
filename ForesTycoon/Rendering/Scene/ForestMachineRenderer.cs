using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>
    /// Draws the harvesters and forwarders and the log piles they leave. The machines follow their tile path through
    /// edge midpoints with a curve inside each tile, so in a bend they swing round like the skid-trail ruts. With the
    /// licensed models installed (Assets/Licensed) their wheels turn and the crane works; without them a simple
    /// stand-in model is drawn.
    /// </summary>
    internal sealed class ForestMachineRenderer : IDisposable
    {
        /// <summary>World units per metre of machine: the 7 m harvester spans about half a tile.</summary>
        internal const float MetreScale = 0.42f;
        private static readonly Matrix4 Axis = Matrix4.CreateRotationX(MathF.PI / 2);   // glTF (x fwd, y up) → game (+X fwd, +Z up)

        private sealed class MachineModel : IDisposable
        {
            internal AnimatedGlbModel Model;
            internal AnimatedModelRenderer Renderer;
            internal AnimatedGlbModel.Pose Pose;
            internal int[] Wheels;
            internal bool[] LeftWheel;
            internal int Crane = -1, Boom = -1;
            internal void Dispose() => Renderer?.Dispose();
            void IDisposable.Dispose() => Dispose();
        }

        private readonly MachineModel[] models = new MachineModel[2];
        private bool loaded;

        private static MachineModel Load(string file)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Licensed", file);
            if (!File.Exists(path)) return null;
            var model = AnimatedGlbModel.Load(path);
            var wheels = new List<int>(); var left = new List<bool>();
            int crane = -1, boom = -1;
            for (int i = 0; i < model.Nodes.Length; i++)
            {
                string name = model.Nodes[i].Name ?? "";
                if (name.StartsWith("wheel", StringComparison.Ordinal)) { wheels.Add(i); left.Add(name.EndsWith(".L", StringComparison.Ordinal)); }
                else if (name == "knee_1") crane = i;
                else if (name == "knee_2") boom = i;
            }
            return new MachineModel { Model = model, Renderer = new AnimatedModelRenderer(model), Pose = model.CreatePose(),
                Wheels = wheels.ToArray(), LeftWheel = left.ToArray(), Crane = crane, Boom = boom };
        }

        /// <summary>Position on the ground plane and heading at a fractional path position.</summary>
        internal static bool TrySample(TerrainMap map, int[] path, double position, out Vector2 point, out Vector2 heading)
        {
            point = default; heading = Vector2.UnitX;
            int k = Math.Clamp((int)Math.Round(position), 0, path.Length - 1);
            if (!map.TryGetTileCenter(path[k], out Vector3 c3)) return false;
            Vector2 c = c3.Xy;
            Vector2 Neighbour(int i) => map.TryGetTileCenter(path[i], out Vector3 n) ? n.Xy : c;
            // Through the edge midpoints, bending at the tile centre: a straight run stays straight, a turn becomes an arc.
            Vector2 entry = k > 0 ? (Neighbour(k - 1) + c) * 0.5f : k + 1 < path.Length ? c - (Neighbour(k + 1) - c) * 0.5f : c;
            Vector2 exit = k + 1 < path.Length ? (Neighbour(k + 1) + c) * 0.5f : k > 0 ? c + (c - Neighbour(k - 1)) * 0.5f : c;
            float t = (float)Math.Clamp(position - k + 0.5, 0, 1);
            point = (1 - t) * (1 - t) * entry + 2 * t * (1 - t) * c + t * t * exit;
            Vector2 d = 2 * (1 - t) * (c - entry) + 2 * t * (exit - c);
            if (d.LengthSquared > 1e-8f) heading = d.Normalized();
            else if ((exit - entry).LengthSquared > 1e-8f) heading = (exit - entry).Normalized();
            return true;
        }

        internal void Draw(Terrain terrain, ForestryLogistics logistics, GraphicsSettings settings, float alpha)
        {
            if (logistics == null) return;
            DrawPiles(terrain, logistics);
            if (logistics.Machines.Count == 0) return;
            if (!loaded)
            {
                loaded = true;
                models[(int)ForestMachineKind.Harvester] = Load("harvester.glb");
                models[(int)ForestMachineKind.Forwarder] = Load("forwarder.glb");
            }
            alpha = Math.Clamp(alpha, 0, 1);
            foreach (var machine in logistics.Machines)
            {
                double position = machine.PreviousPathPosition + (machine.PathPosition - machine.PreviousPathPosition) * alpha;
                if (!TrySample(terrain.Map, machine.Path, position, out Vector2 point, out Vector2 heading)) continue;
                Matrix4 placement = Placement(terrain.Map, point, heading, machine.Kind == ForestMachineKind.Forwarder ? 2.2f : 1.4f);
                if (RenderDevice.Visuals?.ShadowPass != true &&
                    !RenderVisibility.SphereVisible(placement.Row3.Xyz, 4f, RenderDevice.ViewProjection)) continue;
                var model = models[(int)machine.Kind];
                if (model != null) DrawModel(model, machine, position, placement, settings);
                else DrawStandIn(machine, placement);
                if (machine.Kind == ForestMachineKind.Forwarder && machine.Cargo > 0.05f) DrawLoad(machine, placement);
            }
        }

        // Ground-following frame: heading along the path, pitch and roll from the terrain under the machine.
        private static Matrix4 Placement(TerrainMap map, Vector2 point, Vector2 heading, float halfLength)
        {
            map.TryGetSurfaceZ(point.X, point.Y, out float z);
            Vector3 forward = new(heading.X, heading.Y, 0), left = new(-heading.Y, heading.X, 0);
            if (map.TryGetSurfaceZ(point.X + heading.X * halfLength, point.Y + heading.Y * halfLength, out float front) &&
                map.TryGetSurfaceZ(point.X - heading.X * halfLength, point.Y - heading.Y * halfLength, out float back))
                forward.Z = (front - back) / (2 * halfLength);
            if (map.TryGetSurfaceZ(point.X + left.X, point.Y + left.Y, out float side) && map.TryGetSurfaceZ(point.X - left.X, point.Y - left.Y, out float other))
                left.Z = (side - other) / 2;
            forward.Normalize(); Vector3 up = Vector3.Cross(forward, left).Normalized(); left = Vector3.Cross(up, forward).Normalized();
            return new Matrix4(new Vector4(forward, 0), new Vector4(left, 0), new Vector4(up, 0), new Vector4(point.X, point.Y, z + 0.02f, 1));
        }

        private static void DrawModel(MachineModel m, ForestMachine machine, double position, Matrix4 placement, GraphicsSettings settings)
        {
            var model = m.Model;
            // Rest pose, then extra local rotations: wheels roll with the distance driven, the crane works while felling or loading.
            float roll = (float)(position * 5.0 / (0.7 * MetreScale) / 5.0);
            bool working = machine.State is ForestMachineState.Felling or ForestMachineState.Loading or ForestMachineState.Unloading;
            float swing = working ? 0.6f * MathF.Sin((float)machine.WorkTime * 1.3f) : 0;
            float lift = working ? 0.25f * MathF.Sin((float)machine.WorkTime * 2.1f) : 0;
            foreach (int i in model.Order)
            {
                var node = model.Nodes[i];
                Matrix4 local = node.Matrix ?? Matrix4.CreateScale(node.Scale) * Matrix4.CreateFromQuaternion(node.Rotation) * Matrix4.CreateTranslation(node.Translation);
                int wheel = Array.IndexOf(m.Wheels, i);
                if (wheel >= 0) local = Matrix4.CreateRotationY(m.LeftWheel[wheel] ? -roll : roll) * local;
                else if (i == m.Crane) local = Matrix4.CreateRotationY(swing) * local;
                else if (i == m.Boom) local = Matrix4.CreateRotationZ(lift) * local;
                m.Pose.World[i] = node.Parent < 0 ? local : local * m.Pose.World[node.Parent];
            }
            m.Renderer.Draw(m.Pose, Axis * Matrix4.CreateScale(MetreScale) * placement, settings, sourceMaterial: true);
        }

        // Stand-in when the licensed models are missing: chassis, cab, crane post and wheels in the machine colours.
        private static void DrawStandIn(ForestMachine machine, Matrix4 placement)
        {
            bool harvester = machine.Kind == ForestMachineKind.Harvester;
            float s = MetreScale;
            Color body = harvester ? Color.FromArgb(222, 150, 40) : Color.FromArgb(70, 128, 72);
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                float length = harvester ? 7.2f : 10.5f;
                Box(placement, new Vector3(-length / 2, -1.2f, 0.6f) * s, new Vector3(length / 2, 1.2f, 1.5f) * s, body);
                Box(placement, new Vector3(length / 2 - 2.6f, -1.0f, 1.5f) * s, new Vector3(length / 2 - 0.6f, 1.0f, 3.3f) * s, Color.FromArgb(60, 64, 70));
                Box(placement, new Vector3(-0.3f, -0.3f, 1.5f) * s, new Vector3(0.3f, 0.3f, 3.6f) * s, Color.FromArgb(200, 200, 196));
                for (int axle = 0; axle < (harvester ? 2 : 4); axle++)
                {
                    float x = -length / 2 + 1.1f + axle * (length - 2.2f) / (harvester ? 1 : 3);
                    foreach (float y in new[] { -1.3f, 1.3f })
                        Box(placement, new Vector3(x - 0.65f, y - 0.35f, 0) * s, new Vector3(x + 0.65f, y + 0.35f, 1.3f) * s, Color.FromArgb(38, 38, 40));
                }
            });
        }

        // Logs on the forwarder's bunk, filling it as the cargo grows.
        private static void DrawLoad(ForestMachine machine, Matrix4 placement)
        {
            float s = MetreScale;
            int logs = Math.Clamp((int)MathF.Ceiling(machine.CargoFill * 9), 1, 9);
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                for (int i = 0; i < logs; i++)
                {
                    int layer = i / 3, column = i % 3;
                    float y = (column - 1) * 0.55f, z = 1.9f + layer * 0.5f;
                    Box(placement, new Vector3(-5.6f, y - 0.24f, z) * s, new Vector3(-1.4f, y + 0.24f, z + 0.48f) * s,
                        (i & 1) == 0 ? Color.FromArgb(150, 104, 60) : Color.FromArgb(132, 92, 54));
                }
            });
        }

        // Log piles beside the harvester's track and the stack at each landing.
        private static void DrawPiles(Terrain terrain, ForestryLogistics logistics)
        {
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                foreach (var site in logistics.Sites)
                {
                    foreach (var pile in site.Piles) Pile(terrain.Map, pile.Key, pile.Value, 0.6f);
                    if (site.Landing >= 0 && site.LandingStock > 0.05f) Pile(terrain.Map, site.Landing, site.LandingStock, 1f);
                }
            });
        }

        private static void Pile(TerrainMap map, int tile, float volume, float size)
        {
            if (!map.TryGetTileCenter(tile, out Vector3 centre)) return;
            uint hash = (uint)tile * 2654435761u;
            float yaw = (hash >> 20) / 4096f * MathF.PI;
            Vector2 heading = new(MathF.Cos(yaw), MathF.Sin(yaw));
            // Off-centre so the machine passing the tile does not drive through the pile.
            Vector2 at = centre.Xy + new Vector2(-heading.Y, heading.X) * 1.3f;
            Matrix4 placement = Placement(map, at, heading, 0.8f);
            int logs = Math.Clamp((int)MathF.Ceiling(volume / 1.2f), 1, 15);
            float s = MetreScale * size;
            for (int i = 0; i < logs; i++)
            {
                int layer = i < 5 ? 0 : i < 9 ? 1 : i < 12 ? 2 : i < 14 ? 3 : 4;
                int first = layer switch { 0 => 0, 1 => 5, 2 => 9, 3 => 12, _ => 14 };
                int inRow = layer switch { 0 => 5, 1 => 4, 2 => 3, 3 => 2, _ => 1 };
                float y = (i - first - (inRow - 1) / 2f) * 0.5f, z = layer * 0.43f;
                Box(placement, new Vector3(-2f, y - 0.23f, z) * s, new Vector3(2f, y + 0.23f, z + 0.46f) * s,
                    (i % 3) == 0 ? Color.FromArgb(158, 112, 66) : Color.FromArgb(136, 96, 56));
            }
        }

        // An axis-aligned box in the machine frame: top lit, sides shaded, end faces showing the light cut wood.
        private static void Box(Matrix4 frame, Vector3 min, Vector3 max, Color colour)
        {
            Vector3 P(float x, float y, float z) => Vector3.TransformPosition(new Vector3(x, y, z), frame);
            Color Shade(float f) => Color.FromArgb(colour.A, (int)(colour.R * f), (int)(colour.G * f), (int)(colour.B * f));
            void Face(Color c, Vector3 a, Vector3 b, Vector3 d, Vector3 e)
            {
                DynamicPrimitiveBatch.Color4(c);
                DynamicPrimitiveBatch.Vertex3(a); DynamicPrimitiveBatch.Vertex3(b); DynamicPrimitiveBatch.Vertex3(d); DynamicPrimitiveBatch.Vertex3(e);
            }
            Face(Shade(1.05f), P(min.X, min.Y, max.Z), P(max.X, min.Y, max.Z), P(max.X, max.Y, max.Z), P(min.X, max.Y, max.Z));
            Face(Shade(0.8f), P(min.X, min.Y, min.Z), P(max.X, min.Y, min.Z), P(max.X, min.Y, max.Z), P(min.X, min.Y, max.Z));
            Face(Shade(0.7f), P(min.X, max.Y, min.Z), P(max.X, max.Y, min.Z), P(max.X, max.Y, max.Z), P(min.X, max.Y, max.Z));
            Face(Shade(0.9f), P(max.X, min.Y, min.Z), P(max.X, max.Y, min.Z), P(max.X, max.Y, max.Z), P(max.X, min.Y, max.Z));
            Face(Shade(0.9f), P(min.X, min.Y, min.Z), P(min.X, max.Y, min.Z), P(min.X, max.Y, max.Z), P(min.X, min.Y, max.Z));
        }

        public void Dispose()
        {
            foreach (var m in models) m?.Dispose();
            Array.Clear(models);
            loaded = false;
        }
    }
}
