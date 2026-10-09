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
            internal int Crane = -1, Boom = -1, Trailer = -1;
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
            int crane = -1, boom = -1, trailer = -1;
            for (int i = 0; i < model.Nodes.Length; i++)
            {
                string name = model.Nodes[i].Name ?? "";
                if (name.StartsWith("wheel", StringComparison.Ordinal)) { wheels.Add(i); left.Add(name.EndsWith(".L", StringComparison.Ordinal)); }
                else if (name == "knee_1") crane = i;
                else if (name == "knee_2") boom = i;
                else if (name == "trailer") trailer = i;
            }
            return new MachineModel { Model = model, Renderer = new AnimatedModelRenderer(model), Pose = model.CreatePose(),
                Wheels = wheels.ToArray(), LeftWheel = left.ToArray(), Crane = crane, Boom = boom, Trailer = trailer };
        }

        // Stack (sarang) models, smallest first, with the timber volume each one shows.
        private static readonly (string File, float Volume)[] StackSizes =
            { ("stack-1.glb", 4), ("stack-2.glb", 10), ("stack-3.glb", 20), ("stack-4.glb", 40), ("stack-5.glb", 80) };
        private readonly AnimatedGlbModel[] stacks = new AnimatedGlbModel[StackSizes.Length];
        private readonly AnimatedModelRenderer[] stackRenderers = new AnimatedModelRenderer[StackSizes.Length];
        private readonly AnimatedGlbModel.Pose[] stackPoses = new AnimatedGlbModel.Pose[StackSizes.Length];
        private readonly bool[] stackTried = new bool[StackSizes.Length];

        /// <summary>Forwarder geometry, metres: the frame joint behind the front unit and the trailer bogie behind the joint.</summary>
        internal const float ForwarderJoint = 2.2f, ForwarderBogie = 4.8f;

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

        private ImportedSceneAsset depotModel;
        private bool depotTried;

        /// <summary>Where a parked vehicle of a depot stands: the yard in front of the hangar, one bay per vehicle.</summary>
        internal static Vector3 ParkingBay(Terrain terrain, Depot depot, int bay)
        {
            float tile = terrain.Map.TileWidth;
            // Machines side by side next to the hall; the truck lengthwise along the front of the yard.
            var p = depot.Position + (bay < 2 ? new Vector3((bay - 0.5f) * tile * 0.9f, -tile * 0.28f, 0)
                : new Vector3(0, -tile * (0.72f + 0.2f * (bay - 2)), 0));
            if (terrain.Map.TryGetSurfaceZ(p.X, p.Y, out float z)) p.Z = z + 0.02f;
            return p;
        }

        // The hangar on the back half of the 2×2 yard; parked vehicles on the front half.
        private void DrawDepots(Terrain terrain, ForestryLogistics logistics, GraphicsSettings settings)
        {
            if (logistics.Depots.Count == 0) return;
            float tile = terrain.Map.TileWidth;
            if (!depotTried)
            {
                depotTried = true;
                if (File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Licensed", "depot.glb")))
                    depotModel = new ImportedSceneAsset("Assets/Licensed/depot.glb", tile * 1.7f);
            }
            foreach (var depot in logistics.Depots)
            {
                Vector3 hall = depot.Position + new Vector3(0, tile * 0.45f, 0);
                if (depotModel != null) { using var state = depotModel.BeginBatch(); depotModel.Draw(Matrix4.CreateTranslation(hall), settings, state); }
                else
                {
                    // Stand-in: a long shed with a dark gable roof and an open door toward the yard.
                    Matrix4 frame = Matrix4.CreateTranslation(hall);
                    DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
                    {
                        Box(frame, new Vector3(-tile * 0.85f, -tile * 0.4f, 0), new Vector3(tile * 0.85f, tile * 0.4f, tile * 0.38f), Color.FromArgb(178, 176, 170));
                        Box(frame, new Vector3(-tile * 0.88f, -tile * 0.44f, tile * 0.38f), new Vector3(tile * 0.88f, tile * 0.44f, tile * 0.5f), Color.FromArgb(120, 62, 46));
                        Box(frame, new Vector3(-tile * 0.3f, -tile * 0.41f, 0), new Vector3(tile * 0.3f, -tile * 0.39f, tile * 0.3f), Color.FromArgb(48, 48, 50));
                    });
                }
                // The yard: a gravel apron in front of the hall.
                Matrix4 yard = Matrix4.CreateTranslation(depot.Position + new Vector3(0, -tile * 0.5f, 0.01f));
                DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
                    Box(yard, new Vector3(-tile * 0.95f, -tile * 0.45f, 0), new Vector3(tile * 0.95f, tile * 0.45f, 0.01f), Color.FromArgb(150, 141, 122)));
                int truckBay = 2;
                foreach (var truck in logistics.Trucks)
                    if (truck.Home == depot && truck.Phase == TruckPhase.Parked)
                        VehicleRenderer.DrawParked(terrain, ParkingBay(terrain, depot, truckBay++), 0);
            }
        }

        /// <summary>A blinking warning triangle over a broken-down vehicle.</summary>
        private static void BreakdownSign(Vector3 at, float phase)
        {
            if (RenderDevice.Visuals?.ShadowPass == true || (int)(phase * 2) % 2 == 1) return;
            float s = MetreScale;
            Vector3 c = at + new Vector3(0, 0, 5.5f * s);
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Triangles, () =>
            {
                DynamicPrimitiveBatch.Color4(Color.FromArgb(240, 186, 40));
                DynamicPrimitiveBatch.Vertex3(c + new Vector3(-0.9f, 0, 0) * s);
                DynamicPrimitiveBatch.Vertex3(c + new Vector3(0.9f, 0, 0) * s);
                DynamicPrimitiveBatch.Vertex3(c + new Vector3(0, 0, 1.6f) * s);
                DynamicPrimitiveBatch.Vertex3(c + new Vector3(0, -0.9f, 0) * s);
                DynamicPrimitiveBatch.Vertex3(c + new Vector3(0, 0.9f, 0) * s);
                DynamicPrimitiveBatch.Vertex3(c + new Vector3(0, 0, 1.6f) * s);
            });
        }

        internal void Draw(Terrain terrain, ForestryLogistics logistics, GraphicsSettings settings, float alpha)
        {
            if (logistics == null) return;
            DrawDepots(terrain, logistics, settings);
            foreach (var truck in logistics.Trucks)
                if (truck.Upkeep.Broken && truck.Vehicle != null)
                {
                    truck.Vehicle.GetSegment(truck.Vehicle.RoutePosition, out int from, out _, out _);
                    if (terrain.Map.TryGetTileCenter(from, out Vector3 at)) BreakdownSign(at, truck.Upkeep.RepairLeft);
                }
            DrawPiles(terrain, logistics, settings);
            if (logistics.Machines.Count == 0) return;
            if (!loaded)
            {
                loaded = true;
                models[(int)ForestMachineKind.Harvester] = Load("harvester.glb");
                models[(int)ForestMachineKind.Forwarder] = Load("forwarder.glb");
            }
            float tile = terrain.Map.TileWidth;
            alpha = Math.Clamp(alpha, 0, 1);
            foreach (var machine in logistics.Machines)
            {
                double position = machine.PreviousPathPosition + (machine.PathPosition - machine.PreviousPathPosition) * alpha;
                if (!TrySample(terrain.Map, machine.Path, position, out Vector2 point, out Vector2 heading)) continue;
                if (machine.Site == null && machine.Home != null && machine.State == ForestMachineState.Parked && machine.Tile == machine.Home.TileId)
                {
                    // At home: in its bay in the yard, facing the hall.
                    point = ParkingBay(terrain, machine.Home, machine.Kind == ForestMachineKind.Harvester ? 0 : 1).Xy;
                    heading = Vector2.UnitY;
                }
                Matrix4 placement = Placement(terrain.Map, point, heading, machine.Kind == ForestMachineKind.Forwarder ? 2.2f : 1.4f);
                if (RenderDevice.Visuals?.ShadowPass != true &&
                    !RenderVisibility.SphereVisible(placement.Row3.Xyz, 4f, RenderDevice.ViewProjection)) continue;
                float articulation = machine.Kind == ForestMachineKind.Forwarder
                    ? Articulation(terrain.Map, machine.Path, position, point, heading, tile) : 0;
                Matrix4 trailer = TrailerFrame(placement, articulation);
                var model = models[(int)machine.Kind];
                if (model != null) DrawModel(model, machine, position, placement, articulation, settings);
                else DrawStandIn(machine, placement, trailer);
                if (machine.Upkeep.Broken) BreakdownSign(placement.Row3.Xyz, (float)machine.Upkeep.RepairLeft);
                if (machine.Kind == ForestMachineKind.Forwarder && machine.Cargo > 0.05f) DrawLoad(machine, trailer);
            }
        }

        // Ground-following frame: heading along the path, pitch and roll from the terrain under the machine.
        private static Matrix4 Placement(TerrainMap map, Vector2 point, Vector2 heading, float halfLength)
        {
            map.TryGetDrivingSurfaceZ(point.X, point.Y, out float z);
            Vector3 forward = new(heading.X, heading.Y, 0), left = new(-heading.Y, heading.X, 0);
            if (map.TryGetDrivingSurfaceZ(point.X + heading.X * halfLength, point.Y + heading.Y * halfLength, out float front) &&
                map.TryGetDrivingSurfaceZ(point.X - heading.X * halfLength, point.Y - heading.Y * halfLength, out float back))
                forward.Z = (front - back) / (2 * halfLength);
            if (map.TryGetDrivingSurfaceZ(point.X + left.X, point.Y + left.Y, out float side) && map.TryGetDrivingSurfaceZ(point.X - left.X, point.Y - left.Y, out float other))
                left.Z = (side - other) / 2;
            forward.Normalize(); Vector3 up = Vector3.Cross(forward, left).Normalized(); left = Vector3.Cross(up, forward).Normalized();
            return new Matrix4(new Vector4(forward, 0), new Vector4(left, 0), new Vector4(up, 0), new Vector4(point.X, point.Y, z + 0.02f, 1));
        }

        /// <summary>
        /// Forwarder frame steering: the rear unit points from its bogie, which rolls along the path behind, to the frame
        /// joint, so in a bend the two halves fold like a real articulated forwarder. Radians, + = rear unit to the left.
        /// </summary>
        internal static float Articulation(TerrainMap map, int[] path, double position, Vector2 point, Vector2 heading, float tileSize)
        {
            float behind = (ForwarderJoint + ForwarderBogie) * MetreScale / tileSize;
            if (!TrySample(map, path, position - behind, out Vector2 bogie, out _)) return 0;
            Vector2 joint = point - heading * ForwarderJoint * MetreScale;
            Vector2 rear = joint - bogie;
            if (rear.LengthSquared < 1e-8f) return 0;
            float angle = MathF.Atan2(heading.X * rear.Y - heading.Y * rear.X, Vector2.Dot(heading, rear));
            return Math.Clamp(angle, -0.8f, 0.8f);
        }

        // The rear unit's frame: turned by the articulation about the frame joint.
        private static Matrix4 TrailerFrame(Matrix4 placement, float articulation)
        {
            float joint = -ForwarderJoint * MetreScale;
            return Matrix4.CreateTranslation(-joint, 0, 0) * Matrix4.CreateRotationZ(articulation) * Matrix4.CreateTranslation(joint, 0, 0) * placement;
        }

        private static void DrawModel(MachineModel m, ForestMachine machine, double position, Matrix4 placement, float articulation, GraphicsSettings settings)
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
                else if (i == m.Trailer)
                    // Turn the rear unit about the up axis of its parent (the front frame) at the joint.
                    local = Matrix4.CreateScale(node.Scale) * Matrix4.CreateFromQuaternion(node.Rotation)
                        * Matrix4.CreateRotationY(articulation) * Matrix4.CreateTranslation(node.Translation);
                m.Pose.World[i] = node.Parent < 0 ? local : local * m.Pose.World[node.Parent];
            }
            m.Renderer.Draw(m.Pose, Axis * Matrix4.CreateScale(MetreScale) * placement, settings, sourceMaterial: true);
        }

        // Stand-in when the licensed models are missing: chassis, cab, crane post and wheels in the machine colours.
        private static void DrawStandIn(ForestMachine machine, Matrix4 placement, Matrix4 trailer)
        {
            bool harvester = machine.Kind == ForestMachineKind.Harvester;
            float s = MetreScale;
            Color body = harvester ? Color.FromArgb(222, 150, 40) : Color.FromArgb(70, 128, 72), tyre = Color.FromArgb(38, 38, 40);
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                // Front unit: engine, cab and crane post over two wheels a side.
                Box(placement, new Vector3(-2.0f, -1.2f, 0.6f) * s, new Vector3(2.6f, 1.2f, 1.5f) * s, body);
                Box(placement, new Vector3(0.2f, -1.0f, 1.5f) * s, new Vector3(2.2f, 1.0f, 3.3f) * s, Color.FromArgb(60, 64, 70));
                Box(placement, new Vector3(-1.2f, -0.3f, 1.5f) * s, new Vector3(-0.6f, 0.3f, 3.6f) * s, Color.FromArgb(200, 200, 196));
                foreach (float x in new[] { 1.6f, -0.8f })
                    foreach (float y in new[] { -1.3f, 1.3f }) Wheel(placement, new Vector3(x, y, 0.65f) * s, 0.65f * s, 0.35f * s, tyre);
                if (harvester)
                {
                    Box(placement, new Vector3(-3.6f, -1.1f, 0.6f) * s, new Vector3(-2.0f, 1.1f, 1.5f) * s, body);
                    return;
                }
                // Rear unit (articulated): load bed with stakes over a bogie.
                Box(trailer, new Vector3(-7.4f, -1.2f, 0.9f) * s, new Vector3(-2.4f, 1.2f, 1.4f) * s, body);
                foreach (float x in new[] { -7.2f, -4.9f, -2.6f })
                    foreach (float y in new[] { -1.15f, 1.15f })
                        Box(trailer, new Vector3(x - 0.08f, y - 0.08f, 1.4f) * s, new Vector3(x + 0.08f, y + 0.08f, 3.3f) * s, Color.FromArgb(52, 54, 58));
                foreach (float x in new[] { -6.2f, -4.8f })
                    foreach (float y in new[] { -1.3f, 1.3f }) Wheel(trailer, new Vector3(x, y, 0.65f) * s, 0.65f * s, 0.35f * s, tyre);
            });
        }

        // Round logs between the forwarder's stakes, filling the bunk as the cargo grows.
        private static void DrawLoad(ForestMachine machine, Matrix4 trailer)
        {
            float s = MetreScale;
            int logs = Math.Clamp((int)MathF.Ceiling(machine.CargoFill * 12), 1, 12);
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                for (int i = 0; i < logs; i++)
                {
                    int layer = i < 5 ? 0 : i < 9 ? 1 : 2, first = layer == 0 ? 0 : layer == 1 ? 5 : 9, inRow = 5 - layer;
                    float r = 0.21f, y = (i - first - (inRow - 1) / 2f) * 2 * r, z = 1.4f + r + layer * r * 1.73f;
                    RoundLog(trailer, new Vector3(-7.5f + (i % 2) * 0.3f, y, z) * s, new Vector3(-2.3f - (i % 3) * 0.2f, y, z) * s, r * s, i);
                }
            });
        }

        // Stack sites (sarangok): an empty one shows its marking stakes, a filled one its round logs.
        private void DrawPiles(Terrain terrain, ForestryLogistics logistics, GraphicsSettings settings)
        {
            foreach (var stack in logistics.Stacks)
            {
                if (stack.Volume > 0.05f) Stack(terrain.Map, stack.Tile, stack.Volume, settings);
                else StackStakes(terrain.Map, stack.Tile);
            }
        }

        // Four orange-topped stakes mark where the stack will rise.
        private static void StackStakes(TerrainMap map, int tile)
        {
            StackPlace(map, tile, out Vector2 at, out Vector2 heading);
            Matrix4 placement = Placement(map, at, heading, 0.8f);
            float s = MetreScale;
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                foreach (float x in new[] { -2.2f, 2.2f })
                    foreach (float y in new[] { -1.1f, 1.1f })
                    {
                        Box(placement, new Vector3(x - 0.07f, y - 0.07f, 0) * s, new Vector3(x + 0.07f, y + 0.07f, 1.4f) * s, Color.FromArgb(150, 112, 70));
                        Box(placement, new Vector3(x - 0.09f, y - 0.09f, 1.4f) * s, new Vector3(x + 0.09f, y + 0.09f, 1.7f) * s, Color.FromArgb(240, 140, 40));
                    }
            });
        }

        /// <summary>Where a stack stands on its tile: along the trail through it (set off to one side), or along the
        /// network beside it.</summary>
        private static void StackPlace(TerrainMap map, int tile, out Vector2 at, out Vector2 heading)
        {
            map.TryGetTileCenter(tile, out Vector3 centre);
            bool onTrail = map.IsSkidTrail(tile);
            RoadEdge edges = onTrail ? map.GetSkidTrailEdges(tile) : RoadEdge.None;
            if (!onTrail)
            {
                // Beside the network: the logs lie parallel to the road or trail next to them, ready for the crane.
                Span<int> next = stackalloc int[4];
                int count = map.GetTileNeighbours(tile, next);
                for (int i = 0; i < count; i++)
                    if (map.IsNetworkTile(next[i]) && map.TryGetTileCenter(next[i], out Vector3 other))
                    {
                        Vector2 across = (other.Xy - centre.Xy).Normalized();
                        heading = new Vector2(-across.Y, across.X);
                        at = centre.Xy + across * 0.15f * map.TileWidth;
                        return;
                    }
            }
            Tile t = map.Tiles[tile];
            Vector2 w = new(t.W.xPos, t.W.yPos), s = new(t.S.xPos, t.S.yPos), n = new(t.N.xPos, t.N.yPos);
            // The tile's u axis runs W→S, its v axis W→N; a trail along WS/EN edges runs along v.
            bool alongV = (edges & (RoadEdge.WS | RoadEdge.EN)) != 0 && (edges & (RoadEdge.SE | RoadEdge.NW)) == 0;
            bool alongU = (edges & (RoadEdge.SE | RoadEdge.NW)) != 0 && (edges & (RoadEdge.WS | RoadEdge.EN)) == 0;
            Vector2 axis = alongU ? (s - w) : (n - w);
            if (!alongU && !alongV) axis = ((tile * 2654435761u) & 1) == 0 ? s - w : n - w;
            heading = axis.Normalized();
            Vector2 side = new(-heading.Y, heading.X);
            at = onTrail ? centre.Xy + side * (0.3f * axis.Length) : centre.Xy;
        }

        private void Stack(TerrainMap map, int tile, float volume, GraphicsSettings settings)
        {
            StackPlace(map, tile, out Vector2 at, out Vector2 heading);
            Matrix4 placement = Placement(map, at, heading, 0.8f);
            // One stack model that grows with the wood: it rises to full height first, then gets longer along the track.
            const int Model = 2;
            if (TryStackModel(Model))
            {
                float full = StackSizes[Model].Volume;
                float height = Math.Clamp(MathF.Sqrt(volume / (full * 0.3f)), 0.35f, 1f);
                float length = Math.Clamp(volume / (full * height), 0.2f, 2.2f);
                var scale = Matrix4.CreateScale(length, height, height) * Matrix4.CreateScale(MetreScale * 0.55f);
                stackRenderers[Model].Draw(stackPoses[Model], scale * Axis * placement, settings, sourceMaterial: true);
                return;
            }
            // Without the model: a stack of round logs, more layers as the volume grows.
            int logs = Math.Clamp((int)MathF.Ceiling(volume / 0.8f), 1, 21);
            float r = 0.22f, sScale = MetreScale;
            DynamicPrimitiveBatch.Draw(PrimitiveTopology.Quads, () =>
            {
                int i = 0;
                for (int layer = 0, inRow = 6; i < logs && inRow > 0; layer++, inRow--)
                    for (int k = 0; k < inRow && i < logs; k++, i++)
                    {
                        float y = (k - (inRow - 1) / 2f) * 2 * r, z = r + layer * r * 1.73f;
                        RoundLog(placement, new Vector3(-2.1f + (i % 3) * 0.15f, y, z) * sScale, new Vector3(2.1f - (i % 2) * 0.2f, y, z) * sScale, r * sScale, i);
                    }
            });
        }

        private bool TryStackModel(int size)
        {
            if (stacks[size] != null) return true;
            if (!stackTried[size])
            {
                stackTried[size] = true;
                string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Licensed", StackSizes[size].File);
                if (!File.Exists(path)) return false;
                stacks[size] = AnimatedGlbModel.Load(path);
                stackRenderers[size] = new AnimatedModelRenderer(stacks[size]);
                stackPoses[size] = stacks[size].CreatePose();
                stackPoses[size].Evaluate(null, 0);
            }
            return stacks[size] != null;
        }

        /// <summary>A round log: an eight-sided bark mantle with light cut ends, from <paramref name="a"/> to <paramref name="b"/>.</summary>
        private static void RoundLog(Matrix4 frame, Vector3 a, Vector3 b, float radius, int seed)
        {
            Vector3 axis = (b - a).Normalized();
            Vector3 u = Vector3.Cross(axis, Vector3.UnitZ).LengthSquared > 1e-6f ? Vector3.Cross(axis, Vector3.UnitZ).Normalized() : Vector3.UnitY;
            Vector3 v = Vector3.Cross(u, axis);
            Vector3 P(Vector3 p) => Vector3.TransformPosition(p, frame);
            Color bark = (seed % 3) switch { 0 => Color.FromArgb(104, 74, 48), 1 => Color.FromArgb(92, 64, 42), _ => Color.FromArgb(116, 84, 54) };
            Color wood = (seed % 2) == 0 ? Color.FromArgb(222, 184, 132) : Color.FromArgb(208, 166, 112);
            const int Sides = 8;
            for (int i = 0; i < Sides; i++)
            {
                float a0 = i * MathF.Tau / Sides, a1 = (i + 1) * MathF.Tau / Sides;
                Vector3 o0 = (u * MathF.Cos(a0) + v * MathF.Sin(a0)) * radius, o1 = (u * MathF.Cos(a1) + v * MathF.Sin(a1)) * radius;
                // Faces toward the sky are lighter: simple shading without normals.
                float light = 0.75f + 0.3f * Vector3.Dot(((o0 + o1) * 0.5f).Normalized(), Vector3.UnitZ);
                DynamicPrimitiveBatch.Color4(Color.FromArgb((int)Math.Min(255, bark.R * light), (int)Math.Min(255, bark.G * light), (int)Math.Min(255, bark.B * light)));
                DynamicPrimitiveBatch.Vertex3(P(a + o0)); DynamicPrimitiveBatch.Vertex3(P(b + o0));
                DynamicPrimitiveBatch.Vertex3(P(b + o1)); DynamicPrimitiveBatch.Vertex3(P(a + o1));
            }
            DynamicPrimitiveBatch.Color4(wood);
            foreach (var (end, sign) in new[] { (a, -1f), (b, 1f) })
                for (int i = 0; i < Sides; i += 2)
                {
                    float a0 = i * MathF.Tau / Sides, a1 = (i + 1) * MathF.Tau / Sides, a2 = (i + 2) * MathF.Tau / Sides;
                    Vector3 c = end + axis * sign * 0.001f;
                    DynamicPrimitiveBatch.Vertex3(P(c));
                    DynamicPrimitiveBatch.Vertex3(P(c + (u * MathF.Cos(a0) + v * MathF.Sin(a0)) * radius));
                    DynamicPrimitiveBatch.Vertex3(P(c + (u * MathF.Cos(a1) + v * MathF.Sin(a1)) * radius));
                    DynamicPrimitiveBatch.Vertex3(P(c + (u * MathF.Cos(a2) + v * MathF.Sin(a2)) * radius));
                }
        }

        private static void Wheel(Matrix4 frame, Vector3 centre, float radius, float halfWidth, Color colour) =>
            RoundLogLike(frame, centre - Vector3.UnitY * halfWidth, centre + Vector3.UnitY * halfWidth, radius, colour);

        private static void RoundLogLike(Matrix4 frame, Vector3 a, Vector3 b, float radius, Color colour)
        {
            Vector3 P(Vector3 p) => Vector3.TransformPosition(p, frame);
            const int Sides = 8;
            DynamicPrimitiveBatch.Color4(colour);
            for (int i = 0; i < Sides; i++)
            {
                float a0 = i * MathF.Tau / Sides, a1 = (i + 1) * MathF.Tau / Sides;
                Vector3 o0 = new(MathF.Cos(a0) * radius, 0, MathF.Sin(a0) * radius), o1 = new(MathF.Cos(a1) * radius, 0, MathF.Sin(a1) * radius);
                DynamicPrimitiveBatch.Vertex3(P(a + o0)); DynamicPrimitiveBatch.Vertex3(P(b + o0));
                DynamicPrimitiveBatch.Vertex3(P(b + o1)); DynamicPrimitiveBatch.Vertex3(P(a + o1));
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
            foreach (var r in stackRenderers) r?.Dispose();
            depotModel?.Dispose(); depotModel = null; depotTried = false;
            Array.Clear(models); Array.Clear(stacks); Array.Clear(stackRenderers); Array.Clear(stackPoses); Array.Clear(stackTried);
            loaded = false;
        }
    }
}
