using System;
using OpenTK.Graphics.OpenGL;
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
            outlineBudget = settings.Enhanced && settings.VehicleOutlines && context.PixelsPerWorldUnit >= 7 ?
                settings.Quality == GraphicsQuality.High ? 8 : settings.Quality == GraphicsQuality.Medium ? 4 : 0 : 0;
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
                        DrawTruck(new VehicleTransform(center, forward, left, up), vehicle.VisualCargoFill, scale,
                            (float)(routePosition * vehicle.RoadRoute.TileLength / (0.23*scale)),
                            vehicle.RoadRoute.BodyCurvature(routePosition,importedModel.Wheelbase*scale),
                            VehicleVisualMotion.Suspension(routePosition*vehicle.RoadRoute.TileLength,
                                (float)vehicle.CurrentSpeed,vehicle.RoadRoute.Roughness(routePosition,importedModel.Wheelbase*scale),vehicle.CargoFill));
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
                    if (Culled(position, scale)) continue;
                    DrawTruck(new VehicleTransform(position, yaw, pitch), vehicle.VisualCargoFill,scale);
                }
            }
        }

        private static bool Culled(Vector3 center, float scale) => RenderDevice.Visuals?.ShadowPass != true &&
            !RenderVisibility.SphereVisible(center, importedModel.Radius * scale + 0.5f, RenderDevice.ViewProjection);

        private static void DrawTruck(VehicleTransform transform, float cargoFill, float scale, float wheelAngle = 0,float curvature=0,Matrix4? suspension=null)
        {
            EnsureModel();
            Matrix4 matrix=new Matrix4(
                new Vector4(transform.Apply(1,0,0)-transform.Apply(0,0,0),0),
                new Vector4(transform.Apply(0,1,0)-transform.Apply(0,0,0),0),
                new Vector4(transform.Apply(0,0,1)-transform.Apply(0,0,0),0),
                new Vector4(transform.Apply(0,0,0),1));
            using(new RenderStateScope().Disable(EnableCap.CullFace))
                {
                float outline = RenderDevice.Visuals?.ShadowPass != true && outlineBudget > 0 ? 0.7f / outlinePixelsPerUnit / scale : 0;
                if (outline > 0) outlineBudget--;
                importedModel.Draw(Matrix4.CreateScale(scale)*matrix,cargoFill,wheelAngle,curvature,scale,suspension,outline);
            }
        }

        private static GlbTruckModel importedModel;
        private static void EnsureModel()
        {
            if (importedModel != null) return;
            importedModel = GlbTruckModel.Load(System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","Vehicles","log-truck.glb"));
            RenderDevice.Disposing += DisposeImportedModel;
        }
        internal static void DisposeImportedModel(){importedModel?.Dispose(); importedModel=null;}

        internal static void DrawPreview(float cargoFill)
        {
            EnsureModel();
            using(new RenderStateScope().Disable(EnableCap.CullFace))
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
