using System;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct ForestVertexGrowth(Vector3 Origin, Vector3 AnnualScale)
    {
        internal const int Stride = 6 * sizeof(float);
        internal Vector3 At(Vector3 point, float elapsed) => Origin + (point - Origin) * (Vector3.One + AnnualScale * Math.Max(0, elapsed));

        // Identical transform in legacy, textured, outline and depth passes.
        internal const string Shader = @"
layout(location=3) in vec3 forest_origin;
layout(location=4) in vec3 forest_rate;
uniform float forest_elapsed;
vec3 forestScale(){return vec3(1) + forest_rate * max(0, forest_elapsed);}
vec3 forestPoint(vec3 p){return forest_origin + (p - forest_origin) * forestScale();}
vec3 forestNormal(vec3 n){vec3 v=n / forestScale(); return length(v)>0 ? normalize(v) : vec3(0);}
";
    }
}
