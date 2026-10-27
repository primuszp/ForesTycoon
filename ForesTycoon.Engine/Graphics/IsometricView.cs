using System;
using OpenTK.Mathematics;

namespace ForesTycoon.Engine
{
    /// <summary>Prepared yaw/tilt rotation shared by camera placement and terrain visibility.</summary>
    internal readonly struct IsometricView
    {
        private readonly double cosYaw, sinYaw, cosTilt, sinTilt;

        internal IsometricView(float tiltDegrees, float yawDegrees)
        {
            double yaw = yawDegrees * Math.PI / 180, tilt = tiltDegrees * Math.PI / 180;
            cosYaw = Math.Cos(yaw); sinYaw = Math.Sin(yaw);
            cosTilt = Math.Cos(tilt); sinTilt = Math.Sin(tilt);
        }

        internal Vector3 Transform(Vector3 point)
        {
            double x = cosYaw * point.X - sinYaw * point.Y;
            double y = sinYaw * point.X + cosYaw * point.Y;
            return new Vector3((float)x, (float)(cosTilt * y - sinTilt * point.Z),
                (float)(sinTilt * y + cosTilt * point.Z));
        }

        // Each projected coordinate is linear: its extremum lies on the corner selected
        // by the signs of its coefficients. Four support corners replace eight rotations.
        internal void ProjectBounds(Vector3 min, Vector3 max, out Vector2 lower, out Vector2 upper)
        {
            float lowX = cosYaw >= 0 ? min.X : max.X, highX = cosYaw >= 0 ? max.X : min.X;
            float lowY = sinYaw <= 0 ? min.Y : max.Y, highY = sinYaw <= 0 ? max.Y : min.Y;
            lower.X = (float)(cosYaw * lowX - sinYaw * lowY);
            upper.X = (float)(cosYaw * highX - sinYaw * highY);
            lowX = cosTilt * sinYaw >= 0 ? min.X : max.X;
            highX = cosTilt * sinYaw >= 0 ? max.X : min.X;
            lowY = cosTilt * cosYaw >= 0 ? min.Y : max.Y;
            highY = cosTilt * cosYaw >= 0 ? max.Y : min.Y;
            float lowZ = sinTilt <= 0 ? min.Z : max.Z, highZ = sinTilt <= 0 ? max.Z : min.Z;
            // Keep the same evaluation order and precision as Transform.
            lower.Y = (float)(cosTilt * (sinYaw * lowX + cosYaw * lowY) - sinTilt * lowZ);
            upper.Y = (float)(cosTilt * (sinYaw * highX + cosYaw * highY) - sinTilt * highZ);
        }
    }
}
