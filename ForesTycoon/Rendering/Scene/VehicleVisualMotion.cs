using System;
using OpenTK.Mathematics;
namespace ForesTycoon
{
    internal static class VehicleVisualMotion
    {
        internal static float Steering(float curvature,float wheelbase,float lateralOffset)
            => Math.Clamp(MathF.Atan2(wheelbase*curvature,Math.Max(0.15f,1-curvature*lateralOffset)),-1.02f,1.02f);
        internal static Matrix4 Suspension(double distance,float speed,float roughness,float load)
        {
            float moving=Math.Clamp(speed/0.35f,0,1);
            float amplitude=(0.003f+Math.Clamp(roughness,0,1)*0.09f)*moving/(1+load*0.3f);
            float phase=(float)(distance*3.7);
            float heave=amplitude*(MathF.Sin(phase)+0.35f*MathF.Sin(phase*1.63f));
            float pitch=amplitude*0.18f*MathF.Sin(phase-0.8f);
            float roll=amplitude*0.12f*MathF.Sin(phase*0.73f);
            return Matrix4.CreateRotationX(roll)*Matrix4.CreateRotationY(pitch)*Matrix4.CreateTranslation(0,0,heave);
        }
    }
}
