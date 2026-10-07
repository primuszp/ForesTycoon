using System;
using OpenTK.Mathematics;
namespace ForesTycoon.Effects
{
    internal static class FogParticleMotion
    {
        internal static (Vector3 Right,Vector3 Up) CameraBasis(float yawDegrees,float tiltDegrees)
        {
            float yaw=MathHelper.DegreesToRadians(yawDegrees), tilt=MathHelper.DegreesToRadians(tiltDegrees);
            return (new Vector3(MathF.Cos(yaw),-MathF.Sin(yaw),0),
                new Vector3(MathF.Sin(yaw)*MathF.Cos(tilt),MathF.Cos(yaw)*MathF.Cos(tilt),-MathF.Sin(tilt)));
        }
        internal static (Vector4 Position, Vector4 Life) Sample(Vector4 anchor,int layer,double time)
        {
            double seed=anchor.X*12.9898+anchor.Y*78.233+layer*37.719;
            float Hash(double offset){double h=Math.Sin(seed+offset)*43758.5453;return (float)(h-Math.Floor(h));}
            float duration=18+Hash(1)*14;
            double cycle=Math.Floor(time/duration+Hash(2));
            float age=(float)(time/duration+Hash(2)-cycle);
            float fade=Math.Clamp(age/0.18f,0,1)*Math.Clamp((1-age)/0.22f,0,1);
            float radius=anchor.W*(1.5f+Hash(cycle+3)*0.9f)*(0.8f+age*0.35f);
            Vector4 position=new(anchor.X+(Hash(cycle+4)-0.5f)*anchor.W*2+(age-0.5f)*5,
                anchor.Y+(Hash(cycle+5)-0.5f)*anchor.W*2+(age-0.5f)*1.7f,anchor.Z,radius);
            return (position,new Vector4(fade,Hash(7),Hash(8)*20,Hash(9)*20));
        }
    }
}
