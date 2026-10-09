using System;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    static class VehicleRenderer
    {
        private static int outlineBudget;
        private static float outlinePixelsPerUnit;
        internal static void BeginFrame(RenderContext context, GraphicsSettings settings)
        {
            outlinePixelsPerUnit = context.PixelsPerWorldUnit;
            TexturedTruckModel.Settings = settings;
            // Edge highlight on every vehicle once it is a few pixels big: trucks and forest machines share the budget.
            outlineBudget = settings.Enhanced && settings.VehicleOutlines && context.PixelsPerWorldUnit >= 3 ?
                settings.Quality == GraphicsQuality.High ? 24 : settings.Quality == GraphicsQuality.Medium ? 12 : 4 : 0;
        }

        /// <summary>World-space outline width for one more vehicle this frame, or 0 when the budget is spent.</summary>
        internal static float TakeOutline(float scale = 1)
        {
            if (RenderDevice.Visuals?.ShadowPass == true || outlineBudget <= 0 || outlinePixelsPerUnit <= 0) return 0;
            outlineBudget--;
            return 0.8f / outlinePixelsPerUnit / scale;
        }
        public static void Draw(VehicleSystem vehicles, Terrain terrain, float interpolationAlpha)
        {
            if (vehicles.Count == 0) return;
            EnsureModel();
            float scale=terrain.RoadLaneWidth*DioramaScale.TruckLaneFill/importedModel.Width;

            {
                foreach (Vehicle vehicle in vehicles.Vehicles)
                {
                    double routePosition = vehicle.InterpolatedRoutePosition(interpolationAlpha);
                    if (vehicle.RoadRoute != null)
                    {
                        vehicle.RoadRoute.GetPose(routePosition, out Vector3 center, out Vector3 forward,
                            out Vector3 left, out Vector3 up, importedModel.Wheelbase*scale, importedModel.AxleMidpoint*scale);
                        if (Culled(center, scale)) continue;
                        var (articulation, trailerPitch) = Articulation(vehicle.RoadRoute, routePosition, center, forward, left, up, scale);
                        DrawTruck(new VehicleTransform(center, forward, left, up), vehicle.VisualCargoFill, scale, articulation, trailerPitch,
                            (float)(routePosition * vehicle.RoadRoute.TileLength / (0.23*scale)),
                            vehicle.RoadRoute.BodyCurvature(routePosition,importedModel.Wheelbase*scale),
                            VehicleVisualMotion.Suspension(routePosition*vehicle.RoadRoute.TileLength,
                                (float)vehicle.CurrentSpeed,Math.Max(vehicle.RoadRoute.Roughness(routePosition,importedModel.Wheelbase*scale),vehicle.RoadDamage),vehicle.CargoFill));
                        continue;
                    }
                    vehicle.GetSegment(vehicle.InterpolatedRoutePosition(interpolationAlpha),
                        out int fromTile, out int toTile, out float amount);
                    if (!terrain.Map.TryGetRoadTileCenter(fromTile, out Vector3 from)
                        || !terrain.Map.TryGetRoadTileCenter(toTile, out Vector3 to)) continue;

                    Vector3 direction = to - from;
                    float yaw = MathF.Atan2(direction.Y, direction.X);
                    float horizontalLength = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
                    float pitch = MathF.Atan2(direction.Z, horizontalLength);
                    Vector3 position = Vector3.Lerp(from, to, amount);
                    position.Z += 0.025f;
                    if (Culled(position, scale)) continue;
                    DrawTruck(new VehicleTransform(position, yaw, pitch), vehicle.VisualCargoFill,scale);
                }
            }
        }

        private static bool Culled(Vector3 center, float scale) => RenderDevice.Visuals?.ShadowPass != true &&
            !RenderVisibility.SphereVisible(center, importedModel.Radius * scale + 0.5f, RenderDevice.ViewProjection);

        /// <summary>
        /// Semi-trailer yaw relative to the tractor: the trailer points from its own axle group, which rolls along
        /// the road behind, to the kingpin on the tractor. So in a bend the trailer cuts in and its wheels follow the arc,
        /// and over a crest or a dip it pitches so its wheels stay on the road. Both angles are in the tractor's frame.
        /// </summary>
        internal static (float Yaw, float Pitch) Articulation(VehicleRoadRoute route, double position, Vector3 center,
            Vector3 forward, Vector3 left, Vector3 up, float scale)
        {
            EnsureModel();
            if (!importedModel.Articulated) return (0, 0);
            // The kingpin projected to road level (the pose centre is at road level).
            Vector3 kingpin = center + forward * (importedModel.Kingpin * scale);
            // Find the road point exactly one trailer length from the kingpin (the arc and the tractor's chord differ).
            float length = (importedModel.Kingpin - importedModel.TrailerAxle) * scale;
            float behind = (importedModel.AxleMidpoint - importedModel.TrailerAxle) * scale;
            Vector3 d = kingpin - route.PointBehind(position, behind);
            for (int i = 0; i < 4; i++)
            {
                behind += length - d.Length;
                d = kingpin - route.PointBehind(position, behind);
            }
            float x = Vector3.Dot(d, forward), y = Vector3.Dot(d, left), z = Vector3.Dot(d, up);
            if (x * x + y * y < 1e-8f) return (0, 0);
            return (Math.Clamp(MathF.Atan2(y, x), -1.2f, 1.2f), Math.Clamp(MathF.Atan2(z, MathF.Sqrt(x * x + y * y)), -0.5f, 0.5f));
        }

        /// <summary>Height of the drawn trailer axle above the road under it (diagnostics), world units.</summary>
        internal static float TrailerAxleGap(VehicleRoadRoute route, double position)
        {
            EnsureModel();
            float scale = TruckScale;
            route.GetPose(position, out Vector3 center, out Vector3 forward, out Vector3 left, out Vector3 up,
                importedModel.Wheelbase * scale, importedModel.AxleMidpoint * scale);
            var (yaw, pitch) = Articulation(route, position, center, forward, left, up, scale);
            var hinge = Matrix4.CreateTranslation(-importedModel.Kingpin, 0, 0) * Matrix4.CreateRotationY(-pitch)
                * Matrix4.CreateRotationZ(yaw) * Matrix4.CreateTranslation(importedModel.Kingpin, 0, 0);
            Vector3 local = Vector3.TransformPosition(new Vector3(importedModel.TrailerAxle, 0, 0), hinge) * scale;
            Vector3 world = center + forward * local.X + left * local.Y + up * local.Z;
            // Road height under the drawn axle: search along the road for the nearest point in plan.
            float best = float.MaxValue, height = 0;
            for (float behind = 0; behind < importedModel.Radius * 2 * scale; behind += 0.01f * scale)
            {
                Vector3 road = route.PointBehind(position, behind);
                float plan = (road.Xy - world.Xy).LengthSquared;
                if (plan < best) { best = plan; height = road.Z; }
            }
            return world.Z - height;
        }

        internal static float TruckScale { get; set; } = 1;

        /// <summary>An empty truck standing still (parked at its depot).</summary>
        internal static void DrawParked(Terrain terrain, Vector3 position, float yaw)
        {
            EnsureModel();
            float scale = terrain.RoadLaneWidth * DioramaScale.TruckLaneFill / importedModel.Width;
            DrawTruck(new VehicleTransform(position, yaw, 0), 0, scale);
        }

        private static void DrawTruck(VehicleTransform transform, float cargoFill, float scale, float articulation = 0, float trailerPitch = 0, float wheelAngle = 0,float curvature=0,Matrix4? suspension=null)
        {
            EnsureModel();
            Matrix4 matrix=new Matrix4(
                new Vector4(transform.Apply(1,0,0)-transform.Apply(0,0,0),0),
                new Vector4(transform.Apply(0,1,0)-transform.Apply(0,0,0),0),
                new Vector4(transform.Apply(0,0,1)-transform.Apply(0,0,0),0),
                new Vector4(transform.Apply(0,0,0),1));
            using(RenderDevice.CreateStateScope().Disable(RenderCapability.CullFace))
                {
                float outline = RenderDevice.Visuals?.ShadowPass != true && outlineBudget > 0 ? 0.7f / outlinePixelsPerUnit / scale : 0;
                if (outline > 0) outlineBudget--;
                importedModel.Draw(Matrix4.CreateScale(scale)*matrix,cargoFill,wheelAngle,curvature,scale,suspension,outline,articulation,trailerPitch);
            }
        }

        private static ITruckModel importedModel;
        internal static float ModelWidth { get { EnsureModel(); return importedModel.Width; } }
        internal static bool ModelArticulated { get { EnsureModel(); return importedModel.Articulated; } }
        private static void EnsureModel()
        {
            if (importedModel != null) return;
            string path = ModelPath();
            importedModel = path.EndsWith("-textured.glb", StringComparison.Ordinal) ? new TexturedTruckModel(path) : GlbTruckModel.Load(path);
            RenderDevice.Disposing += DisposeImportedModel;
        }

        /// <summary>
        /// The purchased truck (tools/convert_licensed_truck.py, kept out of git: its licence forbids redistribution)
        /// when it is installed locally; otherwise the repository's own log truck.
        /// </summary>
        internal static string ModelPath()
        {
            string licensed = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Licensed", "log-truck-textured.glb");
            return System.IO.File.Exists(licensed) ? licensed : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Vehicles", "log-truck.glb");
        }
        internal static void DisposeImportedModel(){importedModel?.Dispose(); importedModel=null;}

        internal static void DrawPreview(float cargoFill)
        {
            EnsureModel();
            using(RenderDevice.CreateStateScope().Disable(RenderCapability.CullFace))
                importedModel.Draw(Matrix4.Identity,cargoFill,0);
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
