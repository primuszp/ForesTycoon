using System;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    /// <summary>Platform-independent isometric camera state and frame-rate independent easing.</summary>
    sealed class IsometricCamera
    {
        private static readonly float[] TiltAngles = { -30f, -45f, -60f };
        private int tiltIndex = 2;

        public float Zoom { get; set; } = 10f;
        public float TargetZoom { get; set; } = 10f;
        public float Tilt { get; private set; } = -60f;
        public float Yaw { get; set; } = -45f;
        public float TargetYaw { get; set; } = -45f;
        public double ScreenX { get; set; }
        public double ScreenY { get; set; }

        public bool Update(float deltaTimeSeconds, double zoomAnchorX, double zoomAnchorY)
        {
            float zoomDifference = TargetZoom - Zoom;
            if (Math.Abs(zoomDifference) > 0.01f)
            {
                float previousZoom = Zoom;
                Zoom += zoomDifference * SmoothStepFactor(0.18f, deltaTimeSeconds);
                ScreenX = zoomAnchorX - (zoomAnchorX - ScreenX) * (previousZoom / Zoom);
                ScreenY = zoomAnchorY - (zoomAnchorY - ScreenY) * (previousZoom / Zoom);
            }

            float yawDifference = TargetYaw - Yaw;
            if (Math.Abs(yawDifference) > 0.01f)
            {
                Yaw += yawDifference * SmoothStepFactor(0.12f, deltaTimeSeconds);
                return true;
            }

            bool changed = Yaw != TargetYaw;
            Yaw = TargetYaw;
            return changed;
        }

        public void ClampMinimumZoom(float minimumZoom)
        {
            TargetZoom = Math.Max(TargetZoom, minimumZoom);
            Zoom = Math.Max(Zoom, minimumZoom);
        }

        public void ZoomBy(float factor)
        {
            if (!float.IsFinite(factor) || factor <= 0f) throw new ArgumentOutOfRangeException(nameof(factor));
            TargetZoom = Math.Clamp(TargetZoom * factor, 0.005f, 10000f);
        }

        public void StepTilt(int direction)
        {
            tiltIndex = Math.Clamp(tiltIndex + Math.Sign(direction), 0, TiltAngles.Length - 1);
            Tilt = TiltAngles[tiltIndex];
        }

        public void Reset()
        {
            tiltIndex = 2;
            Tilt = TiltAngles[tiltIndex];
            Yaw = TargetYaw = -45f;
            Zoom = TargetZoom = 10f;
            ScreenX = ScreenY = 0.0;
        }

        public Matrix4 CreateViewMatrix() =>
            Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(Yaw))
            * Matrix4.CreateRotationX(MathHelper.DegreesToRadians(Tilt));

        private static float SmoothStepFactor(float factorAt30Fps, float deltaTimeSeconds)
        {
            const float referenceFrameSeconds = 1f / 30f;
            if (deltaTimeSeconds <= 0f) return 0f;
            return 1f - MathF.Pow(1f - factorAt30Fps, deltaTimeSeconds / referenceFrameSeconds);
        }
    }
}
