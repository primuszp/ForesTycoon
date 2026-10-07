using System;
using OpenTK.Mathematics;
namespace ForesTycoon.Effects
{
    internal readonly record struct FogSource(Vector4 Position,float Forest,float Water,float Valley,float Moisture);
    internal static class FogHabitat
    {
        internal static float Density(FogSource source,double time,float wetness,float wind,float heating)
        {
            float habitat=source.Forest*0.35f+source.Water*0.65f+source.Valley*0.4f;
            if(habitat<0.1f) return 0;
            float moisture=Math.Clamp(source.Moisture*0.45f+wetness*0.55f+source.Water*0.3f,0,1);
            float threshold=0.67f-moisture*0.2f-source.Valley*0.06f;
            float field=Noise(source.Position.X/32f+(float)(time*0.0018),
                source.Position.Y/32f-(float)(time*0.0011));
            float patch=Smooth(threshold-0.08f,threshold+0.13f,field);
            return Math.Clamp(patch*(habitat+wetness*0.25f)*(0.5f+moisture*0.5f)/
                (1+wind*0.22f)*(1-Math.Clamp(heating,0,1)*0.45f),0,1);
        }
        private static float Smooth(float a,float b,float x){float t=Math.Clamp((x-a)/(b-a),0,1);return t*t*(3-2*t);}
        private static float Noise(float x,float y)
        {
            float Hash(float a,float b){double h=Math.Sin(a*127.1+b*311.7)*43758.5453;return (float)(h-Math.Floor(h));}
            float ix=MathF.Floor(x), iy=MathF.Floor(y),fx=x-ix,fy=y-iy;
            fx=fx*fx*(3-2*fx); fy=fy*fy*(3-2*fy);
            float a=Hash(ix,iy)*(1-fx)+Hash(ix+1,iy)*fx;
            float b=Hash(ix,iy+1)*(1-fx)+Hash(ix+1,iy+1)*fx;
            return a*(1-fy)+b*fy;
        }
    }
}
